using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Networking;

namespace VResQ.Core
{
    /// <summary>
    /// Master Network Manager for Unity VR & Cockpit.
    /// Manages 20Hz WebSocket Telemetry, Drive Commands, Swarm Mesh Discovery, and Multi-Node Health Probing.
    /// Supports seamless auto-failover and instant switching across Node Zero, Node One, and Node Two.
    /// </summary>
    public class VResqClient : MonoBehaviour
    {
        public static VResqClient Instance { get; private set; }

        [Header("Target Robot Connection")]
        [Tooltip("IP Address of target robot (e.g. 10.66.68.52, 10.66.68.10, 10.66.68.82)")]
        public string robotIp = "10.66.68.52";

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

        private ClientWebSocket _webSocket;
        private CancellationTokenSource _cts;
        private readonly Queue<Action> _mainThreadQueue = new Queue<Action>();
        private readonly object _queueLock = new object();
        private RobotStateSnapshot _latestState = new RobotStateSnapshot();
        public RobotStateSnapshot LatestState => _latestState;

        private float _swarmPollTimer = 0f;
        private float _fleetProbeTimer = 0f;
        private float _lastStateReceivedRealtime = 0f;
        private bool _probingFleet = false;

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

            if (connectOnStart)
            {
                Connect();
            }

            StartCoroutine(ProbeFleetNodesCoroutine());
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

            // Periodic Swarm API Poll every 3 seconds
            _swarmPollTimer += Time.deltaTime;
            if (_swarmPollTimer >= 3.0f)
            {
                _swarmPollTimer = 0f;
                StartCoroutine(PollSwarmPeersRoutine());
            }

            // Periodic Multi-Node Fleet Health Probe
            float probeInterval = VResqFrameworkConfig.Instance != null
                ? Mathf.Max(1.5f, VResqFrameworkConfig.Instance.Profile.fleetProbeIntervalSec)
                : 2.5f;

            _fleetProbeTimer += Time.deltaTime;
            if (_fleetProbeTimer >= probeInterval && !_probingFleet)
            {
                _fleetProbeTimer = 0f;
                StartCoroutine(ProbeFleetNodesCoroutine());
            }
        }

        private void OnDestroy()
        {
            Disconnect();
        }

        public void Connect()
        {
            if (isConnected) return;
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
                        break;
                    }
                }
            }

            if (robotIp == newIp && port == newPort && isConnected) return;

            Debug.Log($"<color=cyan>[VResQ]</color> Switching active robot to {newIp}:{newPort}...");
            Disconnect();
            robotIp = newIp;
            port = newPort;
            EnqueueMainThread(() =>
            {
                OnRobotSwitched?.Invoke(robotIp);
                Connect();
            });
        }

        public void SendDriveCommand(float linear, float angular, int servo = 90, float speed = 0.7f, bool estop = false, bool buzzer = false)
        {
            if (!isConnected || _webSocket == null || _webSocket.State != WebSocketState.Open) return;

            var payload = new ControlPayload
            {
                linear = Mathf.Clamp(linear, -1f, 1f),
                angular = Mathf.Clamp(angular, -1f, 1f),
                servo = Mathf.Clamp(servo, 15, 165),
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
            if (!isConnected || _webSocket == null || _webSocket.State != WebSocketState.Open) return;

            var payload = new ActionPayload { action = action };
            string json = payload.ToJson();
            Debug.Log($"<color=yellow>[VResQ Action]</color> Sending action: {action}");
            Task.Run(() => SendRawJsonAsync(json));
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
            catch (Exception ex)
            {
                Debug.LogWarning($"[VResQ] Send failed: {ex.Message}");
            }
        }

        private async Task WebSocketLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    string wsUrl = $"ws://{robotIp}:{port}/ws";
                    _webSocket = new ClientWebSocket();
                    _webSocket.Options.KeepAliveInterval = TimeSpan.FromSeconds(5);

                    using (var connectTimeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(3.5f)))
                    using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token, connectTimeoutCts.Token))
                    {
                        await _webSocket.ConnectAsync(new Uri(wsUrl), linkedCts.Token);
                    }

                    SetConnected(true);
                    Debug.Log($"<color=green>[VResQ]</color> WebSocket CONNECTED to {robotIp}:{port}!");

                    byte[] buffer = new byte[16384];
                    var ms = new MemoryStream();

                    while (_webSocket.State == WebSocketState.Open && !token.IsCancellationRequested)
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
                    // Quietly retry or failover via ProbeFleetNodesCoroutine
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
                await Task.Delay(1800, token);
            }
        }

        private void ProcessIncomingJson(string rawJson)
        {
            try
            {
                if (rawJson.Contains("\"type\":\"state\"") || rawJson.Contains("\"data\":"))
                {
                    int dataIdx = rawJson.IndexOf("\"data\":");
                    string dataSegment = rawJson;
                    if (dataIdx >= 0)
                    {
                        dataSegment = rawJson.Substring(dataIdx + 7).TrimEnd('}', ' ', '\r', '\n');
                    }

                    RobotStateSnapshot state = JsonUtility.FromJson<RobotStateSnapshot>(dataSegment);
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
                                latencyMs = Mathf.Lerp(latencyMs, Mathf.Clamp(deltaMs * 0.45f, 8f, 180f), 0.25f);
                            }
                            else
                            {
                                latencyMs = 24f;
                            }
                            _lastStateReceivedRealtime = now;
                            OnStateReceived?.Invoke(_latestState);
                        });
                    }
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

        private IEnumerator ProbeFleetNodesCoroutine()
        {
            var cfg = VResqFrameworkConfig.Instance;
            if (cfg == null || cfg.Profile.fleetNodes == null) yield break;

            _probingFleet = true;
            var nodes = cfg.Profile.fleetNodes;

            for (int i = 0; i < nodes.Length; i++)
            {
                var node = nodes[i];
                if (string.IsNullOrEmpty(node.ipAddress)) continue;

                string url = $"http://{node.ipAddress}:{node.port}/api/state";
                float t0 = Time.realtimeSinceStartup;
                using (var www = UnityWebRequest.Get(url))
                {
                    www.timeout = 1;
                    yield return www.SendWebRequest();

                    if (www.result == UnityWebRequest.Result.Success)
                    {
                        node.isOnline = true;
                        node.lastRttMs = Mathf.Round((Time.realtimeSinceStartup - t0) * 1000f);
                        try
                        {
                            string txt = www.downloadHandler.text;
                            var snap = JsonUtility.FromJson<RobotStateSnapshot>(txt);
                            if (snap != null)
                            {
                                if (snap.node_info != null)
                                {
                                    node.remoteNodeName = snap.node_info.node_name;
                                    node.remoteRole = snap.node_info.role;
                                }
                                if (snap.ai_config != null)
                                {
                                    node.detectorFps = snap.ai_config.detector_fps;
                                }
                            }
                        }
                        catch { }
                    }
                    else
                    {
                        node.isOnline = false;
                        node.lastRttMs = 0f;
                    }
                }
            }

            // Seamless auto-failover if current node is offline and another fleet node is online
            if (!isConnected && cfg.Profile.autoFallbackToOnlineNode)
            {
                for (int i = 0; i < nodes.Length; i++)
                {
                    if (nodes[i].isOnline && nodes[i].ipAddress != robotIp)
                    {
                        Debug.Log($"<color=cyan>[VResQ Seamless Failover]</color> Primary {robotIp} offline -> auto-switching to online {nodes[i].displayName} ({nodes[i].ipAddress})");
                        SwitchToNodeIndex(i);
                        break;
                    }
                }
            }

            OnFleetUpdated?.Invoke();
            _probingFleet = false;
        }

        private IEnumerator PollSwarmPeersRoutine()
        {
            string url = $"http://{robotIp}:{port}/api/swarm";
            using (var www = UnityWebRequest.Get(url))
            {
                www.timeout = 2;
                yield return www.SendWebRequest();

                if (www.result == UnityWebRequest.Result.Success)
                {
                    try
                    {
                        string json = www.downloadHandler.text;
                        SwarmApiResponse resp = JsonUtility.FromJson<SwarmApiResponse>(json);
                        if (resp != null && resp.discovered != null)
                        {
                            DiscoveredSwarmPeers = new List<DiscoveredPeerData>(resp.discovered);
                            OnSwarmUpdated?.Invoke(DiscoveredSwarmPeers);
                        }
                    }
                    catch { }
                }
            }
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
