using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VResQ.Control;
using VResQ.Core;

namespace VResQ.HMI
{
    /// <summary>
    /// Interactive Special Operations & Hand Gesture Command Center for V-ResQ Unity VR/Desktop HMI.
    /// Provides genuinely clickable buttons for drive arming, global swarm E-STOP, special robot macros,
    /// camera gimbal presets, OLED/horn/AI/motor-test controls, swarm formations, and XR hand commands.
    /// </summary>
    public class VResqSpecialCommandsPanel : MonoBehaviour
    {
        public static VResqSpecialCommandsPanel Instance { get; private set; }

        [Header("Drawer & Quick Access")]
        public GameObject drawerRoot;
        public Button quickArmButton;
        public Image quickArmBackground;
        public TextMeshProUGUI quickArmLabel;
        public Button quickOpsButton;
        public TextMeshProUGUI liveStatusBanner;

        [Header("Hand Gesture Ribbon (Always-Visible Top Strip)")]
        public TextMeshProUGUI handRibbonPoseText;
        public TextMeshProUGUI handRibbonPendingText;
        public Image handRibbonProgressFill;
        public TextMeshProUGUI handRibbonDriveText;

        [Header("Drawer Hand Gesture Diagnostics")]
        public TextMeshProUGUI drawerHandStatusText;
        public Image drawerHandProgressFill;
        public TextMeshProUGUI gestureToggleLabel;
        public TextMeshProUGUI handDriveToggleLabel;
        public TextMeshProUGUI soundToggleLabel;
        public TextMeshProUGUI hapticsToggleLabel;
        public TextMeshProUGUI aiToggleLabel;

        private bool _aiEnabled = true;
        private float _nextUiRefreshAt = 0f;

        public bool IsDrawerOpen => drawerRoot != null && drawerRoot.activeSelf;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
        }

        private void Start()
        {
            WireButtons();
            if (drawerRoot != null) drawerRoot.SetActive(false);
            RefreshVisualStates();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void ToggleDrawer()
        {
            if (drawerRoot == null) return;
            SetDrawerOpen(!drawerRoot.activeSelf);
        }

        public void SetDrawerOpen(bool open)
        {
            if (drawerRoot == null) return;
            drawerRoot.SetActive(open);
            if (open)
            {
                var hud = GetComponent<RescueRobotVRHUD>();
                if (hud != null && hud.settingsModalRoot != null) hud.settingsModalRoot.SetActive(false);
                VResqHMIFeedback.Instance?.AttachButtonEffectsUnder(drawerRoot.transform);
                RefreshVisualStates();
            }
        }

        public void WireButtons()
        {
            if (quickArmButton != null)
            {
                quickArmButton.onClick.RemoveAllListeners();
                quickArmButton.onClick.AddListener(OnQuickArmClicked);
            }
            if (quickOpsButton != null)
            {
                quickOpsButton.onClick.RemoveAllListeners();
                quickOpsButton.onClick.AddListener(ToggleDrawer);
            }

            if (drawerRoot == null) return;
            var buttons = drawerRoot.GetComponentsInChildren<Button>(true);
            for (int i = 0; i < buttons.Length; i++)
            {
                var b = buttons[i];
                if (b == null) continue;
                string key = b.name.ToUpperInvariant();
                if (!key.StartsWith("OPS_")) continue;
                b.onClick.RemoveAllListeners();

                switch (key)
                {
                    case "OPS_CLOSE": b.onClick.AddListener(() => SetDrawerOpen(false)); break;
                    case "OPS_ARM": b.onClick.AddListener(() => SetDriveArmedWithFeedback(true)); break;
                    case "OPS_DISARM": b.onClick.AddListener(() => SetDriveArmedWithFeedback(false)); break;
                    case "OPS_RESET_ESTOP": b.onClick.AddListener(OnResetEstop); break;
                    case "OPS_SWARM_ESTOP": b.onClick.AddListener(OnGlobalSwarmEstop); break;
                    case "OPS_SPD_ECO": b.onClick.AddListener(() => SetSpeedPreset(0.45f, "ECO 45%")); break;
                    case "OPS_SPD_NORM": b.onClick.AddListener(() => SetSpeedPreset(0.75f, "NORMAL 75%")); break;
                    case "OPS_SPD_TURBO": b.onClick.AddListener(() => SetSpeedPreset(1.00f, "TURBO 100%")); break;

                    case "OPS_ACT_PATROL": b.onClick.AddListener(() => TriggerSpecialAction("patrol", "AUTO-SURVEY")); break;
                    case "OPS_ACT_RADAR": b.onClick.AddListener(() => TriggerSpecialAction("radar_sweep", "RADAR SWEEP")); break;
                    case "OPS_ACT_SCAN": b.onClick.AddListener(() => TriggerSpecialAction("scan", "AREA SCAN")); break;
                    case "OPS_ACT_APPROACH": b.onClick.AddListener(() => TriggerSpecialAction("approach", "APPROACH")); break;
                    case "OPS_ACT_BACKOUT": b.onClick.AddListener(() => TriggerSpecialAction("backout", "BACK OUT")); break;
                    case "OPS_ACT_RTH": b.onClick.AddListener(() => TriggerSpecialAction("return_home", "RETURN HOME")); break;
                    case "OPS_ACT_SAFESTOP": b.onClick.AddListener(OnSafeStop); break;

                    case "OPS_CAM_LEFT": b.onClick.AddListener(() => SetGimbalPreset(35, "LEFT 35 deg")); break;
                    case "OPS_CAM_CENTER": b.onClick.AddListener(() => SetGimbalPreset(90, "CENTER 90 deg")); break;
                    case "OPS_CAM_RIGHT": b.onClick.AddListener(() => SetGimbalPreset(145, "RIGHT 145 deg")); break;
                    case "OPS_HORN_PULSE": b.onClick.AddListener(OnHornPulse); break;
                    case "OPS_OLED_NEXT": b.onClick.AddListener(OnOledNext); break;
                    case "OPS_AI_TOGGLE": b.onClick.AddListener(OnToggleAi); break;
                    case "OPS_MOTOR_TEST": b.onClick.AddListener(OnMotorPulseTest); break;

                    case "OPS_FORM_RELAY": b.onClick.AddListener(() => SetSwarmFormation("relay_chain")); break;
                    case "OPS_FORM_WEDGE": b.onClick.AddListener(() => SetSwarmFormation("wedge")); break;
                    case "OPS_FORM_LINE": b.onClick.AddListener(() => SetSwarmFormation("line")); break;
                    case "OPS_FORM_HOLD": b.onClick.AddListener(() => SetSwarmFormation("hold")); break;

                    case "OPS_HAND_TOGGLE": b.onClick.AddListener(OnToggleHandGestures); break;
                    case "OPS_HAND_DRIVE_TOGGLE": b.onClick.AddListener(OnToggleHandDrive); break;
                    case "OPS_SOUND_TOGGLE": b.onClick.AddListener(OnToggleSound); break;
                    case "OPS_HAPTIC_TOGGLE": b.onClick.AddListener(OnToggleHaptics); break;

                    case "OPS_GEST_ESTOP": b.onClick.AddListener(() => TriggerHandMacro("BOTH_PALMS_ESTOP")); break;
                    case "OPS_GEST_STOP": b.onClick.AddListener(() => TriggerHandMacro("PALM_SAFE_STOP")); break;
                    case "OPS_GEST_ARM": b.onClick.AddListener(() => TriggerHandMacro("R_THUMB_ARM_TOGGLE")); break;
                    case "OPS_GEST_RADAR": b.onClick.AddListener(() => TriggerHandMacro("R_PEACE_RADAR")); break;
                    case "OPS_GEST_NODE": b.onClick.AddListener(() => TriggerHandMacro("L_PEACE_NEXT_NODE")); break;
                    case "OPS_GEST_HORN": b.onClick.AddListener(() => TriggerHandMacro("L_POINT_HORN")); break;
                }
            }
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextUiRefreshAt) return;
            _nextUiRefreshAt = Time.unscaledTime + 0.10f;
            RefreshVisualStates();
        }

        public void RefreshVisualStates()
        {
            var client = VResqClient.Instance != null ? VResqClient.Instance : GetComponent<VResqClient>();
            var teleop = VResqTeleopController.Instance != null ? VResqTeleopController.Instance : GetComponent<VResqTeleopController>();
            var hands = VResqHandGestureController.Instance != null ? VResqHandGestureController.Instance : GetComponent<VResqHandGestureController>();
            var hmi = VResqHMIFeedback.Instance;

            bool armed = teleop != null && teleop.DriveArmed;
            bool estop = client != null && client.LatestState != null && client.LatestState.estop;
            bool auth = client != null && client.LatestState != null && client.LatestState.motion_authorized;

            if (quickArmBackground != null)
            {
                quickArmBackground.color = estop
                    ? new Color(0.68f, 0.08f, 0.14f, 0.96f)
                    : (armed ? new Color(0.04f, 0.48f, 0.28f, 0.96f) : new Color(0.18f, 0.24f, 0.30f, 0.95f));
            }
            if (quickArmLabel != null)
            {
                quickArmLabel.text = estop
                    ? "E-STOP LATCHED\n[CLICK TO RESET]"
                    : (armed ? "DRIVE ARMED\n[CLICK / L3 DISARM]" : "DRIVE DISARMED\n[CLICK / L3 ARM]");
            }

            // Always-visible Hand & HMI Interlock Ribbon
            if (handRibbonPoseText != null)
            {
                handRibbonPoseText.text = hands != null ? hands.GetCompactStatus() : "HANDS: READY";
            }
            if (handRibbonProgressFill != null)
            {
                handRibbonProgressFill.fillAmount = hands != null ? hands.HoldProgress : 0f;
            }
            if (handRibbonPendingText != null)
            {
                if (hands != null && !string.IsNullOrEmpty(hands.PendingCommandLabel))
                {
                    handRibbonPendingText.text = $"HOLD: {hands.PendingCommandLabel} ({Mathf.RoundToInt(hands.HoldProgress * 100f)}%)";
                }
                else if (hands != null && hands.HasActiveHandDrive)
                {
                    handRibbonPendingText.text = $"PINCH-DRIVE: LIN {hands.HandDriveInput.y:+0.00;-0.00} ANG {hands.HandDriveInput.x:+0.00;-0.00}";
                }
                else
                {
                    handRibbonPendingText.text = hands != null ? $"LAST HAND CMD: {hands.LastExecutedCommand}" : "";
                }
            }
            if (handRibbonDriveText != null)
            {
                string ctrlName = teleop != null ? teleop.ActiveControllerName : "INPUT";
                string statusTag = estop ? "<color=#FF4455>E-STOP</color>" : (armed ? "<color=#00FF88>ARMED</color>" : (auth ? "<color=#FFD166>READY (PRESS L3/ENTER)</color>" : "<color=#FF9966>STANDBY</color>"));
                string input = hands != null && hands.handDriveEnabled ? "HAND STICK" : ctrlName;
                handRibbonDriveText.text = $"{statusTag} | {input} | {client?.robotIp}";
            }

            if (drawerRoot == null || !drawerRoot.activeSelf) return;

            if (liveStatusBanner != null)
            {
                string target = client != null ? $"{client.robotIp}:{client.port}" : "--";
                string ack = client != null ? client.LastCommandAck : "--";
                int servo = teleop != null ? teleop.currentServoDeg : 90;
                int spd = teleop != null ? Mathf.RoundToInt(teleop.speedScale * 100f) : 75;
                liveStatusBanner.text =
                    $"TARGET: {target}  |  DRIVE: {(armed ? "ARMED" : "DISARMED")}  |  AUTH: {(auth ? "OK" : "WAIT")}  |  ESTOP: {(estop ? "YES" : "NO")}  |  SERVO: {servo} deg  |  SPEED: {spd}%\n" +
                    $"LAST ACK: {ack}";
            }
            if (drawerHandStatusText != null && hands != null)
            {
                drawerHandStatusText.text = $"{hands.GetCompactStatus()}   |   LAST: {hands.LastExecutedCommand}";
            }
            if (drawerHandProgressFill != null && hands != null)
            {
                drawerHandProgressFill.fillAmount = hands.HoldProgress;
            }
            if (gestureToggleLabel != null && hands != null)
            {
                gestureToggleLabel.text = hands.handGesturesEnabled ? "HAND GESTURES: ON" : "HAND GESTURES: OFF";
            }
            if (handDriveToggleLabel != null && hands != null)
            {
                handDriveToggleLabel.text = hands.handDriveEnabled ? "PINCH HAND-DRIVE: ON" : "PINCH HAND-DRIVE: OFF";
            }
            if (soundToggleLabel != null && hmi != null)
            {
                soundToggleLabel.text = hmi.enableSound ? "HMI SOUND: ON" : "HMI SOUND: OFF";
            }
            if (hapticsToggleLabel != null && hmi != null)
            {
                hapticsToggleLabel.text = hmi.enableHaptics ? "HAPTICS: ON" : "HAPTICS: OFF";
            }
            if (aiToggleLabel != null)
            {
                aiToggleLabel.text = _aiEnabled ? "AI DETECT: ON" : "AI DETECT: OFF";
            }
        }

        private void OnQuickArmClicked()
        {
            var client = VResqClient.Instance != null ? VResqClient.Instance : GetComponent<VResqClient>();
            var teleop = VResqTeleopController.Instance != null ? VResqTeleopController.Instance : GetComponent<VResqTeleopController>();
            if (client != null && client.LatestState != null && client.LatestState.estop)
            {
                OnResetEstop();
                return;
            }
            if (teleop != null)
            {
                SetDriveArmedWithFeedback(!teleop.DriveArmed);
            }
        }

        private void SetDriveArmedWithFeedback(bool arm)
        {
            var teleop = VResqTeleopController.Instance != null ? VResqTeleopController.Instance : GetComponent<VResqTeleopController>();
            if (teleop == null) return;
            teleop.SetDriveArmed(arm);
            bool success = teleop.DriveArmed == arm;
            var hmi = VResqHMIFeedback.Instance;
            if (arm && success)
            {
                hmi?.PlayArmChime();
                hmi?.ShowToast("DRIVE ARMED", "Joystick & Hand-Drive active — live control unlocked", new Color(0.1f, 1f, 0.55f), 2.8f);
            }
            else if (!arm)
            {
                hmi?.PlayDisarmChime();
                hmi?.ShowToast("DRIVE DISARMED", "Robot motors locked to neutral safety state", new Color(1f, 0.78f, 0.18f), 2.2f);
            }
            else
            {
                hmi?.PlayWarningPulse();
                hmi?.ShowToast("CANNOT ARM DRIVE", teleop.DriveStatus, new Color(1f, 0.35f, 0.30f), 3.0f);
            }
            RefreshVisualStates();
        }

        private void OnResetEstop()
        {
            var client = VResqClient.Instance != null ? VResqClient.Instance : GetComponent<VResqClient>();
            client?.ResetEstop();
            client?.SendSwarmMissionCommand("reset_estop");
            VResqHMIFeedback.Instance?.PlayArmChime();
            VResqHMIFeedback.Instance?.ShowToast("RESET E-STOP SENT", "Clearing emergency stop latch on active node & swarm", new Color(0.1f, 1f, 0.55f), 2.5f);
        }

        private void OnGlobalSwarmEstop()
        {
            var teleop = VResqTeleopController.Instance != null ? VResqTeleopController.Instance : GetComponent<VResqTeleopController>();
            var client = VResqClient.Instance != null ? VResqClient.Instance : GetComponent<VResqClient>();
            teleop?.SetDriveArmed(false);
            teleop?.ActionEmergencyStop();
            client?.SendSwarmMissionCommand("estop");
            VResqHMIFeedback.Instance?.PlayWarningPulse();
            VResqHMIFeedback.Instance?.ShowToast("GLOBAL SWARM E-STOP", "Emergency halt broadcast to ALL robot nodes", new Color(1f, 0.20f, 0.25f), 3.5f);
        }

        private void SetSpeedPreset(float speed, string label)
        {
            var cfg = VResqFrameworkConfig.Instance;
            var teleop = VResqTeleopController.Instance != null ? VResqTeleopController.Instance : GetComponent<VResqTeleopController>();
            if (cfg != null)
            {
                cfg.Profile.speedScale = speed;
                cfg.NotifyConfigChanged();
            }
            if (teleop != null) teleop.speedScale = speed;
            VResqHMIFeedback.Instance?.ShowToast($"SPEED PRESET: {label}", "Updated max motor thrust ceiling", new Color(0f, 0.92f, 1f), 2.0f);
            RefreshVisualStates();
        }

        private void TriggerSpecialAction(string actionId, string label)
        {
            var client = VResqClient.Instance != null ? VResqClient.Instance : GetComponent<VResqClient>();
            if (client == null) return;
            client.SendActionCommand(actionId);
            VResqHMIFeedback.Instance?.ShowToast($"SPECIAL COMMAND: {label}", client.LastCommandStatus, new Color(0f, 0.92f, 1f), 2.5f);
            RefreshVisualStates();
        }

        private void OnSafeStop()
        {
            var client = VResqClient.Instance != null ? VResqClient.Instance : GetComponent<VResqClient>();
            client?.StopOperatorMotion();
            client?.SendActionCommand("safe_stop");
            VResqHMIFeedback.Instance?.PlayDisarmChime();
            VResqHMIFeedback.Instance?.ShowToast("SAFE STOP", "Active macro cancelled & drive zeroed", new Color(1f, 0.78f, 0.18f), 2.2f);
        }

        private void SetGimbalPreset(int deg, string label)
        {
            var teleop = VResqTeleopController.Instance != null ? VResqTeleopController.Instance : GetComponent<VResqTeleopController>();
            teleop?.SetServo(deg);
            VResqHMIFeedback.Instance?.ShowToast($"CAMERA GIMBAL: {label}", $"Servo set to {deg} deg", new Color(0f, 0.92f, 1f), 1.8f);
            RefreshVisualStates();
        }

        private void OnHornPulse()
        {
            var client = VResqClient.Instance != null ? VResqClient.Instance : GetComponent<VResqClient>();
            client?.SendBuzzerPulse(0.45f);
            VResqHMIFeedback.Instance?.ShowToast("HORN / BEACON PULSE", "Triggered 0.45s acoustic alert on active robot", new Color(1f, 0.78f, 0.18f), 2.0f);
        }

        private void OnOledNext()
        {
            var hud = GetComponent<RescueRobotVRHUD>();
            hud?.CycleOledPage();
            VResqHMIFeedback.Instance?.ShowToast("OLED DISPLAY PAGE", "Cycled onboard ESP32 OLED telemetry screen", new Color(0f, 0.92f, 1f), 1.8f);
        }

        private void OnToggleAi()
        {
            _aiEnabled = !_aiEnabled;
            var client = VResqClient.Instance != null ? VResqClient.Instance : GetComponent<VResqClient>();
            client?.SendAiConfigCommand(_aiEnabled);
            VResqHMIFeedback.Instance?.ShowToast(_aiEnabled ? "AI DETECTOR ENABLED" : "AI DETECTOR PAUSED", "Sent /api/ai/config to active board", new Color(0.1f, 1f, 0.55f), 2.0f);
            RefreshVisualStates();
        }

        private void OnMotorPulseTest()
        {
            var client = VResqClient.Instance != null ? VResqClient.Instance : GetComponent<VResqClient>();
            client?.SendMotorTestCommand("both", 150, 0.30f);
            VResqHMIFeedback.Instance?.ShowToast("MOTOR PULSE TEST", "Sent short dual-track pulse check (PWM 150 / 0.30s)", new Color(1f, 0.78f, 0.18f), 2.5f);
        }

        private void SetSwarmFormation(string formation)
        {
            var client = VResqClient.Instance != null ? VResqClient.Instance : GetComponent<VResqClient>();
            client?.SendSwarmMissionCommand("set_formation", formation: formation);
            VResqHMIFeedback.Instance?.ShowToast($"SWARM FORMATION: {formation.ToUpperInvariant()}", "Broadcast formation command to all 3 robot nodes", new Color(0f, 0.92f, 1f), 2.4f);
        }

        private void OnToggleHandGestures()
        {
            var hands = VResqHandGestureController.Instance != null ? VResqHandGestureController.Instance : GetComponent<VResqHandGestureController>();
            if (hands == null) return;
            hands.handGesturesEnabled = !hands.handGesturesEnabled;
            VResqHMIFeedback.Instance?.ShowToast(
                hands.handGesturesEnabled ? "HAND GESTURES ENABLED" : "HAND GESTURES DISABLED",
                "OpenXR hand pose detection updated",
                new Color(0f, 0.92f, 1f), 2.0f);
            RefreshVisualStates();
        }

        private void OnToggleHandDrive()
        {
            var hands = VResqHandGestureController.Instance != null ? VResqHandGestureController.Instance : GetComponent<VResqHandGestureController>();
            if (hands == null) return;
            hands.handDriveEnabled = !hands.handDriveEnabled;
            VResqTeleopController.Instance?.SetDriveArmed(false);
            if (hands.handDriveEnabled) hands.handGesturesEnabled = true;
            VResqHMIFeedback.Instance?.ShowToast(
                hands.handDriveEnabled ? "PINCH HAND-DRIVE ENABLED" : "PINCH HAND-DRIVE OFF",
                hands.handDriveEnabled ? "Pinch Right hand while ARMED to steer; Open Palm stops immediately" : "Pinch reserved for UI interaction only",
                hands.handDriveEnabled ? new Color(0.1f, 1f, 0.55f) : new Color(1f, 0.78f, 0.18f), 2.8f);
            RefreshVisualStates();
        }

        private void OnToggleSound()
        {
            var hmi = VResqHMIFeedback.Instance;
            if (hmi == null) return;
            hmi.enableSound = !hmi.enableSound;
            if (hmi.enableSound) hmi.PlayClickConfirm();
            RefreshVisualStates();
        }

        private void OnToggleHaptics()
        {
            var hmi = VResqHMIFeedback.Instance;
            if (hmi == null) return;
            hmi.enableHaptics = !hmi.enableHaptics;
            if (hmi.enableHaptics) hmi.PulseHaptics(0.35f, 0.08f);
            RefreshVisualStates();
        }

        private void TriggerHandMacro(string id)
        {
            var hands = VResqHandGestureController.Instance != null ? VResqHandGestureController.Instance : GetComponent<VResqHandGestureController>();
            hands?.TriggerGestureMacro(id);
            RefreshVisualStates();
        }
    }
}
