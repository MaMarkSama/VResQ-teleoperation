using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VResQ.Core;

namespace VResQ.Control
{
    public class VResqControlPanel : MonoBehaviour
    {
        public Slider speed, deadzone, steering, servo;
        public Toggle grip, invertDrive, invertSteering;
        public TMP_Dropdown[] mapping;
        public Button arm, stop, reset, save, headsetTest;
        public TextMeshProUGUI live;
        private VResqFrameworkConfig config;
        private VResqTeleopController controller;
        private VResqClient client;

        private float _nextLiveRefresh;

        private void Start()
        {
            config = GetComponentInParent<VResqFrameworkConfig>();
            controller = GetComponentInParent<VResqTeleopController>();
            client = GetComponentInParent<VResqClient>();
            if (speed != null) speed.onValueChanged.AddListener(v => { if (config != null) { config.Profile.speedScale = v; config.NotifyConfigChanged(); } });
            if (deadzone != null) deadzone.onValueChanged.AddListener(v => { if (config != null) { config.Profile.stickDeadzone = v; config.NotifyConfigChanged(); } });
            if (steering != null) steering.onValueChanged.AddListener(v => { if (config != null) { config.Profile.steeringSensitivity = v; config.NotifyConfigChanged(); } });
            if (servo != null) servo.onValueChanged.AddListener(v => controller?.SetServo(Mathf.RoundToInt(v)));
            if (grip != null) grip.onValueChanged.AddListener(v => { if (config != null) { config.Profile.questGripRequired = v; config.NotifyConfigChanged(); } });
            if (invertDrive != null) invertDrive.onValueChanged.AddListener(v => { if (config != null) { config.Profile.invertDrive = v; config.NotifyConfigChanged(); } });
            if (invertSteering != null) invertSteering.onValueChanged.AddListener(v => { if (config != null) { config.Profile.invertSteering = v; config.NotifyConfigChanged(); } });
            if (mapping != null)
            {
                for (int i = 0; i < mapping.Length; i++)
                {
                    if (mapping[i] == null) continue;
                    int index = i;
                    mapping[i].ClearOptions();
                    mapping[i].AddOptions(new System.Collections.Generic.List<string>(Enum.GetNames(typeof(QuestButtonAction))));
                    mapping[i].onValueChanged.AddListener(v => SetMapping(index, (QuestButtonAction)v));
                }
            }
            if (arm != null) arm.onClick.AddListener(() =>
            {
                if (controller == null) return;
                controller.SetDriveArmed(true);
                if (controller.DriveArmed) GetComponentInParent<VResqOperationsPanel>()?.Show("Live");
            });
            if (stop != null) stop.onClick.AddListener(() => { controller?.SetDriveArmed(false); controller?.ActionEmergencyStop(); });
            if (reset != null) reset.onClick.AddListener(() => client?.ResetEstop());
            if (save != null) save.onClick.AddListener(() => config?.SaveProfile());
            if (headsetTest != null) headsetTest.onClick.AddListener(() =>
            {
                controller?.SetDriveArmed(false);
                GetComponentInParent<VResQ.Video.VResqPresentationControls>()?.SetBackground(true);
                Camera.main?.GetComponent<VResQ.Video.VResqHeadsetPassthrough>()?.ToggleIsolation(GetComponentInParent<Canvas>());
            });
            if (config != null)
            {
                config.OnConfigChanged.AddListener(Sync);
                Sync();
            }
        }

        private void SetMapping(int index, QuestButtonAction action)
        {
            if (config == null) return;
            if (index == 0) config.Profile.questA = action;
            if (index == 1) config.Profile.questX = action;
            if (index == 2) config.Profile.questY = action;
            if (index == 3) config.Profile.questRightClick = action;
            config.NotifyConfigChanged();
        }

        private void Sync()
        {
            if (config == null || config.Profile == null) return;
            var p = config.Profile;
            if (speed != null) speed.SetValueWithoutNotify(p.speedScale);
            if (deadzone != null) deadzone.SetValueWithoutNotify(p.stickDeadzone);
            if (steering != null) steering.SetValueWithoutNotify(p.steeringSensitivity);
            if (servo != null && controller != null) servo.SetValueWithoutNotify(controller.currentServoDeg);
            if (grip != null) grip.SetIsOnWithoutNotify(p.questGripRequired);
            if (invertDrive != null) invertDrive.SetIsOnWithoutNotify(p.invertDrive);
            if (invertSteering != null) invertSteering.SetIsOnWithoutNotify(p.invertSteering);
            var actions = new[] { p.questA, p.questX, p.questY, p.questRightClick };
            if (mapping != null)
            {
                for (int i = 0; i < mapping.Length && i < actions.Length; i++)
                    if (mapping[i] != null) mapping[i].SetValueWithoutNotify((int)actions[i]);
            }
        }

        private void Update()
        {
            if (config == null || controller == null || client == null || live == null) return;
            if (Time.unscaledTime < _nextLiveRefresh) return;
            _nextLiveRefresh = Time.unscaledTime + 0.15f;
            var p = config.Profile;
            var quest = controller.GetComponent<VResqQuestInput>();
            bool estop = client.LatestState != null && client.LatestState.estop;
            bool authorized = client.LatestState != null && client.LatestState.motion_authorized;
            live.text = $"{controller.DriveStatus}   |   Speed {p.speedScale:P0}   Deadzone {p.stickDeadzone:F2}   Steering {p.steeringSensitivity:F2}\n" +
                $"{quest?.Status}   |   Sent {client.OperatorCommandsSent}   State age {client.StateAgeSeconds:F1}s\n{client.LastCommandStatus}\n" +
                $"{client.LastCommandAck}   |   E-stop {estop}   Authorized {authorized}   Servo {controller.currentServoDeg} deg";
        }

        private void OnDestroy() { if (config != null) config.OnConfigChanged.RemoveListener(Sync); }
    }
}
