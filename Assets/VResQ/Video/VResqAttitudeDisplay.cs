using TMPro;
using UnityEngine;
using VResQ.Core;

namespace VResQ.Video
{
    [DefaultExecutionOrder(200)]
    public class VResqAttitudeDisplay : MonoBehaviour
    {
        public RectTransform horizon;
        public CanvasGroup horizonVisibility;
        public TextMeshProUGUI stateLabel;
        public TextMeshProUGUI pitchLabel;
        public TextMeshProUGUI rollLabel;
        public TextMeshProUGUI yawLabel;
        private float pitch;
        private float roll;
        private bool initialized;

        private void LateUpdate()
        {
            var client = GetComponentInParent<VResqClient>();
            if (client == null) return;
            var state = client.LatestState;
            var receiver = client.GetComponent<VResqCameraReceiver>();
            if (receiver != null && receiver.HasReceivedLiveFrame && receiver.DisplayedSourceIp == client.robotIp &&
                receiver.DisplayedMetadata?.telemetry != null) state = receiver.DisplayedMetadata.telemetry;
            bool valid = state != null && state.telemetry != null && state.telemetry.sensors != null &&
                state.telemetry.sensors.mpu6050 && state.last_telem_age_ms < 1200 &&
                string.IsNullOrEmpty(VResqTelemetryQuality.Validate(state.telemetry));
            Render(state != null ? state.telemetry : null, valid, Time.unscaledDeltaTime);
        }

        public void Render(TelemetryData data, bool valid, float deltaTime)
        {
            valid = valid && data != null && string.IsNullOrEmpty(VResqTelemetryQuality.Validate(data));
            horizonVisibility.alpha = valid ? 1 : .25f;
            stateLabel.text = valid ? "IMU LIVE" : "IMU UNAVAILABLE";
            stateLabel.color = valid ? new Color(.3f, 1f, .8f) : new Color(1f, .75f, .3f);
            if (!valid)
            {
                pitchLabel.text = "PITCH --";
                rollLabel.text = "ROLL --";
                yawLabel.text = "YAW --";
                initialized = false;
                return;
            }
            float blend = initialized ? 1 - Mathf.Exp(-12 * Mathf.Max(0, deltaTime)) : 1;
            pitch = Mathf.Lerp(pitch, data.pitch_deg, blend);
            roll = Mathf.LerpAngle(roll, data.roll_deg, blend);
            initialized = true;
            horizon.localRotation = Quaternion.Euler(0, 0, -roll);
            horizon.anchoredPosition = new Vector2(0, -Mathf.Clamp(pitch, -90, 90) * 1.8f);
            pitchLabel.text = $"PITCH {data.pitch_deg:+0.0;-0.0;0.0} deg";
            rollLabel.text = $"ROLL {data.roll_deg:+0.0;-0.0;0.0} deg";
            yawLabel.text = $"YAW {Mathf.Repeat(data.yaw_deg, 360):000.0} deg";
        }
    }
}
