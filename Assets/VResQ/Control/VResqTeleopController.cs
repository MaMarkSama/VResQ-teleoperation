using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.XR;
using VResQ.Core;
using VResQ.Video;

namespace VResQ.Control
{
    /// <summary>
    /// Universal 100% Joystick / Gamepad / XR Controller / Keyboard Teleoperation Controller.
    /// Supports Xbox, PlayStation, Generic USB Joysticks, Flight Sticks, OpenXR Hand Controllers, and Keyboard/Mouse.
    /// Provides full robot driving, 360° surround look, view switching (External World vs Surround Camera), and swarm switching.
    /// </summary>
    public class VResqTeleopController : MonoBehaviour
    {
        public static VResqTeleopController Instance { get; private set; }

        [Header("Drive Dynamics")]
        [Range(0.2f, 1.0f)] public float speedScale = 0.75f;
        public float commandRateHz = 20f;
        public float stickDeadzone = 0.10f;

        [Header("360° Free-Look & Camera Gimbal")]
        public Transform cameraRigTransform;
        public float lookYawSpeed = 95f;
        public float lookPitchSpeed = 65f;
        public int currentServoDeg = 90;
        public int servoStep = 5;

        [Header("Deadman Safety")]
        [Tooltip("When false (default), Joystick/WASD drives immediately")]
        public bool requireDeadmanSwitch = false;
        [SerializeField] private bool isDeadmanActive = true;
        public bool IsDeadmanActive => isDeadmanActive;

        [Header("Live Controller Diagnostics (Read-Only)")]
        [SerializeField] private string activeControllerName = "SCANNING CONTROLLERS...";
        public string ActiveControllerName => activeControllerName;

        [SerializeField] private bool hasPhysicalJoystick = false;
        public bool HasPhysicalJoystick => hasPhysicalJoystick;

        [SerializeField] private Vector2 liveDriveStick = Vector2.zero;
        public Vector2 LiveDriveStick => liveDriveStick;

        [SerializeField] private Vector2 liveLookStick = Vector2.zero;
        public Vector2 LiveLookStick => liveLookStick;

        [SerializeField] private float effectiveSpeedScale = 0.75f;
        public float EffectiveSpeedScale => effectiveSpeedScale;

        [SerializeField] private int simulatedLeftPwm = 0;
        public int SimulatedLeftPwm => simulatedLeftPwm;

        [SerializeField] private int simulatedRightPwm = 0;
        public int SimulatedRightPwm => simulatedRightPwm;

        [SerializeField] private Vector2 simulatedPose = Vector2.zero;
        public Vector2 SimulatedPose => simulatedPose;

        [SerializeField] private float simulatedHeadingDeg = 0f;
        public float SimulatedHeadingDeg => simulatedHeadingDeg;

        private float _lastSendTime = 0f;
        private bool _wasMoving = false;
        private float _servoFloatDeg = 90f;
        private float _cameraLookYaw = 0f;
        private float _cameraLookPitch = 0f;
        private readonly List<UnityEngine.XR.InputDevice> _xrDevices = new List<UnityEngine.XR.InputDevice>();

        // Edge-trigger states for generic USB joystick buttons
        private readonly bool[] _prevGenericButtons = new bool[16];
        private bool _prevXrPrimaryBtn = false;
        private bool _prevXrSecondaryBtn = false;

        private void Awake()
        {
            Instance = this;
            _servoFloatDeg = currentServoDeg;
        }

        private void Start()
        {
            FindCameraRig();
            SyncFromConfig();
            if (VResqFrameworkConfig.Instance != null)
            {
                VResqFrameworkConfig.Instance.OnConfigChanged.AddListener(SyncFromConfig);
            }
        }

        private void FindCameraRig()
        {
            if (cameraRigTransform != null) return;
            if (Camera.main != null)
            {
                // Prefer rotating Camera Offset or Main Camera parent if inside XR Origin
                if (Camera.main.transform.parent != null)
                    cameraRigTransform = Camera.main.transform.parent;
                else
                    cameraRigTransform = Camera.main.transform;
            }
        }

        public void SyncFromConfig()
        {
            var cfg = VResqFrameworkConfig.Instance;
            if (cfg != null)
            {
                speedScale = cfg.Profile.speedScale;
                stickDeadzone = cfg.Profile.stickDeadzone;
                lookYawSpeed = cfg.Profile.lookYawSpeedDegPerSec;
                requireDeadmanSwitch = cfg.Profile.requireDeadmanSwitch;
                servoStep = cfg.Profile.servoStepDeg;
            }
        }

        private void Update()
        {
            DetectConnectedControllers();
            ProcessAllInputsAndDrive();
        }

        private void DetectConnectedControllers()
        {
            Gamepad gp = Gamepad.current;
            if (gp != null)
            {
                hasPhysicalJoystick = true;
                activeControllerName = !string.IsNullOrEmpty(gp.displayName) ? gp.displayName.ToUpper() : "GAMEPAD CONTROLLER";
                return;
            }

            Joystick js = Joystick.current;
            if (js != null)
            {
                hasPhysicalJoystick = true;
                activeControllerName = !string.IsNullOrEmpty(js.displayName) ? js.displayName.ToUpper() : "USB JOYSTICK";
                return;
            }

            // Check InputSystem devices for any generic HID controller with axes
            foreach (var dev in InputSystem.devices)
            {
                if (dev is Gamepad || dev is Joystick)
                {
                    hasPhysicalJoystick = true;
                    activeControllerName = dev.displayName.ToUpper();
                    return;
                }
            }

            try
            {
                _xrDevices.Clear();
                InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.Controller, _xrDevices);
                if (_xrDevices.Count > 0)
                {
                    hasPhysicalJoystick = true;
                    activeControllerName = $"VR HAND CONTROLLERS ({_xrDevices.Count})";
                    return;
                }
            }
            catch { }

            hasPhysicalJoystick = false;
            activeControllerName = "KEYBOARD & MOUSE (PLUG IN JOYSTICK ANYTIME)";
        }

        private void ProcessAllInputsAndDrive()
        {
            float linear = 0f;
            float angular = 0f;
            float lookX = 0f;
            float lookY = 0f;
            bool buzzer = false;
            bool estop = false;
            bool gripOrShift = false;
            float speedModifier = 1.0f;

            // =====================================================================
            // 1. STANDARD GAMEPAD (Xbox / PlayStation / Switch Pro / XInput / 8BitDo)
            // =====================================================================
            Gamepad gp = Gamepad.current;
            if (gp != null)
            {
                Vector2 lStick = gp.leftStick.ReadValue();
                if (lStick.magnitude > stickDeadzone)
                {
                    linear += lStick.y;
                    angular += lStick.x;
                }

                Vector2 rStick = gp.rightStick.ReadValue();
                if (rStick.magnitude > stickDeadzone)
                {
                    lookX += rStick.x;
                    lookY += rStick.y;
                }

                // Triggers: RT = Turbo Boost (100%), LT = Precision Crawl (45%)
                float rt = gp.rightTrigger.ReadValue();
                float lt = gp.leftTrigger.ReadValue();
                if (rt > 0.15f)
                {
                    gripOrShift = true;
                    speedModifier = Mathf.Lerp(1.0f, 1.35f, rt);
                }
                else if (lt > 0.15f)
                {
                    gripOrShift = true;
                    speedModifier = Mathf.Lerp(1.0f, 0.45f, lt);
                }

                // Bumpers: RB = Cycle View Mode (360/220/180/External), LB = Switch Robot Node
                if (gp.rightShoulder.wasPressedThisFrame)
                {
                    VResqSurroundDisplay.Instance?.CycleNextViewMode();
                }
                if (gp.leftShoulder.wasPressedThisFrame)
                {
                    CycleNextFleetNode();
                }

                // Face Buttons:
                // Y / Triangle (Button North): Instant Toggle มองเห็นรอบนอก (External World) <-> ภาพจากกล้องรอบตัว (Surround Camera)
                if (gp.buttonNorth.wasPressedThisFrame)
                {
                    VResqSurroundDisplay.Instance?.ToggleExternalVsCamera();
                }
                // B / Circle (Button East): Emergency Stop
                if (gp.buttonEast.wasPressedThisFrame)
                {
                    estop = true;
                }
                // X / Square (Button West): SOS Buzzer / Horn
                if (gp.buttonWest.isPressed)
                {
                    buzzer = true;
                }
                // A / Cross (Button South): Autonomous Scan Area
                if (gp.buttonSouth.wasPressedThisFrame)
                {
                    ActionScan();
                }

                // D-Pad: Up/Down = Adjust Speed Limit, Left/Right = Gimbal Servo Pan
                if (gp.dpad.up.wasPressedThisFrame) AdjustSpeedScale(+0.10f);
                if (gp.dpad.down.wasPressedThisFrame) AdjustSpeedScale(-0.10f);
                if (gp.dpad.left.isPressed) _servoFloatDeg = Mathf.Clamp(_servoFloatDeg - 60f * Time.deltaTime, 20f, 160f);
                if (gp.dpad.right.isPressed) _servoFloatDeg = Mathf.Clamp(_servoFloatDeg + 60f * Time.deltaTime, 20f, 160f);

                // Stick Clicks & Menu/Start/Select:
                if (gp.rightStickButton.wasPressedThisFrame)
                {
                    ActionCenter();
                    ResetLookOrientation();
                }
                if (gp.leftStickButton.wasPressedThisFrame || gp.selectButton.wasPressedThisFrame)
                {
                    ResetLookOrientation();
                }
                if (gp.startButton.wasPressedThisFrame)
                {
                    var hud = FindFirstObjectByType<RescueRobotVRHUD>();
                    if (hud != null) hud.ToggleSettingsModal();
                }
            }

            // =====================================================================
            // 2. GENERIC USB JOYSTICK / FLIGHT STICK / DIRECTINPUT CONTROLLER
            // =====================================================================
            Joystick js = Joystick.current;
            if (js != null && gp == null)
            {
                Vector2 stick = js.stick.ReadValue();
                if (stick.magnitude > stickDeadzone)
                {
                    linear += stick.y;
                    angular += stick.x;
                }

                // Read secondary axes (twist / rx / ry / z) if available
                float twist = js.twist != null ? js.twist.ReadValue() : 0f;
                if (Mathf.Abs(twist) > stickDeadzone)
                {
                    lookX += twist;
                }
                else
                {
                    float rx = TryReadAxis(js, "rx") + TryReadAxis(js, "z");
                    float ry = TryReadAxis(js, "ry") + TryReadAxis(js, "rz");
                    if (Mathf.Abs(rx) > stickDeadzone) lookX += rx;
                    if (Mathf.Abs(ry) > stickDeadzone) lookY += ry;
                }

                if (js.trigger != null && js.trigger.isPressed)
                {
                    gripOrShift = true;
                }

                // Read numbered buttons on Generic USB Joystick (Buttons 0..11)
                int btnIdx = 0;
                foreach (var ctrl in js.allControls)
                {
                    if (ctrl is ButtonControl btn && btn != js.trigger && btnIdx < _prevGenericButtons.Length)
                    {
                        bool pressed = btn.isPressed;
                        bool justPressed = pressed && !_prevGenericButtons[btnIdx];
                        _prevGenericButtons[btnIdx] = pressed;

                        if (btnIdx == 0 && justPressed) ActionScan(); // Button 1: Scan
                        else if (btnIdx == 1 && justPressed) estop = true; // Button 2: E-Stop
                        else if (btnIdx == 2 && pressed) buzzer = true; // Button 3: Buzzer
                        else if (btnIdx == 3 && justPressed) VResqSurroundDisplay.Instance?.ToggleExternalVsCamera(); // Button 4: Toggle External vs Surround Camera
                        else if (btnIdx == 4 && justPressed) CycleNextFleetNode(); // Button 5 (L1): Switch Robot
                        else if (btnIdx == 5 && justPressed) VResqSurroundDisplay.Instance?.CycleNextViewMode(); // Button 6 (R1): Cycle Surround Mode
                        else if (btnIdx == 6 && justPressed) ResetLookOrientation(); // Button 7 (Select): Recenter
                        else if (btnIdx == 7 && justPressed)
                        {
                            var hud = FindFirstObjectByType<RescueRobotVRHUD>();
                            if (hud != null) hud.ToggleSettingsModal();
                        }
                        btnIdx++;
                    }
                }
            }

            // =====================================================================
            // 3. VR / XR HAND CONTROLLERS (OpenXR / Meta Quest)
            // =====================================================================
            try
            {
                _xrDevices.Clear();
                InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.Controller, _xrDevices);
                foreach (var device in _xrDevices)
                {
                    bool isLeft = (device.characteristics & InputDeviceCharacteristics.Left) != 0;
                    bool isRight = (device.characteristics & InputDeviceCharacteristics.Right) != 0;

                    if (device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primary2DAxis, out Vector2 axis))
                    {
                        if (isLeft || (!isLeft && !isRight))
                        {
                            if (axis.magnitude > stickDeadzone)
                            {
                                linear += axis.y;
                                angular += axis.x;
                            }
                        }
                        else if (isRight)
                        {
                            if (axis.magnitude > stickDeadzone)
                            {
                                lookX += axis.x;
                                lookY += axis.y;
                            }
                        }
                    }

                    if (device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.gripButton, out bool grip) && grip)
                    {
                        gripOrShift = true;
                    }

                    if (isRight)
                    {
                        if (device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primaryButton, out bool pBtn))
                        {
                            if (pBtn && !_prevXrPrimaryBtn) VResqSurroundDisplay.Instance?.ToggleExternalVsCamera();
                            _prevXrPrimaryBtn = pBtn;
                        }
                        if (device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.secondaryButton, out bool sBtn))
                        {
                            if (sBtn && !_prevXrSecondaryBtn) estop = true;
                            _prevXrSecondaryBtn = sBtn;
                        }
                    }
                }
            }
            catch { }

            // =====================================================================
            // 4. KEYBOARD & MOUSE FALLBACK (PC Testing)
            // =====================================================================
            Keyboard kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) linear += 1f;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) linear -= 1f;
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) angular -= 1f;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) angular += 1f;

                if (kb.qKey.isPressed) _servoFloatDeg = Mathf.Clamp(_servoFloatDeg - 55f * Time.deltaTime, 20f, 160f);
                if (kb.eKey.isPressed) _servoFloatDeg = Mathf.Clamp(_servoFloatDeg + 55f * Time.deltaTime, 20f, 160f);

                if (kb.bKey.isPressed) buzzer = true;
                if (kb.spaceKey.wasPressedThisFrame) estop = true;
                if (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed) gripOrShift = true;

                // F: Instant Toggle มองเห็นรอบนอก (External World) <-> ภาพจากกล้องรอบตัว (Surround Camera)
                if (kb.fKey.wasPressedThisFrame)
                {
                    VResqSurroundDisplay.Instance?.ToggleExternalVsCamera();
                }
                // V: Cycle Surround Modes (360° -> 220° -> 180° -> External World)
                if (kb.vKey.wasPressedThisFrame)
                {
                    VResqSurroundDisplay.Instance?.CycleNextViewMode();
                }

                // 1, 2, 3: Switch Swarm Fleet Nodes
                if (kb.digit1Key.wasPressedThisFrame || kb.numpad1Key.wasPressedThisFrame) VResqClient.Instance?.SwitchToNodeIndex(0);
                if (kb.digit2Key.wasPressedThisFrame || kb.numpad2Key.wasPressedThisFrame) VResqClient.Instance?.SwitchToNodeIndex(1);
                if (kb.digit3Key.wasPressedThisFrame || kb.numpad3Key.wasPressedThisFrame) VResqClient.Instance?.SwitchToNodeIndex(2);

                // Tab: Config Modal, R: Recenter, C: Center Servo
                if (kb.tabKey.wasPressedThisFrame)
                {
                    var hud = FindFirstObjectByType<RescueRobotVRHUD>();
                    if (hud != null) hud.ToggleSettingsModal();
                }
                if (kb.rKey.wasPressedThisFrame) ResetLookOrientation();
                if (kb.cKey.wasPressedThisFrame) ActionCenter();

                if (kb.minusKey.wasPressedThisFrame || kb.numpadMinusKey.wasPressedThisFrame) AdjustSpeedScale(-0.10f);
                if (kb.equalsKey.wasPressedThisFrame || kb.numpadPlusKey.wasPressedThisFrame) AdjustSpeedScale(+0.10f);
            }

            // Mouse Right-Click Drag for 360° Free-Look on PC
            Mouse mouse = Mouse.current;
            if (mouse != null && mouse.rightButton.isPressed)
            {
                Vector2 delta = mouse.delta.ReadValue();
                lookX += Mathf.Clamp(delta.x * 0.15f, -2.5f, 2.5f);
                lookY += Mathf.Clamp(delta.y * 0.15f, -2.5f, 2.5f);
            }

            // =====================================================================
            // 5. APPLY 360° LOOK & SERVO GIMBAL PANNING
            // =====================================================================
            linear = Mathf.Clamp(linear, -1f, 1f);
            angular = Mathf.Clamp(angular, -1f, 1f);
            liveDriveStick = new Vector2(angular, linear);
            liveLookStick = new Vector2(Mathf.Clamp(lookX, -1f, 1f), Mathf.Clamp(lookY, -1f, 1f));

            if (Mathf.Abs(lookX) > 0.01f || Mathf.Abs(lookY) > 0.01f)
            {
                // Pan robot camera servo smoothly with Right Stick X
                _servoFloatDeg = Mathf.Clamp(_servoFloatDeg + lookX * 55f * Time.deltaTime, 20f, 160f);

                // Rotate user camera rig 360° so user can look around the outside world or 360° surround camera!
                FindCameraRig();
                if (cameraRigTransform != null)
                {
                    _cameraLookYaw = (_cameraLookYaw + lookX * lookYawSpeed * Time.deltaTime) % 360f;
                    _cameraLookPitch = Mathf.Clamp(_cameraLookPitch - lookY * lookPitchSpeed * Time.deltaTime, -55f, 55f);
                    cameraRigTransform.localRotation = Quaternion.Euler(_cameraLookPitch, _cameraLookYaw, 0f);
                }
            }

            currentServoDeg = Mathf.RoundToInt(_servoFloatDeg);

            // =====================================================================
            // 6. EVALUATE DEADMAN, LOCAL SIMULATION, AND 20HZ WEBSOCKET TRANSMISSION
            // =====================================================================
            isDeadmanActive = !requireDeadmanSwitch || gripOrShift;
            if (!isDeadmanActive)
            {
                linear = 0f;
                angular = 0f;
            }

            effectiveSpeedScale = Mathf.Clamp(speedScale * speedModifier, 0.2f, 1.0f);

            // Calculate differential drive PWM & local odometry so joystick works 100% visually even when offline!
            float leftTrack = Mathf.Clamp(linear + angular * 0.75f, -1f, 1f) * effectiveSpeedScale;
            float rightTrack = Mathf.Clamp(linear - angular * 0.75f, -1f, 1f) * effectiveSpeedScale;
            simulatedLeftPwm = estop ? 0 : Mathf.RoundToInt(leftTrack * 255f);
            simulatedRightPwm = estop ? 0 : Mathf.RoundToInt(rightTrack * 255f);

            if (Mathf.Abs(linear) > 0.01f || Mathf.Abs(angular) > 0.01f)
            {
                simulatedHeadingDeg = (simulatedHeadingDeg + angular * 65f * effectiveSpeedScale * Time.deltaTime + 360f) % 360f;
                float rad = simulatedHeadingDeg * Mathf.Deg2Rad;
                simulatedPose += new Vector2(Mathf.Sin(rad), Mathf.Cos(rad)) * (linear * 0.85f * effectiveSpeedScale * Time.deltaTime);
            }

            // Send over WebSocket when connected to a live robot node
            if (VResqClient.Instance != null && VResqClient.Instance.IsConnected)
            {
                bool isMoving = (Mathf.Abs(linear) > 0.01f || Mathf.Abs(angular) > 0.01f || buzzer || estop);
                if (Time.realtimeSinceStartup - _lastSendTime >= (1.0f / commandRateHz) || (_wasMoving && !isMoving) || estop)
                {
                    _lastSendTime = Time.realtimeSinceStartup;
                    _wasMoving = isMoving;

                    VResqClient.Instance.SendDriveCommand(
                        linear: linear,
                        angular: angular,
                        servo: currentServoDeg,
                        speed: effectiveSpeedScale,
                        estop: estop,
                        buzzer: buzzer
                    );
                }
            }
        }

        private static float TryReadAxis(UnityEngine.InputSystem.InputDevice dev, string axisName)
        {
            if (dev == null) return 0f;
            var ctrl = dev.TryGetChildControl<AxisControl>(axisName);
            return ctrl != null ? ctrl.ReadValue() : 0f;
        }

        public void CycleNextFleetNode()
        {
            if (VResqClient.Instance == null) return;
            int next = (VResqClient.Instance.ActiveNodeIndex + 1) % 3;
            VResqClient.Instance.SwitchToNodeIndex(next);
        }

        public void ResetLookOrientation()
        {
            _cameraLookYaw = 0f;
            _cameraLookPitch = 0f;
            FindCameraRig();
            if (cameraRigTransform != null)
            {
                cameraRigTransform.localRotation = Quaternion.identity;
            }
            var follow = FindFirstObjectByType<TacticalHUDFollow>();
            if (follow != null) follow.ResetHUDOrientation();
        }

        public void AdjustSpeedScale(float delta)
        {
            speedScale = Mathf.Clamp(Mathf.Round((speedScale + delta) * 100f) / 100f, 0.2f, 1.0f);
            if (VResqFrameworkConfig.Instance != null)
            {
                VResqFrameworkConfig.Instance.Profile.speedScale = speedScale;
                VResqFrameworkConfig.Instance.NotifyConfigChanged();
            }
        }

        public void ActionScan() => VResqClient.Instance?.SendActionCommand("scan");
        public void ActionBeacon() => VResqClient.Instance?.SendActionCommand("beacon");
        public void ActionCenter()
        {
            _servoFloatDeg = 90f;
            currentServoDeg = 90;
            VResqClient.Instance?.SendActionCommand("center");
        }
        public void ActionApproach() => VResqClient.Instance?.SendActionCommand("approach");
        public void ActionBackOut() => VResqClient.Instance?.SendActionCommand("back_out");
        public void ActionEmergencyStop()
        {
            simulatedLeftPwm = 0;
            simulatedRightPwm = 0;
            VResqClient.Instance?.SendDriveCommand(0, 0, 90, 0.7f, estop: true);
        }
        public void ActionToggleSurroundView() => VResqSurroundDisplay.Instance?.CycleNextViewMode();
        public void ActionToggleExternalVsCamera() => VResqSurroundDisplay.Instance?.ToggleExternalVsCamera();
    }
}
