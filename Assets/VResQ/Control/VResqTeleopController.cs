using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using VResQ.Core;

namespace VResQ.Control
{
    /// <summary>
    /// Dual Teleoperation Controller supporting New Input System, Gamepads, XR Controllers, and Legacy Input.
    /// Manages deadman safety, proportional drive, servo panning, and action triggers.
    /// </summary>
    public class VResqTeleopController : MonoBehaviour
    {
        [Header("Drive Dynamics")]
        [Range(0.2f, 1.0f)] public float speedScale = 0.7f;
        public float commandRateHz = 20f;
        public float stickDeadzone = 0.12f;

        [Header("Servo Pan Controls")]
        public int currentServoDeg = 90;
        public int servoStep = 5;

        [Header("Deadman Safety")]
        [Tooltip("When true, driving requires holding deadman switch (Grip or Key)")]
        public bool requireDeadmanSwitch = true;
        [SerializeField] private bool isDeadmanActive = false;
        public bool IsDeadmanActive => isDeadmanActive;

        [Header("Swarm Presets (1 = Node 0, 2 = Node 1, 3 = Node 2)")]
        public string nodeZeroIp = "10.66.68.52";
        public string nodeOneIp = "10.66.68.10";
        public string nodeTwoIp = "10.66.68.82";

        private float _lastSendTime = 0f;
        private bool _wasMoving = false;
        private readonly List<UnityEngine.XR.InputDevice> _xrDevices = new List<UnityEngine.XR.InputDevice>();

        private void Update()
        {
            HandleSwarmSwitching();
            HandleTeleopDrive();
        }

        private void HandleSwarmSwitching()
        {
            if (VResqClient.Instance == null) return;

            // 1. New Input System Keyboard
            Keyboard kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.digit1Key.wasPressedThisFrame || kb.numpad1Key.wasPressedThisFrame)
                {
                    VResqClient.Instance.SwitchRobot(nodeZeroIp, 8000);
                    return;
                }
                if (kb.digit2Key.wasPressedThisFrame || kb.numpad2Key.wasPressedThisFrame)
                {
                    VResqClient.Instance.SwitchRobot(nodeOneIp, 8000);
                    return;
                }
                if (kb.digit3Key.wasPressedThisFrame || kb.numpad3Key.wasPressedThisFrame)
                {
                    VResqClient.Instance.SwitchRobot(nodeTwoIp, 8000);
                    return;
                }
            }

            // 2. Safe Fallback to Legacy Input (if enabled)
            try
            {
                if (UnityEngine.Input.GetKeyDown(KeyCode.Alpha1) || UnityEngine.Input.GetKeyDown(KeyCode.Keypad1))
                    VResqClient.Instance.SwitchRobot(nodeZeroIp, 8000);
                else if (UnityEngine.Input.GetKeyDown(KeyCode.Alpha2) || UnityEngine.Input.GetKeyDown(KeyCode.Keypad2))
                    VResqClient.Instance.SwitchRobot(nodeOneIp, 8000);
                else if (UnityEngine.Input.GetKeyDown(KeyCode.Alpha3) || UnityEngine.Input.GetKeyDown(KeyCode.Keypad3))
                    VResqClient.Instance.SwitchRobot(nodeTwoIp, 8000);
            }
            catch (InvalidOperationException) { }
        }

        private void HandleTeleopDrive()
        {
            if (VResqClient.Instance == null || !VResqClient.Instance.IsConnected) return;

            float linear = 0f;
            float angular = 0f;
            bool buzzer = false;
            bool estop = false;
            bool gripActive = false;
            bool shiftActive = false;

            // 1. New Input System: Keyboard
            Keyboard kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) linear += 1f;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) linear -= 1f;
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) angular -= 1f;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) angular += 1f;

                if (kb.bKey.isPressed) buzzer = true;
                if (kb.spaceKey.wasPressedThisFrame) estop = true;

                if (kb.qKey.isPressed) currentServoDeg = Mathf.Clamp(currentServoDeg - servoStep, 20, 160);
                if (kb.eKey.isPressed) currentServoDeg = Mathf.Clamp(currentServoDeg + servoStep, 20, 160);

                if (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed) shiftActive = true;
            }

            // 2. New Input System: Gamepad
            Gamepad gp = Gamepad.current;
            if (gp != null)
            {
                Vector2 stick = gp.leftStick.ReadValue();
                if (Mathf.Abs(stick.y) > stickDeadzone) linear = stick.y;
                if (Mathf.Abs(stick.x) > stickDeadzone) angular = stick.x;

                if (gp.rightTrigger.isPressed || gp.leftTrigger.isPressed || gp.rightShoulder.isPressed) gripActive = true;
                if (gp.buttonSouth.wasPressedThisFrame) estop = true;
                if (gp.buttonWest.isPressed) buzzer = true;
            }

            // 3. XR / VR Controller Input via UnityEngine.XR
            try
            {
                _xrDevices.Clear();
                InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.Controller, _xrDevices);
                foreach (var device in _xrDevices)
                {
                    if (device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primary2DAxis, out Vector2 axis))
                    {
                        if (Mathf.Abs(axis.y) > stickDeadzone) linear = axis.y;
                        if (Mathf.Abs(axis.x) > stickDeadzone) angular = axis.x;
                    }
                    if (device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.gripButton, out bool grip) && grip)
                    {
                        gripActive = true;
                    }
                }
            }
            catch { }

            // 4. Safe Legacy Input Fallback (wrapped to avoid throwing if New Input System is active)
            try
            {
                if (UnityEngine.Input.GetKey(KeyCode.W) || UnityEngine.Input.GetKey(KeyCode.UpArrow)) linear += 1f;
                if (UnityEngine.Input.GetKey(KeyCode.S) || UnityEngine.Input.GetKey(KeyCode.DownArrow)) linear -= 1f;
                if (UnityEngine.Input.GetKey(KeyCode.A) || UnityEngine.Input.GetKey(KeyCode.LeftArrow)) angular -= 1f;
                if (UnityEngine.Input.GetKey(KeyCode.D) || UnityEngine.Input.GetKey(KeyCode.RightArrow)) angular += 1f;
                if (UnityEngine.Input.GetKey(KeyCode.B)) buzzer = true;
                if (UnityEngine.Input.GetKeyDown(KeyCode.Space)) estop = true;
                if (UnityEngine.Input.GetKey(KeyCode.LeftShift) || UnityEngine.Input.GetKey(KeyCode.RightShift)) shiftActive = true;
            }
            catch (InvalidOperationException) { }

            // 5. Deadman Switch Evaluation
            bool keyboardMoving = Mathf.Abs(linear) > 0.05f || Mathf.Abs(angular) > 0.05f;
            isDeadmanActive = !requireDeadmanSwitch || gripActive || shiftActive || keyboardMoving;

            if (!isDeadmanActive)
            {
                linear = 0f;
                angular = 0f;
            }

            // 6. Rate-limited Command Transmission
            bool isMoving = (Mathf.Abs(linear) > 0.01f || Mathf.Abs(angular) > 0.01f || buzzer || estop);
            if (Time.realtimeSinceStartup - _lastSendTime >= (1.0f / commandRateHz) || (_wasMoving && !isMoving) || estop)
            {
                _lastSendTime = Time.realtimeSinceStartup;
                _wasMoving = isMoving;

                VResqClient.Instance.SendDriveCommand(
                    linear: Mathf.Clamp(linear, -1f, 1f),
                    angular: Mathf.Clamp(angular, -1f, 1f),
                    servo: currentServoDeg,
                    speed: speedScale,
                    estop: estop,
                    buzzer: buzzer
                );
            }
        }

        // --- Public Action Helpers for UI Button Clicks ---
        public void ActionScan()
        {
            VResqClient.Instance?.SendActionCommand("scan");
        }

        public void ActionBeacon()
        {
            VResqClient.Instance?.SendActionCommand("beacon");
        }

        public void ActionCenter()
        {
            currentServoDeg = 90;
            VResqClient.Instance?.SendActionCommand("center");
        }

        public void ActionApproach()
        {
            VResqClient.Instance?.SendActionCommand("approach");
        }

        public void ActionBackOut()
        {
            VResqClient.Instance?.SendActionCommand("back_out");
        }

        public void ActionEmergencyStop()
        {
            VResqClient.Instance?.SendDriveCommand(0, 0, 90, 0.7f, estop: true);
        }
    }
}
