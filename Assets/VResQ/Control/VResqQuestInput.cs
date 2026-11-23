using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using VResQ.Core;
using VResQ.Video;

namespace VResQ.Core
{
    public enum QuestButtonAction { None, HeadsetView, NextCamera, OperatorMenu, CenterServo, NextRobot, Protocol, Swarm, Horn, SpecialOps, ArmToggle }
}

namespace VResQ.Control
{
    public class VResqQuestInput : MonoBehaviour
    {
        public struct Frame { public Vector2 drive, look; public bool grip, horn, estop; }
        private readonly Dictionary<string, bool> previous = new Dictionary<string, bool>();
        public bool LostControllers { get; private set; }
        public string Status { get; private set; } = "Quest controllers unavailable";
        private bool seen;
        private readonly List<InputDevice> devices = new List<InputDevice>();

        public bool Sample(VResqTeleopController teleop, out Frame frame)
        {
            frame = default;
            devices.Clear();
            InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.Controller, devices);
            bool left = false, right = false;
            var cfg = GetComponent<VResqFrameworkConfig>();
            var profile = cfg != null ? cfg.Profile : new VResqProfileData();
            foreach (var device in devices)
            {
                if (!device.isValid || (device.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) && !tracked)) continue;
                bool isLeft = (device.characteristics & InputDeviceCharacteristics.Left) != 0;
                bool isRight = (device.characteristics & InputDeviceCharacteristics.Right) != 0;
                if (!isLeft && !isRight) continue;
                left |= isLeft; right |= isRight;
                device.TryGetFeatureValue(CommonUsages.primary2DAxis, out Vector2 axis);
                if (isLeft)
                {
                    frame.drive = ShapeDrive(axis, profile.stickDeadzone, profile.steeringSensitivity, profile.invertDrive, profile.invertSteering);
                    device.TryGetFeatureValue(CommonUsages.gripButton, out frame.grip);
                    Button(device, "X", CommonUsages.primaryButton, profile.questX, teleop, ref frame);
                    Button(device, "Y", CommonUsages.secondaryButton, profile.questY, teleop, ref frame);
                    if (Edge(device, "LClick", CommonUsages.primary2DAxisClick)) teleop.SetDriveArmed(!teleop.DriveArmed);
                    if (Edge(device, "Menu", CommonUsages.menuButton)) ToggleMenu();
                }
                if (isRight)
                {
                    frame.look = axis.magnitude > profile.stickDeadzone ? axis : Vector2.zero;
                    Button(device, "A", CommonUsages.primaryButton, profile.questA, teleop, ref frame);
                    if (Edge(device, "B", CommonUsages.secondaryButton))
                    {
                        frame.estop = true;
                        teleop.SetDriveArmed(false);
                        Camera.main?.GetComponent<VResqHeadsetPassthrough>()?.RestoreHud();
                    }
                    Button(device, "RClick", CommonUsages.primary2DAxisClick, profile.questRightClick, teleop, ref frame);
                }
            }
            LostControllers = seen && (!left || !right);
            seen |= left || right;
            Status = $"Quest L {(left ? "tracked" : "missing")} / R {(right ? "tracked" : "missing")}";
            if (!left && !right) previous.Clear();
            return left || right;
        }

        public static Vector2 ShapeDrive(Vector2 axis, float deadzone, float steering, bool invertDrive, bool invertSteering)
        {
            if (float.IsNaN(axis.x) || float.IsNaN(axis.y) || float.IsInfinity(axis.x) || float.IsInfinity(axis.y) || axis.magnitude <= deadzone) return Vector2.zero;
            float length = Mathf.Clamp01((axis.magnitude - deadzone) / (1 - deadzone));
            // Blend linear and cubic response for finer low-speed steering.
            axis = axis.normalized * (0.5f * length * (1f + length * length));
            return new Vector2(Mathf.Clamp(axis.x * steering * (invertSteering ? -1 : 1), -1, 1), axis.y * (invertDrive ? -1 : 1));
        }

        private bool Edge(InputDevice device, string key, InputFeatureUsage<bool> usage)
        {
            device.TryGetFeatureValue(usage, out bool value);
            bool edge = value && (!previous.TryGetValue(key, out bool old) || !old);
            previous[key] = value;
            return edge;
        }

        private void Button(InputDevice device, string key, InputFeatureUsage<bool> usage, QuestButtonAction action, VResqTeleopController teleop, ref Frame frame)
        {
            if (action == QuestButtonAction.Horn) { device.TryGetFeatureValue(usage, out bool held); frame.horn |= held; return; }
            if (!Edge(device, key, usage)) return;
            var view = GetComponent<VResqOperationsPanel>();
            var cfg = GetComponent<VResqFrameworkConfig>();
            switch (action)
            {
                case QuestButtonAction.HeadsetView:
                    if (cfg != null) GetComponent<VResqPresentationControls>()?.SetBackground(!cfg.Profile.headsetPassthrough);
                    break;
                case QuestButtonAction.NextCamera: GetComponent<VResqCameraReceiver>()?.CycleCameraSource(); break;
                case QuestButtonAction.OperatorMenu: ToggleMenu(); break;
                case QuestButtonAction.CenterServo: teleop?.ActionCenter(); break;
                case QuestButtonAction.NextRobot: teleop?.SetDriveArmed(false); teleop?.CycleNextFleetNode(); break;
                case QuestButtonAction.Protocol: view?.Show("Protocol"); break;
                case QuestButtonAction.Swarm: view?.Show("Swarm"); break;
                case QuestButtonAction.SpecialOps: GetComponent<VResQ.HMI.VResqSpecialCommandsPanel>()?.ToggleDrawer(); break;
                case QuestButtonAction.ArmToggle: if (teleop != null) teleop.SetDriveArmed(!teleop.DriveArmed); break;
            }
        }

        private void ToggleMenu()
        {
            Camera.main?.GetComponent<VResqHeadsetPassthrough>()?.RestoreHud();
            var view = GetComponent<VResqOperationsPanel>();
            if (view != null) view.Show(view.ActiveTab == "Controls" ? "Live" : "Controls");
        }
    }
}
