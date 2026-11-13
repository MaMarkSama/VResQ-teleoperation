using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;

namespace VResQ.Core
{
    /// <summary>
    /// Master Network Manager for Unity VR & Surround Cockpit.
    /// Connects to V-ResQ FastAPI Backend (/ws, /api/state, /api/swarm, /api/action).
    /// All Unity JsonUtility calls are strictly executed on the Unity Main Thread so background
    /// WebSocket and HTTP tasks never throw main-thread UnityExceptions.
    /// </summary>
    public class VResqClient : MonoBehaviour
    {
        public static VResqClient Instance { get; private set; }

        [Header("Target Robot Connection")]
        [Tooltip("IP Address of target robot (e.g. 192.168.1.138 / .122 / .149 or 172.20.10.4)")]
        public string robotIp = "192.168.1.138";

        [Tooltip("HTTP & WebSocket Port (Default: 8000)")]
        public int port = 8000;

        [Tooltip("Auto-connect when scene starts")]
        public bool connectOnStart = true;

        [Tooltip("Auto-reconnect if connection is lost")]
        public bool autoReconnect = true;

        [Header("Runtime State")]
        [SerializeField] private bool isConnected = false;
        public bool IsConnected => isConnected;

        [SerializeField] private int activeNodeIndex = 0;
        public int ActiveNodeIndex => activeNodeIndex;

        [SerializeField] private float latencyMs = -1f;
        public float LatencyMs => latencyMs;

        [Header("Swarm Mesh Telemetry")]
        public List<DiscoveredPeerData> DiscoveredSwarmPeers = new List<DiscoveredPeerData>();

        [Header("Events")]
        public UnityEvent OnConnected = new UnityEvent();
        public UnityEvent OnDisconnected = new UnityEvent();
        public UnityEvent<RobotStateSnapshot> OnStateReceived = new UnityEvent<RobotStateSnapshot>();
        public UnityEvent<List<DiscoveredPeerData>> OnSwarmUpdated = new UnityEvent<List<DiscoveredPeerData>>();
        public UnityEvent<string> OnRobotSwitched = new UnityEvent<string>();
        public UnityEvent OnFleetUpdated = new UnityEvent();

        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromMilliseconds(2000) };

        private ClientWebSocket _webSocket;
        private CancellationTokenSource _cts;
        private readonly SemaphoreSlim _wsSendLock = new SemaphoreSlim(1, 1);
        private readonly Queue<Action> _mainThreadQueue = new Queue<Action>();
        private readonly object _queueLock = new object();
        private RobotStateSnapshot _latestState = new RobotStateSnapshot();
        public RobotStateSnapshot LatestState => _latestState;
        public string LastTelemetryError { get; private set; } = "";
        public string LastRawState { get; private set; }

        private float _swarmPollTimer = 0f;
        private float _fleetProbeTimer = 0f;
        private float _stateFallbackPollTimer = 0f;
        private float _wsHeartbeatTimer = 0f;
        private float _lastStateReceivedRealtime = 0f;
        private bool _probingFleet = false;
        private bool _pollingStateHttp = false;
        private float _leaseTimer;
        private volatile bool _authenticated;
        private int _connectionGeneration;
        private string _operatorToken;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                bool thisIsHudRoot = GetComponent<RescueRobotVRHUD>() != null;
                bool existingIsHudRoot = Instance.GetComponent<RescueRobotVRHUD>() != null;
                if (thisIsHudRoot && !existingIsHudRoot)
                {
                    VResqClient stale = Instance;
                    Instance = this;
                    Destroy(stale);
                    return;
                }

                Destroy(this);
                return;
            }
            Instance = this;
        }

        private void Start()
        {
            SyncWithFrameworkConfig();

            // Immediately probe all fleet nodes in parallel and poll initial state
            _ = ProbeFleetNodesAsync(initialStartup: true);
            _ = PollStateFallbackAsync();
            _ = PollSwarmPeersAsync();

            if (connectOnStart)
            {
                Connect();
            }
        }

        public void SyncWithFrameworkConfig()
        {
            var cfg = VResqFrameworkConfig.Instance;
            if (cfg != null && cfg.Profile.fleetNodes != null && cfg.Profile.fleetNodes.Length > 0)
            {
                int idx = Mathf.Clamp(cfg.Profile.defaultNodeIndex, 0, cfg.Profile.fleetNodes.Length - 1);
                activeNodeIndex = idx;
                robotIp = cfg.Profile.fleetNodes[idx].ipAddress;
                port = cfg.Profile.fleetNodes[idx].port;
            }
        }

        private void Update()
        {
            PumpMainThreadQueue();
            if (isConnected && _lastStateReceivedRealtime > 0f &&
                Time.realtimeSinceStartup - _lastStateReceivedRealtime > 3f)
                SetConnected(false);

            _leaseTimer += Time.unscaledDeltaTime;
            if (_authenticated && _leaseTimer >= 0.3f)
            {
                _leaseTimer = 0f;
                _ = SendRawJsonAsync("{\"type\":\"renew\",\"ttl_s\":0.75}");
            }

            // 1. Periodic Swarm API Poll every 2.5 seconds
            _swarmPollTimer += Time.deltaTime;
            if (_swarmPollTimer >= 2.5f)
            {
                _swarmPollTimer = 0f;
                _ = PollSwarmPeersAsync();
            }

            // 2. Periodic Multi-Node Fleet Health Probe every 2.0 seconds
            _fleetProbeTimer += Time.deltaTime;
            if (_fleetProbeTimer >= 2.0f && !_probingFleet)
            {
                _fleetProbeTimer = 0f;
                _ = ProbeFleetNodesAsync(initialStartup: false);
            }

            // 3. HTTP /api/state poll every 0.25s if WebSocket hasn't pushed state recently
            _stateFallbackPollTimer += Time.deltaTime;
            if (!_pollingStateHttp && (_stateFallbackPollTimer >= 2f ||
                (_stateFallbackPollTimer >= 0.25f && Time.realtimeSinceStartup - _lastStateReceivedRealtime > 0.20f)))
            {
                _stateFallbackPollTimer = 0f;
                _ = PollStateFallbackAsync();
            }

            // 4. WebSocket state keepalive request if idle (FastAPI /ws replies with state on incoming message)
            _wsHeartbeatTimer += Time.deltaTime;
            if (_wsHeartbeatTimer >= 0.15f && _webSocket != null && _webSocket.State == WebSocketState.Open && (Time.realtimeSinceStartup - _lastStateReceivedRealtime > 0.14f))
            {
                _wsHeartbeatTimer = 0f;
                _ = SendRawJsonAsync("{\"type\":\"ping\"}");
            }
        }

        public void PumpMainThreadQueue()
        {
            while (true)
            {
                Action action = null;
                lock (_queueLock)
                {
                    if (_mainThreadQueue.Count > 0)
                    {
                        action = _mainThreadQueue.Dequeue();
                    }
                }
                if (action == null) break;
                try
                {
                    action.Invoke();
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[VResQ MainThreadQueue] {ex.Message}");
                }
            }
        }

        private void OnDestroy()
        {
            Disconnect();
            if (Instance == this) Instance = null;
        }

        public void Connect()
        {
            Disconnect();
            _cts = new CancellationTokenSource();
            _operatorToken = Environment.GetEnvironmentVariable("VRESQ_OPERATOR_TOKEN") ?? "";
            var token = _cts.Token;
            string ip = robotIp;
            int p = port;
            Task.Run(() => WebSocketLoopAsync(ip, p, token));
        }

        public void Disconnect()
        {
            _connectionGeneration++;
            _authenticated = false;
            _cts?.Cancel();
            if (_webSocket != null)
            {
                try
                {
                    if (_webSocket.State == WebSocketState.Open || _webSocket.State == WebSocketState.Connecting)
                    {
                        _webSocket.Abort();
                    }
                    _webSocket.Dispose();
                }
                catch { }
                _webSocket = null;
            }
            SetConnected(false);
        }

        public void SwitchToNodeIndex(int index)
        {
            var cfg = VResqFrameworkConfig.Instance;
            if (cfg != null && cfg.Profile.fleetNodes != null && index >= 0 && index < cfg.Profile.fleetNodes.Length)
            {
                activeNodeIndex = index;
                cfg.Profile.defaultNodeIndex = index;
                var endpoint = cfg.Profile.fleetNodes[index];
                SwitchRobot(endpoint.ipAddress, endpoint.port);
            }
            else
            {
                string[] fallbackIps = { "172.20.10.4", "172.20.10.2", "172.20.10.6" };
                if (index >= 0 && index < fallbackIps.Length)
                {
                    activeNodeIndex = index;
                    SwitchRobot(fallbackIps[index], 8000);
                }
            }
        }

        public void SwitchRobot(string newIp, int newPort = 8000)
        {
            if (string.IsNullOrEmpty(newIp)) return;

            var cfg = VResqFrameworkConfig.Instance;
            if (cfg != null && cfg.Profile.fleetNodes != null)
            {
                for (int i = 0; i < cfg.Profile.fleetNodes.Length; i++)
                {
                    if (cfg.Profile.fleetNodes[i].ipAddress == newIp)
                    {
                        activeNodeIndex = i;
                        cfg.Profile.defaultNodeIndex = i;
                        break;
                    }
                }
            }

            bool changed = (robotIp != newIp || port != newPort);
            robotIp = newIp;
            port = newPort;

            if (!changed && isConnected) return;

            Debug.Log($"<color=cyan>[VResQ]</color> Switching active robot to {robotIp}:{port}...");
            Disconnect();
            EnqueueMainThread(() =>
            {
                OnRobotSwitched?.Invoke(robotIp);
                Connect();
                _ = PollStateFallbackAsync();
                _ = PollSwarmPeersAsync();
            });
        }

        private int _controlSequence = 0;

        public void SendDriveCommand(float linear, float angular, int servo = 90, float speed = 0.7f, bool estop = false, bool buzzer = false)
        {
            if (!_authenticated || _webSocket == null || _webSocket.State != WebSocketState.Open) return;

            _controlSequence++;
            // Serialize on Unity Main Thread
            var payload = new ControlPayload
            {
                sequence = _controlSequence,
                linear = Mathf.Clamp(linear, -1f, 1f),
                angular = Mathf.Clamp(angular, -1f, 1f),
                servo = Mathf.Clamp(servo, 0, 180),
                speed = Mathf.Clamp01(speed),
                estop = estop,
                buzzer = buzzer
            };

            string json = payload.ToJson();
            _ = SendRawJsonAsync(json);
        }

        public void ResetEstop()
        {
            string json = "{\"type\":\"estop_reset\"}";
            if (_webSocket != null && _webSocket.State == WebSocketState.Open)
            {
                _ = SendRawJsonAsync(json);
            }
            string url = $"http://{robotIp}:{port}/api/estop/reset";
            Task.Run(async () =>
            {
                try { await _httpClient.PostAsync(url, new StringContent("")); } catch { }
            });
        }

        public void SendActionCommand(string action)
        {
            if (string.IsNullOrEmpty(action)) return;
            Debug.Log($"<color=yellow>[VResQ Action]</color> Sending action: {action}");

            if (_webSocket != null && _webSocket.State == WebSocketState.Open)
            {
                // Serialize on Unity Main Thread
                var payload = new ActionPayload { action = action };
                string json = payload.ToJson();
                _ = SendRawJsonAsync(json);
            }
            else
            {
                string url = $"http://{robotIp}:{port}/api/action/{Uri.EscapeDataString(action)}";
                Task.Run(async () =>
                {
                    try { await _httpClient.PostAsync(url, new StringContent("")); } catch { }
                });
            }
        }

        public void SendOledPageCommand(int page)
        {
            int safePage = Mathf.Clamp(page, 0, 3);
            string json = $"{{\"type\":\"oled\",\"page\":{safePage}}}";
            if (_webSocket != null && _webSocket.State == WebSocketState.Open)
            {
                _ = SendRawJsonAsync(json);
            }
            string url = $"http://{robotIp}:{port}/api/oled/{safePage}";
            Task.Run(async () =>
            {
                try { await _httpClient.PostAsync(url, new StringContent("")); } catch { }
            });
        }

        private async Task SendRawJsonAsync(string json)
        {
            ClientWebSocket socket = _webSocket;
            CancellationToken token = _cts != null ? _cts.Token : CancellationToken.None;
            if (socket == null || socket.State != WebSocketState.Open) return;

            bool locked = false;
            try
            {
                locked = await _wsSendLock.WaitAsync(150, token);
                if (!locked) return;
                if (!token.IsCancellationRequested && ReferenceEquals(_webSocket, socket) && socket.State == WebSocketState.Open)
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(json);
                    using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(token))
                    {
                        deadline.CancelAfter(500);
                        await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, deadline.Token);
                    }
                }
            }
            catch { }
            finally
            {
                if (locked) _wsSendLock.Release();
            }
        }

        private async Task WebSocketLoopAsync(string targetIp, int targetPort, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                ClientWebSocket ws = null;
                try
                {
                    string wsUrl = $"ws://{targetIp}:{targetPort}/ws";

                    ws = new ClientWebSocket();
                    ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(5);
                    if (!string.IsNullOrEmpty(_operatorToken))
                        ws.Options.SetRequestHeader("X-VResQ-Token", _operatorToken);
                    token.ThrowIfCancellationRequested();
                    _webSocket = ws;

                    using (var connectTimeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(2.5f)))
                    using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token, connectTimeoutCts.Token))
                    {
                        await ws.ConnectAsync(new Uri(wsUrl), linkedCts.Token);
                    }

                    EnqueueMainThread(() =>
                    {
                        Debug.Log($"<color=green>[VResQ]</color> WebSocket CONNECTED to {targetIp}:{targetPort}!");
                    });

                    await SendRawJsonAsync("{\"type\":\"ping\"}");

                    byte[] buffer = new byte[32768];
                    var ms = new MemoryStream();

                    while (ws != null && ws.State == WebSocketState.Open && !token.IsCancellationRequested)
                    {
                        WebSocketReceiveResult result;
                        ms.SetLength(0);

                        do
                        {
                            result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                            if (result.MessageType == WebSocketMessageType.Close)
                            {
                                await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                                break;
                            }
                            ms.Write(buffer, 0, result.Count);
                            if (ms.Length > 2 * 1024 * 1024)
                                throw new InvalidDataException("Robot state message exceeds 2 MB");
                        } while (!result.EndOfMessage);

                        if (result.MessageType == WebSocketMessageType.Text && ms.Length > 0)
                        {
                            string rawJson = Encoding.UTF8.GetString(ms.ToArray());
                            // IMPORTANT: Always parse JsonUtility on the Unity Main Thread!
                            EnqueueMainThread(() =>
                            {
                                if (token.IsCancellationRequested || !ReferenceEquals(_webSocket, ws)) return;
                                if (rawJson.Contains("\"auth_ok\""))
                                {
                                    _authenticated = true;
                                    _ = SendRawJsonAsync("{\"type\":\"lease\",\"ttl_s\":0.75}");
                                }
                                else ProcessIncomingJsonOnMainThread(rawJson, updateRttFromDelta: false);
                            });
                        }
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception)
                {
                    // Handled seamlessly by HTTP fallback & ProbeFleetNodesAsync
                }
                finally
                {
                    if (ws != null)
                    {
                        try { ws.Dispose(); } catch { }
                        if (ReferenceEquals(_webSocket, ws))
                        {
                            _webSocket = null;
                            _authenticated = false;
                            SetConnected(false);
                        }
                    }
                }

                if (!autoReconnect || token.IsCancellationRequested) break;
                try { await Task.Delay(1200, token); }
                catch (OperationCanceledException) { break; }
                targetIp = robotIp;
                targetPort = port;
            }
        }

        private void ProcessIncomingJsonOnMainThread(string rawJson, bool updateRttFromDelta)
        {
            if (string.IsNullOrEmpty(rawJson)) return;
            LastRawState = rawJson;

            try
            {
                RobotStateSnapshot state = null;

                // 1. Check if wrapped in {"type": "state", "data": { ... }}
                if (rawJson.Contains("\"data\""))
                {
                    WsStateEnvelope env = JsonUtility.FromJson<WsStateEnvelope>(rawJson);
                    if (env != null && env.data != null)
                    {
                        state = env.data;
                    }
                }

                // 2. Direct /api/state snapshot fallback
                if (state == null && (rawJson.Contains("\"telemetry\"") || rawJson.Contains("\"identity\"")))
                {
                    state = JsonUtility.FromJson<RobotStateSnapshot>(rawJson);
                }

                if (state != null)
                {
                    LastTelemetryError = VResqTelemetryQuality.Validate(state.telemetry);
                    if (!string.IsNullOrEmpty(LastTelemetryError)) return;
                    state.ParseRadarPoints(rawJson);
                    _latestState = state;

                    float now = Time.realtimeSinceStartup;
                    _lastStateReceivedRealtime = now;

                    if (!isConnected) SetConnected(true);
                    OnStateReceived?.Invoke(_latestState);
                }
            }
            catch (Exception ex)
            {
                LastTelemetryError = "Malformed telemetry payload";
                Debug.LogWarning($"[VResQ] JSON parse error: {ex.Message}");
            }
        }

        private void SetConnected(bool connected)
        {
            EnqueueMainThread(() =>
            {
                if (isConnected != connected)
                {
                    isConnected = connected;
                    if (isConnected) OnConnected?.Invoke();
                    else OnDisconnected?.Invoke();
                }
            });
        }

        private async Task PollStateFallbackAsync()
        {
            _pollingStateHttp = true;
            try
            {
                string targetIp = robotIp;
                int targetPort = port;
                int generation = _connectionGeneration;
                string url = $"http://{targetIp}:{targetPort}/api/state";
                var sw = System.Diagnostics.Stopwatch.StartNew();
                string json = await _httpClient.GetStringAsync(url);
                sw.Stop();
                long rtt = sw.ElapsedMilliseconds;

                if (!string.IsNullOrEmpty(json))
                {
                    EnqueueMainThread(() =>
                    {
                        if (generation != _connectionGeneration || targetIp != robotIp || targetPort != port) return;
                        latencyMs = rtt;
                        ProcessIncomingJsonOnMainThread(json, updateRttFromDelta: false);
                    });
                }
            }
            catch { }
            finally
            {
                _pollingStateHttp = false;
            }
        }

        private async Task ProbeFleetNodesAsync(bool initialStartup)
        {
            var cfg = VResqFrameworkConfig.Instance;
            if (cfg == null || cfg.Profile.fleetNodes == null) return;

            _probingFleet = true;
            var nodes = cfg.Profile.fleetNodes;
            int count = nodes.Length;

            bool[] onlineResults = new bool[count];
            float[] rttResults = new float[count];
            string[] jsonResults = new string[count];

            Task[] tasks = new Task[count];
            for (int i = 0; i < count; i++)
            {
                int idx = i;
                string ip = nodes[idx].ipAddress;
                int p = nodes[idx].port;

                tasks[i] = Task.Run(async () =>
                {
                    if (string.IsNullOrEmpty(ip)) return;
                    string url = $"http://{ip}:{p}/api/state";
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    try
                    {
                        string txt = await _httpClient.GetStringAsync(url);
                        sw.Stop();
                        if (!string.IsNullOrEmpty(txt))
                        {
                            onlineResults[idx] = true;
                            rttResults[idx] = sw.ElapsedMilliseconds;
                            jsonResults[idx] = txt;
                        }
                    }
                    catch
                    {
                        onlineResults[idx] = false;
                        rttResults[idx] = 0f;
                        jsonResults[idx] = null;
                    }
                });
            }

            await Task.WhenAll(tasks);

            // Parse all JSON responses on the Unity Main Thread!
            EnqueueMainThread(() =>
            {
                for (int i = 0; i < count && i < nodes.Length; i++)
                {
                    var node = nodes[i];
                    node.isOnline = onlineResults[i];
                    node.lastRttMs = rttResults[i];

                    if (onlineResults[i] && !string.IsNullOrEmpty(jsonResults[i]))
                    {
                        try
                        {
                            var snap = JsonUtility.FromJson<RobotStateSnapshot>(jsonResults[i]);
                            if (snap != null)
                            {
                                var id = (snap.identity != null && !string.IsNullOrEmpty(snap.identity.node_name)) ? snap.identity : snap.node_info;
                                if (id != null)
                                {
                                    node.remoteNodeName = !string.IsNullOrEmpty(id.node_name) ? id.node_name : id.name;
                                    node.remoteRole = id.role;
                                }
                                if (snap.ai_config != null)
                                {
                                    node.detectorFps = snap.ai_config.detector_fps;
                                }
                            }
                        }
                        catch { }
                    }
                }

                // Check if current target robot is online
                bool currentOnline = false;
                for (int i = 0; i < nodes.Length; i++)
                {
                    if (nodes[i].ipAddress == robotIp && nodes[i].isOnline)
                    {
                        currentOnline = true;
                        break;
                    }
                }

                // Only auto-failover if not currently connected over WebSocket (prevents switching robots mid-drive)
                if (!currentOnline && !isConnected && (cfg.Profile.autoFallbackToOnlineNode || initialStartup))
                {
                    int bestIdx = -1;
                    for (int i = 0; i < nodes.Length; i++)
                    {
                        if (nodes[i].isOnline && nodes[i].detectorFps > 0.1f)
                        {
                            bestIdx = i;
                            break;
                        }
                    }
                    if (bestIdx < 0)
                    {
                        for (int i = 0; i < nodes.Length; i++)
                        {
                            if (nodes[i].isOnline)
                            {
                                bestIdx = i;
                                break;
                            }
                        }
                    }

                    if (bestIdx >= 0)
                    {
                        Debug.Log($"<color=cyan>[VResQ Seamless Auto-Failover]</color> Target {robotIp} offline -> switching to online {nodes[bestIdx].displayName} ({nodes[bestIdx].ipAddress})!");
                        SwitchToNodeIndex(bestIdx);
                    }
                }

                OnFleetUpdated?.Invoke();
                _probingFleet = false;
            });
        }

        private async Task PollSwarmPeersAsync()
        {
            try
            {
                string url = $"http://{robotIp}:{port}/api/swarm";
                string json = await _httpClient.GetStringAsync(url);
                if (!string.IsNullOrEmpty(json))
                {
                    EnqueueMainThread(() =>
                    {
                        try
                        {
                            SwarmApiResponse resp = JsonUtility.FromJson<SwarmApiResponse>(json);
                            if (resp != null && resp.discovered != null)
                            {
                                DiscoveredSwarmPeers = new List<DiscoveredPeerData>(resp.discovered);
                                OnSwarmUpdated?.Invoke(DiscoveredSwarmPeers);

                                // Dynamically ingest discovered peers into fleetNodes if not present
                                var cfg = VResqFrameworkConfig.Instance;
                                if (cfg != null && cfg.Profile.fleetNodes != null)
                                {
                                    bool fleetChanged = false;
                                    var currentFleet = new List<RobotNodeEndpoint>(cfg.Profile.fleetNodes);
                                    foreach (var peer in resp.discovered)
                                    {
                                        string peerIp = (peer.ips != null && peer.ips.Length > 0) ? peer.ips[0] : "";
                                        if (string.IsNullOrEmpty(peerIp) && !string.IsNullOrEmpty(peer.server))
                                        {
                                            peerIp = peer.server.Split(':')[0];
                                        }
                                        if (string.IsNullOrEmpty(peerIp)) continue;

                                        bool exists = false;
                                        foreach (var f in currentFleet)
                                        {
                                            if (f.ipAddress == peerIp)
                                            {
                                                exists = true;
                                                f.isOnline = true;
                                                f.remoteNodeName = peer.name;
                                                if (peer.properties != null)
                                                {
                                                    f.remoteRole = peer.properties.role;
                                                }
                                                break;
                                            }
                                        }
                                        if (!exists)
                                        {
                                            var newEndpoint = new RobotNodeEndpoint
                                            {
                                                displayName = !string.IsNullOrEmpty(peer.name) ? peer.name.ToUpper() : "SWARM NODE",
                                                roleLabel = (peer.properties != null && !string.IsNullOrEmpty(peer.properties.role)) ? peer.properties.role.ToUpper() : "FOLLOWER",
                                                ipAddress = peerIp,
                                                port = peer.port > 0 ? peer.port : 8000,
                                                isOnline = true,
                                                remoteNodeName = peer.name,
                                                remoteRole = (peer.properties != null) ? peer.properties.role : ""
                                            };
                                            currentFleet.Add(newEndpoint);
                                            fleetChanged = true;
                                            Debug.Log($"<color=cyan>[VResQ Swarm Discovery]</color> Auto-registered discovered node {newEndpoint.displayName} ({peerIp}) into Fleet!");
                                        }
                                    }
                                    if (fleetChanged)
                                    {
                                        cfg.Profile.fleetNodes = currentFleet.ToArray();
                                        OnFleetUpdated?.Invoke();
                                    }
                                }
                            }
                        }
                        catch { }
                    });
                }
            }
            catch { }
        }

        private void EnqueueMainThread(Action action)
        {
            lock (_queueLock)
            {
                _mainThreadQueue.Enqueue(action);
            }
        }
    }
}
