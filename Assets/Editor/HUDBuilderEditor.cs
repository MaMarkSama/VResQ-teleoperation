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

    [MenuItem("V-ResQ/Rebuild Tactical VR Cockpit HUD (Clean UX/UI)", false, 2)]
    public static void BuildFullTacticalHUD()
    {
        RebuildSceneCleanly();
    }

    /// <summary>
    /// Can be called from MenuItem or Unity Batchmode (-executeMethod HUDBuilderEditor.RebuildSceneCleanly)
    /// Rebuilds the entire Tactical_VR_HUD_Root cleanly with zero overlapping elements and saves the scene.
    /// </summary>
    public static void RebuildSceneCleanly()
    {
        if (string.IsNullOrEmpty(EditorSceneManager.GetActiveScene().path))
        {
            EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        }

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

        // 2. Remove all existing Tactical_VR_HUD_Root instances to eliminate legacy overlap
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

        // 3. Create Master World-Space Canvas Root
        GameObject hudRoot = new GameObject("Tactical_VR_HUD_Root");
        Canvas canvas = hudRoot.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;

        CanvasScaler scaler = hudRoot.AddComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 10f;
        hudRoot.AddComponent<GraphicRaycaster>();

        // Add tracked device graphic raycaster if XR UI package is available
        var xrRaycasterType = System.Type.GetType("UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster, Unity.XR.Interaction.Toolkit");
        if (xrRaycasterType != null)
        {
            hudRoot.AddComponent(xrRaycasterType);
        }

        RectTransform rootRect = hudRoot.GetComponent<RectTransform>();
        rootRect.sizeDelta = new Vector2(1920f, 1080f);
        rootRect.localScale = new Vector3(0.00085f, 0.00085f, 0.00085f);

        // 4. Attach Core Framework Components
        VResqFrameworkConfig cfg = hudRoot.AddComponent<VResqFrameworkConfig>();
        VResqClient client = hudRoot.AddComponent<VResqClient>();
        client.robotIp = "10.66.68.52";
        client.port = 8000;

        VResqCameraReceiver camReceiver = hudRoot.AddComponent<VResqCameraReceiver>();
        VResqSurroundDisplay surround = hudRoot.AddComponent<VResqSurroundDisplay>();
        VResqTeleopController teleop = hudRoot.AddComponent<VResqTeleopController>();
        TacticalHUDFollow follow = hudRoot.AddComponent<TacticalHUDFollow>();
        RescueRobotVRHUD ctrl = hudRoot.AddComponent<RescueRobotVRHUD>();

        if (mainCam != null)
        {
            canvas.worldCamera = mainCam;
            follow.targetCamera = mainCam.transform;
            surround.targetCamera = mainCam.transform;
            follow.followDistance = cfg.Profile.hudDistance;
            follow.heightOffset = cfg.Profile.hudHeightOffset;
            follow.ResetHUDOrientation();
        }
        else
        {
            hudRoot.transform.position = new Vector3(0f, 1.2f, 2.25f);
        }

        // Palette
        Color cPanelBg = new Color(0.02f, 0.06f, 0.12f, 0.88f);
        Color cCardBg = new Color(0.04f, 0.11f, 0.20f, 0.90f);
        Color cCyan = new Color(0.0f, 0.92f, 1.0f, 1.0f);
        Color cCyanDim = new Color(0.0f, 0.70f, 0.88f, 0.55f);
        Color cGreen = new Color(0.0f, 1.0f, 0.55f, 1.0f);
        Color cAmber = new Color(1.0f, 0.78f, 0.18f, 1.0f);
        Color cRed = new Color(1.0f, 0.22f, 0.28f, 1.0f);
        Color cTextLight = new Color(0.88f, 0.96f, 1.0f, 1.0f);

        // =========================================================================
        // 1. TOP COMMAND BAR (1740 x 56 at Y = +495)
        // =========================================================================
        GameObject topBar = CreatePanel("TopBar", hudRoot.transform, new Vector2(0f, 495f), new Vector2(1740f, 56f), cPanelBg);
        ctrl.themePanelBackgrounds.Add(topBar.GetComponent<Image>());
        CreateBorder(topBar.transform, new Vector2(1740f, 56f), cCyanDim, 1.5f);

        ctrl.robotIdText = CreateTMP("RobotID", topBar.transform, "● VRESQ-NODE-ZERO  [LEADER]  •  10.66.68.52",
            new Vector2(-600f, 0f), new Vector2(480f, 44f), 20, cCyan, TextAlignmentOptions.Left, true);
        ctrl.themeAccentGraphics.Add(ctrl.robotIdText);

        ctrl.latencyText = CreateTMP("Latency", topBar.transform, "WS: ONLINE  |  VIDEO: 25 FPS",
            new Vector2(-160f, 0f), new Vector2(340f, 44f), 18, cGreen, TextAlignmentOptions.Center, true);

        ctrl.viewModeBadgeText = CreateTMP("ViewModeBadge", topBar.transform, "VIEW: 180° DOME [V]",
            new Vector2(175f, 0f), new Vector2(280f, 44f), 17, cAmber, TextAlignmentOptions.Center, true);

        ctrl.aiStatusBadgeText = CreateTMP("AIStatusBadge", topBar.transform, "AI VISION: 0 TARGETS (8.5 FPS)",
            new Vector2(475f, 0f), new Vector2(300f, 44f), 16, cTextLight, TextAlignmentOptions.Center, true);

        ctrl.clockText = CreateTMP("Clock", topBar.transform, "12:00:00",
            new Vector2(695f, 0f), new Vector2(130f, 44f), 20, Color.white, TextAlignmentOptions.Right, true);

        CreateButton("BTN_CONFIG_TOP", topBar.transform, "⚙ CONFIG",
            new Vector2(810f, 0f), new Vector2(100f, 38f), new Color(0.0f, 0.45f, 0.65f, 0.95f), 15);

        // =========================================================================
        // 2. LEFT WING PANEL: TACTICAL RADAR & SWARM NODE SWITCHER (420 x 790 at X = -690, Y = +20)
        // =========================================================================
        GameObject leftPanel = CreatePanel("LeftPanel_Map", hudRoot.transform, new Vector2(-690f, 20f), new Vector2(420f, 790f), cPanelBg);
        ctrl.leftPanelRect = leftPanel.GetComponent<RectTransform>();
        ctrl.leftPanelRect.localEulerAngles = new Vector3(0f, -cfg.Profile.wingPanelAngle, 0f);
        ctrl.themePanelBackgrounds.Add(leftPanel.GetComponent<Image>());
        CreateBorder(leftPanel.transform, new Vector2(420f, 790f), cCyanDim, 1.5f);

        var hdrLeft = CreateTMP("Header_Left", leftPanel.transform, "◀ TACTICAL RADAR & SWARM FLEET",
            new Vector2(0f, 366f), new Vector2(390f, 32f), 18, cCyan, TextAlignmentOptions.Left, true);
        ctrl.themeAccentGraphics.Add(hdrLeft);
        CreateBox("Header_Left_Line", leftPanel.transform, new Vector2(0f, 346f), new Vector2(390f, 2f), cCyanDim);

        // 2D Radar Scope Card (380 x 330 at Y = +168)
        GameObject radarCard = CreatePanel("Radar_Card", leftPanel.transform, new Vector2(0f, 168f), new Vector2(388f, 332f), cCardBg);
        GameObject radarScreenObj = new GameObject("Radar_Screen");
        radarScreenObj.transform.SetParent(radarCard.transform, false);
        RectTransform radarRt = radarScreenObj.AddComponent<RectTransform>();
        radarRt.anchoredPosition = new Vector2(0f, 24f);
        radarRt.sizeDelta = new Vector2(260f, 260f);
        RawImage radarRawImg = radarScreenObj.AddComponent<RawImage>();
        radarRawImg.color = Color.white;
        CreateBorder(radarScreenObj.transform, new Vector2(264f, 264f), cCyan, 1.5f);

        VResqRadarMap radarComp = leftPanel.AddComponent<VResqRadarMap>();
        radarComp.radarRawImage = radarRawImg;
        radarComp.mapCoordsText = CreateTMP("MapCoords", radarCard.transform,
            "X: +0.00m   Y: +0.00m\nHDG: 000°  |  CAM: 090°  |  VRESQ-NODE-ZERO",
            new Vector2(0f, -134f), new Vector2(370f, 44f), 14, cTextLight, TextAlignmentOptions.Center, false);
        ctrl.radarMap = radarComp;

        // Swarm Fleet Node Switcher Header
        var hdrSwarm = CreateTMP("Header_Swarm", leftPanel.transform, "SWARM NODE SELECTOR (CLICK OR KEY 1-3)",
            new Vector2(0f, -20f), new Vector2(388f, 26f), 15, cAmber, TextAlignmentOptions.Left, true);

        // 3 Interactive Node Cards (Node 0, Node 1, Node 2)
        ctrl.nodeCardButtons = new Button[3];
        ctrl.nodeCardBgs = new Image[3];
        ctrl.nodeCardDots = new Image[3];
        ctrl.nodeCardTexts = new TextMeshProUGUI[3];

        string[] defaultLabels = {
            "<b>[1] NODE 0 (LEADER)</b> <color=#00F0FF>◀ ACTIVE</color>\n<size=13>10.66.68.52:8000  •  <color=#00FF77>PROBING...</color></size>",
            "<b>[2] NODE 1 (WINGMAN)</b>\n<size=13>10.66.68.10:8000  •  <color=#00FF77>PROBING...</color></size>",
            "<b>[3] NODE 2 (SCOUT)</b>\n<size=13>10.66.68.82:8000  •  <color=#00FF77>PROBING...</color></size>"
        };

        float[] cardYs = { -78f, -166f, -254f };
        for (int i = 0; i < 3; i++)
        {
            GameObject cardObj = CreatePanel($"BTN_NODE_{i}", leftPanel.transform, new Vector2(0f, cardYs[i]), new Vector2(388f, 76f),
                i == 0 ? new Color(0f, 0.36f, 0.48f, 0.95f) : cCardBg);
            Image cardBg = cardObj.GetComponent<Image>();
            Button cardBtn = cardObj.AddComponent<Button>();
            cardBtn.targetGraphic = cardBg;
            CreateBorder(cardObj.transform, new Vector2(388f, 76f), cCyanDim, 1f);

            GameObject dotObj = CreateBox($"NodeDot_{i}", cardObj.transform, new Vector2(-168f, 0f), new Vector2(16f, 16f), cGreen);
            TextMeshProUGUI cardTxt = CreateTMP($"NodeTxt_{i}", cardObj.transform, defaultLabels[i],
                new Vector2(12f, 0f), new Vector2(330f, 64f), 16, Color.white, TextAlignmentOptions.Left, false);
            cardTxt.raycastTarget = false;

            ctrl.nodeCardButtons[i] = cardBtn;
            ctrl.nodeCardBgs[i] = cardBg;
            ctrl.nodeCardDots[i] = dotObj.GetComponent<Image>();
            ctrl.nodeCardTexts[i] = cardTxt;
        }

        ctrl.swarmStatusText = CreateTMP("SwarmStatus", leftPanel.transform,
            "mDNS MESH DISCOVERY: ACTIVE  |  AUTO-FAILOVER ON",
            new Vector2(0f, -345f), new Vector2(388f, 40f), 14, cCyan, TextAlignmentOptions.Center, false);

        // =========================================================================
        // 3. CENTER HUD VIEWPORT (880 x 790 at X = 0, Y = +20)
        // =========================================================================
        GameObject centerHUD = new GameObject("Center_HUD");
        centerHUD.transform.SetParent(hudRoot.transform, false);
        RectTransform centerRect = centerHUD.AddComponent<RectTransform>();
        centerRect.anchoredPosition = new Vector2(0f, 20f);
        centerRect.sizeDelta = new Vector2(880f, 790f);

        // Top Compass Tape Card
        GameObject compassCard = CreatePanel("Compass_Card", centerHUD.transform, new Vector2(0f, 358f), new Vector2(520f, 48f), cPanelBg);
        CreateBorder(compassCard.transform, new Vector2(520f, 48f), cCyanDim, 1.5f);
        ctrl.compassHeadingText = CreateTMP("Compass_Heading", compassCard.transform, "◄  HDG 000° N  ►",
            Vector2.zero, new Vector2(500f, 42f), 24, cCyan, TextAlignmentOptions.Center, true);
        ctrl.themeAccentGraphics.Add(ctrl.compassHeadingText);

        // Live Video Screen Frame (780 x 440 at Y = +92)
        GameObject videoFrame = CreatePanel("Center_VideoFrame", centerHUD.transform, new Vector2(0f, 92f), new Vector2(788f, 448f), new Color(0.01f, 0.03f, 0.06f, 0.55f));
        CreateBorder(videoFrame.transform, new Vector2(788f, 448f), cCyan, 2f);

        GameObject vsObj = new GameObject("Center_VideoScreen");
        vsObj.transform.SetParent(centerHUD.transform, false);
        RectTransform vsRt = vsObj.AddComponent<RectTransform>();
        vsRt.anchoredPosition = new Vector2(0f, 92f);
        vsRt.sizeDelta = new Vector2(780f, 440f);
        RawImage vsRaw = vsObj.AddComponent<RawImage>();
        vsRaw.color = new Color(1f, 1f, 1f, 0.92f);
        ctrl.centerVideoScreen = vsRaw;
        camReceiver.targetRawImage = vsRaw;

        // Subtle Center Tactical Reticle inside Video Screen
        CreateBox("Reticle_H", vsObj.transform, Vector2.zero, new Vector2(36f, 1.5f), new Color(0f, 1f, 0.8f, 0.35f));
        CreateBox("Reticle_V", vsObj.transform, Vector2.zero, new Vector2(1.5f, 36f), new Color(0f, 1f, 0.8f, 0.35f));

        // AI Detection Bounding Box Overlay Container
        GameObject bboxContainerObj = new GameObject("AI_BBox_Container");
        bboxContainerObj.transform.SetParent(vsObj.transform, false);
        RectTransform bboxRt = bboxContainerObj.AddComponent<RectTransform>();
        bboxRt.anchorMin = Vector2.zero;
        bboxRt.anchorMax = Vector2.one;
        bboxRt.offsetMin = Vector2.zero;
        bboxRt.offsetMax = Vector2.zero;
        ctrl.aiBboxContainer = bboxRt;

        ctrl.videoOverlayText = CreateTMP("Video_Status_Overlay", vsObj.transform, "",
            new Vector2(0f, -195f), new Vector2(740f, 32f), 15, cCyan, TextAlignmentOptions.Center, false);

        // Gimbal Servo Angle Indicator Bar (Y = -160)
        GameObject gimbalBar = CreatePanel("Gimbal_Bar", centerHUD.transform, new Vector2(0f, -160f), new Vector2(780f, 40f), cPanelBg);
        CreateBorder(gimbalBar.transform, new Vector2(780f, 40f), cCyanDim, 1f);
        ctrl.servoPanText = CreateTMP("ServoPanText", gimbalBar.transform, "GIMBAL PAN: 090° [Q/E]",
            new Vector2(-220f, 0f), new Vector2(300f, 34f), 16, cCyan, TextAlignmentOptions.Left, true);

        GameObject servoTrack = CreateBox("Servo_Track", gimbalBar.transform, new Vector2(130f, 0f), new Vector2(240f, 8f), new Color(0.1f, 0.25f, 0.35f, 0.9f));
        GameObject servoTick = CreateBox("Servo_Tick", servoTrack.transform, Vector2.zero, new Vector2(14f, 22f), cAmber);
        ctrl.servoTickIndicator = servoTick.GetComponent<RectTransform>();

        // Propulsion & Sonar Telemetry Deck (780 x 145 at Y = -268)
        GameObject driveDeck = CreatePanel("Drive_Telemetry_Deck", centerHUD.transform, new Vector2(0f, -268f), new Vector2(780f, 148f), cPanelBg);
        ctrl.themePanelBackgrounds.Add(driveDeck.GetComponent<Image>());
        CreateBorder(driveDeck.transform, new Vector2(780f, 148f), cCyanDim, 1.5f);

        // Left Motor Track Column
        ctrl.motorLeftText = CreateTMP("Motor_L_Txt", driveDeck.transform, "L-TRK +0",
            new Vector2(-270f, 36f), new Vector2(200f, 32f), 20, cGreen, TextAlignmentOptions.Center, true);
        ctrl.motorLeftBarFill = CreateProgressBar("Motor_L_Bar", driveDeck.transform, new Vector2(-270f, -2f), new Vector2(190f, 16f), cGreen, 0f);

        // Center Speed Limit & FSM Mode Column
        ctrl.speedPercentText = CreateTMP("Speed_Txt", driveDeck.transform, "SPEED LIMIT: 75% [-/=]",
            new Vector2(0f, 42f), new Vector2(280f, 32f), 20, Color.white, TextAlignmentOptions.Center, true);
        ctrl.speedBarFill = CreateProgressBar("Speed_Bar", driveDeck.transform, new Vector2(0f, 8f), new Vector2(250f, 16f), cCyan, 0.75f);
        ctrl.fsmActionText = CreateTMP("FSM_Action_Txt", driveDeck.transform, "FSM: MANUAL TELEOP",
            new Vector2(0f, -34f), new Vector2(280f, 28f), 16, cAmber, TextAlignmentOptions.Center, true);

        // Right Motor Track Column
        ctrl.motorRightText = CreateTMP("Motor_R_Txt", driveDeck.transform, "R-TRK +0",
            new Vector2(270f, 36f), new Vector2(200f, 32f), 20, cGreen, TextAlignmentOptions.Center, true);
        ctrl.motorRightBarFill = CreateProgressBar("Motor_R_Bar", driveDeck.transform, new Vector2(270f, -2f), new Vector2(190f, 16f), cGreen, 0f);

        // Ultrasonic Sonar Status Strip inside Drive Deck
        ctrl.ultrasonicDistanceText = CreateTMP("Ultrasonic_Txt", driveDeck.transform, "SONAR: CLEAR",
            new Vector2(-270f, -42f), new Vector2(220f, 28f), 17, cCyan, TextAlignmentOptions.Center, true);
        CreateTMP("Drive_Hint_Txt", driveDeck.transform, "WASD / STICK: DRIVE   |   SPACE: E-STOP",
            new Vector2(230f, -42f), new Vector2(280f, 28f), 13, cTextLight, TextAlignmentOptions.Center, false);

        // Collision Proximity Alert Overlay Banner (Centered over Video Viewport, hidden when safe)
        ctrl.obstacleAlertPanel = CreatePanel("Obstacle_Alert_Modal", centerHUD.transform, new Vector2(0f, 92f), new Vector2(680f, 96f), new Color(0.55f, 0.02f, 0.05f, 0.94f));
        CreateBorder(ctrl.obstacleAlertPanel.transform, new Vector2(680f, 96f), cRed, 3f);
        ctrl.obstacleDistanceText = CreateTMP("Alert_Label", ctrl.obstacleAlertPanel.transform,
            "⚠ PROXIMITY HAZARD — OBSTACLE AHEAD  |  AUTO-BRAKE",
            Vector2.zero, new Vector2(650f, 80f), 22, Color.white, TextAlignmentOptions.Center, true);
        ctrl.obstacleAlertPanel.SetActive(false);

        // =========================================================================
        // 4. RIGHT WING PANEL: ATTITUDE, ENVIRONMENT & HARDWARE HEALTH (420 x 790 at X = +690, Y = +20)
        // =========================================================================
        GameObject rightPanel = CreatePanel("RightPanel_Diagnostics", hudRoot.transform, new Vector2(690f, 20f), new Vector2(420f, 790f), cPanelBg);
        ctrl.rightPanelRect = rightPanel.GetComponent<RectTransform>();
        ctrl.rightPanelRect.localEulerAngles = new Vector3(0f, cfg.Profile.wingPanelAngle, 0f);
        ctrl.themePanelBackgrounds.Add(rightPanel.GetComponent<Image>());
        CreateBorder(rightPanel.transform, new Vector2(420f, 790f), cCyanDim, 1.5f);

        var hdrRight = CreateTMP("Header_Right", rightPanel.transform, "ATTITUDE, ENVIRONMENT & SYSTEM ▶",
            new Vector2(0f, 366f), new Vector2(390f, 32f), 18, cCyan, TextAlignmentOptions.Left, true);
        ctrl.themeAccentGraphics.Add(hdrRight);
        CreateBox("Header_Right_Line", rightPanel.transform, new Vector2(0f, 346f), new Vector2(390f, 2f), cCyanDim);

        // --- CARD 1: 3D GYRO & ARTIFICIAL HORIZON (388 x 210 at Y = +225) ---
        GameObject gyroCard = CreatePanel("Card_Attitude", rightPanel.transform, new Vector2(0f, 225f), new Vector2(388f, 210f), cCardBg);
        CreateBorder(gyroCard.transform, new Vector2(388f, 210f), cCyanDim, 1f);
        CreateTMP("Gyro_Title", gyroCard.transform, "IMU ARTIFICIAL HORIZON & GYRO",
            new Vector2(0f, 84f), new Vector2(364f, 24f), 14, cAmber, TextAlignmentOptions.Left, true);

        // Masked Horizon Viewport on Left side of Card 1 (-95, -10)
        GameObject horizonMaskObj = CreatePanel("Horizon_Mask", gyroCard.transform, new Vector2(-95f, -12f), new Vector2(150f, 150f), new Color(0.02f, 0.14f, 0.24f, 1f));
        horizonMaskObj.AddComponent<RectMask2D>();
        CreateBorder(horizonMaskObj.transform, new Vector2(150f, 150f), cCyan, 1.5f);

        GameObject horizonBallObj = new GameObject("IMU_Horizon");
        horizonBallObj.transform.SetParent(horizonMaskObj.transform, false);
        ctrl.imuHorizonBall = horizonBallObj.AddComponent<RectTransform>();
        ctrl.imuHorizonBall.anchoredPosition = Vector2.zero;
        ctrl.imuHorizonBall.sizeDelta = new Vector2(220f, 220f);

        // Sky & Ground halves inside Horizon Ball
        CreateBox("Sky_Half", ctrl.imuHorizonBall, new Vector2(0f, 55f), new Vector2(220f, 110f), new Color(0.04f, 0.28f, 0.48f, 0.85f));
        CreateBox("Ground_Half", ctrl.imuHorizonBall, new Vector2(0f, -55f), new Vector2(220f, 110f), new Color(0.28f, 0.18f, 0.08f, 0.85f));
        CreateBox("Horizon_Line", ctrl.imuHorizonBall, Vector2.zero, new Vector2(180f, 3f), cGreen);
        CreateBox("Pitch_Ladder_Up", ctrl.imuHorizonBall, new Vector2(0f, 24f), new Vector2(70f, 2f), Color.white);
        CreateBox("Pitch_Ladder_Dn", ctrl.imuHorizonBall, new Vector2(0f, -24f), new Vector2(70f, 2f), Color.white);

        // Readouts on Right side of Card 1 (+85) — ZERO OVERLAP!
        ctrl.pitchText = CreateTMP("Pitch_Val", gyroCard.transform, "PITCH: +0.0°",
            new Vector2(92f, 32f), new Vector2(180f, 30f), 18, Color.white, TextAlignmentOptions.Left, true);
        ctrl.rollText = CreateTMP("Roll_Val", gyroCard.transform, "ROLL:  +0.0°",
            new Vector2(92f, -6f), new Vector2(180f, 30f), 18, Color.white, TextAlignmentOptions.Left, true);
        ctrl.yawText = CreateTMP("Yaw_Val", gyroCard.transform, "YAW:   000.0°",
            new Vector2(92f, -44f), new Vector2(180f, 30f), 18, cCyan, TextAlignmentOptions.Left, true);

        // --- CARD 2: BME280 ENVIRONMENT & SURVIVABILITY (388 x 175 at Y = +14) ---
        GameObject envCard = CreatePanel("Card_Environment", rightPanel.transform, new Vector2(0f, 14f), new Vector2(388f, 175f), cCardBg);
        CreateBorder(envCard.transform, new Vector2(388f, 175f), cCyanDim, 1f);
        CreateTMP("Env_Title", envCard.transform, "ATMOSPHERIC & SURVIVABILITY SENSORS",
            new Vector2(0f, 64f), new Vector2(364f, 24f), 14, cAmber, TextAlignmentOptions.Left, true);

        ctrl.envMetricsText = CreateTMP("Env_Metrics_Txt", envCard.transform,
            "<b>TEMP:</b> 28.5 °C       <b>HUMIDITY:</b> 54 %\n" +
            "<b>PRESS:</b> 1012 hPa    <b>ALTITUDE:</b> 14.2 m\n" +
            "<b>HEAT IDX:</b> 29.4 °C    <b>STATUS:</b> <color=#00FF88>NOMINAL SAFE</color>",
            new Vector2(0f, -12f), new Vector2(364f, 115f), 15, cTextLight, TextAlignmentOptions.Left, false);

        // --- CARD 3: HARDWARE, SERIAL & POWER TELEMETRY (388 x 255 at Y = -220) ---
        GameObject hwCard = CreatePanel("Card_Hardware", rightPanel.transform, new Vector2(0f, -220f), new Vector2(388f, 255f), cCardBg);
        CreateBorder(hwCard.transform, new Vector2(388f, 255f), cCyanDim, 1f);
        CreateTMP("Hw_Title", hwCard.transform, "HARDWARE LINK & POWER BUS",
            new Vector2(0f, 102f), new Vector2(364f, 24f), 14, cAmber, TextAlignmentOptions.Left, true);

        ctrl.hardwareHealthText = CreateTMP("Hardware_Health_Txt", hwCard.transform,
            "<b>ESP32 SERIAL:</b> /dev/ttyTHS1 [<color=#00FF88>LINKED</color>]\n" +
            "<b>MOTOR BRIDGE:</b> <color=#00FF88>ONLINE</color>   |   <b>SONAR:</b> OK\n" +
            "<b>UART FRAMES:</b> RX 100%  •  CRC ERR 0\n" +
            "<b>AI ENGINE:</b> YOLOV8-TRT (8.5 FPS)",
            new Vector2(0f, 18f), new Vector2(364f, 125f), 15, cTextLight, TextAlignmentOptions.Left, false);

        ctrl.healthText = ctrl.hardwareHealthText;
        ctrl.sensorListText = ctrl.hardwareHealthText;

        CreateTMP("Batt_Label", hwCard.transform, "MAIN BATTERY:",
            new Vector2(-115f, -82f), new Vector2(140f, 28f), 15, cCyan, TextAlignmentOptions.Left, true);
        ctrl.batteryText = CreateTMP("Battery_Val", hwCard.transform, "12.4 V  [92%]",
            new Vector2(110f, -82f), new Vector2(150f, 28f), 16, cGreen, TextAlignmentOptions.Right, true);
        ctrl.batteryBarFill = CreateProgressBar("Battery_Bar", hwCard.transform, new Vector2(0f, -108f), new Vector2(364f, 14f), cGreen, 0.92f);

        // =========================================================================
        // 5. BOTTOM COMMAND DECK (1740 x 98 at Y = -465)
        // =========================================================================
        GameObject bottomPanel = CreatePanel("BottomPanel_Controls", hudRoot.transform, new Vector2(0f, -465f), new Vector2(1740f, 98f), cPanelBg);
        ctrl.themePanelBackgrounds.Add(bottomPanel.GetComponent<Image>());
        CreateBorder(bottomPanel.transform, new Vector2(1740f, 98f), cCyanDim, 1.5f);

        // Deadman Status Pill on Left (-655, 0)
        GameObject deadmanPill = CreatePanel("Deadman_Pill", bottomPanel.transform, new Vector2(-655f, 0f), new Vector2(380f, 64f), cCardBg);
        CreateBorder(deadmanPill.transform, new Vector2(380f, 64f), cCyanDim, 1f);
        GameObject dmDot = CreateBox("Deadman_Dot", deadmanPill.transform, new Vector2(-164f, 0f), new Vector2(20f, 20f), cGreen);
        ctrl.deadmanIndicatorDot = dmDot.GetComponent<Image>();
        ctrl.deadmanStatusText = CreateTMP("Deadman_Txt", deadmanPill.transform,
            "DRIVE READY (SEAMLESS WASD / STICK)",
            new Vector2(14f, 0f), new Vector2(325f, 52f), 15, Color.white, TextAlignmentOptions.Left, true);

        // 8 Interactive Tactical Action Buttons across the deck
        string[] btnCodes = { "BTN_SCAN", "BTN_BEACON", "BTN_CENTER", "BTN_APPROACH", "BTN_BACK", "BTN_VIEW", "BTN_CONFIG", "BTN_ESTOP" };
        string[] btnTitles = { "SCAN AREA", "SOS BEACON", "CENTER CAM", "APPROACH", "BACK OUT", "VIEW [V]", "⚙ CONFIG", "🚨 E-STOP" };
        float startX = -380f;
        float spacingX = 168f;

        for (int i = 0; i < btnCodes.Length; i++)
        {
            bool isEstop = (i == btnCodes.Length - 1);
            bool isCfg = (i == btnCodes.Length - 2);
            Color btnColor = isEstop
                ? new Color(0.75f, 0.08f, 0.14f, 0.95f)
                : (isCfg ? new Color(0.0f, 0.42f, 0.62f, 0.95f) : new Color(0.06f, 0.18f, 0.30f, 0.92f));

            CreateButton(btnCodes[i], bottomPanel.transform, btnTitles[i],
                new Vector2(startX + i * spacingX, 0f), new Vector2(154f, 62f), btnColor, 16);
        }

        // =========================================================================
        // 6. INTERACTIVE IN-VR FRAMEWORK SETTINGS MODAL (920 x 620, Pop-up Drawer)
        // =========================================================================
        GameObject modal = CreatePanel("Framework_Settings_Modal", hudRoot.transform, new Vector2(0f, 20f), new Vector2(940f, 640f), new Color(0.01f, 0.04f, 0.09f, 0.97f));
        RectTransform modalRt = modal.GetComponent<RectTransform>();
        modalRt.localPosition = new Vector3(0f, 20f, -25f); // slightly closer to user for clean modal layering
        CreateBorder(modal.transform, new Vector2(940f, 640f), cCyan, 2.5f);

        CreateTMP("Modal_Header", modal.transform, "⚙ V-RESQ FRAMEWORK CONTROL & ERGONOMICS CENTER",
            new Vector2(-40f, 280f), new Vector2(780f, 40f), 22, cCyan, TextAlignmentOptions.Left, true);
        CreateButton("CFG_CLOSE", modal.transform, "✖ CLOSE [TAB]",
            new Vector2(385f, 280f), new Vector2(130f, 40f), new Color(0.6f, 0.12f, 0.18f, 0.95f), 15);
        CreateBox("Modal_Divider", modal.transform, new Vector2(0f, 252f), new Vector2(890f, 2f), cCyanDim);

        // Live Config Summary Card
        GameObject summaryCard = CreatePanel("Summary_Card", modal.transform, new Vector2(0f, 165f), new Vector2(890f, 140f), cCardBg);
        CreateBorder(summaryCard.transform, new Vector2(890f, 140f), cCyanDim, 1f);
        ctrl.cfgSummaryText = CreateTMP("Cfg_Summary_Txt", summaryCard.transform,
            "<b>SURROUND MODE:</b> Surround180   |   <b>CENTER VIEWPORT:</b> PIP + SURROUND\n" +
            "<b>DOME RADIUS:</b> 2.8 m   |   <b>HUD DISTANCE:</b> 1.45 m   |   <b>WING ANGLE:</b> 24°\n" +
            "<b>MAX SPEED LIMIT:</b> 75%   |   <b>DRIVE SAFETY:</b> IMMEDIATE DRIVE (SEAMLESS)",
            Vector2.zero, new Vector2(850f, 124f), 17, Color.white, TextAlignmentOptions.Left, false);

        // Grid of Interactive Tuning Controls
        Color cBtnTune = new Color(0.05f, 0.22f, 0.36f, 0.95f);
        Color cBtnAct = new Color(0.0f, 0.48f, 0.38f, 0.95f);

        // Row 1 (Y = +45): Display Mode, Center Viewport Mode, Color Theme
        CreateButton("CFG_VIEW_CYCLE", modal.transform, "CYCLE SURROUND MODE (180°/220°/360°)", new Vector2(-295f, 45f), new Vector2(285f, 52f), cBtnTune, 14);
        CreateButton("CFG_MONITOR_TOGGLE", modal.transform, "TOGGLE CENTER MONITOR IN DOME", new Vector2(0f, 45f), new Vector2(285f, 52f), cBtnTune, 14);
        CreateButton("CFG_THEME_CYCLE", modal.transform, "SWITCH COCKPIT COLOR THEME", new Vector2(295f, 45f), new Vector2(285f, 52f), cBtnTune, 14);

        // Row 2 (Y = -25): Surround Dome Radius (-/+), HUD Distance (-/+), Wing Curvature (-/+)
        CreateButton("CFG_RADIUS_MINUS", modal.transform, "DOME RADIUS -0.2m", new Vector2(-368f, -25f), new Vector2(140f, 50f), cBtnTune, 14);
        CreateButton("CFG_RADIUS_PLUS", modal.transform, "DOME RADIUS +0.2m", new Vector2(-222f, -25f), new Vector2(140f, 50f), cBtnTune, 14);

        CreateButton("CFG_HUDDIST_MINUS", modal.transform, "HUD DIST -0.1m", new Vector2(-72f, -25f), new Vector2(140f, 50f), cBtnTune, 14);
        CreateButton("CFG_HUDDIST_PLUS", modal.transform, "HUD DIST +0.1m", new Vector2(72f, -25f), new Vector2(140f, 50f), cBtnTune, 14);

        CreateButton("CFG_WING_MINUS", modal.transform, "WING ANGLE -4°", new Vector2(222f, -25f), new Vector2(140f, 50f), cBtnTune, 14);
        CreateButton("CFG_WING_PLUS", modal.transform, "WING ANGLE +4°", new Vector2(368f, -25f), new Vector2(140f, 50f), cBtnTune, 14);

        // Row 3 (Y = -95): Speed Limit (-/+), Deadman Toggle, Auto-Failover Toggle
        CreateButton("CFG_SPEED_MINUS", modal.transform, "MAX SPEED -10%", new Vector2(-368f, -95f), new Vector2(140f, 50f), cBtnTune, 14);
        CreateButton("CFG_SPEED_PLUS", modal.transform, "MAX SPEED +10%", new Vector2(-222f, -95f), new Vector2(140f, 50f), cBtnTune, 14);

        CreateButton("CFG_DEADMAN_TOGGLE", modal.transform, "TOGGLE DEADMAN REQUIREMENT", new Vector2(0f, -95f), new Vector2(285f, 50f), cBtnTune, 14);
        CreateButton("CFG_FAILOVER_TOGGLE", modal.transform, "TOGGLE MULTI-NODE FAILOVER", new Vector2(295f, -95f), new Vector2(285f, 50f), cBtnTune, 14);

        // Row 4 (Y = -175): Recenter HUD, Save JSON Profile, Reset Defaults
        CreateButton("CFG_RECENTER", modal.transform, "🎯 RECENTER HUD TO EYES [R]", new Vector2(-295f, -175f), new Vector2(285f, 54f), cBtnAct, 15);
        CreateButton("CFG_SAVE", modal.transform, "💾 SAVE PROFILE TO DISK", new Vector2(0f, -175f), new Vector2(285f, 54f), cBtnAct, 15);
        CreateButton("CFG_RESET", modal.transform, "↺ RESET DEFAULT PROFILE", new Vector2(295f, -175f), new Vector2(285f, 54f), new Color(0.45f, 0.22f, 0.1f, 0.95f), 15);

        CreateTMP("Modal_Footer", modal.transform,
            "TIP: Use Unity Menu [V-ResQ -> Framework Control Center] in Editor or press [TAB] anytime in VR/Play Mode.",
            new Vector2(0f, -265f), new Vector2(880f, 30f), 14, cCyan, TextAlignmentOptions.Center, false);

        ctrl.settingsModalRoot = modal;
        modal.SetActive(false);

        // Wire buttons & save scene
        ctrl.WireAllInteractiveButtons();
        ctrl.ApplyFrameworkVisuals();

        EditorUtility.SetDirty(hudRoot);
        var activeScene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(activeScene);
        EditorSceneManager.SaveScene(activeScene);

        Selection.activeGameObject = hudRoot;
        Debug.Log("<color=cyan>[V-ResQ Framework]</color> Rebuilt clean Tactical VR Cockpit HUD and saved scene successfully!");
    }

    // =========================================================================
    // EDITOR CONTROL CENTER WINDOW GUI
    // =========================================================================
    private void OnGUI()
    {
        var cfg = Object.FindFirstObjectByType<VResqFrameworkConfig>();
        if (cfg == null)
        {
            EditorGUILayout.HelpBox("No VResqFrameworkConfig found in the current scene. Click below to build the Tactical VR Cockpit HUD.", MessageType.Info);
            if (GUILayout.Button("Build Full Tactical VR Cockpit HUD Now", GUILayout.Height(42)))
            {
                RebuildSceneCleanly();
            }
            return;
        }

        _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);
        var p = cfg.Profile;

        GUIStyle headerStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13 };

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("1. Swarm Fleet Endpoints (Node 0, Node 1, Node 2)", headerStyle);
        EditorGUILayout.BeginVertical("box");
        p.defaultNodeIndex = EditorGUILayout.IntSlider("Default Active Node", p.defaultNodeIndex, 0, 2);
        p.autoFallbackToOnlineNode = EditorGUILayout.Toggle("Seamless Auto-Failover", p.autoFallbackToOnlineNode);
        p.fleetProbeIntervalSec = EditorGUILayout.Slider("Health Probe Interval (s)", p.fleetProbeIntervalSec, 1.5f, 10f);

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
        EditorGUILayout.LabelField("2. Surround Panoramic Display (180° / 220° / 360°)", headerStyle);
        EditorGUILayout.BeginVertical("box");
        p.viewMode = (SurroundViewMode)EditorGUILayout.EnumPopup("Default View Mode", p.viewMode);
        p.surroundRadius = EditorGUILayout.Slider("Surround Dome Radius (m)", p.surroundRadius, 1.6f, 5.0f);
        p.surroundHeight = EditorGUILayout.Slider("Surround Dome Height (m)", p.surroundHeight, 1.4f, 4.5f);
        p.surroundArcAngle = EditorGUILayout.Slider("Surround Arc Angle (°)", p.surroundArcAngle, 90f, 360f);
        p.showCenterMonitorInSurround = EditorGUILayout.Toggle("Show Center PIP Monitor", p.showCenterMonitorInSurround);
        p.centerMonitorOpacityInSurround = EditorGUILayout.Slider("Center Monitor Opacity", p.centerMonitorOpacityInSurround, 0.1f, 1.0f);
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("3. Cockpit UX/UI Ergonomics & Theme", headerStyle);
        EditorGUILayout.BeginVertical("box");
        p.theme = (CockpitTheme)EditorGUILayout.EnumPopup("Cockpit Color Theme", p.theme);
        p.hudDistance = EditorGUILayout.Slider("HUD Distance from Eyes (m)", p.hudDistance, 0.85f, 2.5f);
        p.hudHeightOffset = EditorGUILayout.Slider("HUD Vertical Offset (m)", p.hudHeightOffset, -0.4f, 0.4f);
        p.wingPanelAngle = EditorGUILayout.Slider("Left/Right Wing Angle (°)", p.wingPanelAngle, 0f, 42f);
        p.panelOpacity = EditorGUILayout.Slider("Glass Panel Opacity", p.panelOpacity, 0.4f, 0.98f);
        p.smoothFollowHead = EditorGUILayout.Toggle("Smooth Follow Head Gaze", p.smoothFollowHead);
        p.followDeadzoneAngle = EditorGUILayout.Slider("Free-Look Deadzone (°)", p.followDeadzoneAngle, 10f, 180f);
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("4. Teleoperation & Safety", headerStyle);
        EditorGUILayout.BeginVertical("box");
        p.speedScale = EditorGUILayout.Slider("Default Speed Limit", p.speedScale, 0.2f, 1.0f);
        p.requireDeadmanSwitch = EditorGUILayout.Toggle("Require Deadman Hold", p.requireDeadmanSwitch);
        p.collisionAlertCm = EditorGUILayout.Slider("Proximity Alert Distance (cm)", p.collisionAlertCm, 10f, 60f);
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

        if (GUILayout.Button("🔄 Clean Rebuild Entire HUD Hierarchy & Save Scene", GUILayout.Height(38)))
        {
            RebuildSceneCleanly();
        }

        EditorGUILayout.EndScrollView();
    }

    // =========================================================================
    // UI BUILDER HELPERS
    // =========================================================================
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
