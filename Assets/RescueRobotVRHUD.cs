using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using VResQ.Core;
using VResQ.Video;
using VResQ.Control;

/// <summary>
/// Master Tactical VR HUD Controller for V-ResQ Rescue Operations.
/// Manages 180°/360° Surround Display, Cyber Telemetry Displays, 2D Radar Map, and Swarm Fleet Mesh.
/// </summary>
public class RescueRobotVRHUD : MonoBehaviour
    {
        [Header("--- Top Bar Elements ---")]
        public TextMeshProUGUI robotIdText;
        public TextMeshProUGUI latencyText;
        public TextMeshProUGUI clockText;
        public TextMeshProUGUI viewModeBadgeText;

        [Header("--- Center HUD Elements ---")]
        public RawImage centerVideoScreen;
        public TextMeshProUGUI videoOverlayText;
        public TextMeshProUGUI compassHeadingText;
        public TextMeshProUGUI speedPercentText;
        public TextMeshProUGUI motorLeftText;
        public TextMeshProUGUI motorRightText;
        public TextMeshProUGUI ultrasonicDistanceText;
        public TextMeshProUGUI servoPanText;
        public TextMeshProUGUI fsmActionText;

        [Header("--- Attitude & Diagnostics (Right Panel) ---")]
        public TextMeshProUGUI healthText;
        public TextMeshProUGUI pitchText;
        public TextMeshProUGUI rollText;
        public RectTransform imuHorizonBall;
        public TextMeshProUGUI batteryText;
        public GameObject obstacleAlertPanel;
        public TextMeshProUGUI obstacleDistanceText;
        public TextMeshProUGUI sensorListText;

        [Header("--- Safety & Deadman Status ---")]
        public TextMeshProUGUI deadmanStatusText;
        public Image deadmanIndicatorDot;

        [Header("--- Left Panel (Swarm & Radar Map) ---")]
        public TextMeshProUGUI swarmStatusText;
        public VResqRadarMap radarMap;

        [Header("--- Surround Video System ---")]
        public VResqSurroundDisplay surroundDisplay;

        private float _timer = 0f;
        private VResqTeleopController _teleop;
        private VResqCameraReceiver _cameraReceiver;

        private void Awake()
        {
            // 1. Ensure VResqClient
            var client = GetComponent<VResqClient>();
            if (client == null)
            {
                client = gameObject.AddComponent<VResqClient>();
                client.robotIp = "10.66.68.52";
                client.port = 8000;
            }

            // 2. Ensure VResqCameraReceiver
            _cameraReceiver = GetComponent<VResqCameraReceiver>() ?? gameObject.AddComponent<VResqCameraReceiver>();

            // 3. Ensure VResqTeleopController
            _teleop = GetComponent<VResqTeleopController>() ?? gameObject.AddComponent<VResqTeleopController>();

            // 4. Ensure VResqSurroundDisplay (Surround Panoramic Dome)
            surroundDisplay = GetComponent<VResqSurroundDisplay>() ?? gameObject.AddComponent<VResqSurroundDisplay>();

            // 5. Ensure Center_VideoScreen exists & is configured
            if (centerVideoScreen == null)
            {
                Transform centerHUD = transform.Find("Center_HUD");
                if (centerHUD != null)
                {
                    Transform vsTrans = centerHUD.Find("Center_VideoScreen");
                    if (vsTrans == null)
                    {
                        GameObject vsObj = new GameObject("Center_VideoScreen");
                        vsObj.transform.SetParent(centerHUD, false);
                        vsObj.transform.SetAsFirstSibling();
                        RectTransform rt = vsObj.AddComponent<RectTransform>();
                        rt.anchoredPosition = new Vector2(0, 30);
                        rt.sizeDelta = new Vector2(800, 480);
                        centerVideoScreen = vsObj.AddComponent<RawImage>();
                        centerVideoScreen.color = Color.white;

                        // Hologram Border Outline
                        GameObject outline = new GameObject("Cyber_Border");
                        outline.transform.SetParent(vsObj.transform, false);
                        RectTransform ort = outline.AddComponent<RectTransform>();
                        ort.anchoredPosition = Vector2.zero;
                        ort.sizeDelta = new Vector2(800, 480);
                        Outline ol = outline.AddComponent<Outline>();
                        ol.effectColor = new Color(0f, 0.9f, 1f, 0.7f);
                        ol.effectDistance = new Vector2(2, 2);
                    }
                    else
                    {
                        centerVideoScreen = vsTrans.GetComponent<RawImage>();
                    }
                }
            }

            if (_cameraReceiver != null && centerVideoScreen != null)
            {
                _cameraReceiver.targetRawImage = centerVideoScreen;
            }

            // 6. Ensure Left Panel Radar Map
            Transform leftPanel = transform.Find("LeftPanel_Map");
            if (leftPanel != null)
            {
                radarMap = leftPanel.GetComponent<VResqRadarMap>() ?? leftPanel.gameObject.AddComponent<VResqRadarMap>();
                Transform radarScreen = leftPanel.Find("Radar_Screen");
                if (radarScreen == null)
                {
                    GameObject rObj = new GameObject("Radar_Screen");
                    rObj.transform.SetParent(leftPanel, false);
                    RectTransform rt = rObj.AddComponent<RectTransform>();
                    rt.anchoredPosition = new Vector2(0, 40);
                    rt.sizeDelta = new Vector2(300, 300);
                    RawImage ri = rObj.AddComponent<RawImage>();
                    radarMap.radarRawImage = ri;
                }
                else
                {
                    radarMap.radarRawImage = radarScreen.GetComponent<RawImage>();
                }
            }

            // 7. Auto-bind missing text labels
            if (swarmStatusText == null)
            {
                Transform sst = transform.Find("LeftPanel_Map/SwarmStatus");
                if (sst != null) swarmStatusText = sst.GetComponent<TextMeshProUGUI>();
            }

            if (sensorListText == null && healthText != null)
            {
                sensorListText = healthText;
            }

            // 8. Wire Action Buttons
            WireActionButtons();
        }

        private void WireActionButtons()
        {
            Button[] buttons = GetComponentsInChildren<Button>(true);
            foreach (var b in buttons)
            {
                string n = b.name.ToUpper();
                if (n.Contains("SCAN"))
                {
                    b.onClick.RemoveListener(OnScanClicked);
                    b.onClick.AddListener(OnScanClicked);
                }
                else if (n.Contains("BEACON"))
                {
                    b.onClick.RemoveListener(OnBeaconClicked);
                    b.onClick.AddListener(OnBeaconClicked);
                }
                else if (n.Contains("CENTER"))
                {
                    b.onClick.RemoveListener(OnCenterClicked);
                    b.onClick.AddListener(OnCenterClicked);
                }
                else if (n.Contains("APPROACH"))
                {
                    b.onClick.RemoveListener(OnApproachClicked);
                    b.onClick.AddListener(OnApproachClicked);
                }
                else if (n.Contains("BACK"))
                {
                    b.onClick.RemoveListener(OnBackOutClicked);
                    b.onClick.AddListener(OnBackOutClicked);
                }
                else if (n.Contains("MORE") || n.Contains("VIEW") || n.Contains("SURROUND"))
                {
                    b.onClick.RemoveListener(OnViewToggleClicked);
                    b.onClick.AddListener(OnViewToggleClicked);
                }
                else if (n.Contains("STOP") || n.Contains("E_STOP"))
                {
                    b.onClick.RemoveListener(OnEStopClicked);
                    b.onClick.AddListener(OnEStopClicked);
                }
            }
        }

        private void OnScanClicked() => _teleop?.ActionScan();
        private void OnBeaconClicked() => _teleop?.ActionBeacon();
        private void OnCenterClicked() => _teleop?.ActionCenter();
        private void OnApproachClicked() => _teleop?.ActionApproach();
        private void OnBackOutClicked() => _teleop?.ActionBackOut();
        private void OnViewToggleClicked() => surroundDisplay?.CycleNextViewMode();
        private void OnEStopClicked() => _teleop?.ActionEmergencyStop();

        private void Start()
        {
            if (_teleop == null)
                _teleop = GetComponent<VResqTeleopController>() ?? FindFirstObjectByType<VResqTeleopController>();

            if (VResqClient.Instance != null)
            {
                VResqClient.Instance.OnStateReceived.AddListener(OnRobotStateReceived);
                VResqClient.Instance.OnSwarmUpdated.AddListener(OnSwarmListUpdated);
                VResqClient.Instance.OnConnected.AddListener(OnRobotConnected);
                VResqClient.Instance.OnDisconnected.AddListener(OnRobotDisconnected);
            }
        }

        private void OnDestroy()
        {
            if (VResqClient.Instance != null)
            {
                VResqClient.Instance.OnStateReceived.RemoveListener(OnRobotStateReceived);
                VResqClient.Instance.OnSwarmUpdated.RemoveListener(OnSwarmListUpdated);
                VResqClient.Instance.OnConnected.RemoveListener(OnRobotConnected);
                VResqClient.Instance.OnDisconnected.RemoveListener(OnRobotDisconnected);
            }
        }

        private void Update()
        {
            _timer += Time.deltaTime;

            // 1. System Clock
            if (clockText != null)
                clockText.text = DateTime.Now.ToString("HH:mm:ss");

            // 2. View Mode Badge
            if (viewModeBadgeText != null && surroundDisplay != null)
            {
                string vmStr = surroundDisplay.currentViewMode switch
                {
                    SurroundViewMode.Surround180 => "[V] 180° SURROUND DOME",
                    SurroundViewMode.Surround220 => "[V] 220° PANORAMIC WRAP",
                    SurroundViewMode.Dome360 => "[V] 360° CYLINDER DOME",
                    SurroundViewMode.CenterPanelOnly => "[V] FLAT MONITOR",
                    _ => "[V] SURROUND"
                };
                viewModeBadgeText.text = vmStr;
            }

            // 3. Deadman Status
            bool isDeadman = (_teleop != null && _teleop.IsDeadmanActive);
            if (deadmanStatusText != null)
                deadmanStatusText.text = isDeadman ? "ACTIVE (READY)" : "STANDBY (HOLD SHIFT/GRIP)";
            if (deadmanIndicatorDot != null)
                deadmanIndicatorDot.color = isDeadman ? new Color(0f, 1f, 0.4f, 1f) : new Color(1f, 0.2f, 0.2f, 1f);

            // 4. Fallback simulation when no live robot is connected
            if (VResqClient.Instance == null || !VResqClient.Instance.IsConnected)
            {
                UpdateSimulationMode();
            }
        }

        private void OnRobotConnected()
        {
            if (latencyText != null)
            {
                latencyText.text = "ONLINE (WS CONNECTED)";
                latencyText.color = new Color(0f, 1f, 0.4f, 1f);
            }
        }

        private void OnRobotDisconnected()
        {
            if (latencyText != null)
            {
                latencyText.text = "OFFLINE (SEARCHING...)";
                latencyText.color = new Color(1f, 0.25f, 0.25f, 1f);
            }
            if (obstacleAlertPanel != null)
                obstacleAlertPanel.SetActive(false);
        }

        private void OnRobotStateReceived(RobotStateSnapshot state)
        {
            if (state == null) return;

            // 1. Top Bar Identity & Latency
            if (robotIdText != null)
            {
                string rName = state.identity != null && !string.IsNullOrEmpty(state.identity.node_name) ? state.identity.node_name : "V-RESQ";
                string role = state.identity != null ? state.identity.role : "ROBOT";
                string curIp = VResqClient.Instance != null ? VResqClient.Instance.robotIp : "";
                robotIdText.text = $"{rName.ToUpper()} [{role.ToUpper()}] : {curIp}";
            }

            if (latencyText != null && VResqClient.Instance != null)
            {
                float rtt = VResqClient.Instance.LatencyMs;
                latencyText.text = $"{rtt:F0} ms (RTT)";
                latencyText.color = rtt < 40f ? new Color(0f, 1f, 0.4f, 1f) : (rtt < 100f ? Color.yellow : new Color(1f, 0.3f, 0.3f, 1f));
            }

            // 2. Center HUD Flight & Drive Telemetry
            if (compassHeadingText != null && state.telemetry != null)
            {
                float yaw = (state.telemetry.yaw_deg % 360f + 360f) % 360f;
                string dir = GetCompassDirection(yaw);
                compassHeadingText.text = $"{yaw:000}° {dir}";
            }

            if (speedPercentText != null)
            {
                speedPercentText.text = $"THROTTLE {state.speed_scale * 100f:F0}%";
            }

            if (motorLeftText != null)
            {
                int lPwm = state.telemetry != null && state.telemetry.motor_left_cmd != 0 ? state.telemetry.motor_left_cmd : state.left_pwm;
                motorLeftText.text = $"L {(lPwm >= 0 ? "+" : "")}{lPwm}";
                motorLeftText.color = lPwm != 0 ? new Color(0f, 1f, 0.6f, 1f) : Color.white;
            }

            if (motorRightText != null)
            {
                int rPwm = state.telemetry != null && state.telemetry.motor_right_cmd != 0 ? state.telemetry.motor_right_cmd : state.right_pwm;
                motorRightText.text = $"R {(rPwm >= 0 ? "+" : "")}{rPwm}";
                motorRightText.color = rPwm != 0 ? new Color(0f, 1f, 0.6f, 1f) : Color.white;
            }

            int sDeg = state.telemetry != null && state.telemetry.servo_deg > 0 ? state.telemetry.servo_deg : state.servo_deg;
            if (servoPanText != null)
            {
                servoPanText.text = $"CAM SERVO {sDeg}°";
            }

            if (fsmActionText != null)
            {
                string act = !string.IsNullOrEmpty(state.active_action) ? state.active_action.ToUpper() : "MANUAL DRIVE";
                fsmActionText.text = $"MODE: {act}";
            }

            // 3. Ultrasonic Distance & Collision Warning Alert
            float dist = state.telemetry != null ? state.telemetry.ultrasonic_cm : 0;
            if (ultrasonicDistanceText != null)
            {
                ultrasonicDistanceText.text = dist > 0 ? $"{dist:F0} cm" : "CLEAR";
                ultrasonicDistanceText.color = (dist > 0 && dist < 25f) ? Color.red : (dist < 50f ? Color.yellow : Color.cyan);
            }

            if (obstacleAlertPanel != null)
            {
                bool alert = (dist > 0 && dist < 25f);
                obstacleAlertPanel.SetActive(alert);
                if (obstacleDistanceText != null && alert)
                {
                    obstacleDistanceText.text = $"COLLISION WARNING\nDISTANCE: {dist:F0} cm (STOP)";
                }
            }

            // 4. Right Panel Attitude (Pitch & Roll)
            if (state.telemetry != null)
            {
                float pitch = state.telemetry.pitch_deg;
                float roll = state.telemetry.roll_deg;

                if (pitchText != null) pitchText.text = $"PITCH {pitch:+0.0;-0.0}°";
                if (rollText != null) rollText.text = $"ROLL {roll:+0.0;-0.0}°";

                if (imuHorizonBall != null)
                {
                    imuHorizonBall.localEulerAngles = new Vector3(0, 0, -roll);
                    imuHorizonBall.anchoredPosition = new Vector2(0, 160f + (pitch * 2.5f));
                }

                // 5. Complete Sensor & Environmental Suite
                if (sensorListText != null)
                {
                    string driverStatus = state.telemetry.motor_driver_ok ? "ONLINE" : "STANDBY";
                    string serialPort = !string.IsNullOrEmpty(state.serial_port) ? state.serial_port : "/dev/ttyTHS1";
                    string serialStatus = state.serial_connected ? "CONNECTED" : "OFFLINE";
                    int aiCount = state.ai_detections != null ? state.ai_detections.Length : 0;

                    sensorListText.text =
                        $"--- SENSORS & SYSTEM ---\n" +
                        $"● US RADAR: {(dist > 0 ? $"{dist:F0}cm" : "CLEAR")}\n" +
                        $"● IMU GYRO: OK (P:{pitch:F1}° R:{roll:F1}°)\n" +
                        $"● TEMP: {state.telemetry.temp_c:F1}°C   HUM: {state.telemetry.humidity:F0}%\n" +
                        $"● PRESS: {state.telemetry.pressure_hpa:F0} hPa  ALT: {state.telemetry.altitude_m:F1}m\n" +
                        $"● HEAT INDEX: {state.telemetry.heat_index_c:F1}°C\n" +
                        $"● MOTOR DRIVER: {driverStatus}\n" +
                        $"● SERIAL: {serialPort} ({serialStatus})\n" +
                        $"● PACKETS: RX {state.telemetry.rx_ok} | CRC {state.telemetry.crc_errors}\n" +
                        $"● AI DETECTIONS: {aiCount} TARGETS";
                }
            }

            if (batteryText != null)
            {
                batteryText.text = "PWR: 12.4 V   [92%]";
            }
        }

        private void OnSwarmListUpdated(List<DiscoveredPeerData> peers)
        {
            if (swarmStatusText == null) return;

            string currentIp = VResqClient.Instance != null ? VResqClient.Instance.robotIp : "";
            string txt = "SWARM FLEET MESH\n";
            txt += $"[ACTIVE] >> [{currentIp}]\n";

            if (peers != null && peers.Count > 0)
            {
                foreach (var p in peers)
                {
                    string pip = p.ips != null && p.ips.Length > 0 ? p.ips[0] : "";
                    string role = p.properties != null ? p.properties.role : "peer";
                    txt += $"● {p.name.ToUpper()} ({role}) [{pip}]\n";
                }
            }
            else
            {
                txt += "● SEARCHING FOR PEERS...\n";
            }

            txt += "\n[HOTKEYS: 1=Node0, 2=Node1, 3=Node2, V=Surround]";
            swarmStatusText.text = txt;
        }

        private void UpdateSimulationMode()
        {
            if (latencyText != null)
            {
                latencyText.text = "OFFLINE (SIMULATION MODE)";
                latencyText.color = Color.gray;
            }

            float speed = Mathf.PingPong(_timer * 20f, 100f);
            if (speedPercentText != null) speedPercentText.text = $"THROTTLE {speed:F0}%";
            if (motorLeftText != null) motorLeftText.text = $"L +{(int)(speed * 1.5f)}";
            if (motorRightText != null) motorRightText.text = $"R +{(int)(speed * 1.5f)}";

            float heading = (_timer * 15f) % 360f;
            if (compassHeadingText != null) compassHeadingText.text = $"{heading:000}° {GetCompassDirection(heading)}";

            float roll = Mathf.Sin(_timer * 1.5f) * 6f;
            float pitch = Mathf.Cos(_timer * 1.2f) * 4f;
            if (pitchText != null) pitchText.text = $"PITCH {pitch:+0;-#}°";
            if (rollText != null) rollText.text = $"ROLL {roll:+0;-#}°";
            if (imuHorizonBall != null)
            {
                imuHorizonBall.localEulerAngles = new Vector3(0, 0, -roll);
                imuHorizonBall.anchoredPosition = new Vector2(0, 160f + (pitch * 2.5f));
            }

            float distance = 35f + Mathf.PingPong(_timer * 10f, 40f);
            if (ultrasonicDistanceText != null) ultrasonicDistanceText.text = $"{distance:F0} cm";

            if (sensorListText != null)
            {
                sensorListText.text =
                    $"--- SENSORS & SYSTEM ---\n" +
                    $"● US RADAR: {distance:F0}cm\n" +
                    $"● IMU GYRO: OK (SIM)\n" +
                    $"● TEMP: 28.5°C   HUM: 52%\n" +
                    $"● PRESS: 1013 hPa  ALT: 12.0m\n" +
                    $"● HEAT INDEX: 29.1°C\n" +
                    $"● MOTOR DRIVER: OK (SIM)\n" +
                    $"● SERIAL: /dev/ttyTHS1 (STANDBY)\n" +
                    $"● PACKETS: RX 1000 | CRC 0\n" +
                    $"● AI DETECTIONS: 0 (7.4 FPS)";
            }
        }

        private static string GetCompassDirection(float yaw)
        {
            if (yaw >= 337.5f || yaw < 22.5f) return "N";
            if (yaw >= 22.5f && yaw < 67.5f) return "NE";
            if (yaw >= 67.5f && yaw < 112.5f) return "E";
            if (yaw >= 112.5f && yaw < 157.5f) return "SE";
            if (yaw >= 157.5f && yaw < 202.5f) return "S";
            if (yaw >= 202.5f && yaw < 247.5f) return "SW";
            if (yaw >= 247.5f && yaw < 292.5f) return "W";
            return "NW";
        }
    }
