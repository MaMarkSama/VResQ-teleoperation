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

    [MenuItem("V-ResQ/Rebuild Open-Visor Surround HUD (No Flat Screen)", false, 2)]
    public static void BuildFullTacticalHUD()
    {
        RebuildSceneCleanly();
    }

    /// <summary>
    /// Rebuilds SampleScene.unity with:
    /// 1. Open See-Through Center Visor (NO flat rectangular monitor blocking the middle!)
    /// 2. 3D 360°/220°/180° Surround Camera Dome vs. External 3D World toggle
    /// 3. 100% Joystick / Gamepad / XR Controller integration with Live Joystick Vector Scope
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

        // 2. Ensure 3D External World Environment exists so "มองเห็นรอบนอก" has clear 360° 3D spatial reference
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
        cfg.Profile.viewMode = SurroundViewMode.Surround360;
        cfg.Profile.surroundArcAngle = 360f;
        cfg.Profile.showCenterMonitorInSurround = false;

        VResqClient client = hudRoot.AddComponent<VResqClient>();
        client.robotIp = "10.66.68.52";
        client.port = 8000;

        VResqCameraReceiver camReceiver = hudRoot.AddComponent<VResqCameraReceiver>();
        camReceiver.targetRawImage = null; // Strictly NO flat screen in center!

        VResqSurroundDisplay surround = hudRoot.AddComponent<VResqSurroundDisplay>();
        VResqTeleopController teleop = hudRoot.AddComponent<VResqTeleopController>();
        TacticalHUDFollow follow = hudRoot.AddComponent<TacticalHUDFollow>();
        RescueRobotVRHUD ctrl = hudRoot.AddComponent<RescueRobotVRHUD>();

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

        // Build the 3D Surround Dome immediately so it is ready in the scene
        surround.SyncFromFrameworkConfig();

        // Palette
        Color cPanelBg = new Color(0.02f, 0.06f, 0.12f, 0.80f);
        Color cCardBg = new Color(0.04f, 0.11f, 0.20f, 0.84f);
        Color cCyan = new Color(0.0f, 0.92f, 1.0f, 1.0f);
        Color cCyanDim = new Color(0.0f, 0.70f, 0.88f, 0.50f);
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
            new Vector2(-615f, 0f), new Vector2(460f, 44f), 19, cCyan, TextAlignmentOptions.Left, true);
        ctrl.themeAccentGraphics.Add(ctrl.robotIdText);

        ctrl.latencyText = CreateTMP("Latency", topBar.transform, "WS: ONLINE  |  SURROUND CAM: 25 FPS",
            new Vector2(-210f, 0f), new Vector2(330f, 44f), 17, cGreen, TextAlignmentOptions.Center, true);

        ctrl.viewModeBadgeText = CreateTMP("ViewModeBadge", topBar.transform, "🎥 VIEW: 360° SURROUND CAM (ภาพรอบตัว) [Y/V]",
            new Vector2(160f, 0f), new Vector2(380f, 44f), 16, cAmber, TextAlignmentOptions.Center, true);

        ctrl.aiStatusBadgeText = CreateTMP("AIStatusBadge", topBar.transform, "AI VISION: 0 TARGETS (8.5 FPS)",
            new Vector2(490f, 0f), new Vector2(260f, 44f), 15, cTextLight, TextAlignmentOptions.Center, true);

        ctrl.clockText = CreateTMP("Clock", topBar.transform, "12:00:00",
            new Vector2(685f, 0f), new Vector2(110f, 44f), 19, Color.white, TextAlignmentOptions.Right, true);

        CreateButton("BTN_EXT_CAM_TOP", topBar.transform, "🌐 รอบนอก/กล้อง [Y]",
            new Vector2(800f, 0f), new Vector2(124f, 38f), new Color(0.0f, 0.50f, 0.42f, 0.95f), 13);

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

        // 2D Radar Scope Card
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
            "X: +0.00m   Y: +0.00m\nHDG: 000°  |  CAM: 090°  |  JOYSTICK READY",
            new Vector2(0f, -134f), new Vector2(370f, 44f), 14, cTextLight, TextAlignmentOptions.Center, false);
        ctrl.radarMap = radarComp;

        CreateTMP("Header_Swarm", leftPanel.transform, "SWARM NODE SELECTOR ([LB] OR KEY 1-3)",
            new Vector2(0f, -20f), new Vector2(388f, 26f), 15, cAmber, TextAlignmentOptions.Left, true);

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
            "mDNS MESH: ACTIVE  |  SWITCH: [LB] OR [1-3]",
            new Vector2(0f, -345f), new Vector2(388f, 40f), 14, cCyan, TextAlignmentOptions.Center, false);

        // =========================================================================
        // 3. OPEN SEE-THROUGH CENTER VISOR (NO FLAT SCREEN!) (880 x 790 at X = 0, Y = +20)
        // =========================================================================
        GameObject centerHUD = new GameObject("Center_HUD");
        centerHUD.transform.SetParent(hudRoot.transform, false);
        RectTransform centerRect = centerHUD.AddComponent<RectTransform>();
        centerRect.anchoredPosition = new Vector2(0f, 20f);
        centerRect.sizeDelta = new Vector2(880f, 790f);

        // Top Compass Tape Card
        GameObject compassCard = CreatePanel("Compass_Card", centerHUD.transform, new Vector2(0f, 358f), new Vector2(540f, 46f), cPanelBg);
        CreateBorder(compassCard.transform, new Vector2(540f, 46f), cCyanDim, 1.5f);
        ctrl.compassHeadingText = CreateTMP("Compass_Heading", compassCard.transform, "◄  HDG 000° N  ►",
            Vector2.zero, new Vector2(520f, 40f), 23, cCyan, TextAlignmentOptions.Center, true);
        ctrl.themeAccentGraphics.Add(ctrl.compassHeadingText);

        // Transparent Open Visor Area (Y = +100): Minimalist Aiming Crosshair & Corner Brackets ONLY (100% see-through!)
        GameObject openVisorArea = new GameObject("Open_Visor_Reticle");
        openVisorArea.transform.SetParent(centerHUD.transform, false);
        RectTransform visorRt = openVisorArea.AddComponent<RectTransform>();
        visorRt.anchoredPosition = new Vector2(0f, 95f);
        visorRt.sizeDelta = new Vector2(760f, 420f);

        // Subtle Center Crosshair
        CreateBox("Reticle_H_Left", openVisorArea.transform, new Vector2(-24f, 0f), new Vector2(22f, 2f), new Color(0f, 1f, 0.85f, 0.45f));
        CreateBox("Reticle_H_Right", openVisorArea.transform, new Vector2(24f, 0f), new Vector2(22f, 2f), new Color(0f, 1f, 0.85f, 0.45f));
        CreateBox("Reticle_V_Top", openVisorArea.transform, new Vector2(0f, 24f), new Vector2(2f, 22f), new Color(0f, 1f, 0.85f, 0.45f));
        CreateBox("Reticle_V_Bot", openVisorArea.transform, new Vector2(0f, -24f), new Vector2(2f, 22f), new Color(0f, 1f, 0.85f, 0.45f));

        // Corner Target Brackets (leaves 99% of center completely unobstructed!)
        CreateCornerBracket(openVisorArea.transform, new Vector2(-360f, 195f), new Vector2(1f, -1f), cCyanDim);
        CreateCornerBracket(openVisorArea.transform, new Vector2(360f, 195f), new Vector2(-1f, -1f), cCyanDim);
        CreateCornerBracket(openVisorArea.transform, new Vector2(-360f, -195f), new Vector2(1f, 1f), cCyanDim);
        CreateCornerBracket(openVisorArea.transform, new Vector2(360f, -195f), new Vector2(-1f, 1f), cCyanDim);

        // AI Detection Bounding Box Overlay Container inside Open Visor
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
            "🎮 JOYSTICK ACTIVE: READY   |   STICK X:+0.00  Y:+0.00",
            new Vector2(-165f, 0f), new Vector2(460f, 36f), 14, cGreen, TextAlignmentOptions.Left, true);

        ctrl.servoPanText = CreateTMP("ServoPanText", gimbalBar.transform, "CAM GIMBAL: 090°",
            new Vector2(155f, 0f), new Vector2(160f, 36f), 14, cCyan, TextAlignmentOptions.Right, true);

        GameObject servoTrack = CreateBox("Servo_Track", gimbalBar.transform, new Vector2(320f, 0f), new Vector2(140f, 8f), new Color(0.1f, 0.25f, 0.35f, 0.9f));
        GameObject servoTick = CreateBox("Servo_Tick", servoTrack.transform, Vector2.zero, new Vector2(12f, 20f), cAmber);
        ctrl.servoTickIndicator = servoTick.GetComponent<RectTransform>();

        // Propulsion, Live Joystick Scope & Sonar Telemetry Deck (820 x 154 at Y = -266)
        GameObject driveDeck = CreatePanel("Drive_Telemetry_Deck", centerHUD.transform, new Vector2(0f, -266f), new Vector2(820f, 154f), cPanelBg);
        ctrl.themePanelBackgrounds.Add(driveDeck.GetComponent<Image>());
        CreateBorder(driveDeck.transform, new Vector2(820f, 154f), cCyanDim, 1.5f);

        // Live 2D Joystick Deflection Scope on Far Left of Drive Deck (-335, +6)
        GameObject joyScope = CreatePanel("Joystick_Scope", driveDeck.transform, new Vector2(-335f, 10f), new Vector2(76f, 76f), cCardBg);
        CreateBorder(joyScope.transform, new Vector2(76f, 76f), cCyan, 1.2f);
        CreateBox("Joy_Cross_H", joyScope.transform, Vector2.zero, new Vector2(68f, 1f), cCyanDim);
        CreateBox("Joy_Cross_V", joyScope.transform, Vector2.zero, new Vector2(1f, 68f), cCyanDim);
        GameObject joyDot = CreateBox("Joystick_Dot", joyScope.transform, Vector2.zero, new Vector2(14f, 14f), cGreen);
        ctrl.joystickDotIndicator = joyDot.GetComponent<RectTransform>();
        CreateTMP("Joy_Lbl", driveDeck.transform, "L-STICK", new Vector2(-335f, -44f), new Vector2(90f, 22f), 12, cCyan, TextAlignmentOptions.Center, true);

        // Left Motor Track Column (-185)
        ctrl.motorLeftText = CreateTMP("Motor_L_Txt", driveDeck.transform, "L-TRK +0",
            new Vector2(-185f, 36f), new Vector2(170f, 30f), 19, cGreen, TextAlignmentOptions.Center, true);
        ctrl.motorLeftBarFill = CreateProgressBar("Motor_L_Bar", driveDeck.transform, new Vector2(-185f, 2f), new Vector2(165f, 15f), cGreen, 0f);
        ctrl.ultrasonicDistanceText = CreateTMP("Ultrasonic_Txt", driveDeck.transform, "SONAR: CLEAR",
            new Vector2(-185f, -38f), new Vector2(180f, 28f), 16, cCyan, TextAlignmentOptions.Center, true);

        // Center Speed Limit & FSM Mode Column (+35)
        ctrl.speedPercentText = CreateTMP("Speed_Txt", driveDeck.transform, "SPEED LIMIT: 75% [DPAD UP/DN]",
            new Vector2(35f, 38f), new Vector2(260f, 30f), 17, Color.white, TextAlignmentOptions.Center, true);
        ctrl.speedBarFill = CreateProgressBar("Speed_Bar", driveDeck.transform, new Vector2(35f, 4f), new Vector2(230f, 15f), cCyan, 0.75f);
        ctrl.fsmActionText = CreateTMP("FSM_Action_Txt", driveDeck.transform, "MODE: JOYSTICK TELEOP",
            new Vector2(35f, -38f), new Vector2(260f, 28f), 15, cAmber, TextAlignmentOptions.Center, true);

        // Right Motor Track Column (+280)
        ctrl.motorRightText = CreateTMP("Motor_R_Txt", driveDeck.transform, "R-TRK +0",
            new Vector2(280f, 36f), new Vector2(170f, 30f), 19, cGreen, TextAlignmentOptions.Center, true);
        ctrl.motorRightBarFill = CreateProgressBar("Motor_R_Bar", driveDeck.transform, new Vector2(280f, 2f), new Vector2(165f, 15f), cGreen, 0f);
        CreateTMP("Drive_Hint_Txt", driveDeck.transform, "[Y]: รอบนอก/กล้อง  |  [B]: E-STOP",
            new Vector2(280f, -38f), new Vector2(220f, 28f), 13, cTextLight, TextAlignmentOptions.Center, false);

        // Collision Proximity Alert Overlay Banner (Hidden when safe)
        ctrl.obstacleAlertPanel = CreatePanel("Obstacle_Alert_Modal", centerHUD.transform, new Vector2(0f, 210f), new Vector2(680f, 84f), new Color(0.55f, 0.02f, 0.05f, 0.94f));
        CreateBorder(ctrl.obstacleAlertPanel.transform, new Vector2(680f, 84f), cRed, 3f);
        ctrl.obstacleDistanceText = CreateTMP("Alert_Label", ctrl.obstacleAlertPanel.transform,
            "⚠ PROXIMITY HAZARD — OBSTACLE AHEAD  |  AUTO-BRAKE",
            Vector2.zero, new Vector2(650f, 70f), 21, Color.white, TextAlignmentOptions.Center, true);
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

        GameObject horizonMaskObj = CreatePanel("Horizon_Mask", gyroCard.transform, new Vector2(-95f, -12f), new Vector2(150f, 150f), new Color(0.02f, 0.14f, 0.24f, 1f));
        horizonMaskObj.AddComponent<RectMask2D>();
        CreateBorder(horizonMaskObj.transform, new Vector2(150f, 150f), cCyan, 1.5f);

        GameObject horizonBallObj = new GameObject("IMU_Horizon");
        horizonBallObj.transform.SetParent(horizonMaskObj.transform, false);
        ctrl.imuHorizonBall = horizonBallObj.AddComponent<RectTransform>();
        ctrl.imuHorizonBall.anchoredPosition = Vector2.zero;
        ctrl.imuHorizonBall.sizeDelta = new Vector2(220f, 220f);

        CreateBox("Sky_Half", ctrl.imuHorizonBall, new Vector2(0f, 55f), new Vector2(220f, 110f), new Color(0.04f, 0.28f, 0.48f, 0.85f));
        CreateBox("Ground_Half", ctrl.imuHorizonBall, new Vector2(0f, -55f), new Vector2(220f, 110f), new Color(0.28f, 0.18f, 0.08f, 0.85f));
        CreateBox("Horizon_Line", ctrl.imuHorizonBall, Vector2.zero, new Vector2(180f, 3f), cGreen);
        CreateBox("Pitch_Ladder_Up", ctrl.imuHorizonBall, new Vector2(0f, 24f), new Vector2(70f, 2f), Color.white);
        CreateBox("Pitch_Ladder_Dn", ctrl.imuHorizonBall, new Vector2(0f, -24f), new Vector2(70f, 2f), Color.white);

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
        CreateTMP("Hw_Title", hwCard.transform, "HARDWARE LINK & JOYSTICK MAP",
            new Vector2(0f, 102f), new Vector2(364f, 24f), 14, cAmber, TextAlignmentOptions.Left, true);

        ctrl.hardwareHealthText = CreateTMP("Hardware_Health_Txt", hwCard.transform,
            "<b>ESP32 SERIAL:</b> /dev/ttyTHS1 [<color=#00FF88>LINKED</color>]\n" +
            "<b>L-STICK:</b> DRIVE   |   <b>R-STICK:</b> 360° LOOK & CAM\n" +
            "<b>BTN [Y]:</b> รอบนอก/กล้อง  •  <b>[RB]:</b> 360°/220°/180°\n" +
            "<b>BTN [LB]:</b> สลับหุ่นยนต์  •  <b>BTN [B]:</b> E-STOP",
            new Vector2(0f, 18f), new Vector2(364f, 125f), 14, cTextLight, TextAlignmentOptions.Left, false);

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

        GameObject deadmanPill = CreatePanel("Deadman_Pill", bottomPanel.transform, new Vector2(-655f, 0f), new Vector2(380f, 64f), cCardBg);
        CreateBorder(deadmanPill.transform, new Vector2(380f, 64f), cCyanDim, 1f);
        GameObject dmDot = CreateBox("Deadman_Dot", deadmanPill.transform, new Vector2(-164f, 0f), new Vector2(20f, 20f), cGreen);
        ctrl.deadmanIndicatorDot = dmDot.GetComponent<Image>();
        ctrl.deadmanStatusText = CreateTMP("Deadman_Txt", deadmanPill.transform,
            "JOYSTICK READY (100% FULL CONTROL)",
            new Vector2(14f, 0f), new Vector2(325f, 52f), 15, Color.white, TextAlignmentOptions.Left, true);

        string[] btnCodes = { "BTN_SCAN", "BTN_BEACON", "BTN_CENTER", "BTN_APPROACH", "BTN_EXT_CAM", "BTN_VIEW", "BTN_CONFIG", "BTN_ESTOP" };
        string[] btnTitles = { "SCAN [A]", "BEACON [X]", "CENTER [R3]", "APPROACH", "🌐 รอบนอก/กล้อง [Y]", "360°/180° [RB]", "⚙ CONFIG", "🚨 E-STOP [B]" };
        float startX = -380f;
        float spacingX = 168f;

        for (int i = 0; i < btnCodes.Length; i++)
        {
            bool isEstop = (i == btnCodes.Length - 1);
            bool isCfg = (i == btnCodes.Length - 2);
            bool isExtToggle = (i == 4);
            Color btnColor = isEstop
                ? new Color(0.75f, 0.08f, 0.14f, 0.95f)
                : (isExtToggle ? new Color(0.0f, 0.48f, 0.38f, 0.95f)
                : (isCfg ? new Color(0.0f, 0.42f, 0.62f, 0.95f) : new Color(0.06f, 0.18f, 0.30f, 0.92f)));

            CreateButton(btnCodes[i], bottomPanel.transform, btnTitles[i],
                new Vector2(startX + i * spacingX, 0f), new Vector2(154f, 62f), btnColor, 14);
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

        // Row 1 (Y = +45): Instant External vs Camera Toggle, Cycle Surround Angle, Color Theme
        CreateButton("CFG_EXT_TOGGLE", modal.transform, "🌐 สลับ มองเห็นรอบนอก ⇄ ภาพรอบตัว [Y]", new Vector2(-295f, 45f), new Vector2(285f, 52f), cBtnAct, 14);
        CreateButton("CFG_VIEW_CYCLE", modal.transform, "🎥 เปลี่ยนมุมกล้องรอบตัว (360°/220°/180°)", new Vector2(0f, 45f), new Vector2(285f, 52f), cBtnTune, 14);
        CreateButton("CFG_THEME_CYCLE", modal.transform, "🎨 เปลี่ยนโทนสี HUD (THEME)", new Vector2(295f, 45f), new Vector2(285f, 52f), cBtnTune, 14);

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

        EditorUtility.SetDirty(hudRoot);
        var activeScene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(activeScene);
        EditorSceneManager.SaveScene(activeScene);

        Selection.activeGameObject = hudRoot;
        Debug.Log("<color=cyan>[V-ResQ Framework]</color> Rebuilt Open-Visor Surround HUD (No Flat Screen) + 100% Joystick Control and saved scene!");
    }

    /// <summary>
    /// Creates a clean 3D tactical rescue ground grid & cardinal markers around (0,0,0)
    /// so when the user switches to "มองเห็นรอบนอก (ExternalWorld)", they clearly see the 3D surroundings.
    /// </summary>
    private static void SetupExternal3DWorldEnvironment()
    {
        GameObject oldEnv = GameObject.Find("External_3D_World_Environment");
        if (oldEnv != null) DestroyImmediate(oldEnv);

        GameObject envRoot = new GameObject("External_3D_World_Environment");

        Shader litShader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

        // Tactical Floor Plane
        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "Tactical_Rescue_Floor";
        floor.transform.SetParent(envRoot.transform, false);
        floor.transform.position = new Vector3(0f, -0.05f, 0f);
        floor.transform.localScale = new Vector3(4f, 1f, 4f);
        Material floorMat = new Material(litShader);
        floorMat.color = new Color(0.08f, 0.14f, 0.20f, 1f);
        floor.GetComponent<Renderer>().sharedMaterial = floorMat;

        // 4 Cardinal Direction Beacons around the user so 360° looking in ExternalWorld is super clear
        Vector3[] positions = {
            new Vector3(0f, 1.0f, 6.0f),   // North (+Z)
            new Vector3(6.0f, 1.0f, 0f),   // East (+X)
            new Vector3(0f, 1.0f, -6.0f),  // South (-Z)
            new Vector3(-6.0f, 1.0f, 0f)   // West (-X)
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

    // =========================================================================
    // EDITOR CONTROL CENTER WINDOW GUI
    // =========================================================================
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
