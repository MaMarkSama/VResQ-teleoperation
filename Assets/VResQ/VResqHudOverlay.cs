using UnityEngine;
using UnityEngine.UI;
using VResQ.Core;

namespace VResQ.UI
{
    /// <summary>
    /// Formats live USAR environmental & edge-board telemetry onto a World-Space VR Cockpit Dashboard or Canvas in Unity.
    /// Automatically binds UI Text components by name if not dragged into the Inspector, and seamlessly
    /// switches between ESP32 sensor telemetry (BME280/MPU6050/Ultrasonic) and real Linux Edge Host telemetry
    /// (Board/CPU Temp, RAM, Disk, Odometry Heading, Motor PWM) in LIVE-HW mode.
    /// </summary>
    public class VResqHudOverlay : MonoBehaviour
    {
        [Header("UI Text Displays (Auto-Bound if left empty)")]
        public Text statusText;
        public Text distanceText;
        public Text attitudeText;
        public Text atmosphereText;
        public Text altitudeText;
        public Text survivabilityText;
        public Text victimCountText;

        [Header("Warning Elements")]
        public GameObject collisionAlertBadge;

        private static readonly Color ColorOnline = new Color(0.13f, 0.77f, 0.37f, 1f);
        private static readonly Color ColorCyan = new Color(0.22f, 0.74f, 0.97f, 1f);
        private static readonly Color ColorWarn = new Color(0.96f, 0.62f, 0.04f, 1f);
        private static readonly Color ColorDanger = new Color(0.94f, 0.27f, 0.27f, 1f);
        private static readonly Color ColorStandby = new Color(0.65f, 0.74f, 0.84f, 1f);

        private void Awake()
        {
            AutoBindHudElements();
        }

        private void Start()
        {
            AutoBindHudElements();
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

        [ContextMenu("Auto-Bind HUD Elements")]
        public void AutoBindHudElements()
        {
            Text[] allTexts = GetComponentsInChildren<Text>(true);
            if (allTexts == null || allTexts.Length == 0)
            {
                allTexts = FindObjectsByType<Text>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            }

            foreach (var txt in allTexts)
            {
                if (txt == null) continue;
                string n = txt.gameObject.name.ToLowerInvariant();
                if (statusText == null && (n.Contains("status") || n.Contains("link") || n.Contains("header")))
                    statusText = txt;
                else if (distanceText == null && (n.Contains("dist") || n.Contains("ultra") || n.Contains("sonar")))
                    distanceText = txt;
                else if (attitudeText == null && (n.Contains("att") || n.Contains("imu") || n.Contains("pitch") || n.Contains("hdg") || n.Contains("heading")))
                    attitudeText = txt;
                else if (atmosphereText == null && (n.Contains("atmo") || n.Contains("temp") || n.Contains("env") || n.Contains("bme")))
                    atmosphereText = txt;
                else if (altitudeText == null && (n.Contains("alt") || n.Contains("floor") || n.Contains("sys")))
                    altitudeText = txt;
                else if (survivabilityText == null && (n.Contains("surv") || n.Contains("health") || n.Contains("esp")))
                    survivabilityText = txt;
                else if (victimCountText == null && (n.Contains("victim") || n.Contains("target") || n.Contains("ai") || n.Contains("det")))
                    victimCountText = txt;
            }
        }

        private void OnConnected()
        {
            if (statusText != null)
            {
                string ip = VResqClient.Instance != null ? VResqClient.Instance.robotIp : "";
                statusText.text = $"● LINK: ONLINE ({ip})";
                statusText.color = ColorOnline;
            }
        }

        private void OnDisconnected()
        {
            if (statusText != null)
            {
                statusText.text = "■ LINK: OFFLINE (RECONNECTING...)";
                statusText.color = ColorDanger;
            }
        }

        private void OnStateReceived(RobotStateSnapshot state)
        {
            if (state == null) return;
            var t = state.telemetry;
            var an = state.analytics;
            var host = state.host;
            var sStatus = state.sensor_status;

            bool espOnline = state.esp32_online || (sStatus != null && sStatus.esp32_online);
            bool hasUltra = espOnline && t != null && t.sensors != null && t.sensors.ultrasonic && t.ultrasonic_cm > 0.5f;
            bool hasBme = espOnline && t != null && t.sensors != null && t.sensors.bme280;
            bool hasMpu = espOnline && t != null && t.sensors != null && t.sensors.mpu6050;

            // 1. Top Link & Mode Status
            if (statusText != null)
            {
                string nodeName = (state.identity != null && !string.IsNullOrEmpty(state.identity.node_name))
                    ? state.identity.node_name.ToUpperInvariant()
                    : "VRESQ-NODE";
                int robotId = (state.identity != null && state.identity.robot_id > 0) ? state.identity.robot_id : 1;
                string hwMode = !string.IsNullOrEmpty(state.hw_mode) ? state.hw_mode : "LIVE-HW";
                float rtt = VResqClient.Instance != null ? VResqClient.Instance.LatencyMs : 0f;

                if (state.estop)
                {
                    statusText.text = $"■ E-STOP LATCHED · #{robotId} {nodeName} ({hwMode})";
                    statusText.color = ColorDanger;
                }
                else
                {
                    statusText.text = $"● #{robotId} {nodeName} · {hwMode} · {rtt:F0}ms · {state.mode_name}";
                    statusText.color = ColorOnline;
                }
            }

            // 2. Ultrasonic Distance / Sonar Status (fixes 0cm turning yellow)
            if (distanceText != null)
            {
                if (hasUltra)
                {
                    float dist = t.ultrasonic_cm;
                    distanceText.text = $"SONAR: {dist:F0} CM";
                    distanceText.color = dist < 20f ? ColorDanger : (dist < 50f ? ColorWarn : ColorCyan);
                }
                else
                {
                    distanceText.text = "◆ SONAR: STANDBY";
                    distanceText.color = ColorStandby;
                }
            }

            // 3. Attitude (IMU when ESP32 online, Odometry Heading + Drive PWM when on standby)
            if (attitudeText != null)
            {
                if (hasMpu)
                {
                    attitudeText.text = $"P: {t.pitch_deg:F0}° | R: {t.roll_deg:F0}° | HDG: {t.yaw_deg:F0}° [IMU]";
                    attitudeText.color = ColorCyan;
                }
                else
                {
                    float hdg = (state.pose != null && state.pose.heading_deg != 0f)
                        ? state.pose.heading_deg
                        : ((state.pose_heading * Mathf.Rad2Deg) % 360f + 360f) % 360f;
                    attitudeText.text = $"HDG: {hdg:F0}° [ODO] | L:{state.left_pwm} R:{state.right_pwm} | SRV:{state.servo_deg}°";
                    attitudeText.color = ColorCyan;
                }
            }

            // 4. Atmosphere (BME280 when ESP32 online, Real Linux Board & CPU Thermal when on standby)
            if (atmosphereText != null)
            {
                if (hasBme)
                {
                    atmosphereText.text = $"ENV: {t.temp_c:F1}°C | {t.humidity:F0}% RH | {t.pressure_hpa:F0} hPa";
                    atmosphereText.color = t.temp_c >= 45f ? ColorDanger : ColorOnline;
                }
                else if (host != null && (host.board_temp_c > 0f || host.cpu_temp_c > 0f))
                {
                    float bTemp = host.board_temp_c > 0f ? host.board_temp_c : host.cpu_temp_c;
                    float cTemp = host.cpu_temp_c > 0f ? host.cpu_temp_c : bTemp;
                    atmosphereText.text = $"BOARD: {bTemp:F1}°C | CPU: {cTemp:F1}°C | RAM: {host.ram_pct:F0}%";
                    atmosphereText.color = bTemp >= 70f ? ColorWarn : ColorCyan;
                }
                else
                {
                    atmosphereText.text = "◆ ENV: STANDBY (NO BME280)";
                    atmosphereText.color = ColorStandby;
                }
            }

            // 5. Altitude / Host System Metrics
            if (altitudeText != null)
            {
                if (hasBme)
                {
                    altitudeText.text = $"ALT: {t.altitude_m:F1} m";
                }
                else if (host != null && host.disk_pct > 0f)
                {
                    altitudeText.text = $"DISK: {host.disk_pct:F0}% | LOAD: {host.load_1m:F2}";
                }
                else
                {
                    altitudeText.text = $"POS: ({state.pose_x:F0}, {state.pose_y:F0}) cm";
                }
            }

            // 6. Survivability / ESP32 Link Truth
            if (survivabilityText != null)
            {
                if (hasBme && an != null && an.survivability != null)
                {
                    string cat = !string.IsNullOrEmpty(an.survivability.category) ? an.survivability.category : an.survivability.level;
                    survivabilityText.text = $"SURVIVABILITY: {an.survivability.score}% ({cat})";
                    survivabilityText.color = an.survivability.score < 50 ? ColorDanger : ColorOnline;
                }
                else if (espOnline)
                {
                    survivabilityText.text = $"● ESP32: ONLINE ({state.serial_port})";
                    survivabilityText.color = ColorOnline;
                }
                else if (state.serial_connected)
                {
                    survivabilityText.text = $"▲ UART: {state.serial_port} (WAITING ESP32 RX)";
                    survivabilityText.color = ColorWarn;
                }
                else
                {
                    survivabilityText.text = "◆ ESP32: STANDBY (JETSON HOST MODE)";
                    survivabilityText.color = ColorStandby;
                }
            }

            // 7. AI Victims / Detections (filtered by confidence >= 0.45)
            if (victimCountText != null)
            {
                int validCount = 0;
                if (state.ai_detections != null)
                {
                    for (int i = 0; i < state.ai_detections.Length; i++)
                    {
                        var d = state.ai_detections[i];
                        if (d == null) continue;
                        float conf = d.score > 0f ? d.score : d.confidence;
                        if (conf <= 0f || conf >= 0.45f) validCount++;
                    }
                }
                string camState = state.camera_ready ? "CAM LIVE" : "CAM STANDBY";
                victimCountText.text = $"TARGETS: {validCount} ({camState})";
                victimCountText.color = validCount > 0 ? ColorWarn : ColorStandby;
            }

            // 8. Collision Alert Badge
            if (collisionAlertBadge != null)
            {
                collisionAlertBadge.SetActive(hasUltra && t.ultrasonic_cm < 20f);
            }
        }
    }
}
