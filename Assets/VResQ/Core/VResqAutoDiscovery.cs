using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;

namespace VResQ.Core
{
    /// <summary>
    /// Discovered Robot Node metadata from mDNS, UDP Beacon, or Subnet Sweep.
    /// </summary>
    [Serializable]
    public class DiscoveredRobot
    {
        public string nodeUid;
        public string nodeName;
        public int robotId;
        public string role;
        public string ipAddress;
        public int port;
        public string discoveryMethod; // "mdns", "udp_beacon", "subnet_sweep", "swarm_gossip"
        public float lastSeenTime;
    }

    /// <summary>
    /// Zero-Config Dynamic Robot Discovery for Unity VR & Cockpit.
    /// Automatically detects network changes (switching Wi-Fi / Hotspot / LAN),
    /// listens for robot mDNS & UDP beacons (port 8005/51235), and performs subnet sweeps.
    /// Completely eliminates hardcoded IPs!
    /// </summary>
    public class VResqAutoDiscovery : MonoBehaviour
    {
        public static VResqAutoDiscovery Instance { get; private set; }

        [Header("Discovery Settings")]
        [Tooltip("Listen for robot UDP broadcast beacons (Port 8005)")]
        public bool enableUdpBeaconListener = true;

        [Tooltip("Send mDNS queries for _vresq._tcp.local (Port 5353)")]
        public bool enableMdnsQuery = true;

        [Tooltip("Perform active subnet HTTP sweep if broadcast/multicast is filtered by router/hotspot")]
        public bool enableSubnetSweep = true;

        [Tooltip("Seconds between background network probes")]
        [Range(2f, 15f)]
        public float probeIntervalSeconds = 4.0f;

        [Header("Live Fleet Status (Read-Only)")]
        public string currentLocalIp = "Detecting...";
        public string currentSubnetPrefix = "";
        public int discoveredCount = 0;

        [Header("Events")]
        public UnityEvent<List<DiscoveredRobot>> OnFleetDiscovered = new UnityEvent<List<DiscoveredRobot>>();

        private readonly ConcurrentDictionary<string, DiscoveredRobot> _discoveredNodes = new ConcurrentDictionary<string, DiscoveredRobot>();
        private readonly List<DiscoveredRobot> _cachedList = new List<DiscoveredRobot>();
        private static readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
        private CancellationTokenSource _cts;
        private UdpClient _beaconClient;
        private Thread _beaconThread;
        private HttpClient _probeHttpClient;
        private string _lastObservedNetworkId = "";

        private static float GetNowSeconds() => (float)_clock.Elapsed.TotalSeconds;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            _probeHttpClient = new HttpClient { Timeout = TimeSpan.FromMilliseconds(750) };
        }

        private void OnEnable()
        {
            _cts = new CancellationTokenSource();
            NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;

            DetectLocalNetwork();

            if (enableUdpBeaconListener)
            {
                StartUdpBeaconListener();
            }

            Task.Run(() => BackgroundDiscoveryLoopAsync(_cts.Token));
        }

        private void OnDisable()
        {
            NetworkChange.NetworkAddressChanged -= OnNetworkAddressChanged;
            _cts?.Cancel();
            StopUdpBeaconListener();
        }

        private void Update()
        {
            // Prune nodes not seen for > 18 seconds (thread-safe monotonic clock)
            float now = GetNowSeconds();
            bool changed = false;

            foreach (var kvp in _discoveredNodes)
            {
                if (now - kvp.Value.lastSeenTime > 18.0f)
                {
                    _discoveredNodes.TryRemove(kvp.Key, out _);
                    changed = true;
                }
            }

            discoveredCount = _discoveredNodes.Count;

            if (changed || (_cachedList.Count != discoveredCount))
            {
                _cachedList.Clear();
                _cachedList.AddRange(_discoveredNodes.Values);
                _cachedList.Sort((a, b) => a.robotId.CompareTo(b.robotId));

                SyncDiscoveredToFrameworkConfig();
                OnFleetDiscovered?.Invoke(_cachedList);
            }
        }

        private void OnNetworkAddressChanged(object sender, EventArgs e)
        {
            Debug.Log("[VResQ-Discovery] Network address change detected! Re-scanning network...");
            DetectLocalNetwork();
            _discoveredNodes.Clear();
            _cachedList.Clear();
        }

        /// <summary>
        /// Reads local network adapter to find current IP & subnet (e.g. 172.20.10.x, 192.168.1.x)
        /// </summary>
        public void DetectLocalNetwork()
        {
            try
            {
                string bestIp = "";
                foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                    var ipProps = ni.GetIPProperties();
                    foreach (UnicastIPAddressInformation addr in ipProps.UnicastAddresses)
                    {
                        if (addr.Address.AddressFamily == AddressFamily.InterNetwork)
                        {
                            string ipStr = addr.Address.ToString();
                            if (!ipStr.StartsWith("127.") && !ipStr.StartsWith("169.254."))
                            {
                                bestIp = ipStr;
                                break;
                            }
                        }
                    }
                    if (!string.IsNullOrEmpty(bestIp)) break;
                }

                currentLocalIp = !string.IsNullOrEmpty(bestIp) ? bestIp : "127.0.0.1";
                if (currentLocalIp.Contains("."))
                {
                    var parts = currentLocalIp.Split('.');
                    currentSubnetPrefix = $"{parts[0]}.{parts[1]}.{parts[2]}";
                }
                else
                {
                    currentSubnetPrefix = "";
                }

                Debug.Log($"[VResQ-Discovery] Active LAN IP: {currentLocalIp} | Subnet: {currentSubnetPrefix}.x");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[VResQ-Discovery] DetectLocalNetwork error: {ex.Message}");
            }
        }

        #region UDP Beacon Listener (Port 8005)
        private void StartUdpBeaconListener()
        {
            try
            {
                _beaconClient = new UdpClient();
                _beaconClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _beaconClient.Client.Bind(new IPEndPoint(IPAddress.Any, 8005));

                _beaconThread = new Thread(UdpBeaconListenLoop) { IsBackground = true, Name = "VResqUdpBeaconListener" };
                _beaconThread.Start();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[VResQ-Discovery] UDP Beacon bind on 8005 error: {ex.Message}");
            }
        }

        private void StopUdpBeaconListener()
        {
            try { _beaconClient?.Close(); } catch { }
            _beaconClient = null;
        }

        private void UdpBeaconListenLoop()
        {
            IPEndPoint remoteEp = new IPEndPoint(IPAddress.Any, 0);
            while (_cts != null && !_cts.IsCancellationRequested && _beaconClient != null)
            {
                try
                {
                    byte[] data = _beaconClient.Receive(ref remoteEp);
                    if (data != null && data.Length > 0 && data.Length <= 2048)
                    {
                        string json = Encoding.UTF8.GetString(data);
                        ProcessBeaconJson(json, remoteEp.Address.ToString());
                    }
                }
                catch (SocketException)
                {
                    if (_cts != null && _cts.IsCancellationRequested) break;
                }
                catch (Exception)
                {
                    if (_cts != null && _cts.IsCancellationRequested) break;
                }
            }
        }

        private void ProcessBeaconJson(string json, string sourceIp)
        {
            if (string.IsNullOrEmpty(json) || !json.Contains("vresq")) return;
            try
            {
                // Beacon schema: {"vresq": true, "node_uid": "...", "node_name": "...", "robot_id": "1", "role": "...", "port": 8000}
                var beacon = JsonUtility.FromJson<UdpBeaconPayload>(json);
                if (beacon != null && beacon.vresq)
                {
                    int rId = 1;
                    int.TryParse(beacon.robot_id, out rId);
                    int portVal = (beacon.port >= 1 && beacon.port <= 65535) ? beacon.port : 8000;
                    RegisterNode(new DiscoveredRobot
                    {
                        nodeUid = !string.IsNullOrEmpty(beacon.node_uid) ? beacon.node_uid : $"uid-{sourceIp}",
                        nodeName = !string.IsNullOrEmpty(beacon.node_name) ? beacon.node_name : $"Node {rId}",
                        robotId = rId > 0 ? rId : 1,
                        role = !string.IsNullOrEmpty(beacon.role) ? beacon.role : "robot",
                        ipAddress = sourceIp,
                        port = portVal,
                        discoveryMethod = "udp_beacon",
                        lastSeenTime = GetNowSeconds()
                    });
                }
            }
            catch (Exception) { }
        }

        [Serializable]
        private class UdpBeaconPayload
        {
            public bool vresq;
            public string node_uid;
            public string node_name;
            public string robot_id;
            public string cluster_id;
            public string role;
            public int port;
        }
        #endregion

        #region Background Periodic Discovery Loop
        private async Task BackgroundDiscoveryLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    // 1. Send mDNS Query
                    if (enableMdnsQuery)
                    {
                        SendMdnsQuery();
                    }

                    // 2. Perform Subnet Sweep across candidate IPs if on a known subnet
                    if (enableSubnetSweep && !string.IsNullOrEmpty(currentSubnetPrefix))
                    {
                        await SweepSubnetCandidatesAsync(token);
                    }
                }
                catch (Exception) { }

                await Task.Delay((int)(probeIntervalSeconds * 1000f), token);
            }
        }

        private void SendMdnsQuery()
        {
            try
            {
                using (var udp = new UdpClient())
                {
                    udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                    udp.EnableBroadcast = true;
                    // Standard DNS-SD PTR query packet for "_vresq._tcp.local"
                    byte[] query = BuildDnsSdQueryPacket("_vresq._tcp.local");
                    IPEndPoint mcastEp = new IPEndPoint(IPAddress.Parse("224.0.0.251"), 5353);
                    udp.Send(query, query.Length, mcastEp);
                }
            }
            catch (Exception) { }
        }

        private static byte[] BuildDnsSdQueryPacket(string serviceType)
        {
            // Minimal binary DNS query packet for PTR question
            List<byte> bytes = new List<byte> { 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
            foreach (var part in serviceType.Split('.'))
            {
                if (string.IsNullOrEmpty(part)) continue;
                bytes.Add((byte)part.Length);
                bytes.AddRange(Encoding.ASCII.GetBytes(part));
            }
            bytes.Add(0x00);
            bytes.AddRange(new byte[] { 0x00, 0x0C, 0x00, 0x01 }); // QTYPE = PTR (12), QCLASS = IN (1)
            return bytes.ToArray();
        }

        private async Task SweepSubnetCandidatesAsync(CancellationToken token)
        {
            // Sweep .1 through .20 (covers all mobile hotspots e.g. .2, .3, .4, .5, .6, .10) plus common LAN static IPs
            List<string> candidateIps = new List<string>(24);
            for (int host = 1; host <= 20; host++)
            {
                candidateIps.Add($"{currentSubnetPrefix}.{host}");
            }
            candidateIps.Add($"{currentSubnetPrefix}.50");
            candidateIps.Add($"{currentSubnetPrefix}.82");
            candidateIps.Add($"{currentSubnetPrefix}.100");

            List<Task> tasks = new List<Task>(candidateIps.Count);
            foreach (var ip in candidateIps)
            {
                if (ip == currentLocalIp) continue;
                tasks.Add(ProbeCandidateIpAsync(ip, 8000, token));
            }

            await Task.WhenAll(tasks);
        }

        private async Task ProbeCandidateIpAsync(string ip, int port, CancellationToken token)
        {
            try
            {
                string url = $"http://{ip}:{port}/api/system";
                using (var res = await _probeHttpClient.GetAsync(url, token))
                {
                    if (res.IsSuccessStatusCode)
                    {
                        string body = await res.Content.ReadAsStringAsync();
                        ProcessSystemPayload(body, ip, port);
                    }
                }
            }
            catch (Exception) { }
        }

        private void ProcessSystemPayload(string json, string ip, int port)
        {
            if (string.IsNullOrEmpty(json) || !json.Contains("identity")) return;
            try
            {
                var sys = JsonUtility.FromJson<SystemInfoPayload>(json);
                if (sys != null && sys.identity != null)
                {
                    int rId = sys.identity.robot_id > 0 ? sys.identity.robot_id : 1;
                    RegisterNode(new DiscoveredRobot
                    {
                        nodeUid = !string.IsNullOrEmpty(sys.identity.node_uid) ? sys.identity.node_uid : $"uid-{ip}",
                        nodeName = !string.IsNullOrEmpty(sys.identity.node_name) ? sys.identity.node_name : $"Node {rId}",
                        robotId = rId,
                        role = !string.IsNullOrEmpty(sys.identity.role) ? sys.identity.role : "robot",
                        ipAddress = ip,
                        port = port,
                        discoveryMethod = "subnet_sweep",
                        lastSeenTime = GetNowSeconds()
                    });
                }
            }
            catch (Exception) { }
        }

        [Serializable]
        private class SystemIdentityPayload
        {
            public string node_name;
            public string node_uid;
            public int robot_id;
            public string role;
        }

        [Serializable]
        private class SystemInfoPayload
        {
            public string build_version;
            public string hw_mode;
            public SystemIdentityPayload identity;
        }
        #endregion

        private void RegisterNode(DiscoveredRobot node)
        {
            if (node == null || string.IsNullOrEmpty(node.ipAddress)) return;

            string key = !string.IsNullOrEmpty(node.nodeUid) ? node.nodeUid : node.ipAddress;
            node.lastSeenTime = GetNowSeconds();
            _discoveredNodes[key] = node;
        }

        /// <summary>
        /// Automatically populates VResqFrameworkConfig & switches VResqClient to the active leader
        /// without requiring any user input or hardcoded IP.
        /// </summary>
        private void SyncDiscoveredToFrameworkConfig()
        {
            var cfg = VResqFrameworkConfig.Instance;
            if (cfg == null || cfg.Profile == null) return;

            if (_cachedList.Count == 0) return;

            List<RobotNodeEndpoint> endpoints = new List<RobotNodeEndpoint>();
            for (int i = 0; i < _cachedList.Count && i < 3; i++)
            {
                var d = _cachedList[i];
                endpoints.Add(new RobotNodeEndpoint
                {
                    displayName = d.nodeName.ToUpper(),
                    roleLabel = d.role.ToUpper(),
                    ipAddress = d.ipAddress,
                    port = d.port,
                    isOnline = true
                });
            }

            cfg.Profile.fleetNodes = endpoints.ToArray();
            cfg.NotifyConfigChanged();

            // Auto-point VResqClient to first discovered node if not already connected
            if (VResqClient.Instance != null && !VResqClient.Instance.IsConnected && endpoints.Count > 0)
            {
                VResqClient.Instance.robotIp = endpoints[0].ipAddress;
                VResqClient.Instance.port = endpoints[0].port;
                VResqClient.Instance.Connect();
            }
        }
    }
}
