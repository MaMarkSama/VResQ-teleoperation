using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using VResQ.Core;
using VResQ.Video;
using VResQ.Control;

/// <summary>
/// Master Open-Visor Tactical VR HUD & Web-Dashboard Parity Controller for V-ResQ Rescue Operations.
/// Displays 100% of the exact same telemetry, Smart Assist, Mission Assessment, OLED Mirror,
/// AI Detections, and Swarm Fleet data as the V-ResQ Web Dashboard, while projecting the live
/// camera stream onto the 360°/220°/180° Surround Dome (ภาพรอบตัว) or toggling to External 3D World (มองเห็นรอบนอก).
/// </summary>
public class RescueRobotVRHUD : MonoBehaviour
{
    private static readonly string[] FSM_NAMES = { "IDLE", "MANUAL", "AUTO", "ESTOP", "FAULT" };

    [Header("--- Top Command Bar & Web Ops/Sensor Strip ---")]
    public TextMeshProUGUI robotIdText;
    public TextMeshProUGUI latencyText;
    public TextMeshProUGUI viewModeBadgeText;
    public TextMeshProUGUI aiStatusBadgeText;
    public TextMeshProUGUI clockText;
    public TextMeshProUGUI opsStripText;
    public TextMeshProUGUI sensorStripText;

    [Header("--- Open Center Visor & Propulsion Deck ---")]
    public RawImage centerVideoScreen;
    public RectTransform aiBboxContainer;
    public TextMeshProUGUI videoOverlayText;
    public TextMeshProUGUI compassHeadingText;
    public TextMeshProUGUI controllerStatusText;
    public RectTransform joystickDotIndicator;
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

    [Header("--- Left Wing: Local Map, Swarm Fleet & OLED Mirror ---")]
    public RectTransform leftPanelRect;
    public VResqRadarMap radarMap;
    public Button[] nodeCardButtons = new Button[3];
    public Image[] nodeCardBgs = new Image[3];
    public Image[] nodeCardDots = new Image[3];
    public TextMeshProUGUI[] nodeCardTexts = new TextMeshProUGUI[3];
    public TextMeshProUGUI oledPreviewText;
    public TextMeshProUGUI swarmStatusText;

    [Header("--- Right Wing: IMU Twin, Smart Assist & Mission Assessment ---")]
    public RectTransform rightPanelRect;
    public RectTransform imuHorizonBall;
    public TextMeshProUGUI pitchText;
    public TextMeshProUGUI rollText;
    public TextMeshProUGUI yawText;
    public TextMeshProUGUI envMetricsText;
    public TextMeshProUGUI smartAssistText;
    public TextMeshProUGUI missionAssessmentText;
    public TextMeshProUGUI hardwareHealthText;
    public TextMeshProUGUI batteryText;
    public Image batteryBarFill;

    [Header("--- Legacy Compatibility Aliases ---")]
    public TextMeshProUGUI healthText;
    public TextMeshProUGUI sensorListText;

    [Header("--- User Experience & Audience Tier ---")]
    public HudDensityMode hudDensity = HudDensityMode.Streamlined;
    public TextMeshProUGUI hudDensityBadgeText;
    public TextMeshProUGUI quickHelpHintText;

    public enum HudDensityMode
    {
        Streamlined = 0, // คนทั่วไป: มองง่าย รู้ทันที ไม่บดบังทัศนวิสัยภาพรอบตัว
        TacticalExpert = 1 // ผู้เชี่ยวชาญ: เจาะลึกข้อมูลเซ็นเซอร์ทั้งระบบ สถิติ Swarm, Serial Bus, และการวิเคราะห์สภาพแวดล้อม
    }

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

    private float _missionElapsedSec = 0f;
    private int _selectedOledPage = 0;
    private VResqTeleopController _teleop;
    private VResqCameraReceiver _cameraReceiver;
    private VResqFrameworkConfig _config;
    private readonly List<GameObject> _activeBboxPool = new List<GameObject>();

    private void Awake()
    {
        _config = GetComponent<VResqFrameworkConfig>();
        if (_config == null) _config = gameObject.AddComponent<VResqFrameworkConfig>();

        var client = GetComponent<VResqClient>();
        if (client == null)
        {
            client = gameObject.AddComponent<VResqClient>();
            client.robotIp = "172.20.10.4";
            client.port = 8000;
        }

        _cameraReceiver = GetComponent<VResqCameraReceiver>();
        if (_cameraReceiver == null) _cameraReceiver = gameObject.AddComponent<VResqCameraReceiver>();

        _teleop = GetComponent<VResqTeleopController>();
        if (_teleop == null) _teleop = gameObject.AddComponent<VResqTeleopController>();

        surroundDisplay = GetComponent<VResqSurroundDisplay>();
        if (surroundDisplay == null) surroundDisplay = gameObject.AddComponent<VResqSurroundDisplay>();

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
            else if (n.Contains("BTN_OLED_NEXT")) b.onClick.AddListener(CycleOledPage);
            else if (n.Contains("BTN_SURVEY")) b.onClick.AddListener(() => VResqClient.Instance?.SendActionCommand("patrol"));
            else if (n.Contains("BTN_RTH")) b.onClick.AddListener(() => VResqClient.Instance?.SendActionCommand("return_home"));
            else if (n.Contains("BTN_RADAR_SWEEP")) b.onClick.AddListener(() => VResqClient.Instance?.SendActionCommand("radar_sweep"));
            else if (n.Contains("BTN_SCAN")) b.onClick.AddListener(() => _teleop?.ActionScan());
            else if (n.Contains("BTN_BEACON")) b.onClick.AddListener(() => _teleop?.ActionBeacon());
            else if (n.Contains("BTN_CENTER")) b.onClick.AddListener(() => { _teleop?.ActionCenter(); _teleop?.ResetLookOrientation(); });
            else if (n.Contains("BTN_APPROACH")) b.onClick.AddListener(() => _teleop?.ActionApproach());
            else if (n.Contains("BTN_BACK")) b.onClick.AddListener(() => _teleop?.ActionBackOut());
            else if (n.Contains("BTN_EXT_CAM")) b.onClick.AddListener(() => surroundDisplay?.ToggleExternalVsCamera());
            else if (n.Contains("BTN_VIEW") || n.Contains("BTN_SURROUND")) b.onClick.AddListener(() => surroundDisplay?.CycleNextViewMode());
            else if (n.Contains("BTN_HUD_MODE") || n.Contains("BTN_DENSITY")) b.onClick.AddListener(ToggleHudDensity);
            else if (n.Contains("BTN_CONFIG") || n.Contains("BTN_SETTINGS")) b.onClick.AddListener(ToggleSettingsModal);
            else if (n.Contains("BTN_ESTOP") || n.Contains("BTN_STOP")) b.onClick.AddListener(() => _teleop?.ActionEmergencyStop());
            // Framework Settings Modal Controls
            else if (n.Contains("CFG_CLOSE")) b.onClick.AddListener(ToggleSettingsModal);
            else if (n.Contains("CFG_VIEW_CYCLE")) b.onClick.AddListener(() => { surroundDisplay?.CycleNextViewMode(); RefreshConfigModalText(); });
            else if (n.Contains("CFG_EXT_TOGGLE")) b.onClick.AddListener(() => { surroundDisplay?.ToggleExternalVsCamera(); RefreshConfigModalText(); });
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
            else if (n.Contains("CFG_RECENTER")) b.onClick.AddListener(() => _teleop?.ResetLookOrientation());
            else if (n.Contains("CFG_SAVE")) b.onClick.AddListener(() => { _config?.SaveProfile(); RefreshConfigModalText(); });
            else if (n.Contains("CFG_RESET")) b.onClick.AddListener(() => { _config?.ResetToDefaults(); RefreshConfigModalText(); });
        }
    }

    public void CycleOledPage()
    {
        _selectedOledPage = (_selectedOledPage + 1) % 4;
        VResqClient.Instance?.SendOledPageCommand(_selectedOledPage);
        if (VResqClient.Instance != null && VResqClient.Instance.LatestState != null)
        {
            RenderOledPreview(_selectedOledPage, VResqClient.Instance.LatestState);
        }
    }

    public void SelectFleetNode(int index)
    {
        VResqClient.Instance?.SwitchToNodeIndex(index);
        RefreshFleetNodeCards();
    }

    public void ToggleHudDensity()
    {
        hudDensity = (hudDensity == HudDensityMode.Streamlined)
            ? HudDensityMode.TacticalExpert
            : HudDensityMode.Streamlined;

        ApplyHudDensityVisuals();
        if (VResqClient.Instance != null && VResqClient.Instance.LatestState != null)
        {
            OnRobotStateReceived(VResqClient.Instance.LatestState);
        }
    }

    public void ApplyHudDensityVisuals()
    {
        if (hudDensityBadgeText != null)
        {
            hudDensityBadgeText.text = (hudDensity == HudDensityMode.Streamlined)
                ? "<b>HUD:</b> <color=#00FF88>🟢 STREAMLINED (มุมมองโปร่งสบาย)</color> <size=12><color=#88AACC>[H/R3 เพื่อเจาะลึก]</color></size>"
                : "<b>HUD:</b> <color=#00E5FF>🔷 TACTICAL EXPERT (โหมดผู้เชี่ยวชาญ)</color> <size=12><color=#88AACC>[H/R3 เพื่อย่อ]</color></size>";
        }

        if (quickHelpHintText != null)
        {
            quickHelpHintText.text =
                "<color=#00F0FF><b>🎮 [LS]</b> ขับเคลื่อน</color>   " +
                "<color=#00FF88><b>[RS]</b> หันกล้องรอบตัว</color>   " +
                "<color=#FFB703><b>[Y/F]</b> สลับภาพรอบตัว/ภายนอก</color>   " +
                "<color=#00E5FF><b>[H / R3]</b> สลับ HUD มองง่าย ⟷ เจาะลึก</color>   " +
                "<color=#00FF88><b>[LB / 1-3]</b> สลับหุ่นยนต์</color>";
        }
    }

    public void ToggleSettingsModal()
    {
        if (settingsModalRoot == null) return;
        bool nextState = !settingsModalRoot.activeSelf;
        settingsModalRoot.SetActive(nextState);
        if (nextState) RefreshConfigModalText();
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

        if (leftPanelRect != null)
        {
            leftPanelRect.localEulerAngles = new Vector3(0f, -p.wingPanelAngle, 0f);
        }
        if (rightPanelRect != null)
        {
            rightPanelRect.localEulerAngles = new Vector3(0f, p.wingPanelAngle, 0f);
        }

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
        string viewDesc = p.viewMode == SurroundViewMode.ExternalWorld
            ? "EXTERNAL 3D WORLD (มองเห็นรอบนอก)"
            : $"SURROUND CAMERA {p.surroundArcAngle:F0}° (ภาพจากกล้องรอบตัว)";
        string deadmanMode = p.requireDeadmanSwitch ? "HOLD TRIGGER/SHIFT" : "100% IMMEDIATE JOYSTICK DRIVE";
        string ctrlName = _teleop != null ? _teleop.ActiveControllerName : "READY";

        cfgSummaryText.text =
            $"<b>ACTIVE VIEW:</b> <color=#00F0FF>{viewDesc}</color>\n" +
            $"<b>JOYSTICK / CONTROLLER:</b> <color=#00FF88>{ctrlName}</color>\n" +
            $"<b>SURROUND RADIUS:</b> {p.surroundRadius:F1} m   |   <b>HUD DISTANCE:</b> {p.hudDistance:F2} m   |   <b>WING ANGLE:</b> {p.wingPanelAngle:F0}°\n" +
            $"<b>MAX SPEED LIMIT:</b> {p.speedScale * 100f:F0}%   |   <b>DRIVE MODE:</b> <color=#00FFAA>{deadmanMode}</color>";
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
                string rName = !string.IsNullOrEmpty(n.remoteNodeName) ? n.remoteNodeName.ToUpper() : n.displayName;
                string role = !string.IsNullOrEmpty(n.remoteRole) ? n.remoteRole.ToUpper() : n.roleLabel;
                string statusStr = n.isOnline
                    ? $"<color=#00FF77>ONLINE {n.lastRttMs:F0}ms</color> (AI {n.detectorFps:F1}fps)"
                    : "<color=#FF5566>OFFLINE</color>";
                string activeTag = isSelected ? " <color=#00F0FF>◀ ACTIVE</color>" : "";
                nodeCardTexts[i].text = $"<b>[{i + 1}] {rName} ({role})</b>{activeTag}\n<size=12>{n.ipAddress}:{n.port} • {statusStr}</size>";
            }
        }
    }

    private void Update()
    {
        _missionElapsedSec += Time.deltaTime;

        if (clockText != null)
        {
            TimeSpan ts = TimeSpan.FromSeconds(_missionElapsedSec);
            clockText.text = $"MISSION {ts.Hours:00}:{ts.Minutes:00}:{ts.Seconds:00}";
        }

        if (viewModeBadgeText != null && surroundDisplay != null)
        {
            string modeHint = (hudDensity == HudDensityMode.Streamlined) ? " <color=#00FF88>[STREAMLINED]</color>" : " <color=#00E5FF>[EXPERT]</color>";
            viewModeBadgeText.text = surroundDisplay.currentViewMode switch
            {
                SurroundViewMode.ExternalWorld => $"🌐 VIEW: EXTERNAL WORLD (มองเห็นรอบนอก) [Y/F]{modeHint}",
                SurroundViewMode.Surround360 => $"🎥 VIEW: 360° SURROUND CAM (ภาพกล้องรอบตัว) [Y/V]{modeHint}",
                SurroundViewMode.Surround220 => $"🎥 VIEW: 220° PANORAMA CAM (ภาพกล้องรอบตัว) [Y/V]{modeHint}",
                SurroundViewMode.Surround180 => $"🎥 VIEW: 180° DOME CAM (ภาพกล้องรอบตัว) [Y/V]{modeHint}",
                _ => $"🎥 VIEW: SURROUND CAM [Y/V]{modeHint}"
            };
        }

        ApplyHudDensityVisuals();

        // Feed live robot camera texture to center FPV visor screen
        if (centerVideoScreen != null && _cameraReceiver != null)
        {
            if (_cameraReceiver.targetRawImage != centerVideoScreen)
            {
                _cameraReceiver.targetRawImage = centerVideoScreen;
            }
            if (_cameraReceiver.VideoTexture != null && centerVideoScreen.texture != _cameraReceiver.VideoTexture)
            {
                centerVideoScreen.texture = _cameraReceiver.VideoTexture;
                centerVideoScreen.color = Color.white;
            }
        }

        if (_teleop != null)
        {
            if (controllerStatusText != null)
            {
                Vector2 st = _teleop.LiveDriveStick;
                string joyIcon = _teleop.HasPhysicalJoystick ? "🎮 JOYSTICK:" : "⌨ INPUT:";
                controllerStatusText.text = $"{joyIcon} <b>{_teleop.ActiveControllerName}</b>  |  L-STICK ({st.x:+0.00;-0.00}, {st.y:+0.00;-0.00})";
                controllerStatusText.color = _teleop.HasPhysicalJoystick ? new Color(0f, 1f, 0.55f, 1f) : new Color(0.6f, 0.92f, 1f, 1f);
            }

            if (joystickDotIndicator != null)
            {
                Vector2 st = _teleop.LiveDriveStick;
                joystickDotIndicator.anchoredPosition = new Vector2(st.x * 26f, st.y * 26f);
            }
        }

        bool isDeadman = (_teleop == null || _teleop.IsDeadmanActive);
        if (deadmanStatusText != null)
        {
            bool reqHold = _config != null && _config.Profile.requireDeadmanSwitch;
            deadmanStatusText.text = isDeadman
                ? (reqHold ? "JOYSTICK ENGAGED (DEADMAN HELD)" : "JOYSTICK READY (100% FULL CONTROL)")
                : "DRIVE LOCKED (HOLD TRIGGER / SHIFT)";
        }
        if (deadmanIndicatorDot != null)
        {
            deadmanIndicatorDot.color = isDeadman ? new Color(0f, 1f, 0.45f, 1f) : new Color(1f, 0.3f, 0.25f, 1f);
        }
    }

    private void OnRobotConnected()
    {
        RefreshFleetNodeCards();
    }

    private void OnRobotDisconnected()
    {
        RefreshFleetNodeCards();
        if (latencyText != null)
        {
            latencyText.text = "DISCONNECTED (AUTO-SWITCHING...)";
            latencyText.color = new Color(1f, 0.78f, 0.2f, 1f);
        }
        if (obstacleAlertPanel != null)
            obstacleAlertPanel.SetActive(false);
    }

    /// <summary>
    /// Renders 100% of the exact same state fields as renderState(s) in the Web Dashboard's app.js!
    /// </summary>
    private void OnRobotStateReceived(RobotStateSnapshot s)
    {
        if (s == null) return;
        TelemetryData t = s.telemetry ?? new TelemetryData();
        SensorStatusFlags sensors = t.sensors ?? new SensorStatusFlags();
        bool freshTelemetry = s.last_telem_age_ms > 0.01f && s.last_telem_age_ms < 1200f;

        // 1. Robot Identity & Link Status (Matches Web TopBar & ops-strip)
        var id = (s.identity != null && !string.IsNullOrEmpty(s.identity.node_name)) ? s.identity : s.node_info;
        string rName = id != null && !string.IsNullOrEmpty(id.node_name) ? id.node_name : "vresq-node-zero";
        string role = id != null && !string.IsNullOrEmpty(id.role) ? id.role : "t2-dashboard";
        string curIp = VResqClient.Instance != null ? VResqClient.Instance.robotIp : "172.20.10.4";

        if (robotIdText != null)
        {
            string tierTag = (hudDensity == HudDensityMode.Streamlined) ? "<color=#00FF88>● EASY</color>" : "<color=#00F0FF>◆ EXPERT</color>";
            robotIdText.text = $"{tierTag} <b>{rName.ToUpper()}</b>  <color=#88AACC>[{role.ToUpper()}]</color>  •  {curIp}";
        }

        if (latencyText != null && VResqClient.Instance != null)
        {
            float rtt = VResqClient.Instance.LatencyMs;
            float camFps = _cameraReceiver != null ? _cameraReceiver.StreamFps : 0f;
            int frames = _cameraReceiver != null ? _cameraReceiver.FramesReceived : 0;
            string camStr = frames > 0 ? $"LIVE {Mathf.Max(1f, camFps):F0} FPS" : "CONNECTING...";
            string linkColor = rtt < 40f ? "#00FF88" : (rtt < 100f ? "#FFD166" : "#FF5566");
            latencyText.text = $"<color={linkColor}>ONLINE {rtt:F0}ms</color>  |  CAM: <color=#00F0FF>{camStr}</color>";
            latencyText.color = new Color(0f, 1f, 0.5f, 1f);
        }

        // Ensure current node is marked online in Fleet Cards whenever state is received
        if (_config != null && _config.Profile.fleetNodes != null && VResqClient.Instance != null)
        {
            int idx = VResqClient.Instance.ActiveNodeIndex;
            if (idx >= 0 && idx < _config.Profile.fleetNodes.Length)
            {
                var fn = _config.Profile.fleetNodes[idx];
                fn.isOnline = true;
                fn.lastRttMs = VResqClient.Instance.LatencyMs;
                if (!string.IsNullOrEmpty(rName)) fn.remoteNodeName = rName;
                if (!string.IsNullOrEmpty(role)) fn.remoteRole = role;
                if (s.ai_config != null) fn.detectorFps = s.ai_config.detector_fps;
                RefreshFleetNodeCards();
            }
        }

        // 2. Web Dashboard Ops Strip & 6-Sensor Strip
        bool espOnline = s.esp32_online || (s.sensor_status != null && s.sensor_status.esp32_online) || (freshTelemetry && t.rx_ok > 0);
        string fsmName = (t.fsm_state >= 0 && t.fsm_state < FSM_NAMES.Length) ? FSM_NAMES[t.fsm_state] : t.fsm_state.ToString();
        string serialStr = espOnline
            ? $"<color=#00FF88>ESP32 ONLINE ({s.serial_port})</color>"
            : (s.serial_connected ? $"<color=#FFD166>UART OPEN ({s.serial_port})</color>" : "<color=#00F0FF>HOST TELEMETRY (LIVE-HW)</color>");
        string actStr = !string.IsNullOrEmpty(s.active_action) ? s.active_action : "idle";
        string cmdAgeStr = s.last_command_age_ms > 0.01f ? $"{s.last_command_age_ms:F0} ms" : "--";
        string telAgeStr = espOnline && s.last_telem_age_ms > 0.01f ? $"{s.last_telem_age_ms:F0} ms" : "HOST LIVE";

        float headingDeg = Mathf.Abs(t.yaw_deg) > 0.01f ? t.yaw_deg : (s.pose_heading * Mathf.Rad2Deg);
        float normHdg = ((headingDeg % 360f) + 360f) % 360f;
        float dispTemp = (freshTelemetry && sensors.bme280) ? t.temp_c :
            ((s.environment != null && s.environment.display_temp_c > 0.1f) ? s.environment.display_temp_c :
            ((s.host != null && s.host.board_temp_c > 0.1f) ? s.host.board_temp_c :
            ((s.host != null && s.host.cpu_temp_c > 0.1f) ? s.host.cpu_temp_c : t.temp_c)));
        string tempSourceTag = (freshTelemetry && sensors.bme280) ? "ENV" : "BOARD";

        if (opsStripText != null)
        {
            if (hudDensity == HudDensityMode.Streamlined)
            {
                opsStripText.text =
                    $"<b>หุ่นยนต์:</b> <color=#00F0FF>{rName}</color>   •   <b>การเชื่อมต่อ:</b> {serialStr}   •   <b>โหมด FSM:</b> <color=#00FF88>{fsmName}</color>   •   <b>ภารกิจ:</b> <color=#FFD166>{actStr.ToUpper()}</color>";
            }
            else
            {
                opsStripText.text =
                    $"<b>Robot:</b> {rName}   |   <b>Bus:</b> {serialStr}   |   <b>FSM:</b> {fsmName}   |   <b>Action:</b> <color=#00F0FF>{actStr}</color>   |   <b>Cmd Age:</b> {cmdAgeStr}   |   <b>Telem:</b> {telAgeStr}";
            }
        }

        if (sensorStripText != null)
        {
            string usCard = (freshTelemetry && sensors.ultrasonic) ? $"<color=#00FF88>{t.ultrasonic_cm:F0} cm</color>" : (t.ultrasonic_cm > 0 ? $"{t.ultrasonic_cm:F0} cm" : "standby");
            string bmeCard = (freshTelemetry && sensors.bme280)
                ? $"<color=#00FF88>{t.temp_c:F1}°C / {t.humidity:F0}%</color>"
                : (dispTemp > 0.1f ? $"<color=#00F0FF>Host {dispTemp:F1}°C</color>" : "standby");
            string mpuCard = (freshTelemetry && sensors.mpu6050)
                ? $"<color=#00FF88>P:{t.pitch_deg:F0}° R:{t.roll_deg:F0}°</color>"
                : $"<color=#00F0FF>ODO {normHdg:000.0}°</color>";
            string oledCard = (freshTelemetry && sensors.oled) ? $"<color=#00FF88>Page {s.oled_page}</color>" : $"Page {s.oled_page}";
            string altCard = (freshTelemetry && sensors.bme280)
                ? $"{t.altitude_m:F1} m"
                : (s.host != null ? $"RAM {s.host.ram_pct:F0}% / Disk {s.host.disk_pct:F0}%" : $"{t.altitude_m:F1} m");
            string yawCard = $"{normHdg:000.0}°";

            if (hudDensity == HudDensityMode.Streamlined)
            {
                float alertLimit = _config != null ? _config.Profile.collisionAlertCm : 25f;
                string distPill = t.ultrasonic_cm > 0.5f 
                    ? (t.ultrasonic_cm < alertLimit ? $"<color=#FF2A6D>⚠ {t.ultrasonic_cm:F0} cm (ใกล้)</color>" : $"<color=#00FF88>{t.ultrasonic_cm:F0} cm (ปลอดภัย)</color>") 
                    : "<color=#70889B>-- cm (ทางโล่ง)</color>";
                string tempPill = dispTemp > 0.1f ? $"<color=#00F0FF>{dispTemp:F1}°C ({tempSourceTag})</color>" : "<color=#70889B>--</color>";
                string hdgPill = $"<color=#00FF88>{normHdg:000}° {GetCompassDirection(normHdg)}</color>";
                sensorStripText.text =
                    $"<b>ระยะข้างหน้า:</b> {distPill}   •   <b>อุณหภูมิ:</b> {tempPill}   •   <b>ทิศทาง:</b> {hdgPill}";
            }
            else
            {
                sensorStripText.text =
                    $"<b>Ultrasonic:</b> {usCard}   |   <b>Thermal:</b> {bmeCard}   |   <b>IMU/Odo:</b> {mpuCard}   |   <b>OLED:</b> {oledCard}   |   <b>Host:</b> {altCard}   |   <b>Fused Yaw:</b> <color=#00F0FF>{yawCard}</color>";
            }
        }

        // 3. AI Detection Badge & Bounding Boxes (Matches Web AI: JETSON (51 DET | 9.2 FPS))
        int aiCount = s.ai_detections != null ? s.ai_detections.Length : 0;
        int totalDet = s.ai_config != null ? s.ai_config.total_detections : 0;
        float aiFps = s.ai_config != null ? s.ai_config.detector_fps : 0f;
        string aiMode = (s.ai_config != null && !string.IsNullOrEmpty(s.ai_config.mode)) ? s.ai_config.mode.ToUpper() : "JETSON";

        if (aiStatusBadgeText != null)
        {
            aiStatusBadgeText.text = $"AI: {aiMode} ({aiCount} LIVE / {totalDet} TOTAL | {aiFps:F1} FPS)";
            aiStatusBadgeText.color = aiCount > 0 ? new Color(1f, 0.85f, 0.2f, 1f) : new Color(0f, 1f, 0.55f, 1f);
        }

        RenderAiDetections(s.ai_detections);

        // 4. Center Compass & Drive / Propulsion Telemetry
        if (compassHeadingText != null)
        {
            compassHeadingText.text = $"◄  HDG {normHdg:000.0}° {GetCompassDirection(normHdg)}  |  POSE ({s.pose_x:+0.0;-0.0}, {s.pose_y:+0.0;-0.0})  ►";
        }

        float throttlePct = (s.speed_scale > 0.01f ? s.speed_scale : (_teleop != null ? _teleop.EffectiveSpeedScale : 0.75f)) * 100f;
        string torquePreset = throttlePct >= 95f ? "TURBO 100%" : (throttlePct >= 80f ? "NORM 85%" : "ECO 65%");
        if (speedPercentText != null)
        {
            speedPercentText.text = $"SPEED: {throttlePct:F0}% ({torquePreset})";
        }
        if (speedBarFill != null)
        {
            speedBarFill.fillAmount = Mathf.Clamp01(throttlePct / 100f);
        }

        int lPwm = (t.motor_left_cmd != 0) ? t.motor_left_cmd : s.left_pwm;
        int rPwm = (t.motor_right_cmd != 0) ? t.motor_right_cmd : s.right_pwm;
        // If user is pushing joystick locally while robot motors are 0, show responsive feedback
        if (lPwm == 0 && rPwm == 0 && _teleop != null && (_teleop.SimulatedLeftPwm != 0 || _teleop.SimulatedRightPwm != 0))
        {
            lPwm = _teleop.SimulatedLeftPwm;
            rPwm = _teleop.SimulatedRightPwm;
        }
        UpdateMotorGauges(lPwm, rPwm);

        int sDeg = s.servo_deg > 0 ? s.servo_deg : t.servo_deg;
        UpdateServoGauge(sDeg);

        if (fsmActionText != null)
        {
            fsmActionText.text = $"DRIVE: L {lPwm} / R {rPwm}  •  ACTION: {actStr.ToUpper()}";
        }

        float dist = t.ultrasonic_cm;
        float alertCm = _config != null ? _config.Profile.collisionAlertCm : 25f;
        if (ultrasonicDistanceText != null)
        {
            ultrasonicDistanceText.text = dist > 0.5f ? $"DISTANCE: {dist:F0} cm" : "DISTANCE: -- cm (NO ECHO)";
            ultrasonicDistanceText.color = (dist > 0.5f && dist < alertCm) ? new Color(1f, 0.25f, 0.3f, 1f) : new Color(0f, 0.95f, 1f, 1f);
        }

        if (obstacleAlertPanel != null)
        {
            bool alert = (dist > 0.5f && dist < alertCm);
            obstacleAlertPanel.SetActive(alert);
            if (obstacleDistanceText != null && alert)
            {
                obstacleDistanceText.text = $"⚠ PROXIMITY HAZARD — {dist:F0} CM AHEAD  |  AUTO-BRAKE";
            }
        }

        // 5. OLED Display Mirror (Exact match to renderOledPreview in Web app.js)
        RenderOledPreview(s.oled_page, s);

        // 6. Right Wing Card 1: IMU & Radar Twin + 8-Metric Grid
        float pitch = t.pitch_deg;
        float roll = t.roll_deg;
        if (pitchText != null) pitchText.text = $"PITCH: {pitch:+0.0;-0.0}°";
        if (rollText != null) rollText.text = $"ROLL:  {roll:+0.0;-0.0}°";
        if (yawText != null) yawText.text = $"YAW:   {normHdg:000.0}°";

        if (imuHorizonBall != null)
        {
            imuHorizonBall.localEulerAngles = new Vector3(0f, 0f, -roll);
            imuHorizonBall.anchoredPosition = new Vector2(0f, Mathf.Clamp(pitch * 1.8f, -35f, 35f));
        }

        if (envMetricsText != null)
        {
            bool hasBme = freshTelemetry && sensors.bme280;
            float cpuTemp = s.host != null ? s.host.cpu_temp_c : 0f;
            float ramPct = s.host != null ? s.host.ram_pct : 0f;
            float diskPct = s.host != null ? s.host.disk_pct : 0f;
            float load1m = s.host != null ? s.host.load_1m : 0f;

            if (hudDensity == HudDensityMode.Streamlined)
            {
                if (hasBme)
                {
                    envMetricsText.text =
                        $"<b>อุณหภูมิ:</b> <color=#00F0FF>{t.temp_c:F1}°C</color>   <b>ความชื้น:</b> <color=#00FF88>{t.humidity:F0}%</color>   <b>ความกดอากาศ:</b> <color=#FFD166>{t.pressure_hpa:F0} hPa</color>";
                }
                else
                {
                    envMetricsText.text =
                        $"<b>อุณหภูมิบอร์ด:</b> <color=#00F0FF>{dispTemp:F1}°C</color>   <b>CPU:</b> <color=#00FF88>{cpuTemp:F1}°C</color>   <b>RAM/Disk:</b> <color=#FFD166>{ramPct:F0}% / {diskPct:F0}%</color>";
                }
            }
            else
            {
                if (hasBme)
                {
                    envMetricsText.text =
                        $"<b>Pitch:</b> {pitch:F1}°     <b>Roll:</b> {roll:F1}°     <b>Yaw/Hdg:</b> {normHdg:F1}°     <b>Alt:</b> {t.altitude_m:F1} m\n" +
                        $"<b>Temp:</b> {t.temp_c:F1} °C    <b>Humid:</b> {t.humidity:F0} %    <b>Pressure:</b> {t.pressure_hpa:F0} hPa    <b>Heat Idx:</b> {t.heat_index_c:F1} °C";
                }
                else
                {
                    envMetricsText.text =
                        $"<b>Pitch:</b> {pitch:F1}°     <b>Roll:</b> {roll:F1}°     <b>Yaw/Hdg:</b> {normHdg:F1}° [ODO]     <b>Load:</b> {load1m:F2}\n" +
                        $"<b>Board Temp:</b> {dispTemp:F1} °C    <b>CPU Temp:</b> {cpuTemp:F1} °C    <b>RAM:</b> {ramPct:F0} %    <b>Disk:</b> {diskPct:F0} %";
                }
            }
        }

        // 7. Right Wing Card 2: Smart Assist (Exact match to renderSmartAssist in Web app.js)
        int onlineSensorCount = (sensors.ultrasonic ? 1 : 0) + (sensors.bme280 ? 1 : 0) + (sensors.mpu6050 ? 1 : 0) + (sensors.oled ? 1 : 0);
        int score = 0;
        if (espOnline)
        {
            score += 50;
            score += onlineSensorCount * 12;
        }
        else
        {
            // LIVE-HW Host Compute Readiness Score
            score = 92;
            if (s.serial_connected) score += 4;
            if (dispTemp > 78f) score -= 12;
        }
        if (dist > 0f && dist < 15f) score -= 20;
        score = Mathf.Clamp(score, 0, 100);

        float curRtt = VResqClient.Instance != null ? VResqClient.Instance.LatencyMs : 12f;
        string scoreColor = score >= 75 ? "#00FF88" : (score >= 45 ? "#FFD166" : "#FF5566");
        string linkQual = espOnline
            ? $"<color=#00FF88>{s.last_telem_age_ms:F0} ms live</color>"
            : $"<color=#00FF88>WS {curRtt:F0} ms live</color>";
        string obsStatus = dist > 0f ? (dist < 15f ? $"<color=#FF5566>close {dist:F0} cm</color>" : $"<color=#00FF88>{dist:F0} cm clear</color>") : "clear (no echo)";
        string busStatus = espOnline
            ? $"<color=#00FF88>{onlineSensorCount}/4 sensors online</color>"
            : (s.serial_connected ? $"<color=#FFD166>UART {s.serial_port} (wait ESP32)</color>" : "<color=#00F0FF>Host Compute Online (LIVE-HW)</color>");
        string suggestion = (dist > 0f && dist < 20f)
            ? "Obstacle close ahead — Back Out or Turn"
            : (espOnline ? "ESP32 & Host nominal — Ready for Teleop / Survey" : "Host Compute & Camera Ready — Connect ESP32 for Motor/Sensor I/O");

        if (smartAssistText != null)
        {
            if (hudDensity == HudDensityMode.Streamlined)
            {
                smartAssistText.text =
                    $"<b>ความพร้อมระบบ:</b> <color={scoreColor}>{score}%</color>   •   <b>สิ่งกีดขวาง:</b> {obsStatus}\n" +
                    $"<b>คำแนะนำ:</b> <color=#00F0FF>{suggestion}</color>";
            }
            else
            {
                smartAssistText.text =
                    $"<b>Health Score:</b> <color={scoreColor}>{score}%</color>   |   <b>Link:</b> {linkQual}   |   <b>Obstacle:</b> {obsStatus}\n" +
                    $"<b>Sensor Bus:</b> {busStatus}   |   <b>Roughness:</b> {t.terrain_roughness:F2}\n" +
                    $"<b>Suggestion:</b> <color=#00F0FF>{suggestion}</color>";
            }
        }

        // 8. Right Wing Card 3: Mission Assessment & Intelligence (Matches Web assessment-panel)
        int survScore = (s.analytics != null && s.analytics.survivability != null && s.analytics.survivability.score > 0)
            ? s.analytics.survivability.score
            : 100;
        string tractionStr = (s.analytics != null && s.analytics.traction != null && !string.IsNullOrEmpty(s.analytics.traction.status))
            ? s.analytics.traction.status.Replace("_", " ")
            : "TRACTION NOMINAL (สภาพพื้นผิวปกติ)";
        float dewPoint = t.temp_c - ((100f - Mathf.Max(1f, t.humidity)) / 5f);
        float maxTilt = Mathf.Max(Mathf.Abs(pitch), Mathf.Abs(roll));
        string terrainGrade = maxTilt > 20f ? "<color=#FF5566>GRADE C (ลาดชันสูง)</color>" : (maxTilt > 10f ? "<color=#FFD166>GRADE B (เอียงเล็กน้อย)</color>" : "<color=#00FF88>GRADE A (พื้นเรียบ ปลอดภัย)</color>");

        if (hardwareHealthText != null)
        {
            if (hudDensity == HudDensityMode.Streamlined)
            {
                hardwareHealthText.text =
                    $"<b>อัตราการอยู่รอด (SURVIVABILITY):</b> <size=18><color=#00FF88>{survScore}%</color></size>\n" +
                    $"<b>สภาพพื้นผิว:</b> <color=#00F0FF>{tractionStr}</color>   •   <b>ความชัน:</b> {terrainGrade}";
            }
            else
            {
                hardwareHealthText.text =
                    $"<b>SURVIVABILITY SCORE: <color=#00FF88>{survScore}%</color></b>\n" +
                    $"• <b>การยึดเกาะ/ล้อขับ:</b> <color=#00FF88>{tractionStr}</color>\n" +
                    $"• <b>สภาพโพรงอากาศ:</b> STABLE ATMOSPHERE   |   <b>จุดน้ำค้าง:</b> DP {dewPoint:F1}°C\n" +
                    $"• <b>ความลาดชัน:</b> {terrainGrade}\n" +
                    $"• <b>AI Field Assessment:</b> โหนด {rName} ออนไลน์ (AI {totalDet} เหตุการณ์, กล้อง Servo {sDeg}°)";
            }
        }
    }

    private void RenderOledPreview(int page, RobotStateSnapshot s)
    {
        if (oledPreviewText == null || s == null) return;
        TelemetryData t = s.telemetry ?? new TelemetryData();
        SensorStatusFlags sensors = t.sensors ?? new SensorStatusFlags();
        bool fresh = s.last_telem_age_ms > 0.01f && s.last_telem_age_ms < 1200f;
        string fsm = (t.fsm_state >= 0 && t.fsm_state < FSM_NAMES.Length) ? FSM_NAMES[t.fsm_state] : "--";

        string line0, line1, line2;
        switch (page)
        {
            case 1:
                line0 = "OLED [PAGE 1: SENSORS]";
                line1 = $"US {(sensors.ultrasonic ? "OK" : "--")} {t.ultrasonic_cm:F0}cm";
                line2 = $"BME {(sensors.bme280 ? "OK" : "--")}  MPU {(sensors.mpu6050 ? "OK" : "--")}";
                break;
            case 2:
                line0 = "OLED [PAGE 2: MOTION]";
                line1 = $"L {s.left_pwm}   R {s.right_pwm}";
                line2 = $"P {t.pitch_deg:F1}°   R {t.roll_deg:F1}°";
                break;
            case 3:
                line0 = "OLED [PAGE 3: LINK]";
                line1 = $"RX {t.rx_ok}   CRC {t.crc_errors}";
                line2 = fresh ? "UART2 live" : "waiting for ESP32";
                break;
            default:
                line0 = "OLED [PAGE 0: MAIN]";
                line1 = $"FSM {fsm}   RX {t.rx_ok}";
                line2 = $"US {t.ultrasonic_cm:F0}cm   Servo {s.servo_deg}°";
                break;
        }

        oledPreviewText.text = $"<color=#00F0FF>{line0}</color>\n<b>{line1}</b>\n<size=12><color=#99CCEE>{line2}</color></size>";
    }

    private void UpdateMotorGauges(int lPwm, int rPwm)
    {
        if (motorLeftText != null)
        {
            motorLeftText.text = $"L-MOTOR: {lPwm} PWM";
            motorLeftText.color = lPwm != 0 ? new Color(0f, 1f, 0.65f, 1f) : new Color(0.75f, 0.9f, 1f, 0.9f);
        }
        if (motorLeftBarFill != null)
        {
            motorLeftBarFill.fillAmount = Mathf.Clamp01(Mathf.Abs(lPwm) / 255f);
        }

        if (motorRightText != null)
        {
            motorRightText.text = $"R-MOTOR: {rPwm} PWM";
            motorRightText.color = rPwm != 0 ? new Color(0f, 1f, 0.65f, 1f) : new Color(0.75f, 0.9f, 1f, 0.9f);
        }
        if (motorRightBarFill != null)
        {
            motorRightBarFill.fillAmount = Mathf.Clamp01(Mathf.Abs(rPwm) / 255f);
        }
    }

    private void UpdateServoGauge(int sDeg)
    {
        if (sDeg <= 0) sDeg = 90;
        if (servoPanText != null)
        {
            servoPanText.text = $"SERVO: {sDeg} deg";
        }
        if (servoTickIndicator != null)
        {
            float normServo = Mathf.InverseLerp(0f, 180f, sDeg);
            servoTickIndicator.anchoredPosition = new Vector2((normServo - 0.5f) * 130f, 0f);
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
        swarmStatusText.text = $"P2P-MESH DISCOVERED: {peerCount} PEERS  |  SWITCH: [LB] OR [1-3]";
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
