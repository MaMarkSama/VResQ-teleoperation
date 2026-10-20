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
    /// Master Network Manager for Unity VR & Cockpit.
    /// Manages 20Hz WebSocket Telemetry, Drive Commands, and Swarm Mesh Discovery.
    /// Supports dynamic switching between Node Zero (10.66.68.52), Node One (10.66.68.10), and Node Two (10.66.68.82).
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

        [SerializeField] private float latencyMs = 0f;
        public float LatencyMs => latencyMs;

        [Header("Swarm Mesh Telemetry")]
        public List<DiscoveredPeerData> DiscoveredSwarmPeers = new List<DiscoveredPeerData>();

        [Header("Events")]
        public UnityEvent OnConnected;
        public UnityEvent OnDisconnected;
        public UnityEvent<RobotStateSnapshot> OnStateReceived;
        public UnityEvent<List<DiscoveredPeerData>> OnSwarmUpdated;
        public UnityEvent<string> OnRobotSwitched;

        private ClientWebSocket _webSocket;
        private CancellationTokenSource _cts;
        private readonly Queue<Action> _mainThreadQueue = new Queue<Action>();
        private readonly object _queueLock = new object();
        private RobotStateSnapshot _latestState = new RobotStateSnapshot();
        public RobotStateSnapshot LatestState => _latestState;

        private float _swarmPollTimer = 0f;
        private float _pingTimer = 0f;
        private float _lastPingSentTime = 0f;

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
            if (connectOnStart)
            {
                Connect();
            }
        }

        private void Update()
        {
            // Execute actions dispatched from background thread onto Unity main thread
            lock (_queueLock)
            {
                while (_mainThreadQueue.Count > 0)
                {
                    _mainThreadQueue.Dequeue()?.Invoke();
                }
            }

            // Periodic Swarm API Poll every 3 seconds to discover peer robots
            _swarmPollTimer += Time.deltaTime;
            if (_swarmPollTimer >= 3.0f)
            {
                _swarmPollTimer = 0f;
                StartCoroutine(PollSwarmPeersRoutine());
            }

            // Estimate latency RTT
            _pingTimer += Time.deltaTime;
            if (_pingTimer >= 1.0f && isConnected)
            {
                _pingTimer = 0f;
                _lastPingSentTime = Time.realtimeSinceStartup;
            }
        }

        private void OnDestroy()
        {
            Disconnect();
        }

        public void Connect()
        {
            if (isConnected) return;
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

        public void SwitchRobot(string newIp, int newPort = 8000)
        {
            if (string.IsNullOrEmpty(newIp)) return;
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

                    Debug.Log($"<color=cyan>[VResQ]</color> Connecting to WebSocket: {wsUrl}");
                    using (var connectTimeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(4)))
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
                catch (Exception ex)
                {
                    Debug.LogWarning($"[VResQ] WebSocket connection error: {ex.Message}");
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
                await Task.Delay(2000, token);
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

                        if (_lastPingSentTime > 0)
                        {
                            latencyMs = Mathf.Round((Time.realtimeSinceStartup - _lastPingSentTime) * 1000f);
                            _lastPingSentTime = 0f;
                        }

                        EnqueueMainThread(() =>
                        {
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

        private IEnumerator PollSwarmPeersRoutine()
        {
            string url = $"http://{robotIp}:{port}/api/swarm";
            using (var www = UnityEngine.Networking.UnityWebRequest.Get(url))
            {
                www.timeout = 2;
                yield return www.SendWebRequest();

                if (www.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
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
