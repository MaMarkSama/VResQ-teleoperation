using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VResQ.Core;

namespace VResQ.Video
{
    [RequireComponent(typeof(VResqFrameworkConfig), typeof(VResqCameraReceiver), typeof(VResqSurroundDisplay))]
    public class VResqPresentationControls : MonoBehaviour
    {
        public Toggle backgroundToggle;
        public Toggle monitorToggle;
        public Slider brightnessSlider;
        public TextMeshProUGUI statusText;
        public Shader cameraShader;
        public Button resetDiagnostics;
        private Material material;
        private VResqFrameworkConfig config;
        private VResqCameraReceiver receiver;
        private float nextStatus;
        private Camera viewCamera;
        private CameraClearFlags originalClearFlags;
        private Color originalBackground;

        private void OnEnable()
        {
            config = GetComponent<VResqFrameworkConfig>();
            receiver = GetComponent<VResqCameraReceiver>();
            config.OnConfigChanged.AddListener(Apply);
            if (backgroundToggle != null) backgroundToggle.onValueChanged.AddListener(SetBackground);
            if (monitorToggle != null) monitorToggle.onValueChanged.AddListener(SetMonitor);
            if (brightnessSlider != null) brightnessSlider.onValueChanged.AddListener(SetBrightness);
            if (resetDiagnostics != null) resetDiagnostics.onClick.AddListener(receiver.ResetDiagnostics);
            Apply();
        }

        private void OnDisable()
        {
            if (config != null) config.OnConfigChanged.RemoveListener(Apply);
            if (backgroundToggle != null) backgroundToggle.onValueChanged.RemoveListener(SetBackground);
            if (monitorToggle != null) monitorToggle.onValueChanged.RemoveListener(SetMonitor);
            if (brightnessSlider != null) brightnessSlider.onValueChanged.RemoveListener(SetBrightness);
            if (resetDiagnostics != null) resetDiagnostics.onClick.RemoveListener(receiver.ResetDiagnostics);
            if (viewCamera != null)
            {
                viewCamera.clearFlags = originalClearFlags;
                viewCamera.backgroundColor = originalBackground;
            }
        }

        public void SetBackground(bool enabled)
        {
            config.Profile.backgroundVisible = enabled;
            if (enabled)
            {
                var surround = GetComponent<VResqSurroundDisplay>();
                surround.SetViewMode(surround.lastCameraSurroundMode);
            }
            config.NotifyConfigChanged();
        }
        public void SetMonitor(bool enabled) { config.Profile.robotMonitorVisible = enabled; config.NotifyConfigChanged(); }
        public void SetBrightness(float value) { config.Profile.cameraBrightness = value; config.NotifyConfigChanged(); }

        public void Apply()
        {
            if (config == null || receiver == null) return;
            var profile = config.Profile;
            if (viewCamera == null && Camera.main != null)
            {
                viewCamera = Camera.main;
                originalClearFlags = viewCamera.clearFlags;
                originalBackground = viewCamera.backgroundColor;
            }
            if (viewCamera != null)
            {
                viewCamera.clearFlags = profile.backgroundVisible ? originalClearFlags : CameraClearFlags.SolidColor;
                viewCamera.backgroundColor = profile.backgroundVisible ? originalBackground : Color.black;
            }
            if (backgroundToggle != null) backgroundToggle.SetIsOnWithoutNotify(profile.backgroundVisible);
            if (monitorToggle != null) monitorToggle.SetIsOnWithoutNotify(profile.robotMonitorVisible);
            if (brightnessSlider != null) brightnessSlider.SetValueWithoutNotify(profile.cameraBrightness);
            if (receiver.targetRawImage != null)
            {
                receiver.targetRawImage.enabled = profile.robotMonitorVisible;
                receiver.targetRawImage.color = Color.white;
                if (receiver.targetRawImage.texture == null) receiver.targetRawImage.texture = Texture2D.blackTexture;
                if (material == null && cameraShader != null) material = new Material(cameraShader);
                if (material != null)
                {
                    material.SetFloat("_Brightness", profile.cameraBrightness);
                    receiver.targetRawImage.material = material;
                }
            }
            GetComponent<VResqSurroundDisplay>().ApplyGeometryAndMode();
        }

        private void Update()
        {
            if (Time.unscaledTime < nextStatus || statusText == null) return;
            nextStatus = Time.unscaledTime + .2f;
            var frame = receiver.DisplayedMetadata;
            var client = GetComponent<VResqClient>();
            string quality = !string.IsNullOrEmpty(receiver.LastDataError) ? receiver.LastDataError :
                client != null && !string.IsNullOrEmpty(client.LastTelemetryError) ? client.LastTelemetryError : "OK";
            statusText.text = $"{receiver.FrameSyncStatus} | {quality} | Rejected {receiver.RejectedFrames}\n" +
                (frame == null ? "Frame --" : $"{frame.frame_id} | {frame.timestamp_source}");
        }

        private void OnDestroy()
        {
            if (material != null) Destroy(material);
        }
    }
}
