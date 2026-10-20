using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using VResQ.Core;
using VResQ.Video;
using VResQ.Control;

public class RescueRobotVRHUD : MonoBehaviour
{
    [Header("--- Top Bar Elements ---")]
    public TextMeshProUGUI robotIdText;
    public TextMeshProUGUI latencyText;
    public TextMeshProUGUI clockText;

    [Header("--- Center HUD Elements ---")]
    public RawImage centerVideoScreen;
    public TextMeshProUGUI videoOverlayText;
    public TextMeshProUGUI compassHeadingText;
    public TextMeshProUGUI speedPercentText;
    public TextMeshProUGUI motorLeftText;
    public TextMeshProUGUI motorRightText;
    public TextMeshProUGUI ultrasonicDistanceText;

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

    [Header("--- Left Panel (Swarm & Map) ---")]
    public TextMeshProUGUI swarmStatusText;

    private float timer = 0f;
    private VResqTeleopController teleop;

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
        var cam = GetComponent<VResqCameraReceiver>() ?? gameObject.AddComponent<VResqCameraReceiver>();

        // 3. Ensure VResqTeleopController
        teleop = GetComponent<VResqTeleopController>() ?? gameObject.AddComponent<VResqTeleopController>();

        // 4. Ensure Center_VideoScreen exists & is configured
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

                    // Cyber Outline
                    GameObject outline = new GameObject("Border");
                    outline.transform.SetParent(vsObj.transform, false);
                    RectTransform ort = outline.AddComponent<RectTransform>();
                    ort.anchoredPosition = Vector2.zero;
                    ort.sizeDelta = new Vector2(800, 480);
                    Outline ol = outline.AddComponent<Outline>();
                    ol.effectColor = new Color(0f, 0.8f, 1f, 0.7f);
                    ol.effectDistance = new Vector2(2, 2);
                }
                else
                {
                    centerVideoScreen = vsTrans.GetComponent<RawImage>();
                }
            }
        }

        if (cam != null && centerVideoScreen != null)
        {
            cam.targetRawImage = centerVideoScreen;
        }

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
            else if (n.Contains("STOP") || n.Contains("E_STOP"))
            {
                b.onClick.RemoveListener(OnEStopClicked);
                b.onClick.AddListener(OnEStopClicked);
            }
        }
    }

    private void OnScanClicked() => teleop?.ActionScan();
    private void OnBeaconClicked() => teleop?.ActionBeacon();
    private void OnCenterClicked() => teleop?.ActionCenter();
    private void OnApproachClicked() => teleop?.ActionApproach();
    private void OnBackOutClicked() => teleop?.ActionBackOut();
    private void OnEStopClicked() => teleop?.ActionEmergencyStop();

    void Start()
    {
        if (teleop == null)
            teleop = GetComponent<VResqTeleopController>() ?? FindFirstObjectByType<VResqTeleopController>();

        if (VResqClient.Instance != null)
        {
            VResqClient.Instance.OnStateReceived.AddListener(OnRobotStateReceived);
            VResqClient.Instance.OnSwarmUpdated.AddListener(OnSwarmListUpdated);
            VResqClient.Instance.OnConnected.AddListener(OnRobotConnected);
            VResqClient.Instance.OnDisconnected.AddListener(OnRobotDisconnected);
        }
    }

    void OnDestroy()
    {
        if (VResqClient.Instance != null)
        {
            VResqClient.Instance.OnStateReceived.RemoveListener(OnRobotStateReceived);
            VResqClient.Instance.OnSwarmUpdated.RemoveListener(OnSwarmListUpdated);
            VResqClient.Instance.OnConnected.RemoveListener(OnRobotConnected);
            VResqClient.Instance.OnDisconnected.RemoveListener(OnRobotDisconnected);
        }
    }

    void Update()
    {
        timer += Time.deltaTime;

        // 1. Clock
        if (clockText != null)
            clockText.text = System.DateTime.Now.ToString("HH:mm:ss");

        // 2. Deadman status from Teleop Controller
        bool isDeadman = (teleop != null && teleop.IsDeadmanActive);
        if (deadmanStatusText != null)
            deadmanStatusText.text = isDeadman ? "ACTIVE" : "STANDBY";
        if (deadmanIndicatorDot != null)
            deadmanIndicatorDot.color = isDeadman ? new Color(0f, 1f, 0.4f, 1f) : new Color(1f, 0.2f, 0.2f, 1f);

        // 3. Fallback simulation if no real robot connected
        if (VResqClient.Instance == null || !VResqClient.Instance.IsConnected)
        {
            UpdateSimulationMode();
        }
    }

    private void OnRobotConnected()
    {
        if (latencyText != null)
        {
            latencyText.text = "CONNECTED";
            latencyText.color = new Color(0f, 1f, 0.4f, 1f);
        }
    }

    private void OnRobotDisconnected()
    {
        if (latencyText != null)
        {
            latencyText.text = "OFFLINE";
            latencyText.color = new Color(1f, 0.25f, 0.25f, 1f);
        }
        if (obstacleAlertPanel != null)
            obstacleAlertPanel.SetActive(false);
    }

    private void OnRobotStateReceived(RobotStateSnapshot state)
    {
        if (state == null) return;

        // Top Bar
        if (robotIdText != null)
        {
            string rName = state.identity != null && !string.IsNullOrEmpty(state.identity.node_name) ? state.identity.node_name : "V-RESQ";
            string role = state.identity != null ? state.identity.role : "ROBOT";
            robotIdText.text = $"{rName.ToUpper()} ({role})";
        }

        if (latencyText != null && VResqClient.Instance != null)
        {
            float rtt = VResqClient.Instance.LatencyMs;
            latencyText.text = $"{rtt:F0} ms";
            latencyText.color = rtt < 40f ? new Color(0f, 1f, 0.4f, 1f) : (rtt < 100f ? Color.yellow : new Color(1f, 0.3f, 0.3f, 1f));
        }

        // Center HUD Telemetry
        if (compassHeadingText != null && state.telemetry != null)
        {
            float yaw = (state.telemetry.yaw_deg % 360f + 360f) % 360f;
            string dir = GetCompassDirection(yaw);
            compassHeadingText.text = $"{yaw:000}° {dir}";
        }

        if (speedPercentText != null)
        {
            speedPercentText.text = $"SPD {state.speed_scale * 100f:F0}%";
        }

        if (motorLeftText != null)
        {
            int lPwm = state.telemetry != null ? state.telemetry.motor_left_cmd : state.left_pwm;
            motorLeftText.text = $"L {(lPwm >= 0 ? "+" : "")}{lPwm}";
        }

        if (motorRightText != null)
        {
            int rPwm = state.telemetry != null ? state.telemetry.motor_right_cmd : state.right_pwm;
            motorRightText.text = $"R {(rPwm >= 0 ? "+" : "")}{rPwm}";
        }

        // Ultrasonic & Collision Alert
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
                obstacleDistanceText.text = $"{dist:F0} cm (STOP)";
            }
        }

        // Attitude (Pitch & Roll)
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

            if (sensorListText != null)
            {
                sensorListText.text = $"● US: {(dist > 0 ? "OK" : "--")}    ● IMU: OK\n● TEMP: {state.telemetry.temp_c:F1}°C   ● HUM: {state.telemetry.humidity:F0}%\n● MOTOR DRIVER: {(state.telemetry.motor_driver_ok ? "OK" : "STANDBY")}";
            }
        }

        if (batteryText != null)
        {
            batteryText.text = "12.4 V   92%";
        }
    }

    private void OnSwarmListUpdated(List<DiscoveredPeerData> peers)
    {
        if (swarmStatusText == null) return;

        string currentIp = VResqClient.Instance != null ? VResqClient.Instance.robotIp : "";
        string txt = "SWARM FLEET MESH\n";
        txt += $"● THIS ROBOT [{currentIp}]\n";

        if (peers != null && peers.Count > 0)
        {
            foreach (var p in peers)
            {
                string pip = p.ips != null && p.ips.Length > 0 ? p.ips[0] : "";
                string role = p.properties != null ? p.properties.role : "peer";
                txt += $"● {p.name} ({role}) [{pip}]\n";
            }
        }
        else
        {
            txt += "● SEARCHING FOR PEERS...\n";
        }

        txt += "\n[Press 1=Node0, 2=Node1, 3=Node2]";
        swarmStatusText.text = txt;
    }

    private void UpdateSimulationMode()
    {
        if (latencyText != null)
        {
            latencyText.text = "OFFLINE (SIM)";
            latencyText.color = Color.gray;
        }

        float speed = Mathf.PingPong(timer * 20f, 100f);
        if (speedPercentText != null) speedPercentText.text = $"SPD {speed:F0}%";
        if (motorLeftText != null) motorLeftText.text = $"L +{(int)(speed * 1.5f)}";
        if (motorRightText != null) motorRightText.text = $"R +{(int)(speed * 1.5f)}";

        float heading = (timer * 15f) % 360f;
        if (compassHeadingText != null) compassHeadingText.text = $"{heading:000}° {GetCompassDirection(heading)}";

        float roll = Mathf.Sin(timer * 1.5f) * 6f;
        float pitch = Mathf.Cos(timer * 1.2f) * 4f;
        if (pitchText != null) pitchText.text = $"PITCH {pitch:+0;-#}°";
        if (rollText != null) rollText.text = $"ROLL {roll:+0;-#}°";
        if (imuHorizonBall != null)
        {
            imuHorizonBall.localEulerAngles = new Vector3(0, 0, -roll);
            imuHorizonBall.anchoredPosition = new Vector2(0, 160f + (pitch * 2.5f));
        }

        float distance = 35f + Mathf.PingPong(timer * 10f, 40f);
        if (ultrasonicDistanceText != null) ultrasonicDistanceText.text = $"{distance:F0} cm";
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
