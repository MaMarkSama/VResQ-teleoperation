using System;
using System.IO;
using UnityEngine;
using UnityEngine.Events;
using VResQ.Video;

namespace VResQ.Core
{
    public enum CockpitTheme
    {
        CyberCyan = 0,
        EmeraldRescue = 1,
        AmberHazard = 2,
        CrimsonOps = 3
    }

    [Serializable]
    public class RobotNodeEndpoint
    {
        public string displayName = "NODE ZERO";
        public string roleLabel = "LEADER";
        public string ipAddress = "172.20.10.3";
        public int port = 8000;

        [NonSerialized] public bool isOnline = false;
        [NonSerialized] public float lastRttMs = 0f;
        [NonSerialized] public string remoteNodeName = "";
        [NonSerialized] public string remoteRole = "";
        [NonSerialized] public float detectorFps = 0f;
    }

    [Serializable]
    public class VResqProfileData
    {
        // 1. Network & Swarm Fleet
        public RobotNodeEndpoint[] fleetNodes = new RobotNodeEndpoint[]
        {
            new RobotNodeEndpoint { displayName = "NODE 0", roleLabel = "COMMANDER", ipAddress = "192.168.1.138", port = 8000 },
            new RobotNodeEndpoint { displayName = "NODE 1", roleLabel = "RELAY", ipAddress = "192.168.1.122", port = 8000 },
            new RobotNodeEndpoint { displayName = "NODE 2", roleLabel = "SCOUT", ipAddress = "192.168.1.149", port = 8000 }
        };
        public int defaultNodeIndex = 0;
        public bool autoFallbackToOnlineNode = true;
        public float fleetProbeIntervalSec = 2.5f;

        // 2. Surround Panoramic Camera vs External World View + Center Robot Monitor
        public SurroundViewMode viewMode = SurroundViewMode.ExternalWorld;
        public float surroundRadius = 2.8f;
        public float surroundHeight = 2.8f;
        public float surroundArcAngle = 360f;
        public float surroundVerticalOffset = 0.0f;
        public bool showCenterMonitorInSurround = true;
        public bool backgroundVisible = true;
        public bool robotMonitorVisible = true;
        public float cameraBrightness = 1.2f;
        public float centerMonitorOpacityInSurround = 1.0f;

        // 3. HUD Ergonomics & Visual Theme
        public CockpitTheme theme = CockpitTheme.CyberCyan;
        public float hudDistance = 1.45f;
        public float hudHeightOffset = -0.04f;
        public float hudScale = 0.00085f;
        public float wingPanelAngle = 24f;
        public float panelOpacity = 0.82f;
        public bool smoothFollowHead = true;
        public float followDeadzoneAngle = 25f;

        // 4. Joystick / Gamepad & Teleoperation Control
        public bool enableJoystickControl = true;
        public float speedScale = 0.75f;
        public float stickDeadzone = 0.10f;
        public bool rightStickControlsLookAndGimbal = true;
        public float lookYawSpeedDegPerSec = 95f;
        public bool requireDeadmanSwitch = false;
        public int servoStepDeg = 5;
        public float collisionAlertCm = 25f;
        public float collisionCautionCm = 55f;
    }

    /// <summary>
    /// Central Framework Configuration Manager for V-ResQ Unity Teleoperation.
    /// Allows real-time customization in Inspector, Editor Window, and In-VR Settings UI with JSON persistence.
    /// </summary>
    [ExecuteAlways]
    public class VResqFrameworkConfig : MonoBehaviour
    {
        public static VResqFrameworkConfig Instance { get; private set; }

        [Header("--- Active Framework Profile ---")]
        public VResqProfileData Profile = new VResqProfileData();

        [Header("--- Events ---")]
        public UnityEvent OnConfigChanged = new UnityEvent();

        private string ConfigFilePath => Path.Combine(Application.persistentDataPath, "vresq_framework_config.json");

        private void OnEnable()
        {
            Instance = this;
        }

        private void Awake()
        {
            Instance = this;
            if (Application.isPlaying)
            {
                LoadProfile();
            }
        }

        public void NotifyConfigChanged()
        {
            Profile.cameraBrightness = float.IsNaN(Profile.cameraBrightness) || float.IsInfinity(Profile.cameraBrightness)
                ? 1.2f : Mathf.Clamp(Profile.cameraBrightness, 0.5f, 2f);
            OnConfigChanged?.Invoke();
        }

        public void SaveProfile()
        {
            try
            {
                string json = JsonUtility.ToJson(Profile, true);
                File.WriteAllText(ConfigFilePath, json);
                Debug.Log($"<color=cyan>[VResqConfig]</color> Saved profile to {ConfigFilePath}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[VResqConfig] Could not save profile: {ex.Message}");
            }
            NotifyConfigChanged();
        }

        public void LoadProfile()
        {
            try
            {
                if (File.Exists(ConfigFilePath))
                {
                    string json = File.ReadAllText(ConfigFilePath);
                    JsonUtility.FromJsonOverwrite(json, Profile);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[VResqConfig] Could not load profile: {ex.Message}");
            }

            if (Profile.fleetNodes == null || Profile.fleetNodes.Length < 3 ||
                (Profile.fleetNodes.Length > 0 && Profile.fleetNodes[0].ipAddress.StartsWith("10.66.")))
            {
                Profile.fleetNodes = new RobotNodeEndpoint[]
                {
                    new RobotNodeEndpoint { displayName = "NODE ZERO", roleLabel = "COMMANDER", ipAddress = "192.168.1.138", port = 8000 },
                    new RobotNodeEndpoint { displayName = "NODE ONE", roleLabel = "RELAY", ipAddress = "192.168.1.122", port = 8000 },
                    new RobotNodeEndpoint { displayName = "NODE TWO", roleLabel = "SCOUT", ipAddress = "192.168.1.149", port = 8000 }
                };
            }

            NotifyConfigChanged();
        }

        public void ResetToDefaults()
        {
            Profile = new VResqProfileData();
            SaveProfile();
        }

        public Color GetPrimaryThemeColor()
        {
            return Profile.theme switch
            {
                CockpitTheme.CyberCyan => new Color(0f, 0.92f, 1f, 1f),
                CockpitTheme.EmeraldRescue => new Color(0.1f, 1f, 0.55f, 1f),
                CockpitTheme.AmberHazard => new Color(1f, 0.72f, 0.1f, 1f),
                CockpitTheme.CrimsonOps => new Color(1f, 0.28f, 0.35f, 1f),
                _ => new Color(0f, 0.92f, 1f, 1f)
            };
        }

        public Color GetPanelBackgroundColor()
        {
            return Profile.theme switch
            {
                CockpitTheme.CyberCyan => new Color(0.02f, 0.06f, 0.12f, Profile.panelOpacity),
                CockpitTheme.EmeraldRescue => new Color(0.02f, 0.10f, 0.07f, Profile.panelOpacity),
                CockpitTheme.AmberHazard => new Color(0.11f, 0.07f, 0.02f, Profile.panelOpacity),
                CockpitTheme.CrimsonOps => new Color(0.11f, 0.02f, 0.04f, Profile.panelOpacity),
                _ => new Color(0.02f, 0.06f, 0.12f, Profile.panelOpacity)
            };
        }
    }
}
