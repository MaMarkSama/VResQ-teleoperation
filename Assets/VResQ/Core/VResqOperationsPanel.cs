using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using VResQ.Video;

namespace VResQ.Core
{
    public class VResqOperationsPanel : MonoBehaviour
    {
        public const string BuildVersion = "2026.11.16.2";
        public GameObject panel;
        public GameObject[] liveSections;
        public Button[] tabs;
        public TextMeshProUGUI heading;
        public TextMeshProUGUI summary;
        public TextMeshProUGUI status;
        public TextMeshProUGUI body;
        public ScrollRect scroll;
        public Button refreshButton;
        public Button exportButton;
        public Button pageUpButton;
        public Button pageDownButton;
        public Button restartXrButton;
        public GameObject controlsPanel, swarmTools;
        public TMP_Dropdown nodeSelector;
        public Button connectNodeButton, scanButton, rawButton;
        public VResqSwarmPlot swarmPlot;
        private readonly List<string> nodeHosts = new List<string>();
        private readonly List<int> nodePorts = new List<int>();
        private string nodeSignature = "";
        private bool rawView;
        private JToken currentValue;
        public string ActiveTab { get; private set; } = "Live";
        public string LastPayload { get; private set; }
        private UnityWebRequest request;
        private bool fetching;
        private int generation;
        private float nextPoll;
        private float receivedAt = -1;
        private string source = "";
        private string failure = "";
        private readonly Dictionary<GameObject, bool> liveVisibility = new Dictionary<GameObject, bool>();
        private readonly string[] names = { "Live", "Telemetry", "Protocol", "Swarm", "System", "Controls" };

        private void Start()
        {
            for (int i = 0; i < tabs.Length; i++)
            {
                string tab = names[i];
                tabs[i].onClick.AddListener(() => Show(tab));
            }
            refreshButton.onClick.AddListener(() => nextPoll = 0);
            exportButton.onClick.AddListener(Export);
            pageUpButton.onClick.AddListener(() => ScrollPage(1));
            pageDownButton.onClick.AddListener(() => ScrollPage(-1));
            restartXrButton.onClick.AddListener(RestartXr);
            rawButton.onClick.AddListener(() => SetRawView(!rawView));
            connectNodeButton.onClick.AddListener(ConnectSelectedNode);
            scanButton.onClick.AddListener(() => { GetComponent<VResqAutoDiscovery>()?.RequestRescan(); nextPoll = 0; });
            Show("Live");
            Debug.Log("[VRESQ_BUILD] " + BuildVersion);
        }

        public void Show(string tab)
        {
            if (Array.IndexOf(names, tab) < 0) return;
            if (tab != "Live" && Application.isPlaying) GetComponent<VResQ.Control.VResqTeleopController>()?.SetDriveArmed(false);
            generation++;
            request?.Abort();
            fetching = false;
            if (ActiveTab == "Live" && tab != "Live")
            {
                liveVisibility.Clear();
                foreach (var section in liveSections)
                    if (section != null) liveVisibility[section] = section.activeSelf;
            }
            ActiveTab = tab;
            panel.SetActive(tab != "Live");
            restartXrButton.gameObject.SetActive(tab == "System");
            rawButton.gameObject.SetActive(tab == "Protocol" || tab == "Swarm");
            nodeHosts.Clear(); nodePorts.Clear(); nodeSignature = "<unloaded>";
            nodeSelector.ClearOptions();
            nodeSelector.AddOptions(new List<string> { "Waiting for discovery" });
            connectNodeButton.interactable = false;
            swarmPlot.SetSnapshot(null);
            controlsPanel.SetActive(tab == "Controls");
            swarmTools.SetActive(tab == "Swarm");
            swarmPlot.gameObject.SetActive(tab == "Swarm");
            scroll.viewport.gameObject.SetActive(tab != "Controls");
            summary.gameObject.SetActive(tab != "Controls");
            status.gameObject.SetActive(tab != "Controls");
            foreach (var button in new[] { refreshButton, exportButton, pageUpButton, pageDownButton }) button.gameObject.SetActive(tab != "Controls");
            summary.rectTransform.sizeDelta = new Vector2(tab == "Swarm" ? 800 : 1610, 92);
            summary.rectTransform.anchoredPosition = new Vector2(tab == "Swarm" ? -400 : 0, 350);
            foreach (var section in liveSections)
                if (section != null && (tab != "Live" || liveVisibility.ContainsKey(section)))
                    section.SetActive(tab == "Live" && liveVisibility[section]);
            var hud = GetComponent<RescueRobotVRHUD>();
            if (hud.settingsModalRoot != null) hud.settingsModalRoot.SetActive(false);
            heading.text = tab;
            summary.text = "Waiting for response";
            body.text = "";
            LastPayload = null;
            currentValue = null;
            receivedAt = -1;
            failure = "";
            scroll.verticalNormalizedPosition = 1;
            nextPoll = 0;
            if (tab == "System") ApplyPayload("{\"status\":\"Awaiting robot response\"}");
            for (int i = 0; i < tabs.Length; i++)
                tabs[i].image.color = names[i] == tab ? new Color(0, .45f, .4f) : new Color(.05f, .12f, .17f);
        }

        private void Update()
        {
            if (ActiveTab == "Live" || ActiveTab == "Controls") return;
            var client = GetComponent<VResqClient>();
            string endpoint = client.robotIp + ":" + client.port;
            if (source != endpoint)
            {
                source = endpoint;
                Show(ActiveTab);
            }
            status.text = $"{source}  |  {(receivedAt < 0 ? "No payload" : $"Age {Time.unscaledTime - receivedAt:F1}s")}  |  {failure}  |  Build {BuildVersion}";
            if (!fetching && Time.unscaledTime >= nextPoll)
            {
                nextPoll = Time.unscaledTime + 1;
                StartCoroutine(Fetch(client.robotIp, client.port, generation));
            }
        }

        private IEnumerator Fetch(string host, int port, int currentGeneration)
        {
            fetching = true;
            string path = ActiveTab == "Telemetry" ? "/api/state" : ActiveTab == "Protocol" ? "/api/system" : "/api/" + ActiveTab.ToLowerInvariant();
            Uri uri;
            try { uri = new UriBuilder("http", host, port, path).Uri; }
            catch (Exception error) { failure = error.Message; fetching = false; yield break; }
            using (var download = UnityWebRequest.Get(uri))
            {
                request = download;
                download.timeout = 3;
                string token = Environment.GetEnvironmentVariable("VRESQ_OPERATOR_TOKEN");
                if (!string.IsNullOrEmpty(token)) download.SetRequestHeader("X-VResQ-Token", token);
                yield return download.SendWebRequest();
                if (currentGeneration != generation) yield break;
                request = null;
                if (download.result != UnityWebRequest.Result.Success)
                {
                    if (ActiveTab == "System") ApplyPayload(new JObject { ["reachable"] = false, ["error"] = download.error }.ToString());
                    failure = "STALE / " + download.error;
                    nextPoll = Time.unscaledTime + 2;
                    fetching = false;
                    yield break;
                }
                if (download.downloadedBytes > 2 * 1024 * 1024)
                {
                    failure = "Payload exceeds 2 MB";
                    fetching = false;
                    yield break;
                }
                JToken payload = null;
                try { payload = JObject.Parse(download.downloadHandler.text); }
                catch (JsonException error) { failure = "Invalid JSON: " + error.Message; }
                if (ActiveTab == "Swarm" && payload is JObject swarm)
                {
                    foreach (string extra in new[] { "map", "timeline" })
                    {
                        using (var detail = UnityWebRequest.Get(new UriBuilder("http", host, port, "/api/swarm/" + extra).Uri))
                        {
                            request = detail;
                            detail.timeout = 2;
                            if (!string.IsNullOrEmpty(token)) detail.SetRequestHeader("X-VResQ-Token", token);
                            yield return detail.SendWebRequest();
                            if (currentGeneration != generation) yield break;
                            request = null;
                            try { swarm["_" + extra] = detail.result == UnityWebRequest.Result.Success && detail.downloadedBytes <= 2 * 1024 * 1024 ? JToken.Parse(detail.downloadHandler.text) : new JObject { ["error"] = detail.error ?? "Response exceeds limit" }; }
                            catch (JsonException error) { swarm["_" + extra] = new JObject { ["error"] = error.Message }; }
                        }
                    }
                }
                try { if (payload != null) ApplyPayload(payload.ToString()); }
                catch (Exception error) when (error is JsonException || error is ArgumentException || error is InvalidOperationException || error is FormatException)
                { failure = "Invalid response schema: " + error.Message; }
                fetching = false;
                nextPoll = Time.unscaledTime + 1;
            }
        }

        public void ScrollPage(float direction)
        {
            float range = Mathf.Max(1, body.rectTransform.rect.height - scroll.viewport.rect.height);
            scroll.verticalNormalizedPosition = Mathf.Clamp01(scroll.verticalNormalizedPosition + direction * scroll.viewport.rect.height * .8f / range);
        }

        public void ApplyPayload(string json)
        {
            var value = JToken.Parse(json);
            if (!(value is JObject)) throw new JsonException("Expected a JSON object");
            if (ActiveTab == "System")
                value = new JObject { ["operator"] = OperatorDiagnostics(), ["robot"] = value };
            LastPayload = value.ToString(Formatting.Indented);
            currentValue = value;
            SetRawView(rawView);
            receivedAt = Time.unscaledTime;
            failure = "LIVE";
            if (ActiveTab == "Protocol")
            {
                var serial = value["serial"] ?? value;
                summary.text = $"{Read(serial, "protocol.name")}  |  TX {Read(serial, "traffic.tx_frames")}  RX {Read(serial, "traffic.rx_frames")}\nCRC checked {Read(serial, "integrity.crc_checked")}  Errors {Read(serial, "integrity.crc_errors")}  |  Telemetry age {Read(serial, "latency.telemetry_age_ms")} ms";
            }
            else if (ActiveTab == "Swarm")
            {
                summary.text = $"Alive {Read(value, "swarm_summary.alive_nodes")} / {Read(value, "swarm_summary.total_nodes")}  |  Leader {Read(value, "swarm_summary.leader_name")}\nSynced {Read(value, "swarm_summary.version_synced")}  |  {Read(value, "swarm_summary.formation")}";
                UpdateNodes(value);
                swarmPlot.SetSnapshot(value["_map"]);
            }
            else if (ActiveTab == "Telemetry")
                summary.text = $"Robot {Read(value, "identity.node_name")}  |  ESP32 {Read(value, "esp32_online")}  |  E-stop {Read(value, "estop")}\nRX {Read(value, "telemetry.rx_ok")}  CRC errors {Read(value, "telemetry.crc_errors")}  FSM {Read(value, "telemetry.fsm_state")}";
            else summary.text = $"XR active {Read(value, "operator.xr_device_active")}\n{Read(value, "operator.passthrough")}";
        }

        private static string Read(JToken value, string path) { return value.SelectToken(path)?.ToString() ?? "--"; }

        public void SetRawView(bool raw)
        {
            rawView = raw;
            body.richText = !raw && (ActiveTab == "Protocol" || ActiveTab == "Swarm");
            body.text = currentValue == null ? "" : body.richText ? (ActiveTab == "Protocol" ? VResqDashboardReport.Protocol(currentValue) : VResqDashboardReport.Swarm(currentValue)) : LastPayload;
            rawButton.GetComponentInChildren<TextMeshProUGUI>().text = raw ? "Overview" : "Raw JSON";
            body.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(720, body.GetPreferredValues(body.text, body.rectTransform.rect.width, Mathf.Infinity).y + 30));
        }

        private void UpdateNodes(JToken value)
        {
            var nodes = value["nodes"] as JArray ?? value["discovered"] as JArray ?? new JArray();
            var labels = new List<string>(); var hosts = new List<string>(); var ports = new List<int>();
            foreach (var node in nodes)
            {
                if (!(node is JObject)) continue;
                string host = (string)node["ip"] ?? (string)(node["ips"] as JArray)?.First;
                if ((bool?)node["is_self"] == true && System.Net.IPAddress.TryParse(host, out var selfAddress) && System.Net.IPAddress.IsLoopback(selfAddress)) host = GetComponent<VResqClient>().robotIp;
                int port = 8000;
                if (node["port"] != null && !int.TryParse(node["port"].ToString(), out port)) continue;
                if (!System.Net.IPAddress.TryParse(host, out _) || port < 1 || port > 65535) continue;
                hosts.Add(host); ports.Add(port);
                labels.Add(((string)node["node_name"] ?? (string)node["name"] ?? host) + "  " + host + ":" + port);
            }
            string signature = string.Join("|", labels);
            if (signature == nodeSignature) return;
            string selected = nodeSelector.value < nodeHosts.Count ? nodeHosts[nodeSelector.value] : "";
            nodeSignature = signature;
            nodeHosts.Clear(); nodeHosts.AddRange(hosts); nodePorts.Clear(); nodePorts.AddRange(ports);
            nodeSelector.ClearOptions();
            nodeSelector.AddOptions(labels.Count > 0 ? labels : new List<string> { "No address advertised" });
            nodeSelector.SetValueWithoutNotify(Mathf.Max(0, hosts.IndexOf(selected)));
            connectNodeButton.interactable = hosts.Count > 0;
        }

        public void ConnectSelectedNode()
        {
            int index = nodeSelector.value;
            if (index < 0 || index >= nodeHosts.Count) return;
            GetComponent<VResQ.Control.VResqTeleopController>()?.SetDriveArmed(false);
            GetComponent<VResqClient>().SwitchRobot(nodeHosts[index], nodePorts[index]);
            Show("Live");
        }

        private void RestartXr()
        {
            var config = GetComponent<VResqFrameworkConfig>();
            if (config != null)
            {
                config.Profile.headsetPassthrough = true;
                config.NotifyConfigChanged();
            }
            Camera.main?.GetComponent<VResqHeadsetPassthrough>()?.RestartSession();
        }

        private JObject OperatorDiagnostics()
        {
            var headset = Camera.main != null ? Camera.main.GetComponent<VResqHeadsetPassthrough>() : null;
            return new JObject
            {
                ["build"] = BuildVersion,
                ["xr_device_active"] = UnityEngine.XR.XRSettings.isDeviceActive,
                ["xr_runtime"] = UnityEngine.XR.XRSettings.loadedDeviceName,
                ["passthrough"] = headset != null ? headset.Status : "Headset controller missing",
                ["xr_diagnostics"] = headset != null ? headset.Diagnostics() : null,
                ["camera_alpha"] = Camera.main != null ? Camera.main.backgroundColor.a : -1,
                ["telemetry_validation"] = GetComponent<VResqClient>().LastTelemetryError,
                ["frame_id"] = GetComponent<VResqCameraReceiver>().DisplayedMetadata?.frame_id,
                ["camera_error"] = GetComponent<VResqCameraReceiver>().LastDataError
            };
        }

        public void Export()
        {
            if (string.IsNullOrEmpty(LastPayload)) { failure = "No payload to save"; return; }
            try
            {
                string directory = Path.Combine(Application.persistentDataPath, "diagnostics");
                Directory.CreateDirectory(directory);
                string file = Path.Combine(directory, ActiveTab + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json");
                File.WriteAllText(file, LastPayload);
                failure = "Saved " + file;
            }
            catch (IOException error) { failure = error.Message; }
            catch (UnauthorizedAccessException error) { failure = error.Message; }
        }

        private void OnDisable()
        {
            generation++;
            request?.Abort();
            StopAllCoroutines();
            fetching = false;
        }
    }
}
