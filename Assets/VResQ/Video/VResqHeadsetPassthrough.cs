using System.Collections;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using Newtonsoft.Json.Linq;

namespace VResQ.Video
{
    [RequireComponent(typeof(Camera), typeof(ARCameraManager))]
    public class VResqHeadsetPassthrough : MonoBehaviour
    {
        private bool requested = true;
        private Coroutine initialization;
        private string lastStatus;
        private float nextLog;

        public JObject Diagnostics()
        {
            var manager = XRGeneralSettings.Instance != null ? XRGeneralSettings.Instance.Manager : null;
            var cameraManager = GetComponent<ARCameraManager>();
            var features = new JArray();
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
                if (!OpenXRRuntime.IsExtensionEnabled("XR_FB_passthrough")) return "Link passthrough unavailable - check Developer settings";
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
