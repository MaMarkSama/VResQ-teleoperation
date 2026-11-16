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
        public const string BuildVersion = "2026.11.16.1";
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
        private readonly string[] names = { "Live", "Telemetry", "Protocol", "Swarm", "System" };

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
            Show("Live");
            Debug.Log("[VRESQ_BUILD] " + BuildVersion);
        }

        public void Show(string tab)
        {
            if (Array.IndexOf(names, tab) < 0) return;
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
            foreach (var section in liveSections)
                if (section != null && (tab != "Live" || liveVisibility.ContainsKey(section)))
                    section.SetActive(tab == "Live" && liveVisibility[section]);
            var hud = GetComponent<RescueRobotVRHUD>();
            if (hud.settingsModalRoot != null) hud.settingsModalRoot.SetActive(false);
            heading.text = tab;
            summary.text = "Waiting for response";
            body.text = "";
            LastPayload = null;
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
            if (ActiveTab == "Live") return;
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
            string path = ActiveTab == "Telemetry" ? "/api/state" : "/api/" + ActiveTab.ToLowerInvariant();
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
                fetching = false;
                request = null;
                if (download.result != UnityWebRequest.Result.Success)
                {
                    if (ActiveTab == "System") ApplyPayload(new JObject { ["reachable"] = false, ["error"] = download.error }.ToString());
                    failure = "STALE / " + download.error;
                    nextPoll = Time.unscaledTime + 2;
                    yield break;
                }
                if (download.downloadedBytes > 2 * 1024 * 1024)
                {
                    failure = "Payload exceeds 2 MB";
                    yield break;
                }
                try { ApplyPayload(download.downloadHandler.text); }
                catch (JsonException error) { failure = "Invalid JSON: " + error.Message; }
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
            if (ActiveTab == "System")
                value = new JObject { ["operator"] = OperatorDiagnostics(), ["robot"] = value };
            LastPayload = value.ToString(Formatting.Indented);
            body.text = LastPayload;
            body.richText = false;
            body.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,
                Mathf.Max(720, body.GetPreferredValues(LastPayload, body.rectTransform.rect.width, Mathf.Infinity).y + 30));
            receivedAt = Time.unscaledTime;
            failure = "LIVE";
            if (ActiveTab == "Protocol")
                summary.text = $"{Read(value, "protocol.name")}  |  TX {Read(value, "traffic.tx_frames")}  RX {Read(value, "traffic.rx_frames")}\nCRC checked {Read(value, "integrity.crc_checked")}  Errors {Read(value, "integrity.crc_errors")}  |  Telemetry age {Read(value, "latency.telemetry_age_ms")} ms";
            else if (ActiveTab == "Swarm")
                summary.text = $"Build {Read(value, "build_version")}  |  Alive {Read(value, "swarm_summary.alive_nodes")}\nVersion synced {Read(value, "swarm_summary.version_synced")}  |  Formation {Read(value, "follow_leader.formation")}";
            else if (ActiveTab == "Telemetry")
                summary.text = $"Robot {Read(value, "identity.node_name")}  |  ESP32 {Read(value, "esp32_online")}  |  E-stop {Read(value, "estop")}\nRX {Read(value, "telemetry.rx_ok")}  CRC errors {Read(value, "telemetry.crc_errors")}  FSM {Read(value, "telemetry.fsm_state")}";
            else summary.text = $"XR active {Read(value, "operator.xr_device_active")}\n{Read(value, "operator.passthrough")}";
        }

        private static string Read(JToken value, string path) { return value.SelectToken(path)?.ToString() ?? "--"; }

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
