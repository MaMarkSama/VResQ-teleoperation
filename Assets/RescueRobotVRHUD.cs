using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using VResQ.Core;
using VResQ.Video;
using VResQ.Control;

/// <summary>
/// Master Tactical VR HUD & Framework UI Controller for V-ResQ Rescue Operations.
/// Manages 180°/220°/360° Surround Display, Non-Overlapping Telemetry Cards,
/// Interactive Swarm Node Switcher, AI Detection Bounding Boxes, and In-VR Framework Settings Modal.
/// </summary>
public class RescueRobotVRHUD : MonoBehaviour
{
    [Header("--- Top Command Bar ---")]
    public TextMeshProUGUI robotIdText;
    public TextMeshProUGUI latencyText;
    public TextMeshProUGUI viewModeBadgeText;
    public TextMeshProUGUI aiStatusBadgeText;
    public TextMeshProUGUI clockText;

    [Header("--- Center HUD Viewport ---")]
    public RawImage centerVideoScreen;
    public RectTransform aiBboxContainer;
    public TextMeshProUGUI videoOverlayText;
    public TextMeshProUGUI compassHeadingText;
    public TextMeshProUGUI speedPercentText;
    public Image speedBarFill;
    public TextMeshProUGUI motorLeftText;
    public Image motorLeftBarFill;
    public TextMeshProUGUI motorRightText;
    public Image motorRightBarFill;
    public TextMeshProUGUI ultrasonicDistanceText;
    public TextMeshProUGUI servoPanText;
    public RectTransform servoTickIndicator;
    public TextMeshProUGUI fsmActionText;
    public GameObject obstacleAlertPanel;
    public TextMeshProUGUI obstacleDistanceText;

    [Header("--- Left Wing: Radar & Swarm Fleet ---")]
    public RectTransform leftPanelRect;
    public VResqRadarMap radarMap;
    public Button[] nodeCardButtons = new Button[3];
    public Image[] nodeCardBgs = new Image[3];
    public Image[] nodeCardDots = new Image[3];
    public TextMeshProUGUI[] nodeCardTexts = new TextMeshProUGUI[3];
    public TextMeshProUGUI swarmStatusText;

    [Header("--- Right Wing: Attitude, Environment & Hardware ---")]
    public RectTransform rightPanelRect;
    public RectTransform imuHorizonBall;
    public TextMeshProUGUI pitchText;
    public TextMeshProUGUI rollText;
    public TextMeshProUGUI yawText;
    public TextMeshProUGUI envMetricsText;
    public TextMeshProUGUI hardwareHealthText;
    public TextMeshProUGUI batteryText;
    public Image batteryBarFill;

    [Header("--- Legacy Compatibility Aliases ---")]
    public TextMeshProUGUI healthText;
    public TextMeshProUGUI sensorListText;

    [Header("--- Bottom Command Deck ---")]
    public TextMeshProUGUI deadmanStatusText;
    public Image deadmanIndicatorDot;

    [Header("--- Framework Settings Modal (In-VR Configurable) ---")]
    public GameObject settingsModalRoot;
    public TextMeshProUGUI cfgSummaryText;

    [Header("--- Surround Video System ---")]
    public VResqSurroundDisplay surroundDisplay;

    [Header("--- Themeable Accent Elements ---")]
    public List<Graphic> themeAccentGraphics = new List<Graphic>();
    public List<Image> themePanelBackgrounds = new List<Image>();

    private float _timer = 0f;
    private VResqTeleopController _teleop;
    private VResqCameraReceiver _cameraReceiver;
    private VResqFrameworkConfig _config;
    private readonly List<GameObject> _activeBboxPool = new List<GameObject>();

    private void Awake()
    {
        _config = GetComponent<VResqFrameworkConfig>() ?? gameObject.AddComponent<VResqFrameworkConfig>();

        var client = GetComponent<VResqClient>();
        if (client == null)
        {
            client = gameObject.AddComponent<VResqClient>();
            client.robotIp = "10.66.68.52";
            client.port = 8000;
        }

        _cameraReceiver = GetComponent<VResqCameraReceiver>() ?? gameObject.AddComponent<VResqCameraReceiver>();
        _teleop = GetComponent<VResqTeleopController>() ?? gameObject.AddComponent<VResqTeleopController>();
        surroundDisplay = GetComponent<VResqSurroundDisplay>() ?? gameObject.AddComponent<VResqSurroundDisplay>();

        if (_cameraReceiver != null && centerVideoScreen != null)
        {
            _cameraReceiver.targetRawImage = centerVideoScreen;
        }

        WireAllInteractiveButtons();
    }

    private void Start()
    {
        if (VResqClient.Instance != null)
        {
            VResqClient.Instance.OnStateReceived.AddListener(OnRobotStateReceived);
            VResqClient.Instance.OnSwarmUpdated.AddListener(OnSwarmListUpdated);
            VResqClient.Instance.OnFleetUpdated.AddListener(RefreshFleetNodeCards);
            VResqClient.Instance.OnConnected.AddListener(OnRobotConnected);
            VResqClient.Instance.OnDisconnected.AddListener(OnRobotDisconnected);
        }

        if (_config != null)
        {
            _config.OnConfigChanged.AddListener(ApplyFrameworkVisuals);
        }

        ApplyFrameworkVisuals();
        RefreshFleetNodeCards();
    }

    private void OnDestroy()
    {
        if (VResqClient.Instance != null)
        {
            VResqClient.Instance.OnStateReceived.RemoveListener(OnRobotStateReceived);
            VResqClient.Instance.OnSwarmUpdated.RemoveListener(OnSwarmListUpdated);
            VResqClient.Instance.OnFleetUpdated.RemoveListener(RefreshFleetNodeCards);
            VResqClient.Instance.OnConnected.RemoveListener(OnRobotConnected);
            VResqClient.Instance.OnDisconnected.RemoveListener(OnRobotDisconnected);
        }
    }

    public void WireAllInteractiveButtons()
    {
        Button[] buttons = GetComponentsInChildren<Button>(true);
        foreach (var b in buttons)
        {
            string n = b.name.ToUpperInvariant();
            b.onClick.RemoveAllListeners();

            if (n.Contains("BTN_NODE_0")) b.onClick.AddListener(() => SelectFleetNode(0));
            else if (n.Contains("BTN_NODE_1")) b.onClick.AddListener(() => SelectFleetNode(1));
            else if (n.Contains("BTN_NODE_2")) b.onClick.AddListener(() => SelectFleetNode(2));
            else if (n.Contains("BTN_SCAN")) b.onClick.AddListener(() => _teleop?.ActionScan());
            else if (n.Contains("BTN_BEACON")) b.onClick.AddListener(() => _teleop?.ActionBeacon());
            else if (n.Contains("BTN_CENTER")) b.onClick.AddListener(() => _teleop?.ActionCenter());
            else if (n.Contains("BTN_APPROACH")) b.onClick.AddListener(() => _teleop?.ActionApproach());
            else if (n.Contains("BTN_BACK")) b.onClick.AddListener(() => _teleop?.ActionBackOut());
            else if (n.Contains("BTN_VIEW") || n.Contains("BTN_SURROUND")) b.onClick.AddListener(() => surroundDisplay?.CycleNextViewMode());
            else if (n.Contains("BTN_CONFIG") || n.Contains("BTN_SETTINGS")) b.onClick.AddListener(ToggleSettingsModal);
            else if (n.Contains("BTN_ESTOP") || n.Contains("BTN_STOP")) b.onClick.AddListener(() => _teleop?.ActionEmergencyStop());
            // Framework Settings Modal Controls
            else if (n.Contains("CFG_CLOSE")) b.onClick.AddListener(ToggleSettingsModal);
            else if (n.Contains("CFG_VIEW_CYCLE")) b.onClick.AddListener(() => { surroundDisplay?.CycleNextViewMode(); RefreshConfigModalText(); });
            else if (n.Contains("CFG_MONITOR_TOGGLE")) b.onClick.AddListener(ToggleCenterMonitorMode);
            else if (n.Contains("CFG_RADIUS_MINUS")) b.onClick.AddListener(() => AdjustSurroundRadius(-0.2f));
            else if (n.Contains("CFG_RADIUS_PLUS")) b.onClick.AddListener(() => AdjustSurroundRadius(+0.2f));
            else if (n.Contains("CFG_HUDDIST_MINUS")) b.onClick.AddListener(() => AdjustHudDistance(-0.1f));
            else if (n.Contains("CFG_HUDDIST_PLUS")) b.onClick.AddListener(() => AdjustHudDistance(+0.1f));
            else if (n.Contains("CFG_WING_MINUS")) b.onClick.AddListener(() => AdjustWingAngle(-4f));
            else if (n.Contains("CFG_WING_PLUS")) b.onClick.AddListener(() => AdjustWingAngle(+4f));
            else if (n.Contains("CFG_SPEED_MINUS")) b.onClick.AddListener(() => { _teleop?.AdjustSpeedScale(-0.1f); RefreshConfigModalText(); });
            else if (n.Contains("CFG_SPEED_PLUS")) b.onClick.AddListener(() => { _teleop?.AdjustSpeedScale(+0.1f); RefreshConfigModalText(); });
            else if (n.Contains("CFG_DEADMAN_TOGGLE")) b.onClick.AddListener(ToggleDeadmanRequirement);
            else if (n.Contains("CFG_FAILOVER_TOGGLE")) b.onClick.AddListener(ToggleAutoFailover);
            else if (n.Contains("CFG_THEME_CYCLE")) b.onClick.AddListener(CycleCockpitTheme);
            else if (n.Contains("CFG_RECENTER")) b.onClick.AddListener(() => GetComponent<TacticalHUDFollow>()?.ResetHUDOrientation());
            else if (n.Contains("CFG_SAVE")) b.onClick.AddListener(() => { _config?.SaveProfile(); RefreshConfigModalText(); });
            else if (n.Contains("CFG_RESET")) b.onClick.AddListener(() => { _config?.ResetToDefaults(); RefreshConfigModalText(); });
        }
    }

    public void SelectFleetNode(int index)
    {
        VResqClient.Instance?.SwitchToNodeIndex(index);
        RefreshFleetNodeCards();
    }

    public void ToggleSettingsModal()
    {
        if (settingsModalRoot == null) return;
        bool nextState = !settingsModalRoot.activeSelf;
        settingsModalRoot.SetActive(nextState);
        if (nextState) RefreshConfigModalText();
    }

    private void ToggleCenterMonitorMode()
    {
        if (_config == null) return;
        _config.Profile.showCenterMonitorInSurround = !_config.Profile.showCenterMonitorInSurround;
        _config.NotifyConfigChanged();
    }

    private void AdjustSurroundRadius(float delta)
    {
        if (_config == null) return;
        _config.Profile.surroundRadius = Mathf.Clamp(_config.Profile.surroundRadius + delta, 1.6f, 5.0f);
        _config.NotifyConfigChanged();
    }

    private void AdjustHudDistance(float delta)
    {
        if (_config == null) return;
        _config.Profile.hudDistance = Mathf.Clamp(_config.Profile.hudDistance + delta, 0.85f, 2.5f);
        _config.NotifyConfigChanged();
    }

    private void AdjustWingAngle(float delta)
    {
        if (_config == null) return;
        _config.Profile.wingPanelAngle = Mathf.Clamp(_config.Profile.wingPanelAngle + delta, 0f, 42f);
        _config.NotifyConfigChanged();
    }

    private void ToggleDeadmanRequirement()
    {
        if (_config == null) return;
        _config.Profile.requireDeadmanSwitch = !_config.Profile.requireDeadmanSwitch;
        _config.NotifyConfigChanged();
    }

    private void ToggleAutoFailover()
    {
        if (_config == null) return;
        _config.Profile.autoFallbackToOnlineNode = !_config.Profile.autoFallbackToOnlineNode;
        _config.NotifyConfigChanged();
    }

    private void CycleCockpitTheme()
    {
        if (_config == null) return;
        int next = ((int)_config.Profile.theme + 1) % 4;
        _config.Profile.theme = (CockpitTheme)next;
        _config.NotifyConfigChanged();
    }

    public void ApplyFrameworkVisuals()
    {
        if (_config == null) _config = VResqFrameworkConfig.Instance;
        if (_config == null) return;

        var p = _config.Profile;

        // 1. Wing Panel Ergonomic Curvature
        if (leftPanelRect != null)
        {
            leftPanelRect.localEulerAngles = new Vector3(0f, -p.wingPanelAngle, 0f);
        }
        if (rightPanelRect != null)
        {
            rightPanelRect.localEulerAngles = new Vector3(0f, p.wingPanelAngle, 0f);
        }

        // 2. Center Monitor Opacity in Surround Mode vs Flat Mode
        if (centerVideoScreen != null)
        {
            bool isSurround = p.viewMode != SurroundViewMode.CenterPanelOnly;
            if (isSurround && !p.showCenterMonitorInSurround)
            {
                centerVideoScreen.color = new Color(1f, 1f, 1f, 0.06f);
            }
            else if (isSurround)
            {
                centerVideoScreen.color = new Color(1f, 1f, 1f, p.centerMonitorOpacityInSurround);
            }
            else
            {
                centerVideoScreen.color = Color.white;
            }
        }

        // 3. Apply Theme Colors
        Color primary = _config.GetPrimaryThemeColor();
        Color panelBg = _config.GetPanelBackgroundColor();

        foreach (var g in themeAccentGraphics)
        {
            if (g != null) g.color = primary;
        }
        foreach (var bg in themePanelBackgrounds)
        {
            if (bg != null) bg.color = panelBg;
        }

        RefreshConfigModalText();
        RefreshFleetNodeCards();
    }

    private void RefreshConfigModalText()
    {
        if (cfgSummaryText == null || _config == null) return;
        var p = _config.Profile;
        string monitorMode = p.showCenterMonitorInSurround ? "PIP + SURROUND" : "TRANSPARENT HUD ONLY";
        string deadmanMode = p.requireDeadmanSwitch ? "REQUIRE HOLD (SHIFT/GRIP)" : "IMMEDIATE DRIVE (SEAMLESS)";
        string failoverMode = p.autoFallbackToOnlineNode ? "ENABLED (AUTO-SWITCH)" : "MANUAL ONLY";

        cfgSummaryText.text =
            $"<b>SURROUND MODE:</b> <color=#00F0FF>{p.viewMode}</color>   |   <b>CENTER VIEWPORT:</b> <color=#00FFAA>{monitorMode}</color>\n" +
            $"<b>DOME RADIUS:</b> {p.surroundRadius:F1} m   |   <b>DOME ARC:</b> {p.surroundArcAngle:F0}°   |   <b>HUD DISTANCE:</b> {p.hudDistance:F2} m\n" +
            $"<b>WING CURVE ANGLE:</b> {p.wingPanelAngle:F0}°   |   <b>THEME:</b> <color=#FFD166>{p.theme}</color>\n" +
            $"<b>MAX SPEED LIMIT:</b> {p.speedScale * 100f:F0}%   |   <b>DRIVE SAFETY:</b> <color=#00FFAA>{deadmanMode}</color>\n" +
            $"<b>MULTI-NODE AUTO-FAILOVER:</b> {failoverMode}";
    }

    public void RefreshFleetNodeCards()
    {
        if (_config == null || _config.Profile.fleetNodes == null) return;
        var nodes = _config.Profile.fleetNodes;
        int activeIdx = VResqClient.Instance != null ? VResqClient.Instance.ActiveNodeIndex : _config.Profile.defaultNodeIndex;

        for (int i = 0; i < 3 && i < nodes.Length; i++)
        {
            var n = nodes[i];
            bool isSelected = (i == activeIdx);

            if (nodeCardBgs != null && i < nodeCardBgs.Length && nodeCardBgs[i] != null)
            {
                nodeCardBgs[i].color = isSelected
                    ? new Color(0.0f, 0.36f, 0.48f, 0.95f)
                    : new Color(0.04f, 0.10f, 0.18f, 0.85f);
            }

            if (nodeCardDots != null && i < nodeCardDots.Length && nodeCardDots[i] != null)
            {
                nodeCardDots[i].color = n.isOnline
                    ? new Color(0.0f, 1.0f, 0.45f, 1f)
                    : new Color(1.0f, 0.25f, 0.3f, 0.85f);
            }

            if (nodeCardTexts != null && i < nodeCardTexts.Length && nodeCardTexts[i] != null)
            {
                string role = !string.IsNullOrEmpty(n.remoteRole) ? n.remoteRole.ToUpper() : n.roleLabel;
                string statusStr = n.isOnline ? $"<color=#00FF77>ONLINE {n.lastRttMs:F0}ms</color>" : "<color=#FF5566>OFFLINE</color>";
                string activeTag = isSelected ? " <color=#00F0FF>◀ ACTIVE</color>" : "";
                nodeCardTexts[i].text = $"<b>[{i + 1}] {n.displayName} ({role})</b>{activeTag}\n<size=13>{n.ipAddress}:{n.port}  •  {statusStr}</size>";
            }
        }
    }

    private void Update()
    {
        _timer += Time.deltaTime;

        if (clockText != null)
            clockText.text = DateTime.Now.ToString("HH:mm:ss");

        if (viewModeBadgeText != null && surroundDisplay != null)
        {
            viewModeBadgeText.text = surroundDisplay.currentViewMode switch
            {
                SurroundViewMode.Surround180 => "VIEW: 180° DOME [V]",
                SurroundViewMode.Surround220 => "VIEW: 220° PANORAMA [V]",
                SurroundViewMode.Dome360 => "VIEW: 360° FULL SPHERE [V]",
                SurroundViewMode.CenterPanelOnly => "VIEW: COCKPIT MONITOR [V]",
                _ => "VIEW: SURROUND [V]"
            };
        }

        bool isDeadman = (_teleop == null || _teleop.IsDeadmanActive);
        if (deadmanStatusText != null)
        {
            bool reqHold = _config != null && _config.Profile.requireDeadmanSwitch;
            deadmanStatusText.text = isDeadman
                ? (reqHold ? "DRIVE ENGAGED (DEADMAN HELD)" : "DRIVE READY (SEAMLESS WASD / STICK)")
                : "DRIVE LOCKED (HOLD SHIFT OR GRIP)";
        }
        if (deadmanIndicatorDot != null)
        {
            deadmanIndicatorDot.color = isDeadman ? new Color(0f, 1f, 0.45f, 1f) : new Color(1f, 0.3f, 0.25f, 1f);
        }

        if (VResqClient.Instance == null || !VResqClient.Instance.IsConnected)
        {
            UpdateStandbyPreviewMode();
        }
    }

    private void OnRobotConnected()
    {
        RefreshFleetNodeCards();
        if (videoOverlayText != null)
        {
            videoOverlayText.text = "";
        }
    }

    private void OnRobotDisconnected()
    {
        RefreshFleetNodeCards();
        if (latencyText != null)
        {
            latencyText.text = "LINK: RECONNECTING...";
            latencyText.color = new Color(1f, 0.7f, 0.2f, 1f);
        }
        if (obstacleAlertPanel != null)
            obstacleAlertPanel.SetActive(false);
    }

    private void OnRobotStateReceived(RobotStateSnapshot state)
    {
        if (state == null) return;

        // 1. Top Command Bar
        string rName = (state.node_info != null && !string.IsNullOrEmpty(state.node_info.node_name))
            ? state.node_info.node_name
            : (state.identity != null && !string.IsNullOrEmpty(state.identity.node_name) ? state.identity.node_name : "VRESQ-UNIT");
        string role = (state.node_info != null && !string.IsNullOrEmpty(state.node_info.role))
            ? state.node_info.role
            : (state.identity != null ? state.identity.role : "LEADER");
        string curIp = VResqClient.Instance != null ? VResqClient.Instance.robotIp : "10.66.68.52";

        if (robotIdText != null)
        {
            robotIdText.text = $"● {rName.ToUpper()}  [{role.ToUpper()}]  •  {curIp}";
        }

        if (latencyText != null && VResqClient.Instance != null)
        {
            float rtt = VResqClient.Instance.LatencyMs;
            float camFps = _cameraReceiver != null ? _cameraReceiver.StreamFps : 0f;
            latencyText.text = $"WS: {rtt:F0}ms  |  VIDEO: {camFps:F0} FPS";
            latencyText.color = rtt < 45f ? new Color(0f, 1f, 0.45f, 1f) : (rtt < 110f ? new Color(1f, 0.85f, 0.2f, 1f) : new Color(1f, 0.35f, 0.35f, 1f));
        }

        int aiCount = state.ai_detections != null ? state.ai_detections.Length : 0;
        float aiFps = state.ai_config != null ? state.ai_config.detector_fps : 0f;
        if (aiStatusBadgeText != null)
        {
            aiStatusBadgeText.text = $"AI VISION: {aiCount} TARGETS ({aiFps:F1} FPS)";
            aiStatusBadgeText.color = aiCount > 0 ? new Color(1f, 0.85f, 0.2f, 1f) : new Color(0.55f, 0.9f, 1f, 0.9f);
        }

        // 2. Center HUD Flight & Propulsion
        float yaw = state.telemetry != null ? ((state.telemetry.yaw_deg % 360f) + 360f) % 360f : 0f;
        if (compassHeadingText != null)
        {
            compassHeadingText.text = $"◄  HDG {yaw:000}° {GetCompassDirection(yaw)}  ►";
        }

        float throttlePct = (state.speed_scale > 0.01f ? state.speed_scale : (_teleop != null ? _teleop.speedScale : 0.75f)) * 100f;
        if (speedPercentText != null)
        {
            speedPercentText.text = $"SPEED LIMIT: {throttlePct:F0}% [-/=]";
        }
        if (speedBarFill != null)
        {
            speedBarFill.fillAmount = Mathf.Clamp01(throttlePct / 100f);
        }

        int lPwm = state.telemetry != null && state.telemetry.motor_left_cmd != 0 ? state.telemetry.motor_left_cmd : state.left_pwm;
        int rPwm = state.telemetry != null && state.telemetry.motor_right_cmd != 0 ? state.telemetry.motor_right_cmd : state.right_pwm;

        if (motorLeftText != null)
        {
            motorLeftText.text = $"L-TRK {(lPwm >= 0 ? "+" : "")}{lPwm}";
            motorLeftText.color = lPwm != 0 ? new Color(0f, 1f, 0.65f, 1f) : new Color(0.75f, 0.9f, 1f, 0.9f);
        }
        if (motorLeftBarFill != null)
        {
            motorLeftBarFill.fillAmount = Mathf.Clamp01(Mathf.Abs(lPwm) / 255f);
        }

        if (motorRightText != null)
        {
            motorRightText.text = $"R-TRK {(rPwm >= 0 ? "+" : "")}{rPwm}";
            motorRightText.color = rPwm != 0 ? new Color(0f, 1f, 0.65f, 1f) : new Color(0.75f, 0.9f, 1f, 0.9f);
        }
        if (motorRightBarFill != null)
        {
            motorRightBarFill.fillAmount = Mathf.Clamp01(Mathf.Abs(rPwm) / 255f);
        }

        int sDeg = state.telemetry != null && state.telemetry.servo_deg > 0 ? state.telemetry.servo_deg : state.servo_deg;
        if (sDeg == 0) sDeg = 90;
        if (servoPanText != null)
        {
            servoPanText.text = $"GIMBAL PAN: {sDeg:000}° [Q/E]";
        }
        if (servoTickIndicator != null)
        {
            float normServo = Mathf.InverseLerp(20f, 160f, sDeg);
            float barWidth = 220f;
            servoTickIndicator.anchoredPosition = new Vector2((normServo - 0.5f) * barWidth, 0f);
        }

        if (fsmActionText != null)
        {
            string act = !string.IsNullOrEmpty(state.active_action) ? state.active_action.ToUpper() : "MANUAL TELEOP";
            fsmActionText.text = $"FSM: {act}";
        }

        // 3. Ultrasonic Range & Collision Alert
        float dist = state.telemetry != null ? state.telemetry.ultrasonic_cm : 0f;
        float alertCm = _config != null ? _config.Profile.collisionAlertCm : 25f;
        float cautionCm = _config != null ? _config.Profile.collisionCautionCm : 55f;

        if (ultrasonicDistanceText != null)
        {
            ultrasonicDistanceText.text = dist > 0.5f ? $"SONAR: {dist:F0} cm" : "SONAR: CLEAR";
            ultrasonicDistanceText.color = (dist > 0.5f && dist < alertCm)
                ? new Color(1f, 0.25f, 0.3f, 1f)
                : ((dist > 0.5f && dist < cautionCm) ? new Color(1f, 0.85f, 0.2f, 1f) : new Color(0f, 0.95f, 1f, 1f));
        }

        if (obstacleAlertPanel != null)
        {
            bool alert = (dist > 0.5f && dist < alertCm);
            obstacleAlertPanel.SetActive(alert);
            if (obstacleDistanceText != null && alert)
            {
                obstacleDistanceText.text = $"⚠ PROXIMITY HAZARD — {dist:F0} CM AHEAD  |  AUTO-BRAKE / BACK OUT";
            }
        }

        // 4. AI Bounding Boxes on Center Viewport
        RenderAiDetections(state.ai_detections);

        // 5. Right Wing Panel: Attitude, Environment, and Hardware Health
        if (state.telemetry != null)
        {
            float pitch = state.telemetry.pitch_deg;
            float roll = state.telemetry.roll_deg;

            if (pitchText != null) pitchText.text = $"PITCH: {pitch:+0.0;-0.0}°";
            if (rollText != null) rollText.text = $"ROLL:  {roll:+0.0;-0.0}°";
            if (yawText != null) yawText.text = $"YAW:   {yaw:000.0}°";

            if (imuHorizonBall != null)
            {
                imuHorizonBall.localEulerAngles = new Vector3(0f, 0f, -roll);
                imuHorizonBall.anchoredPosition = new Vector2(0f, Mathf.Clamp(pitch * 1.8f, -35f, 35f));
            }

            if (envMetricsText != null)
            {
                float hi = state.telemetry.heat_index_c;
                string airSafety = hi > 41f ? "<color=#FF4455>HAZARD (EXTREME HEAT)</color>" :
                                   (hi > 32f ? "<color=#FFD166>CAUTION (WARM)</color>" : "<color=#00FF88>NOMINAL SAFE</color>");
                envMetricsText.text =
                    $"<b>TEMP:</b> {state.telemetry.temp_c:F1} °C       <b>HUMIDITY:</b> {state.telemetry.humidity:F0} %\n" +
                    $"<b>PRESS:</b> {state.telemetry.pressure_hpa:F0} hPa    <b>ALTITUDE:</b> {state.telemetry.altitude_m:F1} m\n" +
                    $"<b>HEAT IDX:</b> {hi:F1} °C    <b>STATUS:</b> {airSafety}";
            }

            if (hardwareHealthText != null)
            {
                string drv = state.telemetry.motor_driver_ok ? "<color=#00FF88>ONLINE</color>" : "<color=#FFD166>STANDBY</color>";
                string serPort = !string.IsNullOrEmpty(state.serial_port) ? state.serial_port : "/dev/ttyTHS1";
                string serState = state.serial_connected ? "<color=#00FF88>LINKED</color>" : "<color=#FF5566>DISCONNECTED</color>";
                string aiMode = state.ai_config != null && !string.IsNullOrEmpty(state.ai_config.mode) ? state.ai_config.mode.ToUpper() : "YOLOV8-TRT";

                hardwareHealthText.text =
                    $"<b>ESP32 SERIAL:</b> {serPort} [{serState}]\n" +
                    $"<b>MOTOR BRIDGE:</b> {drv}   |   <b>SONAR:</b> {(dist > 0 ? $"{dist:F0}cm" : "OK")}\n" +
                    $"<b>UART FRAMES:</b> RX {state.telemetry.rx_ok}  •  CRC ERR {state.telemetry.crc_errors}\n" +
                    $"<b>AI ENGINE:</b> {aiMode} ({aiFps:F1} FPS)";
            }
        }

        if (batteryText != null)
        {
            batteryText.text = "12.4 V  [92%]";
        }
        if (batteryBarFill != null)
        {
            batteryBarFill.fillAmount = 0.92f;
        }
    }

    private void RenderAiDetections(AIDetection[] detections)
    {
        if (aiBboxContainer == null) return;

        int count = detections != null ? detections.Length : 0;
        while (_activeBboxPool.Count < count)
        {
            GameObject boxObj = new GameObject($"AI_BBox_{_activeBboxPool.Count}");
            boxObj.transform.SetParent(aiBboxContainer, false);
            RectTransform rt = boxObj.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);

            Image img = boxObj.AddComponent<Image>();
            img.color = new Color(0f, 1f, 0.55f, 0.14f);
            img.raycastTarget = false;

            Outline ol = boxObj.AddComponent<Outline>();
            ol.effectColor = new Color(0f, 1f, 0.55f, 0.95f);
            ol.effectDistance = new Vector2(2f, -2f);

            GameObject lblObj = new GameObject("Label");
            lblObj.transform.SetParent(boxObj.transform, false);
            RectTransform lrt = lblObj.AddComponent<RectTransform>();
            lrt.anchorMin = new Vector2(0f, 1f);
            lrt.anchorMax = new Vector2(1f, 1f);
            lrt.pivot = new Vector2(0f, 0f);
            lrt.anchoredPosition = new Vector2(0f, 2f);
            lrt.sizeDelta = new Vector2(0f, 22f);

            TextMeshProUGUI tmp = lblObj.AddComponent<TextMeshProUGUI>();
            tmp.fontSize = 14f;
            tmp.fontStyle = FontStyles.Bold;
            tmp.color = new Color(0f, 1f, 0.55f, 1f);
            tmp.alignment = TextAlignmentOptions.BottomLeft;
            tmp.raycastTarget = false;

            _activeBboxPool.Add(boxObj);
        }

        float containerW = aiBboxContainer.rect.width > 10f ? aiBboxContainer.rect.width : 760f;
        float containerH = aiBboxContainer.rect.height > 10f ? aiBboxContainer.rect.height : 430f;

        float srcW = 640f;
        float srcH = 480f;
        if (_cameraReceiver != null && _cameraReceiver.VideoTexture != null && _cameraReceiver.VideoTexture.width > 16)
        {
            srcW = _cameraReceiver.VideoTexture.width;
            srcH = _cameraReceiver.VideoTexture.height;
        }

        for (int i = 0; i < _activeBboxPool.Count; i++)
        {
            if (i >= count)
            {
                _activeBboxPool[i].SetActive(false);
                continue;
            }

            var d = detections[i];
            float[] coords = (d.bbox != null && d.bbox.Length >= 4) ? d.bbox : d.box;
            if (coords == null || coords.Length < 4)
            {
                _activeBboxPool[i].SetActive(false);
                continue;
            }

            float x1 = coords[0];
            float y1 = coords[1];
            float x2 = coords[2];
            float y2 = coords[3];

            float normX = Mathf.Clamp01(x1 / srcW);
            float normY = Mathf.Clamp01(y1 / srcH);
            float normW = Mathf.Clamp01((x2 - x1) / srcW);
            float normH = Mathf.Clamp01((y2 - y1) / srcH);

            GameObject go = _activeBboxPool[i];
            go.SetActive(true);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchoredPosition = new Vector2(normX * containerW, -normY * containerH);
            rt.sizeDelta = new Vector2(Mathf.Max(24f, normW * containerW), Mathf.Max(24f, normH * containerH));

            var tmp = go.GetComponentInChildren<TextMeshProUGUI>();
            if (tmp != null)
            {
                float conf = d.confidence > 0.001f ? d.confidence : d.score;
                string lbl = !string.IsNullOrEmpty(d.label) ? d.label.ToUpper() : "TARGET";
                tmp.text = $"{lbl} {conf * 100f:F0}%";
            }
        }
    }

    private void OnSwarmListUpdated(List<DiscoveredPeerData> peers)
    {
        if (swarmStatusText == null) return;
        int peerCount = peers != null ? peers.Count : 0;
        swarmStatusText.text = $"mDNS MESH DISCOVERY: {peerCount} PEERS ACTIVE  |  HOTKEYS [1] [2] [3]";
    }

    private void UpdateStandbyPreviewMode()
    {
        float heading = (_timer * 12f) % 360f;
        if (compassHeadingText != null)
            compassHeadingText.text = $"◄  HDG {heading:000}° {GetCompassDirection(heading)}  ►";

        float roll = Mathf.Sin(_timer * 1.4f) * 4.5f;
        float pitch = Mathf.Cos(_timer * 1.1f) * 3.0f;
        if (pitchText != null) pitchText.text = $"PITCH: {pitch:+0.0;-0.0}°";
        if (rollText != null) rollText.text = $"ROLL:  {roll:+0.0;-0.0}°";
        if (yawText != null) yawText.text = $"YAW:   {heading:000.0}°";

        if (imuHorizonBall != null)
        {
            imuHorizonBall.localEulerAngles = new Vector3(0f, 0f, -roll);
            imuHorizonBall.anchoredPosition = new Vector2(0f, pitch * 1.8f);
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
