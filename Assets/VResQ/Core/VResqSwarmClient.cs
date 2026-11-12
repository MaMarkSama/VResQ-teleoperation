using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;

namespace VResQ.Core
{
    /// <summary>
    /// V-ResQ Swarm Command & Control (C2) Client for Unity 3D / VR.
    /// Automatically discovers and synchronizes all 3 physical boards:
    /// - Node Zero (Commander, robot_id=1): 172.20.10.4:8080
    /// - Node One  (Relay,     robot_id=2): 172.20.10.2:8080
    /// - Node Two  (Scout,     robot_id=3): 172.20.10.3:8080
    /// Listens to the 5 Hz UDP Gossip Bus (port 51235) AND polls /api/swarm + /api/swarm/map
    /// with automatic leader failover if the primary node drops offline.
    /// </summary>
    public class VResqSwarmClient : MonoBehaviour
    {
        [Header("Swarm Fleet Endpoints")]
        public string[] candidateEndpoints = new string[]
        {
            "http://172.20.10.4:8000",
            "http://172.20.10.2:8000",
            "http://172.20.10.6:8000",
            "http://172.20.10.3:8000"
        };

        public int activeEndpointIndex = 0;
        public int udpGossipPort = 51235;
        public float pollIntervalSeconds = 1.0f;

        [Header("Live Swarm State (Read-Only)")]
        public string activeLeaderEndpoint = "http://172.20.10.4:8000";
        public int activeLeaderId = 1;
        public int aliveNodesCount = 0;
        public bool versionSynced = false;
        public string currentFormation = "relay_chain";
        public string currentScenario = "normal";

        public SwarmSummaryResponse LatestSwarmSummary { get; private set; }
        public FusedMapResponse LatestFusedMap { get; private set; }
        public Dictionary<int, SwarmFrame> NodesById { get; private set; } = new Dictionary<int, SwarmFrame>();

        public event Action<SwarmSummaryResponse> OnSwarmSummaryUpdated;
        public event Action<FusedMapResponse> OnFusedMapUpdated;
        public event Action<SwarmFrame> OnUdpGossipFrameReceived;

        private UdpClient _udpClient;
        private Thread _udpThread;
        private volatile bool _running;
        private readonly ConcurrentQueue<string> _udpJsonQueue = new ConcurrentQueue<string>();

        private void OnEnable()
        {
            _running = true;
            StartUdpListener();
            StartCoroutine(PollSwarmLoop());
        }

        private void OnDisable()
        {
            _running = false;
            StopUdpListener();
            StopAllCoroutines();
        }

        private void Update()
        {
            while (_udpJsonQueue.TryDequeue(out string json))
            {
                try
                {
                    SwarmFrame frame = JsonUtility.FromJson<SwarmFrame>(json);
                    if (frame != null && frame.robot_id > 0)
                    {
                        NodesById[frame.robot_id] = frame;
                        OnUdpGossipFrameReceived?.Invoke(frame);
                    }
                }
                catch (Exception)
                {
                    // Ignore malformed UDP frames
                }
            }
        }

        private void StartUdpListener()
        {
            try
            {
                _udpClient = new UdpClient();
                _udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, udpGossipPort));
                _udpThread = new Thread(UdpListenLoop) { IsBackground = true };
                _udpThread.Start();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[VResqSwarmClient] UDP gossip listener on port {udpGossipPort} unavailable: {ex.Message}");
            }
        }

        private void StopUdpListener()
        {
            try { _udpClient?.Close(); } catch { }
            _udpClient = null;
        }

        private void UdpListenLoop()
        {
            IPEndPoint remoteEp = new IPEndPoint(IPAddress.Any, 0);
            while (_running && _udpClient != null)
            {
                try
                {
                    byte[] data = _udpClient.Receive(ref remoteEp);
                    if (data != null && data.Length > 0)
                    {
                        string raw = Encoding.UTF8.GetString(data);
                        _udpJsonQueue.Enqueue(raw);
                    }
                }
                catch (SocketException)
                {
                    if (!_running) break;
                }
                catch (Exception)
                {
                    if (!_running) break;
                }
            }
        }

        private IEnumerator PollSwarmLoop()
        {
            while (_running)
            {
                if (candidateEndpoints != null && candidateEndpoints.Length > 0)
                {
                    string endpoint = candidateEndpoints[activeEndpointIndex % candidateEndpoints.Length].TrimEnd('/');
                    activeLeaderEndpoint = endpoint;

                    bool summaryOk = false;
                    using (UnityWebRequest req = UnityWebRequest.Get(endpoint + "/api/swarm"))
                    {
                        req.timeout = 2;
                        yield return req.SendWebRequest();
                        if (req.result == UnityWebRequest.Result.Success)
                        {
                            summaryOk = true;
                            SwarmSummaryResponse summary = JsonUtility.FromJson<SwarmSummaryResponse>(req.downloadHandler.text);
                            if (summary != null)
                            {
                                LatestSwarmSummary = summary;
                                var sb = summary.swarm_summary;
                                activeLeaderId = (sb != null && sb.leader_robot_id > 0) ? sb.leader_robot_id : (summary.leader_id > 0 ? summary.leader_id : 1);
                                aliveNodesCount = (sb != null && sb.alive_nodes > 0) ? sb.alive_nodes : summary.alive_nodes;
                                versionSynced = (sb != null) ? sb.version_synced : summary.version_synced;
                                currentFormation = (sb != null && !string.IsNullOrEmpty(sb.formation)) ? sb.formation : (!string.IsNullOrEmpty(summary.formation) ? summary.formation : "relay_chain");
                                currentScenario = (sb != null && !string.IsNullOrEmpty(sb.scenario)) ? sb.scenario : (!string.IsNullOrEmpty(summary.scenario) ? summary.scenario : "normal");

                                if (summary.local_node != null && summary.local_node.robot_id > 0)
                                    NodesById[summary.local_node.robot_id] = summary.local_node;
                                if (summary.self != null && summary.self.robot_id > 0)
                                    NodesById[summary.self.robot_id] = summary.self;

                                if (summary.nodes != null)
                                {
                                    foreach (var node in summary.nodes)
                                    {
                                        if (node != null && node.robot_id > 0)
                                            NodesById[node.robot_id] = node;
                                    }
                                }
                                if (summary.peers != null)
                                {
                                    foreach (var peer in summary.peers)
                                    {
                                        if (peer != null && peer.robot_id > 0)
                                            NodesById[peer.robot_id] = peer;
                                    }
                                }
                                OnSwarmSummaryUpdated?.Invoke(summary);
                            }
                        }
                    }

                    if (!summaryOk)
                    {
                        // Automatic failover to next board in the fleet
                        activeEndpointIndex = (activeEndpointIndex + 1) % candidateEndpoints.Length;
                    }
                    else
                    {
                        using (UnityWebRequest mapReq = UnityWebRequest.Get(endpoint + "/api/swarm/map"))
                        {
                            mapReq.timeout = 2;
                            yield return mapReq.SendWebRequest();
                            if (mapReq.result == UnityWebRequest.Result.Success)
                            {
                                FusedMapResponse map = JsonUtility.FromJson<FusedMapResponse>(mapReq.downloadHandler.text);
                                if (map != null)
                                {
                                    LatestFusedMap = map;
                                    OnFusedMapUpdated?.Invoke(map);
                                }
                            }
                        }
                    }
                }

                yield return new WaitForSeconds(pollIntervalSeconds);
            }
        }

        public void SetSwarmFormation(string formation)
        {
            SendSwarmMission(new SwarmMissionRequest
            {
                action = "set_formation",
                command = "set_formation",
                formation = formation
            });
        }

        public void SetSwarmScenario(string scenario)
        {
            SendSwarmMission(new SwarmMissionRequest
            {
                action = "set_scenario",
                command = "set_scenario",
                scenario = scenario
            });
        }

        public void TriggerSwarmEstop()
        {
            SendSwarmMission(new SwarmMissionRequest
            {
                action = "estop",
                command = "estop"
            });
        }

        public void ResetSwarmEstop()
        {
            SendSwarmMission(new SwarmMissionRequest
            {
                action = "reset_estop",
                command = "reset_estop"
            });
        }

        public void DispatchWaypoint(int targetRobotId, float xMeters, float yMeters)
        {
            SendSwarmMission(new SwarmMissionRequest
            {
                action = "waypoint",
                command = "waypoint",
                robot_id = targetRobotId,
                target_robot_id = targetRobotId,
                x = xMeters * 100f,
                y = yMeters * 100f
            });
        }

        public void SendSwarmMission(SwarmMissionRequest request)
        {
            StartCoroutine(PostSwarmMissionCoroutine(request));
        }

        private IEnumerator PostSwarmMissionCoroutine(SwarmMissionRequest request)
        {
            string url = activeLeaderEndpoint.TrimEnd('/') + "/api/swarm/mission";
            byte[] body = Encoding.UTF8.GetBytes(request.ToJson());
            using (UnityWebRequest req = new UnityWebRequest(url, "POST"))
            {
                req.uploadHandler = new UploadHandlerRaw(body);
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
                req.timeout = 3;
                yield return req.SendWebRequest();
            }
        }
    }
}
