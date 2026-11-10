using UnityEngine;
using UnityEngine.UI;
using VResQ.Core;

namespace VResQ.UI
{
    /// <summary>
    /// Formats live USAR environmental telemetry onto a World-Space VR Cockpit Dashboard or Canvas in Unity.
    /// </summary>
    public class VResqHudOverlay : MonoBehaviour
    {
        [Header("UI Text Displays")]
        public Text statusText;
        public Text distanceText;
        public Text attitudeText;
        public Text atmosphereText;
        public Text altitudeText;
        public Text survivabilityText;
        public Text victimCountText;

        [Header("Warning Elements")]
        public GameObject collisionAlertBadge;

        private void Start()
        {
            if (VResqClient.Instance != null)
            {
                VResqClient.Instance.OnConnected.AddListener(OnConnected);
                VResqClient.Instance.OnDisconnected.AddListener(OnDisconnected);
                VResqClient.Instance.OnStateReceived.AddListener(OnStateReceived);
            }
        }

        private void OnDestroy()
        {
            if (VResqClient.Instance != null)
            {
                VResqClient.Instance.OnConnected.RemoveListener(OnConnected);
                VResqClient.Instance.OnDisconnected.RemoveListener(OnDisconnected);
                VResqClient.Instance.OnStateReceived.RemoveListener(OnStateReceived);
            }
        }

        private void OnConnected()
        {
            if (statusText != null)
            {
                statusText.text = "LINK: ONLINE (20Hz)";
                statusText.color = Color.green;
            }
        }

        private void OnDisconnected()
        {
            if (statusText != null)
            {
                statusText.text = "LINK: OFFLINE";
                statusText.color = Color.red;
            }
        }

        private void OnStateReceived(RobotStateSnapshot state)
        {
            if (state == null) return;
            var t = state.telemetry;
            var an = state.analytics;

            // Distance
            if (distanceText != null && t != null)
            {
                float dist = t.ultrasonic_cm;
                distanceText.text = dist > 0 ? $"{dist:F0} CM" : "-- CM";
                distanceText.color = dist > 0 && dist < 20 ? Color.red : (dist < 50 ? Color.yellow : Color.cyan);
            }

            // Attitude
            if (attitudeText != null && t != null)
            {
                attitudeText.text = $"P: {t.pitch_deg:F0}° | R: {t.roll_deg:F0}° | HDG: {t.yaw_deg:F0}°";
            }

            // Atmosphere (Temp & Humidity)
            if (atmosphereText != null && t != null)
            {
                atmosphereText.text = $"{t.temp_c:F1}°C | {t.humidity:F0}% RH";
            }

            // Altitude
            if (altitudeText != null && t != null)
            {
                altitudeText.text = $"ALT: {t.altitude_m:F1} m";
            }

            // Survivability
            if (survivabilityText != null && an != null && an.survivability != null)
            {
                survivabilityText.text = $"SURVIVABILITY: {an.survivability.score}% ({an.survivability.level})";
            }

            // Victims / Survivors
            if (victimCountText != null)
            {
                int count = state.ai_detections != null ? state.ai_detections.Length : 0;
                victimCountText.text = $"TARGETS: {count}";
            }

            // Collision Alert
            if (collisionAlertBadge != null && t != null)
            {
                float dist = t.ultrasonic_cm;
                collisionAlertBadge.SetActive(dist > 0 && dist < 20f);
            }
        }
    }
}
