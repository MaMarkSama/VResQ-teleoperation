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

        private void Start()
        {
            config = GetComponentInParent<VResqFrameworkConfig>();
            controller = GetComponentInParent<VResqTeleopController>();
            client = GetComponentInParent<VResqClient>();
            speed.onValueChanged.AddListener(v => { config.Profile.speedScale = v; config.NotifyConfigChanged(); });
            deadzone.onValueChanged.AddListener(v => { config.Profile.stickDeadzone = v; config.NotifyConfigChanged(); });
            steering.onValueChanged.AddListener(v => { config.Profile.steeringSensitivity = v; config.NotifyConfigChanged(); });
            servo.onValueChanged.AddListener(v => controller.SetServo(Mathf.RoundToInt(v)));
            grip.onValueChanged.AddListener(v => { config.Profile.questGripRequired = v; config.NotifyConfigChanged(); });
            invertDrive.onValueChanged.AddListener(v => { config.Profile.invertDrive = v; config.NotifyConfigChanged(); });
            invertSteering.onValueChanged.AddListener(v => { config.Profile.invertSteering = v; config.NotifyConfigChanged(); });
            for (int i = 0; i < mapping.Length; i++)
            {
                int index = i;
                mapping[i].ClearOptions();
                mapping[i].AddOptions(new System.Collections.Generic.List<string>(Enum.GetNames(typeof(QuestButtonAction))));
                mapping[i].onValueChanged.AddListener(v => SetMapping(index, (QuestButtonAction)v));
            }
            arm.onClick.AddListener(() =>
            {
                controller.SetDriveArmed(true);
                if (controller.DriveArmed) GetComponentInParent<VResqOperationsPanel>().Show("Live");
            });
            stop.onClick.AddListener(() => { controller.SetDriveArmed(false); controller.ActionEmergencyStop(); });
            reset.onClick.AddListener(client.ResetEstop);
            save.onClick.AddListener(config.SaveProfile);
            headsetTest.onClick.AddListener(() =>
            {
                controller.SetDriveArmed(false);
                GetComponentInParent<VResQ.Video.VResqPresentationControls>().SetBackground(true);
                Camera.main.GetComponent<VResQ.Video.VResqHeadsetPassthrough>().ToggleIsolation(GetComponentInParent<Canvas>());
            });
            config.OnConfigChanged.AddListener(Sync);
            Sync();
        }

        private void SetMapping(int index, QuestButtonAction action)
        {
            if (index == 0) config.Profile.questA = action;
            if (index == 1) config.Profile.questX = action;
            if (index == 2) config.Profile.questY = action;
            if (index == 3) config.Profile.questRightClick = action;
            config.NotifyConfigChanged();
        }

        private void Sync()
        {
            var p = config.Profile;
            speed.SetValueWithoutNotify(p.speedScale);
            deadzone.SetValueWithoutNotify(p.stickDeadzone);
            steering.SetValueWithoutNotify(p.steeringSensitivity);
            servo.SetValueWithoutNotify(controller.currentServoDeg);
            grip.SetIsOnWithoutNotify(p.questGripRequired);
            invertDrive.SetIsOnWithoutNotify(p.invertDrive);
            invertSteering.SetIsOnWithoutNotify(p.invertSteering);
            var actions = new[] { p.questA, p.questX, p.questY, p.questRightClick };
            for (int i = 0; i < mapping.Length; i++) mapping[i].SetValueWithoutNotify((int)actions[i]);
        }

        private void Update()
        {
            if (config == null) return;
            var p = config.Profile;
            var quest = controller.GetComponent<VResqQuestInput>();
            live.text = $"{controller.DriveStatus}   |   Speed {p.speedScale:P0}   Deadzone {p.stickDeadzone:F2}   Steering {p.steeringSensitivity:F2}\n" +
                $"{quest?.Status}   |   Sent {client.OperatorCommandsSent}   State age {client.StateAgeSeconds:F1}s\n{client.LastCommandStatus}\n" +
                $"{client.LastCommandAck}   |   E-stop {client.LatestState.estop}   Authorized {client.LatestState.motion_authorized}   Servo {controller.currentServoDeg} deg";
        }

        private void OnDestroy() { if (config != null) config.OnConfigChanged.RemoveListener(Sync); }
    }
}
