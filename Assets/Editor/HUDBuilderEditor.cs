using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.UI;
using TMPro;
using VResQ.Core;
using VResQ.Video;
using VResQ.Control;

public class HUDBuilderEditor : EditorWindow
{
    private Vector2 _scrollPos;

    [MenuItem("V-ResQ/Framework Control Center (UX/UI & Config)", false, 1)]
    public static void ShowControlCenter()
    {
        var win = GetWindow<HUDBuilderEditor>("V-ResQ Framework");
        win.minSize = new Vector2(460, 620);
        win.Show();
    }

    [MenuItem("V-ResQ/Rebuild Open-Visor Surround HUD (Web Dashboard Parity)", false, 2)]
    public static void BuildFullTacticalHUD()
    {
        RebuildSceneCleanly();
    }

    /// <summary>
    /// Rebuilds SampleScene.unity with:
    /// 1. 100% Web Dashboard Data Parity (Ops Strip, 6-Sensor Strip, Drive/Torque, OLED Mirror, Local Map & Trail, IMU Twin, Smart Assist, Mission Assessment)
    /// 2. Open See-Through Center Visor + 360°/220°/180° Surround Camera Dome vs. External 3D World toggle
    /// 3. 100% Joystick / Gamepad / XR Controller integration
    /// </summary>
    public static void RebuildSceneCleanly()
    {
        if (string.IsNullOrEmpty(EditorSceneManager.GetActiveScene().path))
        {
            EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        }

        // Ensure Unity 6 allows HTTP connections to 10.66.68.x robots
        PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;

        // 1. Align Main Camera inside XR Origin so camera is at (0,0,0) local offset
        Camera mainCam = Camera.main;
        if (mainCam == null)
        {
            mainCam = Object.FindFirstObjectByType<Camera>();
        }
        if (mainCam != null)
        {
            mainCam.transform.localPosition = Vector3.zero;
            mainCam.transform.localRotation = Quaternion.identity;
            mainCam.nearClipPlane = 0.05f;
        }

        // 2. Ensure 3D External World Environment exists for "มองเห็นรอบนอก"
        SetupExternal3DWorldEnvironment();

        // 3. Remove all existing Tactical_VR_HUD_Root instances
        var existingHuds = Object.FindObjectsByType<RescueRobotVRHUD>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var oldHud in existingHuds)
        {
            if (oldHud != null && oldHud.gameObject != null)
            {
                DestroyImmediate(oldHud.gameObject);
            }
        }
        GameObject strayRoot = GameObject.Find("Tactical_VR_HUD_Root");
        while (strayRoot != null)
        {
            DestroyImmediate(strayRoot);
            strayRoot = GameObject.Find("Tactical_VR_HUD_Root");
        }
        foreach (var strayClient in Object.FindObjectsByType<VResqClient>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (strayClient != null && strayClient.gameObject != null) DestroyImmediate(strayClient.gameObject);
        }
        foreach (var strayDisc in Object.FindObjectsByType<VResqAutoDiscovery>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (strayDisc != null && strayDisc.gameObject != null) DestroyImmediate(strayDisc.gameObject);
        }
        GameObject staleNetworkManager = GameObject.Find("NetworkManager");
        while (staleNetworkManager != null)
        {
            DestroyImmediate(staleNetworkManager);
            staleNetworkManager = GameObject.Find("NetworkManager");
        }

        // 4. Create Master World-Space Canvas Root
        GameObject hudRoot = new GameObject("Tactical_VR_HUD_Root");
        Canvas canvas = hudRoot.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;

        CanvasScaler scaler = hudRoot.AddComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 10f;
        hudRoot.AddComponent<GraphicRaycaster>();

        var xrRaycasterType = System.Type.GetType("UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster, Unity.XR.Interaction.Toolkit");
        if (xrRaycasterType != null)
        {
            hudRoot.AddComponent(xrRaycasterType);
        }

        RectTransform rootRect = hudRoot.GetComponent<RectTransform>();
        rootRect.sizeDelta = new Vector2(1920f, 1080f);
        rootRect.localScale = new Vector3(0.00085f, 0.00085f, 0.00085f);

        // 5. Attach Core Framework Components
        VResqFrameworkConfig cfg = hudRoot.AddComponent<VResqFrameworkConfig>();
        cfg.Profile.viewMode = SurroundViewMode.ExternalWorld; // Default: VR Environment around, Robot Camera in center
        cfg.Profile.surroundArcAngle = 360f;
        cfg.Profile.defaultNodeIndex = 0; // Default to Node 0 (Commander C2)
        cfg.Profile.autoFallbackToOnlineNode = true;
        cfg.Profile.showCenterMonitorInSurround = true;

        VResqAutoDiscovery autoDiscovery = hudRoot.AddComponent<VResqAutoDiscovery>();

        VResqClient client = hudRoot.AddComponent<VResqClient>();
        client.robotIp = "192.168.1.138";
        client.port = 8000;

        VResqCameraReceiver camReceiver = hudRoot.AddComponent<VResqCameraReceiver>();

        VResqSurroundDisplay surround = hudRoot.AddComponent<VResqSurroundDisplay>();
        surround.currentViewMode = SurroundViewMode.ExternalWorld;
        VResqTeleopController teleop = hudRoot.AddComponent<VResqTeleopController>();
        TacticalHUDFollow follow = hudRoot.AddComponent<TacticalHUDFollow>();
        RescueRobotVRHUD ctrl = hudRoot.AddComponent<RescueRobotVRHUD>();
        ctrl.surroundDisplay = surround;

        if (mainCam != null)
        {
            canvas.worldCamera = mainCam;
            follow.targetCamera = mainCam.transform;
            surround.targetCamera = mainCam.transform;
            teleop.cameraRigTransform = mainCam.transform.parent != null ? mainCam.transform.parent : mainCam.transform;
            follow.followDistance = cfg.Profile.hudDistance;
            follow.heightOffset = cfg.Profile.hudHeightOffset;
            follow.ResetHUDOrientation();
        }
        else
        {
            hudRoot.transform.position = new Vector3(0f, 1.2f, 2.25f);
        }

        // Palette
        Color cPanelBg = new Color(0.02f, 0.06f, 0.12f, 0.82f);
        Color cCardBg = new Color(0.04f, 0.11f, 0.20f, 0.86f);
        Color cCyan = new Color(0.0f, 0.92f, 1.0f, 1.0f);
        Color cCyanDim = new Color(0.0f, 0.70f, 0.88f, 0.50f);
        Color cGreen = new Color(0.0f, 1.0f, 0.55f, 1.0f);
        Color cAmber = new Color(1.0f, 0.78f, 0.18f, 1.0f);
        Color cRed = new Color(1.0f, 0.22f, 0.28f, 1.0f);
        Color cTextLight = new Color(0.88f, 0.96f, 1.0f, 1.0f);

        // =========================================================================
        // 1. TOP COMMAND BAR + WEB DASHBOARD OPS & SENSOR STRIP (1740 x 110 at Y = +480)
        // =========================================================================
        GameObject topBar = CreatePanel("TopBar", hudRoot.transform, new Vector2(0f, 480f), new Vector2(1740f, 108f), cPanelBg);
        ctrl.themePanelBackgrounds.Add(topBar.GetComponent<Image>());
        CreateBorder(topBar.transform, new Vector2(1740f, 108f), cCyanDim, 1.5f);

        // Top Row (Y = +28)
        ctrl.robotIdText = CreateTMP("RobotID", topBar.transform, "● VRESQ-NODE-ZERO  [T1-COMMANDER]  •  172.20.10.4",
            new Vector2(-615f, 28f), new Vector2(460f, 36f), 19, cCyan, TextAlignmentOptions.Left, true);
        ctrl.themeAccentGraphics.Add(ctrl.robotIdText);

        ctrl.latencyText = CreateTMP("Latency", topBar.transform, "DASHBOARD OK  |  RTT: 18 ms  |  VIDEO: 25 FPS",
            new Vector2(-195f, 28f), new Vector2(360f, 36f), 16, cGreen, TextAlignmentOptions.Center, true);

        ctrl.viewModeBadgeText = CreateTMP("ViewModeBadge", topBar.transform, "🎥 VIEW: 360° SURROUND CAM (ภาพรอบตัว) [Y/V]",
            new Vector2(175f, 28f), new Vector2(360f, 36f), 15, cAmber, TextAlignmentOptions.Center, true);

        ctrl.aiStatusBadgeText = CreateTMP("AIStatusBadge", topBar.transform, "AI: JETSON (0 LIVE / 51 TOTAL | 9.2 FPS)",
            new Vector2(510f, 28f), new Vector2(300f, 36f), 14, cGreen, TextAlignmentOptions.Center, true);

        ctrl.clockText = CreateTMP("Clock", topBar.transform, "MISSION 00:00:00",
            new Vector2(745f, 28f), new Vector2(180f, 36f), 16, Color.white, TextAlignmentOptions.Right, true);

        CreateBox("TopBar_Divider", topBar.transform, new Vector2(0f, 6f), new Vector2(1710f, 1.5f), cCyanDim);

        // Middle Row (Y = -10): Web Dashboard Ops Strip (Robot | Serial | FSM | Action | Command Age | Telemetry Age)
        ctrl.opsStripText = CreateTMP("OpsStrip", topBar.transform,
            "<b>Robot:</b> vresq-node-two   |   <b>Serial:</b> offline   |   <b>FSM:</b> IDLE   |   <b>Action:</b> <color=#00F0FF>idle</color>   |   <b>Cmd Age:</b> 18 ms   |   <b>Telem Age:</b> --",
            new Vector2(-95f, -10f), new Vector2(1500f, 26f), 14, cTextLight, TextAlignmentOptions.Left, false);

        // Bottom Row (Y = -34): Web Dashboard 6-Sensor Strip (Ultrasonic | BME280 | MPU6050 | OLED | Altitude | Fused Yaw)
        ctrl.sensorStripText = CreateTMP("SensorStrip", topBar.transform,
            "<b>Ultrasonic:</b> -- cm   |   <b>BME280:</b> offline   |   <b>MPU6050:</b> offline   |   <b>OLED:</b> Page 0   |   <b>Altitude:</b> 0.0 m   |   <b>Fused Yaw:</b> <color=#00F0FF>078.6°</color>",
            new Vector2(-95f, -34f), new Vector2(1500f, 26f), 14, cTextLight, TextAlignmentOptions.Left, false);

        CreateButton("BTN_EXT_CAM_TOP", topBar.transform, "🌐 รอบนอก/กล้อง [Y]",
            new Vector2(775f, -22f), new Vector2(155f, 44f), new Color(0.0f, 0.50f, 0.42f, 0.95f), 14);

        // =========================================================================
        // 2. LEFT WING PANEL: LOCAL MAP, SWARM FLEET & OLED MIRROR (420 x 790 at X = -690, Y = -8)
        // =========================================================================
        GameObject leftPanel = CreatePanel("LeftPanel_Map", hudRoot.transform, new Vector2(-690f, -8f), new Vector2(420f, 790f), cPanelBg);
        ctrl.leftPanelRect = leftPanel.GetComponent<RectTransform>();
        ctrl.leftPanelRect.localEulerAngles = new Vector3(0f, -cfg.Profile.wingPanelAngle, 0f);
        ctrl.themePanelBackgrounds.Add(leftPanel.GetComponent<Image>());
        CreateBorder(leftPanel.transform, new Vector2(420f, 790f), cCyanDim, 1.5f);

        var hdrLeft = CreateTMP("Header_Left", leftPanel.transform, "◀ LOCAL MAP, SWARM & OLED MIRROR",
            new Vector2(0f, 368f), new Vector2(390f, 30f), 17, cCyan, TextAlignmentOptions.Left, true);
        ctrl.themeAccentGraphics.Add(hdrLeft);
        CreateBox("Header_Left_Line", leftPanel.transform, new Vector2(0f, 350f), new Vector2(390f, 2f), cCyanDim);

        // Local Map & Nav Card (388 x 290 at Y = +195)
        GameObject radarCard = CreatePanel("Radar_Card", leftPanel.transform, new Vector2(0f, 195f), new Vector2(388f, 290f), cCardBg);
        GameObject radarScreenObj = new GameObject("Radar_Screen");
        radarScreenObj.transform.SetParent(radarCard.transform, false);
        RectTransform radarRt = radarScreenObj.AddComponent<RectTransform>();
        radarRt.anchoredPosition = new Vector2(0f, 22f);
        radarRt.sizeDelta = new Vector2(224f, 224f);
        RawImage radarRawImg = radarScreenObj.AddComponent<RawImage>();
        radarRawImg.color = Color.white;
        CreateBorder(radarScreenObj.transform, new Vector2(228f, 228f), cCyan, 1.5f);

        VResqRadarMap radarComp = leftPanel.AddComponent<VResqRadarMap>();
        radarComp.radarRawImage = radarRawImg;
        radarComp.mapCoordsText = CreateTMP("MapCoords", radarCard.transform,
            "POSE: X -1.98m   Y -11.27m\nHDG: 078.6°  |  SERVO: 037°  |  VRESQ-NODE-TWO",
            new Vector2(0f, -114f), new Vector2(376f, 40f), 13, cTextLight, TextAlignmentOptions.Center, false);
        ctrl.radarMap = radarComp;

        // Swarm Fleet Node Selector (3 Cards)
        CreateTMP("Header_Swarm", leftPanel.transform, "SWARM ROBOTS ([LB] OR KEY 1-3 TO SWITCH)",
            new Vector2(0f, 32f), new Vector2(388f, 24f), 14, cAmber, TextAlignmentOptions.Left, true);

        ctrl.nodeCardButtons = new Button[3];
        ctrl.nodeCardBgs = new Image[3];
        ctrl.nodeCardDots = new Image[3];
        ctrl.nodeCardTexts = new TextMeshProUGUI[3];

        string[] defaultLabels = {
            "<b>[1] VRESQ-NODE-ZERO (T1-COMMANDER)</b> <color=#00F0FF>◀ ACTIVE</color>\n<size=12>172.20.10.4:8000 • <color=#00FF77>ONLINE (LIVE-HW)</color></size>",
            "<b>[2] VRESQ-NODE-ONE (T2-FOLLOWER)</b>\n<size=12>172.20.10.2:8000 • <color=#00FF77>ONLINE (LIVE-HW)</color></size>",
            "<b>[3] VRESQ-NODE-TWO (T2-SCOUT)</b>\n<size=12>172.20.10.6:8000 • <color=#00FF77>ONLINE (LIVE-HW)</color></size>"
        };

        float[] cardYs = { -16f, -86f, -156f };
        for (int i = 0; i < 3; i++)
        {
            GameObject cardObj = CreatePanel($"BTN_NODE_{i}", leftPanel.transform, new Vector2(0f, cardYs[i]), new Vector2(388f, 62f),
                i == 2 ? new Color(0f, 0.36f, 0.48f, 0.95f) : cCardBg);
            Image cardBg = cardObj.GetComponent<Image>();
            Button cardBtn = cardObj.AddComponent<Button>();
            cardBtn.targetGraphic = cardBg;
            CreateBorder(cardObj.transform, new Vector2(388f, 62f), cCyanDim, 1f);

            GameObject dotObj = CreateBox($"NodeDot_{i}", cardObj.transform, new Vector2(-170f, 0f), new Vector2(14f, 14f), cGreen);
            TextMeshProUGUI cardTxt = CreateTMP($"NodeTxt_{i}", cardObj.transform, defaultLabels[i],
                new Vector2(10f, 0f), new Vector2(335f, 54f), 14, Color.white, TextAlignmentOptions.Left, false);
            cardTxt.raycastTarget = false;

            ctrl.nodeCardButtons[i] = cardBtn;
            ctrl.nodeCardBgs[i] = cardBg;
            ctrl.nodeCardDots[i] = dotObj.GetComponent<Image>();
            ctrl.nodeCardTexts[i] = cardTxt;
        }

        // Web Dashboard OLED Display Mirror Card (Y = -255)
        GameObject oledCard = CreatePanel("OLED_Mirror_Card", leftPanel.transform, new Vector2(0f, -255f), new Vector2(388f, 108f), new Color(0.01f, 0.03f, 0.06f, 0.95f));
        CreateBorder(oledCard.transform, new Vector2(388f, 108f), cCyan, 1.5f);
        ctrl.oledPreviewText = CreateTMP("OLED_Preview_Txt", oledCard.transform,
            "<color=#00F0FF>OLED [PAGE 0: MAIN]</color>\n<b>FSM IDLE   RX 0</b>\n<size=12><color=#99CCEE>US 0cm   Servo 37°</color></size>",
            new Vector2(-45f, 0f), new Vector2(270f, 92f), 15, Color.white, TextAlignmentOptions.Left, false);
        CreateButton("BTN_OLED_NEXT", oledCard.transform, "OLED\nPAGE ▶",
            new Vector2(138f, 0f), new Vector2(88f, 76f), new Color(0.05f, 0.25f, 0.38f, 0.95f), 13);

        ctrl.swarmStatusText = CreateTMP("SwarmStatus", leftPanel.transform,
            "P2P-MESH DISCOVERED: ACTIVE  |  SWITCH: [LB] OR [1-3]",
            new Vector2(0f, -348f), new Vector2(388f, 34f), 13, cCyan, TextAlignmentOptions.Center, false);

        // =========================================================================
        // 3. OPEN SEE-THROUGH CENTER VISOR (NO FLAT SCREEN!) (880 x 790 at X = 0, Y = -8)
        // =========================================================================
        GameObject centerHUD = new GameObject("Center_HUD");
        centerHUD.transform.SetParent(hudRoot.transform, false);
        RectTransform centerRect = centerHUD.AddComponent<RectTransform>();
        centerRect.anchoredPosition = new Vector2(0f, -8f);
        centerRect.sizeDelta = new Vector2(880f, 790f);

        // Top Compass & Pose Tape Card
        GameObject compassCard = CreatePanel("Compass_Card", centerHUD.transform, new Vector2(0f, 355f), new Vector2(620f, 44f), cPanelBg);
        CreateBorder(compassCard.transform, new Vector2(620f, 44f), cCyanDim, 1.5f);
        ctrl.compassHeadingText = CreateTMP("Compass_Heading", compassCard.transform, "◄  HDG 078.6° E  |  POSE (-2.0, -11.3)  ►",
            Vector2.zero, new Vector2(600f, 38f), 20, cCyan, TextAlignmentOptions.Center, true);
        ctrl.themeAccentGraphics.Add(ctrl.compassHeadingText);

        // Transparent Open Visor Area (Center FPV Robot Camera Screen with Reticle & AI BBoxes)
        GameObject openVisorArea = new GameObject("Open_Visor_Reticle");
        openVisorArea.transform.SetParent(centerHUD.transform, false);
        RectTransform visorRt = openVisorArea.AddComponent<RectTransform>();
        visorRt.anchoredPosition = new Vector2(0f, 95f);
        visorRt.sizeDelta = new Vector2(760f, 420f);

        // 1. Center FPV Robot Camera Screen (แสดงภาพสดจากกล้องหุ่นยนต์ตรงกลาง)
        GameObject videoScreenObj = new GameObject("Center_Robot_Camera_Screen");
        videoScreenObj.transform.SetParent(openVisorArea.transform, false);
        RectTransform videoRt = videoScreenObj.AddComponent<RectTransform>();
        videoRt.anchorMin = Vector2.zero;
        videoRt.anchorMax = Vector2.one;
        videoRt.offsetMin = Vector2.zero;
        videoRt.offsetMax = Vector2.zero;
        RawImage videoRawImg = videoScreenObj.AddComponent<RawImage>();
        videoRawImg.color = new Color(0.08f, 0.12f, 0.18f, 0.95f);
        ctrl.centerVideoScreen = videoRawImg;
        camReceiver.targetRawImage = videoRawImg;
        CreateBorder(videoScreenObj.transform, new Vector2(760f, 420f), cCyanDim, 1.5f);

        CreateBox("Reticle_H_Left", openVisorArea.transform, new Vector2(-24f, 0f), new Vector2(22f, 2f), new Color(0f, 1f, 0.85f, 0.45f));
        CreateBox("Reticle_H_Right", openVisorArea.transform, new Vector2(24f, 0f), new Vector2(22f, 2f), new Color(0f, 1f, 0.85f, 0.45f));
        CreateBox("Reticle_V_Top", openVisorArea.transform, new Vector2(0f, 24f), new Vector2(2f, 22f), new Color(0f, 1f, 0.85f, 0.45f));
        CreateBox("Reticle_V_Bot", openVisorArea.transform, new Vector2(0f, -24f), new Vector2(2f, 22f), new Color(0f, 1f, 0.85f, 0.45f));

        CreateCornerBracket(openVisorArea.transform, new Vector2(-360f, 195f), new Vector2(1f, -1f), cCyanDim);
        CreateCornerBracket(openVisorArea.transform, new Vector2(360f, 195f), new Vector2(-1f, -1f), cCyanDim);
        CreateCornerBracket(openVisorArea.transform, new Vector2(-360f, -195f), new Vector2(1f, 1f), cCyanDim);
        CreateCornerBracket(openVisorArea.transform, new Vector2(360f, -195f), new Vector2(-1f, 1f), cCyanDim);

        GameObject bboxContainerObj = new GameObject("AI_BBox_Container");
        bboxContainerObj.transform.SetParent(openVisorArea.transform, false);
        RectTransform bboxRt = bboxContainerObj.AddComponent<RectTransform>();
        bboxRt.anchorMin = Vector2.zero;
        bboxRt.anchorMax = Vector2.one;
        bboxRt.offsetMin = Vector2.zero;
        bboxRt.offsetMax = Vector2.zero;
        ctrl.aiBboxContainer = bboxRt;

        // Gimbal & Live Controller Status Bar (Y = -155)
        GameObject gimbalBar = CreatePanel("Gimbal_And_Controller_Bar", centerHUD.transform, new Vector2(0f, -155f), new Vector2(820f, 42f), cPanelBg);
        CreateBorder(gimbalBar.transform, new Vector2(820f, 42f), cCyanDim, 1f);

        ctrl.controllerStatusText = CreateTMP("ControllerStatusTxt", gimbalBar.transform,
            "🎮 JOYSTICK: READY  |  L-STICK (+0.00, +0.00)",
            new Vector2(-165f, 0f), new Vector2(460f, 36f), 14, cGreen, TextAlignmentOptions.Left, true);

        ctrl.servoPanText = CreateTMP("ServoPanText", gimbalBar.transform, "SERVO: 37 deg",
            new Vector2(155f, 0f), new Vector2(160f, 36f), 14, cCyan, TextAlignmentOptions.Right, true);

        GameObject servoTrack = CreateBox("Servo_Track", gimbalBar.transform, new Vector2(320f, 0f), new Vector2(140f, 8f), new Color(0.1f, 0.25f, 0.35f, 0.9f));
        GameObject servoTick = CreateBox("Servo_Tick", servoTrack.transform, Vector2.zero, new Vector2(12f, 20f), cAmber);
        ctrl.servoTickIndicator = servoTick.GetComponent<RectTransform>();

        // Drive & Propulsion Deck (Matches Web Dashboard Drive & Propulsion Panel)
        GameObject driveDeck = CreatePanel("Drive_Telemetry_Deck", centerHUD.transform, new Vector2(0f, -266f), new Vector2(820f, 154f), cPanelBg);
        ctrl.themePanelBackgrounds.Add(driveDeck.GetComponent<Image>());
        CreateBorder(driveDeck.transform, new Vector2(820f, 154f), cCyanDim, 1.5f);

        // Live 2D Joystick Deflection Scope
        GameObject joyScope = CreatePanel("Joystick_Scope", driveDeck.transform, new Vector2(-335f, 10f), new Vector2(76f, 76f), cCardBg);
        CreateBorder(joyScope.transform, new Vector2(76f, 76f), cCyan, 1.2f);
        CreateBox("Joy_Cross_H", joyScope.transform, Vector2.zero, new Vector2(68f, 1f), cCyanDim);
        CreateBox("Joy_Cross_V", joyScope.transform, Vector2.zero, new Vector2(1f, 68f), cCyanDim);
        GameObject joyDot = CreateBox("Joystick_Dot", joyScope.transform, Vector2.zero, new Vector2(14f, 14f), cGreen);
        ctrl.joystickDotIndicator = joyDot.GetComponent<RectTransform>();
        CreateTMP("Joy_Lbl", driveDeck.transform, "L-STICK", new Vector2(-335f, -44f), new Vector2(90f, 22f), 12, cCyan, TextAlignmentOptions.Center, true);

        // Left Motor Track Column
        ctrl.motorLeftText = CreateTMP("Motor_L_Txt", driveDeck.transform, "L-MOTOR: 0 PWM",
            new Vector2(-185f, 36f), new Vector2(180f, 30f), 17, cGreen, TextAlignmentOptions.Center, true);
        ctrl.motorLeftBarFill = CreateProgressBar("Motor_L_Bar", driveDeck.transform, new Vector2(-185f, 2f), new Vector2(165f, 15f), cGreen, 0f);
        ctrl.ultrasonicDistanceText = CreateTMP("Ultrasonic_Txt", driveDeck.transform, "DISTANCE: -- cm",
            new Vector2(-185f, -38f), new Vector2(185f, 28f), 15, cCyan, TextAlignmentOptions.Center, true);

        // Center Speed Limit & FSM Mode Column
        ctrl.speedPercentText = CreateTMP("Speed_Txt", driveDeck.transform, "SPEED: 75% (ECO 65%)",
            new Vector2(35f, 38f), new Vector2(260f, 30f), 16, Color.white, TextAlignmentOptions.Center, true);
        ctrl.speedBarFill = CreateProgressBar("Speed_Bar", driveDeck.transform, new Vector2(35f, 4f), new Vector2(230f, 15f), cCyan, 0.75f);
        ctrl.fsmActionText = CreateTMP("FSM_Action_Txt", driveDeck.transform, "DRIVE: L 0 / R 0  •  ACTION: IDLE",
            new Vector2(35f, -38f), new Vector2(260f, 28f), 14, cAmber, TextAlignmentOptions.Center, true);

        // Right Motor Track Column
        ctrl.motorRightText = CreateTMP("Motor_R_Txt", driveDeck.transform, "R-MOTOR: 0 PWM",
            new Vector2(280f, 36f), new Vector2(180f, 30f), 17, cGreen, TextAlignmentOptions.Center, true);
        ctrl.motorRightBarFill = CreateProgressBar("Motor_R_Bar", driveDeck.transform, new Vector2(280f, 2f), new Vector2(165f, 15f), cGreen, 0f);
        CreateTMP("Drive_Hint_Txt", driveDeck.transform, "[Y]: รอบนอก/กล้อง  |  [B]: E-STOP",
            new Vector2(280f, -38f), new Vector2(220f, 28f), 13, cTextLight, TextAlignmentOptions.Center, false);

        ctrl.obstacleAlertPanel = CreatePanel("Obstacle_Alert_Modal", centerHUD.transform, new Vector2(0f, 210f), new Vector2(680f, 84f), new Color(0.55f, 0.02f, 0.05f, 0.94f));
        CreateBorder(ctrl.obstacleAlertPanel.transform, new Vector2(680f, 84f), cRed, 3f);
        ctrl.obstacleDistanceText = CreateTMP("Alert_Label", ctrl.obstacleAlertPanel.transform,
            "⚠ PROXIMITY HAZARD — OBSTACLE AHEAD  |  AUTO-BRAKE",
            Vector2.zero, new Vector2(650f, 70f), 21, Color.white, TextAlignmentOptions.Center, true);
        ctrl.obstacleAlertPanel.SetActive(false);

        // =========================================================================
        // 4. RIGHT WING PANEL: IMU TWIN, SMART ASSIST & MISSION ASSESSMENT (420 x 790 at X = +690, Y = -8)
        // =========================================================================
        GameObject rightPanel = CreatePanel("RightPanel_Diagnostics", hudRoot.transform, new Vector2(690f, -8f), new Vector2(420f, 790f), cPanelBg);
        ctrl.rightPanelRect = rightPanel.GetComponent<RectTransform>();
        ctrl.rightPanelRect.localEulerAngles = new Vector3(0f, cfg.Profile.wingPanelAngle, 0f);
        ctrl.themePanelBackgrounds.Add(rightPanel.GetComponent<Image>());
        CreateBorder(rightPanel.transform, new Vector2(420f, 790f), cCyanDim, 1.5f);

        var hdrRight = CreateTMP("Header_Right", rightPanel.transform, "IMU TWIN, SMART ASSIST & ASSESSMENT ▶",
            new Vector2(0f, 368f), new Vector2(390f, 30f), 16, cCyan, TextAlignmentOptions.Left, true);
        ctrl.themeAccentGraphics.Add(hdrRight);
        CreateBox("Header_Right_Line", rightPanel.transform, new Vector2(0f, 350f), new Vector2(390f, 2f), cCyanDim);

        // --- CARD 1: IMU & RADAR TWIN + 8-METRIC GRID (388 x 250 at Y = +210) ---
        GameObject gyroCard = CreatePanel("Card_Attitude", rightPanel.transform, new Vector2(0f, 210f), new Vector2(388f, 250f), cCardBg);
        CreateBorder(gyroCard.transform, new Vector2(388f, 250f), cCyanDim, 1f);
        CreateTMP("Gyro_Title", gyroCard.transform, "IMU & RADAR TWIN (8-METRIC TELEMETRY)",
            new Vector2(0f, 106f), new Vector2(364f, 22f), 14, cAmber, TextAlignmentOptions.Left, true);

        GameObject horizonMaskObj = CreatePanel("Horizon_Mask", gyroCard.transform, new Vector2(-95f, 22f), new Vector2(132f, 132f), new Color(0.02f, 0.14f, 0.24f, 1f));
        horizonMaskObj.AddComponent<RectMask2D>();
        CreateBorder(horizonMaskObj.transform, new Vector2(132f, 132f), cCyan, 1.5f);

        GameObject horizonBallObj = new GameObject("IMU_Horizon");
        horizonBallObj.transform.SetParent(horizonMaskObj.transform, false);
        ctrl.imuHorizonBall = horizonBallObj.AddComponent<RectTransform>();
        ctrl.imuHorizonBall.anchoredPosition = Vector2.zero;
        ctrl.imuHorizonBall.sizeDelta = new Vector2(200f, 200f);

        CreateBox("Sky_Half", ctrl.imuHorizonBall, new Vector2(0f, 50f), new Vector2(200f, 100f), new Color(0.04f, 0.28f, 0.48f, 0.85f));
        CreateBox("Ground_Half", ctrl.imuHorizonBall, new Vector2(0f, -50f), new Vector2(200f, 100f), new Color(0.28f, 0.18f, 0.08f, 0.85f));
        CreateBox("Horizon_Line", ctrl.imuHorizonBall, Vector2.zero, new Vector2(160f, 3f), cGreen);

        ctrl.pitchText = CreateTMP("Pitch_Val", gyroCard.transform, "PITCH: +0.0°",
            new Vector2(92f, 56f), new Vector2(180f, 26f), 17, Color.white, TextAlignmentOptions.Left, true);
        ctrl.rollText = CreateTMP("Roll_Val", gyroCard.transform, "ROLL:  +0.0°",
            new Vector2(92f, 22f), new Vector2(180f, 26f), 17, Color.white, TextAlignmentOptions.Left, true);
        ctrl.yawText = CreateTMP("Yaw_Val", gyroCard.transform, "YAW:   078.6°",
            new Vector2(92f, -12f), new Vector2(180f, 26f), 17, cCyan, TextAlignmentOptions.Left, true);

        // 8-Metric Grid at bottom of Card 1
        ctrl.envMetricsText = CreateTMP("Env_Metrics_Txt", gyroCard.transform,
            "<b>Pitch:</b> 0.0°     <b>Roll:</b> 0.0°     <b>Yaw/Hdg:</b> 78.6°     <b>Alt:</b> 0.0 m\n" +
            "<b>Temp:</b> 0.0 °C    <b>Humid:</b> 0 %    <b>Pressure:</b> 0 hPa    <b>Heat Idx:</b> 0.0 °C",
            new Vector2(0f, -82f), new Vector2(368f, 64f), 13, cTextLight, TextAlignmentOptions.Left, false);

        // --- CARD 2: SMART ASSIST PANEL (388 x 165 at Y = -10) ---
        GameObject assistCard = CreatePanel("Card_SmartAssist", rightPanel.transform, new Vector2(0f, -10f), new Vector2(388f, 165f), cCardBg);
        CreateBorder(assistCard.transform, new Vector2(388f, 165f), cCyanDim, 1f);
        CreateTMP("Assist_Title", assistCard.transform, "SMART ASSIST & DIAGNOSTICS",
            new Vector2(0f, 62f), new Vector2(364f, 22f), 14, cAmber, TextAlignmentOptions.Left, true);

        ctrl.smartAssistText = CreateTMP("Smart_Assist_Txt", assistCard.transform,
            "<b>Health Score:</b> <color=#FFD166>25%</color>   |   <b>Link:</b> stale/offline   |   <b>Obstacle:</b> no echo\n" +
            "<b>Sensor Bus:</b> ESP32 serial offline   |   <b>Roughness:</b> 0.00\n" +
            "<b>Suggestion:</b> <color=#00F0FF>System nominal — Ready for Teleop / Survey</color>",
            new Vector2(0f, -14f), new Vector2(368f, 110f), 14, cTextLight, TextAlignmentOptions.Left, false);

        // --- CARD 3: MISSION ASSESSMENT & INTELLIGENCE (388 x 255 at Y = -232) ---
        GameObject hwCard = CreatePanel("Card_Hardware", rightPanel.transform, new Vector2(0f, -232f), new Vector2(388f, 255f), cCardBg);
        CreateBorder(hwCard.transform, new Vector2(388f, 255f), cCyanDim, 1f);
        CreateTMP("Hw_Title", hwCard.transform, "MISSION ASSESSMENT & INTELLIGENCE",
            new Vector2(0f, 104f), new Vector2(364f, 22f), 14, cAmber, TextAlignmentOptions.Left, true);

        ctrl.hardwareHealthText = CreateTMP("Hardware_Health_Txt", hwCard.transform,
            "<b>SURVIVABILITY SCORE: <color=#00FF88>100%</color></b>\n" +
            "• <b>การยึดเกาะ/ล้อขับ:</b> <color=#00FF88>TRACTION NOMINAL (สภาพพื้นผิวปกติ)</color>\n" +
            "• <b>สภาพโพรงอากาศ:</b> STABLE ATMOSPHERE   |   <b>จุดน้ำค้าง:</b> DP --°C\n" +
            "• <b>ความลาดชัน:</b> <color=#00FF88>GRADE A (พื้นเรียบ ปลอดภัย)</color>\n" +
            "• <b>AI Field Assessment:</b> เชื่อมต่อโหนดหุ่นยนต์และสตรีมกล้องรอบตัวสำเร็จ",
            new Vector2(0f, 16f), new Vector2(368f, 135f), 13, cTextLight, TextAlignmentOptions.Left, false);

        ctrl.healthText = ctrl.hardwareHealthText;
        ctrl.sensorListText = ctrl.hardwareHealthText;

        CreateTMP("Batt_Label", hwCard.transform, "MAIN BATTERY:",
            new Vector2(-115f, -84f), new Vector2(140f, 26f), 14, cCyan, TextAlignmentOptions.Left, true);
        ctrl.batteryText = CreateTMP("Battery_Val", hwCard.transform, "12.4 V  [92%]",
            new Vector2(110f, -84f), new Vector2(150f, 26f), 15, cGreen, TextAlignmentOptions.Right, true);
        ctrl.batteryBarFill = CreateProgressBar("Battery_Bar", hwCard.transform, new Vector2(0f, -108f), new Vector2(364f, 12f), cGreen, 0.92f);

        // =========================================================================
        // 5. BOTTOM COMMAND DECK: AUTONOMOUS MISSIONS & SPECIAL COMMANDS (1740 x 98 at Y = -465)
        // =========================================================================
        GameObject bottomPanel = CreatePanel("BottomPanel_Controls", hudRoot.transform, new Vector2(0f, -465f), new Vector2(1740f, 98f), cPanelBg);
        ctrl.themePanelBackgrounds.Add(bottomPanel.GetComponent<Image>());
        CreateBorder(bottomPanel.transform, new Vector2(1740f, 98f), cCyanDim, 1.5f);

        GameObject deadmanPill = CreatePanel("Deadman_Pill", bottomPanel.transform, new Vector2(-685f, 0f), new Vector2(320f, 64f), cCardBg);
        CreateBorder(deadmanPill.transform, new Vector2(320f, 64f), cCyanDim, 1f);
        GameObject dmDot = CreateBox("Deadman_Dot", deadmanPill.transform, new Vector2(-138f, 0f), new Vector2(18f, 18f), cGreen);
        ctrl.deadmanIndicatorDot = dmDot.GetComponent<Image>();
        ctrl.deadmanStatusText = CreateTMP("Deadman_Txt", deadmanPill.transform,
            "JOYSTICK READY (100% CONTROL)",
            new Vector2(10f, 0f), new Vector2(275f, 52f), 14, Color.white, TextAlignmentOptions.Left, true);

        string[] btnCodes = { "BTN_SURVEY", "BTN_RTH", "BTN_RADAR_SWEEP", "BTN_SCAN", "BTN_BEACON", "BTN_CENTER", "BTN_EXT_CAM", "BTN_VIEW", "BTN_CONFIG", "BTN_ESTOP" };
        string[] btnTitles = { "AUTO-SURVEY", "RETURN HOME", "RADAR SWEEP", "SCAN [A]", "BEACON [X]", "CENTER [R3]", "🌐 รอบนอก/กล้อง [Y]", "360°/220° [RB]", "⚙ CONFIG", "🚨 E-STOP [B]" };
        float startX = -435f;
        float spacingX = 138f;

        for (int i = 0; i < btnCodes.Length; i++)
        {
            bool isEstop = (i == btnCodes.Length - 1);
            bool isCfg = (i == btnCodes.Length - 2);
            bool isExtToggle = (i == 6);
            Color btnColor = isEstop
                ? new Color(0.75f, 0.08f, 0.14f, 0.95f)
                : (isExtToggle ? new Color(0.0f, 0.48f, 0.38f, 0.95f)
                : (isCfg ? new Color(0.0f, 0.42f, 0.62f, 0.95f) : new Color(0.06f, 0.18f, 0.30f, 0.92f)));

            CreateButton(btnCodes[i], bottomPanel.transform, btnTitles[i],
                new Vector2(startX + i * spacingX, 0f), new Vector2(130f, 62f), btnColor, 13);
        }

        // =========================================================================
        // 6. INTERACTIVE IN-VR FRAMEWORK SETTINGS MODAL (940 x 640, Pop-up Drawer)
        // =========================================================================
        GameObject modal = CreatePanel("Framework_Settings_Modal", hudRoot.transform, new Vector2(0f, 20f), new Vector2(940f, 640f), new Color(0.01f, 0.04f, 0.09f, 0.97f));
        RectTransform modalRt = modal.GetComponent<RectTransform>();
        modalRt.localPosition = new Vector3(0f, 20f, -25f);
        CreateBorder(modal.transform, new Vector2(940f, 640f), cCyan, 2.5f);

        CreateTMP("Modal_Header", modal.transform, "⚙ V-RESQ FRAMEWORK CONTROL & JOYSTICK CENTER",
            new Vector2(-40f, 280f), new Vector2(780f, 40f), 22, cCyan, TextAlignmentOptions.Left, true);
        CreateButton("CFG_CLOSE", modal.transform, "✖ CLOSE [START]",
            new Vector2(385f, 280f), new Vector2(140f, 40f), new Color(0.6f, 0.12f, 0.18f, 0.95f), 14);
        CreateBox("Modal_Divider", modal.transform, new Vector2(0f, 252f), new Vector2(890f, 2f), cCyanDim);

        GameObject summaryCard = CreatePanel("Summary_Card", modal.transform, new Vector2(0f, 165f), new Vector2(890f, 140f), cCardBg);
        CreateBorder(summaryCard.transform, new Vector2(890f, 140f), cCyanDim, 1f);
        ctrl.cfgSummaryText = CreateTMP("Cfg_Summary_Txt", summaryCard.transform,
            "<b>ACTIVE VIEW:</b> SURROUND CAMERA 360° (ภาพจากกล้องรอบตัว)\n" +
            "<b>JOYSTICK / CONTROLLER:</b> READY (100% FULL CONTROL)\n" +
            "<b>SURROUND RADIUS:</b> 2.8 m   |   <b>HUD DISTANCE:</b> 1.45 m   |   <b>WING ANGLE:</b> 24°\n" +
            "<b>MAX SPEED LIMIT:</b> 75%   |   <b>DRIVE MODE:</b> 100% IMMEDIATE JOYSTICK DRIVE",
            Vector2.zero, new Vector2(850f, 124f), 17, Color.white, TextAlignmentOptions.Left, false);

        Color cBtnTune = new Color(0.05f, 0.22f, 0.36f, 0.95f);
        Color cBtnAct = new Color(0.0f, 0.48f, 0.38f, 0.95f);

        CreateButton("CFG_EXT_TOGGLE", modal.transform, "🌐 สลับ มองเห็นรอบนอก ⇄ ภาพรอบตัว [Y]", new Vector2(-295f, 45f), new Vector2(285f, 52f), cBtnAct, 14);
        CreateButton("CFG_VIEW_CYCLE", modal.transform, "🎥 เปลี่ยนมุมกล้องรอบตัว (360°/220°/180°)", new Vector2(0f, 45f), new Vector2(285f, 52f), cBtnTune, 14);
        CreateButton("CFG_THEME_CYCLE", modal.transform, "🎨 เปลี่ยนโทนสี HUD (THEME)", new Vector2(295f, 45f), new Vector2(285f, 52f), cBtnTune, 14);

        CreateButton("CFG_RADIUS_MINUS", modal.transform, "DOME RADIUS -0.2m", new Vector2(-368f, -25f), new Vector2(140f, 50f), cBtnTune, 14);
        CreateButton("CFG_RADIUS_PLUS", modal.transform, "DOME RADIUS +0.2m", new Vector2(-222f, -25f), new Vector2(140f, 50f), cBtnTune, 14);

        CreateButton("CFG_HUDDIST_MINUS", modal.transform, "HUD DIST -0.1m", new Vector2(-72f, -25f), new Vector2(140f, 50f), cBtnTune, 14);
        CreateButton("CFG_HUDDIST_PLUS", modal.transform, "HUD DIST +0.1m", new Vector2(72f, -25f), new Vector2(140f, 50f), cBtnTune, 14);

        CreateButton("CFG_WING_MINUS", modal.transform, "WING ANGLE -4°", new Vector2(222f, -25f), new Vector2(140f, 50f), cBtnTune, 14);
        CreateButton("CFG_WING_PLUS", modal.transform, "WING ANGLE +4°", new Vector2(368f, -25f), new Vector2(140f, 50f), cBtnTune, 14);

        CreateButton("CFG_SPEED_MINUS", modal.transform, "MAX SPEED -10%", new Vector2(-368f, -95f), new Vector2(140f, 50f), cBtnTune, 14);
        CreateButton("CFG_SPEED_PLUS", modal.transform, "MAX SPEED +10%", new Vector2(-222f, -95f), new Vector2(140f, 50f), cBtnTune, 14);

        CreateButton("CFG_DEADMAN_TOGGLE", modal.transform, "TOGGLE DEADMAN REQUIREMENT", new Vector2(0f, -95f), new Vector2(285f, 50f), cBtnTune, 14);
        CreateButton("CFG_FAILOVER_TOGGLE", modal.transform, "TOGGLE MULTI-NODE FAILOVER", new Vector2(295f, -95f), new Vector2(285f, 50f), cBtnTune, 14);

        CreateButton("CFG_RECENTER", modal.transform, "🎯 RECENTER VIEW & HUD [SELECT/R]", new Vector2(-295f, -175f), new Vector2(285f, 54f), cBtnAct, 15);
        CreateButton("CFG_SAVE", modal.transform, "💾 SAVE PROFILE TO DISK", new Vector2(0f, -175f), new Vector2(285f, 54f), cBtnAct, 15);
        CreateButton("CFG_RESET", modal.transform, "↺ RESET DEFAULT PROFILE", new Vector2(295f, -175f), new Vector2(285f, 54f), new Color(0.45f, 0.22f, 0.1f, 0.95f), 15);

        CreateTMP("Modal_Footer", modal.transform,
            "JOYSTICK CONTROLS: [L-Stick] ขับหุ่นยนต์  |  [R-Stick] หันมองรอบตัว 360° & หันกล้อง  |  [Y] สลับมองรอบนอก/กล้องรอบตัว  |  [LB] สลับหุ่นยนต์",
            new Vector2(0f, -265f), new Vector2(900f, 30f), 13, cCyan, TextAlignmentOptions.Center, false);

        ctrl.settingsModalRoot = modal;
        modal.SetActive(false);

        ctrl.WireAllInteractiveButtons();
        ctrl.ApplyFrameworkVisuals();
        surround.EnsureSurroundScreenReady(forceRebuildMesh: true);

        EditorUtility.SetDirty(hudRoot);
        var activeScene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(activeScene);
        EditorSceneManager.SaveScene(activeScene);

        Selection.activeGameObject = hudRoot;
        Debug.Log($"<color=cyan>[V-ResQ Framework]</color> Rebuilt Open-Visor Surround HUD in {activeScene.path}!");
    }

    public static void RebuildAndVerifyRuntime()
    {
        // 1. Rebuild BasicScene.unity if it exists
        if (System.IO.File.Exists("Assets/Scenes/BasicScene.unity"))
        {
            EditorSceneManager.OpenScene("Assets/Scenes/BasicScene.unity");
            RebuildSceneCleanly();
        }

        // 2. Rebuild SampleScene.unity and keep it as the active scene
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        RebuildSceneCleanly();

        // 3. Live Runtime Verification Test inside Unity's Mono Runtime
        var client = Object.FindFirstObjectByType<VResqClient>();
        var cam = Object.FindFirstObjectByType<VResqCameraReceiver>();
        var surround = Object.FindFirstObjectByType<VResqSurroundDisplay>();

        if (client != null && cam != null && surround != null)
        {
            client.Connect();
            cam.StartStream();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 2800)
            {
                System.Threading.Thread.Sleep(40);
                client.PumpMainThreadQueue();
                cam.PumpFrameUpdate();
            }

            string nodeName = client.LatestState != null && client.LatestState.identity != null
                ? client.LatestState.identity.node_name
                : "none";
            int texW = cam.VideoTexture != null ? cam.VideoTexture.width : 0;
            int texH = cam.VideoTexture != null ? cam.VideoTexture.height : 0;

            Debug.Log($"[VRESQ_VERIFY] Connected={client.IsConnected}, ActiveRobot={client.robotIp}, NodeName={nodeName}, CamFramesDecoded={cam.FramesReceived}, HasLiveFrame={cam.HasReceivedLiveFrame}, TexSize={texW}x{texH}");

            cam.StopStream();
            client.Disconnect();
        }
    }

    private static void SetupExternal3DWorldEnvironment()
    {
        GameObject oldEnv = GameObject.Find("External_3D_World_Environment");
        if (oldEnv != null) DestroyImmediate(oldEnv);

        GameObject envRoot = new GameObject("External_3D_World_Environment");
        Shader litShader = Shader.Find("Universal Render Pipeline/Lit");
        if (litShader == null) litShader = Shader.Find("Standard");

        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "Tactical_Rescue_Floor";
        floor.transform.SetParent(envRoot.transform, false);
        floor.transform.position = new Vector3(0f, -0.05f, 0f);
        floor.transform.localScale = new Vector3(4f, 1f, 4f);
        Material floorMat = new Material(litShader);
        floorMat.color = new Color(0.08f, 0.14f, 0.20f, 1f);
        floor.GetComponent<Renderer>().sharedMaterial = floorMat;

        Vector3[] positions = {
            new Vector3(0f, 1.0f, 6.0f),
            new Vector3(6.0f, 1.0f, 0f),
            new Vector3(0f, 1.0f, -6.0f),
            new Vector3(-6.0f, 1.0f, 0f)
        };
        Color[] colors = {
            new Color(0f, 0.9f, 1f, 1f),
            new Color(0f, 1f, 0.5f, 1f),
            new Color(1f, 0.7f, 0.1f, 1f),
            new Color(0.7f, 0.4f, 1f, 1f)
        };
        string[] names = { "Beacon_North_Front", "Beacon_East_Right", "Beacon_South_Rear", "Beacon_West_Left" };

        for (int i = 0; i < 4; i++)
        {
            GameObject pillar = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pillar.name = names[i];
            pillar.transform.SetParent(envRoot.transform, false);
            pillar.transform.position = positions[i];
            pillar.transform.localScale = new Vector3(0.25f, 1.0f, 0.25f);
            Material m = new Material(litShader);
            m.color = colors[i];
            pillar.GetComponent<Renderer>().sharedMaterial = m;
        }
    }

    private static void CreateCornerBracket(Transform parent, Vector2 cornerPos, Vector2 dir, Color color)
    {
        float len = 28f;
        float thick = 2.5f;
        CreateBox("Bracket_H", parent, cornerPos + new Vector2(dir.x * len * 0.5f, 0f), new Vector2(len, thick), color);
        CreateBox("Bracket_V", parent, cornerPos + new Vector2(0f, dir.y * len * 0.5f), new Vector2(thick, len), color);
    }

    private void OnGUI()
    {
        var cfg = Object.FindFirstObjectByType<VResqFrameworkConfig>();
        if (cfg == null)
        {
            EditorGUILayout.HelpBox("No VResqFrameworkConfig found in the current scene. Click below to build the Open-Visor Surround HUD.", MessageType.Info);
            if (GUILayout.Button("Build Open-Visor Surround HUD Now", GUILayout.Height(42)))
            {
                RebuildSceneCleanly();
            }
            return;
        }

        _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);
        var p = cfg.Profile;

        GUIStyle headerStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13 };

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("1. Surround Camera vs. External World (ไม่มีจอกลางบังสายตา)", headerStyle);
        EditorGUILayout.BeginVertical("box");
        p.viewMode = (SurroundViewMode)EditorGUILayout.EnumPopup("Active View Mode", p.viewMode);
        p.surroundRadius = EditorGUILayout.Slider("Surround Dome Radius (m)", p.surroundRadius, 1.6f, 5.0f);
        p.surroundHeight = EditorGUILayout.Slider("Surround Dome Height (m)", p.surroundHeight, 1.4f, 4.5f);
        p.surroundArcAngle = EditorGUILayout.Slider("Surround Arc Angle (°)", p.surroundArcAngle, 120f, 360f);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("🌐 มองเห็นรอบนอก (External 3D World)", GUILayout.Height(28)))
        {
            var sur = Object.FindFirstObjectByType<VResqSurroundDisplay>();
            if (sur != null) sur.SetViewMode(SurroundViewMode.ExternalWorld);
        }
        if (GUILayout.Button("🎥 ภาพกล้องรอบตัว 360° (Surround 360°)", GUILayout.Height(28)))
        {
            var sur = Object.FindFirstObjectByType<VResqSurroundDisplay>();
            if (sur != null) sur.SetViewMode(SurroundViewMode.Surround360);
        }
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("2. Joystick / Gamepad & Teleoperation (100% Controller)", headerStyle);
        EditorGUILayout.BeginVertical("box");
        p.enableJoystickControl = EditorGUILayout.Toggle("Enable Full Joystick Control", p.enableJoystickControl);
        p.speedScale = EditorGUILayout.Slider("Default Speed Limit", p.speedScale, 0.2f, 1.0f);
        p.stickDeadzone = EditorGUILayout.Slider("Joystick Deadzone", p.stickDeadzone, 0.02f, 0.30f);
        p.lookYawSpeedDegPerSec = EditorGUILayout.Slider("360° Look Speed (°/s)", p.lookYawSpeedDegPerSec, 30f, 180f);
        p.requireDeadmanSwitch = EditorGUILayout.Toggle("Require Deadman Hold", p.requireDeadmanSwitch);
        p.collisionAlertCm = EditorGUILayout.Slider("Proximity Alert Distance (cm)", p.collisionAlertCm, 10f, 60f);
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("3. Swarm Fleet Endpoints (Node 0, Node 1, Node 2)", headerStyle);
        EditorGUILayout.BeginVertical("box");
        p.defaultNodeIndex = EditorGUILayout.IntSlider("Default Active Node", p.defaultNodeIndex, 0, 2);
        p.autoFallbackToOnlineNode = EditorGUILayout.Toggle("Seamless Auto-Failover", p.autoFallbackToOnlineNode);

        if (p.fleetNodes != null)
        {
            for (int i = 0; i < p.fleetNodes.Length; i++)
            {
                EditorGUILayout.BeginHorizontal();
                p.fleetNodes[i].displayName = EditorGUILayout.TextField(p.fleetNodes[i].displayName, GUILayout.Width(90));
                p.fleetNodes[i].roleLabel = EditorGUILayout.TextField(p.fleetNodes[i].roleLabel, GUILayout.Width(80));
                p.fleetNodes[i].ipAddress = EditorGUILayout.TextField(p.fleetNodes[i].ipAddress);
                p.fleetNodes[i].port = EditorGUILayout.IntField(p.fleetNodes[i].port, GUILayout.Width(55));
                if (Application.isPlaying && GUILayout.Button("Switch", GUILayout.Width(60)))
                {
                    VResqClient.Instance?.SwitchToNodeIndex(i);
                }
                EditorGUILayout.EndHorizontal();
            }
        }
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("4. Cockpit UX/UI Ergonomics & Theme", headerStyle);
        EditorGUILayout.BeginVertical("box");
        p.theme = (CockpitTheme)EditorGUILayout.EnumPopup("Cockpit Color Theme", p.theme);
        p.hudDistance = EditorGUILayout.Slider("HUD Distance from Eyes (m)", p.hudDistance, 0.85f, 2.5f);
        p.wingPanelAngle = EditorGUILayout.Slider("Left/Right Wing Angle (°)", p.wingPanelAngle, 0f, 42f);
        p.panelOpacity = EditorGUILayout.Slider("Glass Panel Opacity", p.panelOpacity, 0.4f, 0.98f);
        p.smoothFollowHead = EditorGUILayout.Toggle("Smooth Follow Head Gaze", p.smoothFollowHead);
        p.followDeadzoneAngle = EditorGUILayout.Slider("Free-Look Deadzone (°)", p.followDeadzoneAngle, 10f, 180f);
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(10);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Apply Live to Scene", GUILayout.Height(34)))
        {
            cfg.NotifyConfigChanged();
            var hud = Object.FindFirstObjectByType<RescueRobotVRHUD>();
            if (hud != null) hud.ApplyFrameworkVisuals();
            var follow = Object.FindFirstObjectByType<TacticalHUDFollow>();
            if (follow != null) follow.SyncFromConfig();
            var sur = Object.FindFirstObjectByType<VResqSurroundDisplay>();
            if (sur != null) sur.SyncFromFrameworkConfig();
            EditorUtility.SetDirty(cfg);
        }
        if (GUILayout.Button("Save JSON Profile", GUILayout.Height(34)))
        {
            cfg.SaveProfile();
        }
        EditorGUILayout.EndHorizontal();

        if (GUILayout.Button("🔄 Clean Rebuild Open-Visor Surround HUD & Save Scene", GUILayout.Height(38)))
        {
            RebuildSceneCleanly();
        }

        EditorGUILayout.EndScrollView();
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

    private static GameObject CreateBox(string name, Transform parent, Vector2 pos, Vector2 size, Color color)
    {
        GameObject box = CreatePanel(name, parent, pos, size, color);
        box.GetComponent<Image>().raycastTarget = false;
        return box;
    }

    private static void CreateBorder(Transform parent, Vector2 size, Color borderColor, float thickness)
    {
        float halfW = size.x * 0.5f;
        float halfH = size.y * 0.5f;
        CreateBox("Border_Top", parent, new Vector2(0f, halfH), new Vector2(size.x, thickness), borderColor);
        CreateBox("Border_Bottom", parent, new Vector2(0f, -halfH), new Vector2(size.x, thickness), borderColor);
        CreateBox("Border_Left", parent, new Vector2(-halfW, 0f), new Vector2(thickness, size.y), borderColor);
        CreateBox("Border_Right", parent, new Vector2(halfW, 0f), new Vector2(thickness, size.y), borderColor);
    }

    private static Image CreateProgressBar(string name, Transform parent, Vector2 pos, Vector2 size, Color fillColor, float initialFill)
    {
        GameObject bg = CreatePanel(name + "_Bg", parent, pos, size, new Color(0.06f, 0.14f, 0.22f, 0.95f));
        bg.GetComponent<Image>().raycastTarget = false;

        GameObject fillObj = new GameObject(name + "_Fill");
        fillObj.transform.SetParent(bg.transform, false);
        RectTransform rt = fillObj.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(2f, 2f);
        rt.offsetMax = new Vector2(-2f, -2f);

        Image fillImg = fillObj.AddComponent<Image>();
        fillImg.color = fillColor;
        fillImg.type = Image.Type.Filled;
        fillImg.fillMethod = Image.FillMethod.Horizontal;
        fillImg.fillAmount = Mathf.Clamp01(initialFill);
        fillImg.raycastTarget = false;
        return fillImg;
    }

    private static TextMeshProUGUI CreateTMP(string name, Transform parent, string content, Vector2 pos, Vector2 size, int fontSize, Color color, TextAlignmentOptions align, bool bold)
    {
        GameObject txtObj = new GameObject(name);
        txtObj.transform.SetParent(parent, false);
        RectTransform rect = txtObj.AddComponent<RectTransform>();
        rect.anchoredPosition = pos;
        rect.sizeDelta = size;

        TextMeshProUGUI tmp = txtObj.AddComponent<TextMeshProUGUI>();
        tmp.text = content;
        tmp.fontSize = fontSize;
        tmp.color = color;
        tmp.alignment = align;
        tmp.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.raycastTarget = false;
        return tmp;
    }

    private static GameObject CreateButton(string name, Transform parent, string label, Vector2 pos, Vector2 size, Color bgColor, int fontSize)
    {
        GameObject btnObj = CreatePanel(name, parent, pos, size, bgColor);
        Button btn = btnObj.AddComponent<Button>();
        ColorBlock cb = btn.colors;
        cb.normalColor = Color.white;
        cb.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
        cb.pressedColor = new Color(0.75f, 0.95f, 1f, 1f);
        btn.colors = cb;

        CreateBorder(btnObj.transform, size, new Color(0f, 0.88f, 1f, 0.65f), 1.2f);
        CreateTMP("Label", btnObj.transform, label, Vector2.zero, size, fontSize, Color.white, TextAlignmentOptions.Center, true);
        return btnObj;
    }
}
