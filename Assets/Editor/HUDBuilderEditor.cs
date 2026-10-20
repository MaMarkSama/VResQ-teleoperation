using UnityEngine;
using UnityEditor;
using UnityEngine.UI;
using TMPro;
using VResQ.Core;
using VResQ.Video;
using VResQ.Control;

public class HUDBuilderEditor : Editor
{
    [MenuItem("Tools/Upgrade Existing Scene HUD (Add Live Camera & Controls)")]
    public static void UpgradeExistingHUD()
    {
        RescueRobotVRHUD hud = FindFirstObjectByType<RescueRobotVRHUD>();
        if (hud == null)
        {
            GenerateHUD();
            return;
        }

        GameObject hudRoot = hud.gameObject;

        // Ensure VResqClient
        VResqClient client = hudRoot.GetComponent<VResqClient>() ?? hudRoot.AddComponent<VResqClient>();
        client.robotIp = "10.66.68.52";
        client.port = 8000;

        // Ensure VResqCameraReceiver
        VResqCameraReceiver cameraReceiver = hudRoot.GetComponent<VResqCameraReceiver>() ?? hudRoot.AddComponent<VResqCameraReceiver>();

        // Ensure VResqTeleopController
        VResqTeleopController teleop = hudRoot.GetComponent<VResqTeleopController>() ?? hudRoot.AddComponent<VResqTeleopController>();
        teleop.nodeZeroIp = "10.66.68.52";
        teleop.nodeOneIp = "10.66.68.10";
        teleop.nodeTwoIp = "10.66.68.82";

        // Find or create Center_HUD & Center_VideoScreen
        Transform centerHUD = hudRoot.transform.Find("Center_HUD");
        if (centerHUD != null)
        {
            Transform existingScreen = centerHUD.Find("Center_VideoScreen");
            if (existingScreen == null)
            {
                GameObject videoScreenObj = CreatePanel("Center_VideoScreen", centerHUD, new Vector2(0, 30), new Vector2(800, 480), Color.black);
                videoScreenObj.transform.SetAsFirstSibling();
                RawImage rawImg = videoScreenObj.AddComponent<RawImage>();
                rawImg.color = Color.white;
                hud.centerVideoScreen = rawImg;
                cameraReceiver.targetRawImage = rawImg;
                CreateBorderOutline(videoScreenObj.transform, new Vector2(800, 480), new Color(0f, 0.94f, 1f, 0.5f));
            }
            else
            {
                RawImage rawImg = existingScreen.GetComponent<RawImage>() ?? existingScreen.gameObject.AddComponent<RawImage>();
                hud.centerVideoScreen = rawImg;
                cameraReceiver.targetRawImage = rawImg;
            }
        }

        // Wire Buttons
        Transform bottomBar = hudRoot.transform.Find("Bottom_ActionBar");
        if (bottomBar != null)
        {
            WireButton(bottomBar, "Btn_SCAN", () => teleop.ActionScan());
            WireButton(bottomBar, "Btn_BEACON", () => teleop.ActionBeacon());
            WireButton(bottomBar, "Btn_CENTER", () => teleop.ActionCenter());
            WireButton(bottomBar, "Btn_APPROACH", () => teleop.ActionApproach());
            WireButton(bottomBar, "Btn_BACK OUT", () => teleop.ActionBackOut());
            WireButton(bottomBar, "Btn_MORE", () => teleop.ActionEmergencyStop());
            WireButton(bottomBar, "Btn_E-STOP", () => teleop.ActionEmergencyStop());
        }

        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(hudRoot.scene);
        Debug.Log("<color=cyan>[SUCCESS]</color> Upgraded Existing Scene HUD with Live Camera Screen, Full Teleoperation, and Swarm Telemetry!");
    }

    [MenuItem("Tools/Generate Rescue Robot VR HUD")]
    public static void GenerateHUD()
    {
        // 1. Root Object
        GameObject hudRoot = new GameObject("Tactical_VR_HUD_Root");
        hudRoot.transform.position = new Vector3(0, 1.2f, 1.8f);

        Canvas canvas = hudRoot.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        hudRoot.AddComponent<CanvasScaler>();
        hudRoot.AddComponent<GraphicRaycaster>();

        RectTransform rootRect = hudRoot.GetComponent<RectTransform>();
        rootRect.sizeDelta = new Vector2(1920, 1080);
        rootRect.localScale = Vector3.one * 0.001f;

        // Core Components
        RescueRobotVRHUD hudManager = hudRoot.AddComponent<RescueRobotVRHUD>();
        VResqClient client = hudRoot.AddComponent<VResqClient>();
        client.robotIp = "10.66.68.52"; // Node Zero default
        client.port = 8000;

        VResqCameraReceiver cameraReceiver = hudRoot.AddComponent<VResqCameraReceiver>();
        VResqTeleopController teleop = hudRoot.AddComponent<VResqTeleopController>();
        teleop.nodeZeroIp = "10.66.68.52";
        teleop.nodeOneIp = "10.66.68.10";
        teleop.nodeTwoIp = "10.66.68.82";

        TacticalHUDFollow follow = hudRoot.AddComponent<TacticalHUDFollow>();
        follow.followDistance = 1.6f;
        follow.heightOffset = -0.05f;

        Color panelBgColor = new Color(0.024f, 0.063f, 0.118f, 0.82f);
        Color cyanTextColor = new Color(0f, 0.94f, 1f, 1f);
        Color greenTextColor = new Color(0f, 1f, 0.4f, 1f);
        Color alertColor = new Color(1f, 0.23f, 0.19f, 1f);

        // 2. Top Bar
        GameObject topBar = CreatePanel("TopBar", hudRoot.transform, new Vector2(0, 480), new Vector2(1800, 70), panelBgColor);
        hudManager.robotIdText = CreateTMP("RobotID_Text", topBar.transform, new Vector2(-600, 0), new Vector2(350, 50), "ROBOT 01 (V-RESQ LEADER)", 24, Color.white, TextAlignmentOptions.Center);
        hudManager.latencyText = CreateTMP("Latency_Text", topBar.transform, new Vector2(200, 0), new Vector2(200, 50), "CONNECTING...", 22, greenTextColor, TextAlignmentOptions.Center);
        hudManager.clockText = CreateTMP("Clock_Text", topBar.transform, new Vector2(750, 0), new Vector2(200, 50), "12:00:00", 24, Color.white, TextAlignmentOptions.Right);

        // 3. Left Panel (Local Map & Swarm)
        GameObject leftPanel = CreatePanel("LeftPanel_Map", hudRoot.transform, new Vector2(-680, 50), new Vector2(460, 720), panelBgColor);
        leftPanel.transform.localEulerAngles = new Vector3(0, 18f, 0);
        CreateTMP("MapTitle", leftPanel.transform, new Vector2(-120, 320), new Vector2(220, 40), "SWARM MESH & MAP", 20, cyanTextColor, TextAlignmentOptions.Left);
        GameObject mapBox = CreatePanel("MapGridPlaceholder", leftPanel.transform, new Vector2(0, 130), new Vector2(400, 320), new Color(0.04f, 0.12f, 0.2f, 0.9f));
        hudManager.swarmStatusText = CreateTMP("SwarmStatus", leftPanel.transform, new Vector2(0, -170), new Vector2(400, 240), "SWARM FLEET MESH\n● CONNECTING TO MESH...\n\n[Press 1=Node0, 2=Node1, 3=Node2]", 15, Color.white, TextAlignmentOptions.TopLeft);

        // 4. Center HUD (Camera Screen, Compass, Speed, Distance)
        GameObject centerHUD = new GameObject("Center_HUD");
        centerHUD.transform.SetParent(hudRoot.transform, false);
        centerHUD.transform.localPosition = new Vector3(0, 50, 0);

        // FPV Live Video Screen (RawImage)
        GameObject videoScreenObj = CreatePanel("Center_VideoScreen", centerHUD.transform, new Vector2(0, 30), new Vector2(800, 480), Color.black);
        RawImage videoRawImage = videoScreenObj.AddComponent<RawImage>();
        videoRawImage.color = Color.white;
        hudManager.centerVideoScreen = videoRawImage;
        cameraReceiver.targetRawImage = videoRawImage;

        // Cyber Frame outline for Video Screen
        CreateBorderOutline(videoScreenObj.transform, new Vector2(800, 480), new Color(0f, 0.94f, 1f, 0.5f));

        hudManager.compassHeadingText = CreateTMP("CompassHeading_Text", centerHUD.transform, new Vector2(0, 305), new Vector2(240, 40), "000° N", 24, cyanTextColor, TextAlignmentOptions.Center);
        hudManager.speedPercentText = CreateTMP("Speed_Text", centerHUD.transform, new Vector2(0, -245), new Vector2(200, 50), "SPD 70%", 28, cyanTextColor, TextAlignmentOptions.Center);
        hudManager.motorLeftText = CreateTMP("MotorL_Text", centerHUD.transform, new Vector2(-280, -245), new Vector2(160, 40), "L +0", 22, Color.white, TextAlignmentOptions.Center);
        hudManager.motorRightText = CreateTMP("MotorR_Text", centerHUD.transform, new Vector2(280, -245), new Vector2(160, 40), "R +0", 22, Color.white, TextAlignmentOptions.Center);
        hudManager.ultrasonicDistanceText = CreateTMP("Distance_Text", centerHUD.transform, new Vector2(0, -290), new Vector2(240, 45), "-- cm", 24, Color.yellow, TextAlignmentOptions.Center);

        // 5. Right Panel (Attitude, Sensors, Diagnostics)
        GameObject rightPanel = CreatePanel("RightPanel_Diagnostics", hudRoot.transform, new Vector2(680, 50), new Vector2(460, 720), panelBgColor);
        rightPanel.transform.localEulerAngles = new Vector3(0, -18f, 0);
        hudManager.healthText = CreateTMP("Health_Text", rightPanel.transform, new Vector2(100, 320), new Vector2(180, 40), "100%", 26, greenTextColor, TextAlignmentOptions.Right);
        CreateTMP("HealthLabel", rightPanel.transform, new Vector2(-100, 320), new Vector2(180, 40), "ROBOT HEALTH", 22, cyanTextColor, TextAlignmentOptions.Left);

        // IMU Artificial Horizon
        GameObject imuBox = CreatePanel("IMU_Horizon", rightPanel.transform, new Vector2(0, 160), new Vector2(220, 220), new Color(0.1f, 0.2f, 0.35f, 1f));
        hudManager.imuHorizonBall = imuBox.GetComponent<RectTransform>();
        hudManager.pitchText = CreateTMP("Pitch_Text", rightPanel.transform, new Vector2(-80, 30), new Vector2(140, 30), "PITCH 0°", 18, Color.white, TextAlignmentOptions.Center);
        hudManager.rollText = CreateTMP("Roll_Text", rightPanel.transform, new Vector2(80, 30), new Vector2(140, 30), "ROLL 0°", 18, Color.white, TextAlignmentOptions.Center);

        // Diagnostic Checklist
        hudManager.sensorListText = CreateTMP("SensorList", rightPanel.transform, new Vector2(0, -80), new Vector2(400, 120), "● ULTRASONIC: OK    ● IMU: OK\n● BME280: OK            ● OLED: OK\n● MOTOR DRIVER: OK", 15, greenTextColor, TextAlignmentOptions.TopLeft);
        hudManager.batteryText = CreateTMP("Battery_Text", rightPanel.transform, new Vector2(0, -170), new Vector2(400, 40), "12.4 V   92%", 22, greenTextColor, TextAlignmentOptions.Right);

        // Obstacle Alert Frame
        GameObject alertBox = CreatePanel("ObstacleAlert_Panel", rightPanel.transform, new Vector2(0, -260), new Vector2(400, 100), new Color(0.5f, 0.05f, 0.05f, 0.95f));
        hudManager.obstacleAlertPanel = alertBox;
        alertBox.SetActive(false);
        CreateTMP("AlertTitle", alertBox.transform, new Vector2(-60, 20), new Vector2(240, 30), "OBSTACLE ALERT", 18, alertColor, TextAlignmentOptions.Left);
        hudManager.obstacleDistanceText = CreateTMP("AlertDist_Text", alertBox.transform, new Vector2(-60, -15), new Vector2(240, 30), "15 cm (STOP)", 22, Color.white, TextAlignmentOptions.Left);

        // 6. Bottom Action Bar
        GameObject bottomBar = CreatePanel("Bottom_ActionBar", hudRoot.transform, new Vector2(0, -420), new Vector2(1500, 140), panelBgColor);
        bottomBar.transform.localEulerAngles = new Vector3(-12f, 0, 0);

        // Deadman Status
        hudManager.deadmanStatusText = CreateTMP("DeadmanText", bottomBar.transform, new Vector2(0, 38), new Vector2(300, 35), "STANDBY", 20, greenTextColor, TextAlignmentOptions.Center);
        GameObject dot = CreatePanel("DeadmanDot", bottomBar.transform, new Vector2(-75, 38), new Vector2(16, 16), Color.red);
        hudManager.deadmanIndicatorDot = dot.GetComponent<Image>();

        // Action Buttons with Teleop wiring
        string[] cmds = { "SCAN", "BEACON", "CENTER", "APPROACH", "BACK OUT", "E-STOP" };
        for (int i = 0; i < cmds.Length; i++)
        {
            float posX = -500 + (i * 200);
            Button btn = CreateButton(cmds[i], bottomBar.transform, new Vector2(posX, -25), new Vector2(170, 55), cmds[i]);
            string cmdName = cmds[i];
            btn.onClick.AddListener(() =>
            {
                if (cmdName == "SCAN") teleop.ActionScan();
                else if (cmdName == "BEACON") teleop.ActionBeacon();
                else if (cmdName == "CENTER") teleop.ActionCenter();
                else if (cmdName == "APPROACH") teleop.ActionApproach();
                else if (cmdName == "BACK OUT") teleop.ActionBackOut();
                else if (cmdName == "E-STOP") teleop.ActionEmergencyStop();
            });
        }

        Selection.activeGameObject = hudRoot;
        Undo.RegisterCreatedObjectUndo(hudRoot, "Generate Tactical VR HUD");
        Debug.Log("<color=cyan>[SUCCESS]</color> Complete V-ResQ Tactical VR Cockpit HUD with Live Video & Full Controls Generated!");
    }

    private static void WireButton(Transform parent, string btnName, UnityEngine.Events.UnityAction action)
    {
        Transform t = parent.Find(btnName);
        if (t != null)
        {
            Button btn = t.GetComponent<Button>();
            if (btn != null)
            {
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(action);
            }
        }
    }

    private static GameObject CreatePanel(string name, Transform parent, Vector2 pos, Vector2 size, Color color)
    {
        GameObject panel = new GameObject(name);
        panel.transform.SetParent(parent, false);

        RectTransform rect = panel.AddComponent<RectTransform>();
        rect.anchoredPosition = pos;
        rect.sizeDelta = size;

        Image img = panel.AddComponent<Image>();
        img.color = color;
        return panel;
    }

    private static void CreateBorderOutline(Transform parent, Vector2 size, Color color)
    {
        GameObject outline = new GameObject("Cyber_Border_Outline");
        outline.transform.SetParent(parent, false);
        RectTransform rt = outline.AddComponent<RectTransform>();
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = size;

        Outline ol = outline.AddComponent<Outline>();
        ol.effectColor = color;
        ol.effectDistance = new Vector2(2, 2);
    }

    private static TextMeshProUGUI CreateTMP(string name, Transform parent, Vector2 pos, Vector2 size, string text, float fontSize, Color color, TextAlignmentOptions align)
    {
        GameObject tmpObj = new GameObject(name);
        tmpObj.transform.SetParent(parent, false);

        RectTransform rect = tmpObj.AddComponent<RectTransform>();
        rect.anchoredPosition = pos;
        rect.sizeDelta = size;

        TextMeshProUGUI tmp = tmpObj.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.color = color;
        tmp.alignment = align;
        tmp.fontStyle = FontStyles.Bold;
        return tmp;
    }

    private static Button CreateButton(string name, Transform parent, Vector2 pos, Vector2 size, string label)
    {
        GameObject btnObj = CreatePanel($"Btn_{name}", parent, pos, size, new Color(0.08f, 0.16f, 0.28f, 0.9f));
        Button btn = btnObj.AddComponent<Button>();

        ColorBlock cb = btn.colors;
        cb.highlightedColor = new Color(0f, 0.8f, 1f, 0.8f);
        cb.pressedColor = new Color(1f, 0.5f, 0f, 1f);
        btn.colors = cb;

        CreateTMP("Label", btnObj.transform, Vector2.zero, size, label, 18, Color.white, TextAlignmentOptions.Center);
        return btn;
    }
}
