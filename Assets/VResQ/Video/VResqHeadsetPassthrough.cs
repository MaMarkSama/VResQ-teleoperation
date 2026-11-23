using System.Collections;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using Newtonsoft.Json.Linq;
using Unity.XR.CompositionLayers;
using Unity.XR.CompositionLayers.Layers;

namespace VResQ.Video
{
    [RequireComponent(typeof(Camera), typeof(ARCameraManager))]
    public class VResqHeadsetPassthrough : MonoBehaviour
    {
        private bool requested = true;
        private Coroutine initialization;
        private string lastStatus;
        private float nextLog;
        private float nextLoaderRetry;
        private Canvas isolatedCanvas;
        public bool Isolated => isolatedCanvas != null && !isolatedCanvas.enabled;
        public void ToggleIsolation(Canvas canvas)
        {
            if (Isolated) RestoreHud();
            else { isolatedCanvas = canvas; canvas.enabled = false; }
        }
        public void RestoreHud() { if (isolatedCanvas != null) isolatedCanvas.enabled = true; isolatedCanvas = null; }

        public JObject Diagnostics()
        {
            var manager = XRGeneralSettings.Instance != null ? XRGeneralSettings.Instance.Manager : null;
            var cameraManager = GetComponent<ARCameraManager>();
            var features = new JArray();
            var layers = new JArray();
            foreach (var layer in FindObjectsByType<CompositionLayer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                layers.Add(new JObject { ["name"] = layer.name, ["active"] = layer.isActiveAndEnabled, ["order"] = layer.Order,
                    ["type"] = layer.LayerData?.GetType().Name, ["blend"] = layer.LayerData?.BlendType.ToString() });
            if (OpenXRSettings.Instance != null)
                foreach (var feature in OpenXRSettings.Instance.GetFeatures<UnityEngine.XR.OpenXR.Features.OpenXRFeature>())
                    if (feature != null) features.Add(new JObject { ["name"] = feature.GetType().Name, ["enabled"] = feature.enabled });
            return new JObject
            {
                ["requested"] = requested,
                ["status"] = Status,
                ["loader"] = manager?.activeLoader != null ? manager.activeLoader.GetType().Name : "none",
                ["runtime_name"] = OpenXRRuntime.name,
                ["runtime_version"] = OpenXRRuntime.version.ToString(),
                ["passthrough_extension"] = OpenXRRuntime.IsExtensionEnabled("XR_FB_passthrough"),
                ["ar_session_state"] = ARSession.state.ToString(),
                ["camera_manager_enabled"] = cameraManager.enabled,
                ["camera_subsystem"] = cameraManager.subsystem?.subsystemDescriptor.id ?? "none",
                ["camera_subsystem_running"] = cameraManager.subsystem != null && cameraManager.subsystem.running,
                ["camera_clear"] = GetComponent<Camera>().clearFlags.ToString(),
                ["camera_alpha"] = GetComponent<Camera>().backgroundColor.a,
                ["features"] = features,
                ["composition_layers"] = layers,
                ["headset_only_test"] = Isolated,
                ["log_file"] = Application.consoleLogPath
            };
        }

        public string Status
        {
            get
            {
                if (!requested) return "Headset view OFF";
                if (initialization != null) return "Initializing Quest Link XR";
                var manager = XRGeneralSettings.Instance != null ? XRGeneralSettings.Instance.Manager : null;
                if (manager == null || manager.activeLoader == null) return "Link headset unavailable - reconnect Link and toggle Headset view";
                if (!OpenXRRuntime.IsExtensionEnabled("XR_FB_passthrough")) return $"Runtime: {OpenXRRuntime.name}; XR_FB_passthrough unavailable. Select Meta Horizon Link runtime and enable passthrough on this PC.";
                var cameraManager = GetComponent<ARCameraManager>();
                if (cameraManager.subsystem == null) return "Passthrough camera subsystem missing";
                if (!cameraManager.subsystem.running) return "Passthrough camera subsystem stopped";
                return "Passthrough subsystem running";
            }
        }

        public void Request(bool value)
        {
            bool changed = requested != value;
            requested = value;
            var camera = GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = value ? Color.clear : Color.black;
            camera.allowHDR = false;
            camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;
            GetComponent<ARCameraManager>().enabled = value;
            if (changed && value) RetryInitialization();
        }

        private IEnumerator Start()
        {
            // XR manual initialization requires graphics initialization to have completed.
            yield return null;
            Request(requested);
            if (requested) RetryInitialization();
        }

        public void RetryInitialization()
        {
            if (!Application.isPlaying || !requested || initialization != null) return;
            var manager = XRGeneralSettings.Instance != null ? XRGeneralSettings.Instance.Manager : null;
            if (manager != null && manager.activeLoader == null)
                initialization = StartCoroutine(InitializeAfterLinkConnects(manager));
        }

        public void RestartSession()
        {
            if (!Application.isPlaying || initialization != null) return;
            requested = true;
            var manager = XRGeneralSettings.Instance != null ? XRGeneralSettings.Instance.Manager : null;
            if (manager != null) initialization = StartCoroutine(InitializeAfterLinkConnects(manager, true));
        }

        private IEnumerator InitializeAfterLinkConnects(XRManagerSettings manager, bool restart = false)
        {
            if (restart)
            {
                GetComponent<ARCameraManager>().enabled = false;
                var previousSession = FindFirstObjectByType<ARSession>();
                if (previousSession != null) previousSession.enabled = false;
                manager.StopSubsystems();
                manager.DeinitializeLoader();
            }
            yield return null;
            if (manager.activeLoader == null) yield return manager.InitializeLoader();
            if (manager.activeLoader != null)
            {
                manager.StartSubsystems();
                var session = FindFirstObjectByType<ARSession>();
                if (session != null)
                {
                    session.enabled = false;
                    session.enabled = true;
                }
                var cameraManager = GetComponent<ARCameraManager>();
                cameraManager.enabled = false;
                cameraManager.enabled = requested;
            }
            initialization = null;
        }

        private void Update()
        {
            if (Time.unscaledTime < nextLog) return;
            nextLog = Time.unscaledTime + 1;
            if (requested && initialization == null && Time.unscaledTime >= nextLoaderRetry)
            {
                nextLoaderRetry = Time.unscaledTime + 8f;
                RetryInitialization();
            }
            // Restore alpha blending after a late XR session or composition-layer restart.
            if (requested && XRSettings.isDeviceActive)
                foreach (var layer in FindObjectsByType<CompositionLayer>(FindObjectsSortMode.None))
                    if (layer.LayerData is DefaultLayerData && layer.LayerData.BlendType != BlendType.Premultiply)
                        layer.LayerData.BlendType = BlendType.Premultiply;
            string state = Status;
            if (state == lastStatus) return;
            lastStatus = state;
            Debug.Log($"[VRESQ_PASSTHROUGH] {state}; XRDeviceActive={XRSettings.isDeviceActive}; ARSession={ARSession.state}; Alpha={GetComponent<Camera>().backgroundColor.a}");
        }

        private void OnDisable()
        {
            if (initialization != null) StopCoroutine(initialization);
            initialization = null;
        }
    }
}
