using System;
using System.Collections;
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
    /// Features instant parallel multi-node health probing (10.66.68.52, 10.66.68.10, 10.66.68.82)
    /// and automatic failover to whichever robot node is online.
    /// </summary>
    public class VResqClient : MonoBehaviour
    {
        public static VResqClient Instance { get; private set; }

        [Header("Target Robot Connection")]
        [Tooltip("IP Address of target robot (10.66.68.52, 10.66.68.10, 10.66.68.82)")]
        public string robotIp = "10.66.68.82";

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

        [SerializeField] private float latencyMs = 0f;
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

        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromMilliseconds(1800) };

        private ClientWebSocket _webSocket;
        private CancellationTokenSource _cts;
        private readonly Queue<Action> _mainThreadQueue = new Queue<Action>();
        private readonly object _queueLock = new object();
        private RobotStateSnapshot _latestState = new RobotStateSnapshot();
        public RobotStateSnapshot LatestState => _latestState;

        private float _swarmPollTimer = 0f;
        private float _fleetProbeTimer = 0f;
        private float _stateFallbackPollTimer = 0f;
        private float _lastStateReceivedRealtime = 0f;
        private bool _probingFleet = false;
        private bool _pollingStateHttp = false;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void Start()
        {
            SyncWithFrameworkConfig();

            // Immediately probe all fleet nodes in parallel so we lock onto the online robot within 0.3s
            _ = ProbeFleetNodesAsync(initialStartup: true);

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
            lock (_queueLock)
            {
                while (_mainThreadQueue.Count > 0)
                {
                    _mainThreadQueue.Dequeue()?.Invoke();
                }
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

            // 3. HTTP /api/state fallback poll if WebSocket hasn't pushed state in > 0.4s
            _stateFallbackPollTimer += Time.deltaTime;
            if (_stateFallbackPollTimer >= 0.4f && !_pollingStateHttp && (Time.realtimeSinceStartup - _lastStateReceivedRealtime > 0.35f))
            {
                _stateFallbackPollTimer = 0f;
                _ = PollStateFallbackAsync();
            }
        }

        private void OnDestroy()
        {
            Disconnect();
        }

        public void Connect()
        {
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            Task.Run(() => WebSocketLoopAsync(_cts.Token));
        }

        public void Disconnect()
        {
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
                string[] fallbackIps = { "10.66.68.52", "10.66.68.10", "10.66.68.82" };
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

        public void SendDriveCommand(float linear, float angular, int servo = 90, float speed = 0.7f, bool estop = false, bool buzzer = false)
        {
            if (_webSocket == null || _webSocket.State != WebSocketState.Open) return;

            var payload = new ControlPayload
            {
                linear = Mathf.Clamp(linear, -1f, 1f),
                angular = Mathf.Clamp(angular, -1f, 1f),
                servo = Mathf.Clamp(servo, 0, 180),
                speed = Mathf.Clamp01(speed),
                estop = estop,
                buzzer = buzzer
            };

            string json = payload.ToJson();
            Task.Run(() => SendRawJsonAsync(json));
        }

        public void SendActionCommand(string action)
        {
            if (string.IsNullOrEmpty(action)) return;
            Debug.Log($"<color=yellow>[VResQ Action]</color> Sending action: {action}");

            if (_webSocket != null && _webSocket.State == WebSocketState.Open)
            {
                var payload = new ActionPayload { action = action };
                string json = payload.ToJson();
                Task.Run(() => SendRawJsonAsync(json));
            }
            else
            {
                // HTTP POST fallback just like Web Dashboard app.js
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
                Task.Run(() => SendRawJsonAsync(json));
            }
        }

        private async Task SendRawJsonAsync(string json)
        {
            try
            {
                if (_webSocket != null && _webSocket.State == WebSocketState.Open)
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(json);
                    await _webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
                }
            }
            catch { }
        }

        private async Task WebSocketLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    string targetIp = robotIp;
                    int targetPort = port;
                    string wsUrl = $"ws://{targetIp}:{targetPort}/ws";

                    _webSocket = new ClientWebSocket();
                    _webSocket.Options.KeepAliveInterval = TimeSpan.FromSeconds(5);

                    using (var connectTimeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(2.5f)))
                    using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token, connectTimeoutCts.Token))
                    {
                        await _webSocket.ConnectAsync(new Uri(wsUrl), linkedCts.Token);
                    }

                    SetConnected(true);
                    Debug.Log($"<color=green>[VResQ]</color> WebSocket CONNECTED to {targetIp}:{targetPort}!");

                    // Send initial control packet so FastAPI /ws immediately responds with state
                    string initMsg = "{\"type\":\"control\",\"linear\":0,\"angular\":0,\"servo\":90,\"speed\":0.75,\"estop\":false,\"buzzer\":false}";
                    await SendRawJsonAsync(initMsg);

                    byte[] buffer = new byte[32768];
                    var ms = new MemoryStream();

                    while (_webSocket != null && _webSocket.State == WebSocketState.Open && !token.IsCancellationRequested)
                    {
                        WebSocketReceiveResult result;
                        ms.SetLength(0);

                        do
                        {
                            result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                            if (result.MessageType == WebSocketMessageType.Close)
                            {
                                await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                                break;
                            }
                            ms.Write(buffer, 0, result.Count);
                        } while (!result.EndOfMessage);

                        if (result.MessageType == WebSocketMessageType.Text && ms.Length > 0)
                        {
                            string rawJson = Encoding.UTF8.GetString(ms.ToArray());
                            ProcessIncomingJson(rawJson);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception)
                {
                    // Handled seamlessly by ProbeFleetNodesAsync & autoReconnect
                }
                finally
                {
                    SetConnected(false);
                    if (_webSocket != null)
                    {
                        try { _webSocket.Dispose(); } catch { }
                        _webSocket = null;
                    }
                }

                if (!autoReconnect || token.IsCancellationRequested) break;
                await Task.Delay(1200, token);
            }
        }

        private void ProcessIncomingJson(string rawJson)
        {
            if (string.IsNullOrEmpty(rawJson)) return;

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
                    state.ParseRadarPoints(rawJson);
                    _latestState = state;

                    EnqueueMainThread(() =>
                    {
                        float now = Time.realtimeSinceStartup;
                        if (_lastStateReceivedRealtime > 0f)
                        {
                            float deltaMs = (now - _lastStateReceivedRealtime) * 1000f;
                            latencyMs = Mathf.Lerp(latencyMs, Mathf.Clamp(deltaMs, 8f, 160f), 0.25f);
                        }
                        else
                        {
                            latencyMs = 22f;
                        }
                        _lastStateReceivedRealtime = now;
                        if (!isConnected) SetConnected(true);
                        OnStateReceived?.Invoke(_latestState);
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[VResQ] JSON parse error: {ex.Message}");
            }
        }

        private void SetConnected(bool connected)
        {
            if (isConnected != connected)
            {
                isConnected = connected;
                EnqueueMainThread(() =>
                {
                    if (isConnected) OnConnected?.Invoke();
                    else OnDisconnected?.Invoke();
                });
            }
        }

        private async Task PollStateFallbackAsync()
        {
            _pollingStateHttp = true;
            try
            {
                string url = $"http://{robotIp}:{port}/api/state";
                var sw = System.Diagnostics.Stopwatch.StartNew();
                string json = await _httpClient.GetStringAsync(url);
                sw.Stop();
                if (!string.IsNullOrEmpty(json))
                {
                    EnqueueMainThread(() =>
                    {
                        latencyMs = sw.ElapsedMilliseconds;
                    });
                    ProcessIncomingJson(json);
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

            Task[] tasks = new Task[nodes.Length];
            for (int i = 0; i < nodes.Length; i++)
            {
                int idx = i;
                tasks[i] = Task.Run(async () =>
                {
                    var node = nodes[idx];
                    if (string.IsNullOrEmpty(node.ipAddress)) return;
                    string url = $"http://{node.ipAddress}:{node.port}/api/state";
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    try
                    {
                        string txt = await _httpClient.GetStringAsync(url);
                        sw.Stop();
                        node.isOnline = true;
                        node.lastRttMs = sw.ElapsedMilliseconds;

                        var snap = JsonUtility.FromJson<RobotStateSnapshot>(txt);
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
                    catch
                    {
                        node.isOnline = false;
                        node.lastRttMs = 0f;
                    }
                });
            }

            await Task.WhenAll(tasks);

            EnqueueMainThread(() =>
            {
                // Seamless auto-failover if current node is offline and another fleet node is online
                bool currentOnline = false;
                for (int i = 0; i < nodes.Length; i++)
                {
                    if (nodes[i].ipAddress == robotIp && nodes[i].isOnline)
                    {
                        currentOnline = true;
                        break;
                    }
                }

                if (!currentOnline && (cfg.Profile.autoFallbackToOnlineNode || initialStartup))
                {
                    for (int i = 0; i < nodes.Length; i++)
                    {
                        if (nodes[i].isOnline)
                        {
                            Debug.Log($"<color=cyan>[VResQ Seamless Auto-Failover]</color> Target {robotIp} offline -> switching to online {nodes[i].displayName} ({nodes[i].ipAddress})!");
                            SwitchToNodeIndex(i);
                            break;
                        }
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
