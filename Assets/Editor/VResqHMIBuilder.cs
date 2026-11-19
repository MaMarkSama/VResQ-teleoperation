using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using VResQ.Control;
using VResQ.HMI;

/// <summary>
/// Builds and wires the Human-Machine Interface (HMI) interactive cockpit layer:
/// 1. Tactile feedback hub (VResqHMIFeedback + VResqHMIButtonFX on all HUD buttons)
/// 2. OpenXR Hand Gesture & Opt-in Hand-Drive Controller (VResqHandGestureController)
/// 3. Always-visible HMI Hand & Drive Interlock Ribbon + non-blocking HMI Toast Banner
/// 4. Clickable ARM DRIVE & SPECIAL OPS quick buttons on BottomPanel_Controls
/// 5. Collapsible Special_Ops_Drawer with full special commands, gimbal presets, swarm formations, & hand gestures
/// </summary>
public static class VResqHMIBuilder
{
    public static void Configure(RescueRobotVRHUD hud)
    {
        if (hud == null) return;
        GameObject root = hud.gameObject;

        var hmi = root.GetComponent<VResqHMIFeedback>() ?? root.AddComponent<VResqHMIFeedback>();
        var hands = root.GetComponent<VResqHandGestureController>() ?? root.AddComponent<VResqHandGestureController>();
        var special = root.GetComponent<VResqSpecialCommandsPanel>() ?? root.AddComponent<VResqSpecialCommandsPanel>();

        foreach (string name in new[] { "HMI_Hand_Ribbon", "HMI_Toast_Banner", "Special_Ops_Drawer" })
        {
            var old = root.transform.Find(name);
            if (old != null) Object.DestroyImmediate(old.gameObject);
        }

        Color cPanel = new Color(0.015f, 0.045f, 0.085f, 0.96f);
        Color cCard = new Color(0.035f, 0.095f, 0.165f, 0.94f);
        Color cCyan = new Color(0.0f, 0.92f, 1.0f, 1.0f);
        Color cCyanDim = new Color(0.0f, 0.72f, 0.88f, 0.55f);
        Color cGreen = new Color(0.06f, 0.52f, 0.30f, 0.96f);
        Color cAmber = new Color(0.55f, 0.36f, 0.04f, 0.96f);
        Color cRed = new Color(0.68f, 0.08f, 0.14f, 0.96f);
        Color cBtn = new Color(0.05f, 0.22f, 0.34f, 0.96f);
        Color cSwarm = new Color(0.04f, 0.34f, 0.38f, 0.96f);
        Color cHand = new Color(0.16f, 0.18f, 0.42f, 0.96f);

        // 1. Upgrade Deadman_Pill on BottomPanel_Controls into clickable ARM DRIVE + SPECIAL OPS buttons
        Transform bottom = root.transform.Find("BottomPanel_Controls");
        if (bottom != null)
        {
            foreach (string oldBtn in new[] { "BTN_ARM_QUICK", "BTN_SPECIAL_OPS", "HMI_ARM_QUICK", "HMI_SPECIAL_OPS" })
            {
                var t = bottom.Find(oldBtn);
                if (t != null) Object.DestroyImmediate(t.gameObject);
            }
            var oldDeadman = bottom.Find("Deadman_Pill");
            if (oldDeadman != null) oldDeadman.gameObject.SetActive(false);

            // Clickable ARM / DISARM Safety Latch (left half of old Deadman_Pill)
            // Named HMI_* so RescueRobotVRHUD.WireAllInteractiveButtons() (BTN_/CFG_) never strips listeners.
            var armBtn = MakeButton(bottom, "HMI_ARM_QUICK", "DRIVE DISARMED\n[CLICK / L3 ARM]",
                new Vector2(-768f, 0f), new Vector2(162f, 64f), new Color(0.18f, 0.24f, 0.30f, 0.95f), 12);
            special.quickArmButton = armBtn;
            special.quickArmBackground = armBtn.GetComponent<Image>();
            special.quickArmLabel = armBtn.GetComponentInChildren<TextMeshProUGUI>();

            // Clickable SPECIAL OPS Drawer Trigger (right half of old Deadman_Pill)
            var opsBtn = MakeButton(bottom, "HMI_SPECIAL_OPS", "SPECIAL OPS\n& HANDS [O]",
                new Vector2(-598f, 0f), new Vector2(162f, 64f), new Color(0.05f, 0.36f, 0.46f, 0.96f), 13);
            special.quickOpsButton = opsBtn;
        }

        // 2. Always-Visible Thin HMI Hand & Interlock Ribbon (between TopBar +534 and Nav +569, non-blocking)
        var ribbon = Rect("HMI_Hand_Ribbon", root.transform, new Vector2(0f, 551f), new Vector2(1740f, 30f));
        var ribbonImg = ribbon.gameObject.AddComponent<Image>();
        ribbonImg.color = new Color(0.01f, 0.04f, 0.07f, 0.90f);
        ribbonImg.raycastTarget = false;

        special.handRibbonPoseText = Label(ribbon, "HANDS: STANDBY", new Vector2(-600f, 0f), new Vector2(500f, 26f), 14, cCyan, TextAlignmentOptions.MidlineLeft);
        special.handRibbonPendingText = Label(ribbon, "LAST HAND CMD: READY", new Vector2(-80f, 0f), new Vector2(440f, 26f), 13, Color.white, TextAlignmentOptions.MidlineCenter);

        var progBg = Rect("Ribbon_Prog_Bg", ribbon, new Vector2(215f, 0f), new Vector2(120f, 12f));
        var progBgImg = progBg.gameObject.AddComponent<Image>();
        progBgImg.color = new Color(0.08f, 0.18f, 0.24f, 0.95f);
        progBgImg.raycastTarget = false;
        var progFill = Rect("Ribbon_Prog_Fill", progBg, Vector2.zero, new Vector2(120f, 12f));
        var progFillImg = progFill.gameObject.AddComponent<Image>();
        progFillImg.color = cCyan;
        progFillImg.type = Image.Type.Filled;
        progFillImg.fillMethod = Image.FillMethod.Horizontal;
        progFillImg.fillAmount = 0f;
        progFillImg.raycastTarget = false;
        special.handRibbonProgressFill = progFillImg;

        special.handRibbonDriveText = Label(ribbon, "HMI: STANDBY", new Vector2(580f, 0f), new Vector2(560f, 26f), 14, new Color(0.9f, 0.96f, 1f), TextAlignmentOptions.MidlineRight);

        // 3. Non-Blocking Diegetic HMI Toast Banner (fades in on commands/gestures)
        var toast = Rect("HMI_Toast_Banner", root.transform, new Vector2(0f, 392f), new Vector2(760f, 66f));
        toast.localPosition = new Vector3(0f, 392f, -24f);
        var toastCg = toast.gameObject.AddComponent<CanvasGroup>();
        toastCg.blocksRaycasts = false;
        toastCg.interactable = false;
        toastCg.alpha = 0f;
        var toastBg = toast.gameObject.AddComponent<Image>();
        toastBg.color = new Color(0.02f, 0.08f, 0.14f, 0.94f);
        toastBg.raycastTarget = false;
        var toastBar = Rect("Toast_Accent", toast, new Vector2(-374f, 0f), new Vector2(10f, 66f)).gameObject.AddComponent<Image>();
        toastBar.color = cCyan;
        toastBar.raycastTarget = false;

        hmi.toastRoot = toast.gameObject;
        hmi.toastBackground = toastBg;
        hmi.toastAccentBar = toastBar;
        hmi.toastCanvasGroup = toastCg;
        hmi.toastTitleText = Label(toast, "HMI READY", new Vector2(12f, 14f), new Vector2(710f, 26f), 18, cCyan, TextAlignmentOptions.MidlineLeft);
        hmi.toastTitleText.fontStyle = FontStyles.Bold;
        hmi.toastDetailText = Label(toast, "", new Vector2(12f, -14f), new Vector2(710f, 24f), 14, Color.white, TextAlignmentOptions.MidlineLeft);
        toast.gameObject.SetActive(false);

        // 4. Collapsible Special Operations & Hand Gesture Command Drawer
        var drawer = Rect("Special_Ops_Drawer", root.transform, new Vector2(0f, 8f), new Vector2(1380f, 760f));
        drawer.localPosition = new Vector3(0f, 8f, -28f);
        var drawerImg = drawer.gameObject.AddComponent<Image>();
        drawerImg.color = cPanel;
        drawerImg.raycastTarget = true;
        AddBorder(drawer, new Vector2(1380f, 760f), cCyan, 2.5f);

        var title = Label(drawer, "V-RESQ HUMAN-MACHINE SPECIAL COMMANDS & HAND CONTROL CENTER", new Vector2(-120f, 342f), new Vector2(1060f, 40f), 22, cCyan, TextAlignmentOptions.MidlineLeft);
        title.fontStyle = FontStyles.Bold;
        MakeButton(drawer, "OPS_CLOSE", "CLOSE [O]", new Vector2(580f, 342f), new Vector2(160f, 42f), cRed, 15);

        var statusCard = Rect("Status_Card", drawer, new Vector2(0f, 282f), new Vector2(1320f, 56f));
        var statusImg = statusCard.gameObject.AddComponent<Image>();
        statusImg.color = cCard;
        statusImg.raycastTarget = false;
        special.liveStatusBanner = Label(statusCard, "TARGET: --  |  DRIVE: DISARMED", Vector2.zero, new Vector2(1280f, 48f), 15, Color.white, TextAlignmentOptions.MidlineLeft);

        // Row 1: Drive Interlock & Speed Presets
        Label(drawer, "1. DRIVE INTERLOCK, SAFETY & SPEED PRESETS", new Vector2(0f, 226f), new Vector2(1320f, 26f), 15, new Color(1f, 0.82f, 0.25f), TextAlignmentOptions.MidlineLeft);
        MakeButton(drawer, "OPS_ARM", "ARM DRIVE", new Vector2(-564f, 182f), new Vector2(180f, 52f), cGreen, 15);
        MakeButton(drawer, "OPS_DISARM", "DISARM", new Vector2(-376f, 182f), new Vector2(180f, 52f), cAmber, 15);
        MakeButton(drawer, "OPS_RESET_ESTOP", "RESET E-STOP", new Vector2(-188f, 182f), new Vector2(180f, 52f), cGreen, 14);
        MakeButton(drawer, "OPS_SWARM_ESTOP", "GLOBAL SWARM\nE-STOP", new Vector2(10f, 182f), new Vector2(200f, 52f), cRed, 13);
        MakeButton(drawer, "OPS_SPD_ECO", "ECO 45%", new Vector2(236f, 182f), new Vector2(150f, 52f), cBtn, 14);
        MakeButton(drawer, "OPS_SPD_NORM", "NORMAL 75%", new Vector2(398f, 182f), new Vector2(150f, 52f), cBtn, 14);
        MakeButton(drawer, "OPS_SPD_TURBO", "TURBO 100%", new Vector2(560f, 182f), new Vector2(150f, 52f), cAmber, 14);

        // Row 2: Special Robot Missions & Macros
        Label(drawer, "2. SPECIAL ROBOT MISSIONS & AUTONOMOUS MACROS", new Vector2(0f, 122f), new Vector2(1320f, 26f), 15, new Color(1f, 0.82f, 0.25f), TextAlignmentOptions.MidlineLeft);
        string[] actCodes = { "OPS_ACT_PATROL", "OPS_ACT_RADAR", "OPS_ACT_SCAN", "OPS_ACT_APPROACH", "OPS_ACT_BACKOUT", "OPS_ACT_RTH", "OPS_ACT_SAFESTOP" };
        string[] actLabels = { "AUTO-SURVEY", "RADAR SWEEP", "AREA SCAN", "APPROACH", "BACK OUT", "RETURN HOME", "SAFE STOP" };
        for (int i = 0; i < actCodes.Length; i++)
        {
            Color col = i == actCodes.Length - 1 ? cAmber : cBtn;
            MakeButton(drawer, actCodes[i], actLabels[i], new Vector2(-564f + i * 188f, 78f), new Vector2(178f, 52f), col, 14);
        }

        // Row 3: Camera Gimbal Quick-Aim & Hardware Specials
        Label(drawer, "3. CAMERA GIMBAL PRESETS & HARDWARE SPECIALS", new Vector2(0f, 18f), new Vector2(1320f, 26f), 15, new Color(1f, 0.82f, 0.25f), TextAlignmentOptions.MidlineLeft);
        MakeButton(drawer, "OPS_CAM_LEFT", "CAM LEFT 35", new Vector2(-564f, -26f), new Vector2(178f, 52f), cBtn, 14);
        MakeButton(drawer, "OPS_CAM_CENTER", "CAM CENTER 90", new Vector2(-376f, -26f), new Vector2(178f, 52f), cBtn, 14);
        MakeButton(drawer, "OPS_CAM_RIGHT", "CAM RIGHT 145", new Vector2(-188f, -26f), new Vector2(178f, 52f), cBtn, 14);
        MakeButton(drawer, "OPS_HORN_PULSE", "HORN PULSE", new Vector2(0f, -26f), new Vector2(178f, 52f), cAmber, 14);
        MakeButton(drawer, "OPS_OLED_NEXT", "OLED PAGE >", new Vector2(188f, -26f), new Vector2(178f, 52f), cBtn, 14);
        var aiBtn = MakeButton(drawer, "OPS_AI_TOGGLE", "AI DETECT: ON", new Vector2(376f, -26f), new Vector2(178f, 52f), cGreen, 14);
        special.aiToggleLabel = aiBtn.GetComponentInChildren<TextMeshProUGUI>();
        MakeButton(drawer, "OPS_MOTOR_TEST", "MOTOR PULSE", new Vector2(564f, -26f), new Vector2(178f, 52f), cAmber, 14);

        // Row 4: Swarm Formation Commands (Broadcast to Node 0 / 1 / 2)
        Label(drawer, "4. SWARM FORMATION COMMANDS (NODE ZERO / ONE / TWO)", new Vector2(0f, -86f), new Vector2(1320f, 26f), 15, new Color(1f, 0.82f, 0.25f), TextAlignmentOptions.MidlineLeft);
        MakeButton(drawer, "OPS_FORM_RELAY", "RELAY CHAIN", new Vector2(-495f, -130f), new Vector2(310f, 50f), cSwarm, 15);
        MakeButton(drawer, "OPS_FORM_WEDGE", "WEDGE FORMATION", new Vector2(-165f, -130f), new Vector2(310f, 50f), cSwarm, 15);
        MakeButton(drawer, "OPS_FORM_LINE", "LINE FORMATION", new Vector2(165f, -130f), new Vector2(310f, 50f), cSwarm, 15);
        MakeButton(drawer, "OPS_FORM_HOLD", "HOLD POSITION", new Vector2(495f, -130f), new Vector2(310f, 50f), cSwarm, 15);

        // Row 5: Hand Gesture Command Center & HMI Options
        Label(drawer, "5. HAND GESTURE COMMANDS & HUMAN-MACHINE FEEDBACK", new Vector2(0f, -188f), new Vector2(1320f, 26f), 15, cCyan, TextAlignmentOptions.MidlineLeft);
        var handCard = Rect("Hand_Diag_Card", drawer, new Vector2(0f, -228f), new Vector2(1320f, 44f));
        var handCardImg = handCard.gameObject.AddComponent<Image>();
        handCardImg.color = cCard;
        handCardImg.raycastTarget = false;
        special.drawerHandStatusText = Label(handCard, "HANDS: STANDBY | LAST: READY", new Vector2(-210f, 0f), new Vector2(860f, 34f), 14, cCyan, TextAlignmentOptions.MidlineLeft);

        var dProgBg = Rect("Drawer_Hand_Prog_Bg", handCard, new Vector2(430f, 0f), new Vector2(380f, 16f));
        var dProgBgImg = dProgBg.gameObject.AddComponent<Image>();
        dProgBgImg.color = new Color(0.08f, 0.18f, 0.24f, 0.95f);
        dProgBgImg.raycastTarget = false;
        var dProgFill = Rect("Drawer_Hand_Prog_Fill", dProgBg, Vector2.zero, new Vector2(380f, 16f));
        var dProgFillImg = dProgFill.gameObject.AddComponent<Image>();
        dProgFillImg.color = cCyan;
        dProgFillImg.type = Image.Type.Filled;
        dProgFillImg.fillMethod = Image.FillMethod.Horizontal;
        dProgFillImg.fillAmount = 0f;
        dProgFillImg.raycastTarget = false;
        special.drawerHandProgressFill = dProgFillImg;

        var bGest = MakeButton(drawer, "OPS_HAND_TOGGLE", "HAND GESTURES: ON", new Vector2(-495f, -282f), new Vector2(310f, 46f), cHand, 14);
        special.gestureToggleLabel = bGest.GetComponentInChildren<TextMeshProUGUI>();
        var bHDrive = MakeButton(drawer, "OPS_HAND_DRIVE_TOGGLE", "PINCH HAND-DRIVE: OFF", new Vector2(-165f, -282f), new Vector2(310f, 46f), cHand, 14);
        special.handDriveToggleLabel = bHDrive.GetComponentInChildren<TextMeshProUGUI>();
        var bSound = MakeButton(drawer, "OPS_SOUND_TOGGLE", "HMI SOUND: ON", new Vector2(165f, -282f), new Vector2(310f, 46f), cBtn, 14);
        special.soundToggleLabel = bSound.GetComponentInChildren<TextMeshProUGUI>();
        var bHaptic = MakeButton(drawer, "OPS_HAPTIC_TOGGLE", "HAPTICS: ON", new Vector2(495f, -282f), new Vector2(310f, 46f), cBtn, 14);
        special.hapticsToggleLabel = bHaptic.GetComponentInChildren<TextMeshProUGUI>();

        // Clickable Hand Gesture Shortcuts (usable via VR Ray/Poke, Mouse, or XR Hand Dwell)
        string[] gCodes = { "OPS_GEST_ESTOP", "OPS_GEST_STOP", "OPS_GEST_ARM", "OPS_GEST_RADAR", "OPS_GEST_NODE", "OPS_GEST_HORN" };
        string[] gLabels = { "BOTH PALMS\nE-STOP", "OPEN PALM\nSAFE STOP", "R-THUMB UP\nARM / RESET", "R-PEACE\nRADAR SWEEP", "L-PEACE\nNEXT NODE", "L-POINT\nHORN PULSE" };
        for (int i = 0; i < gCodes.Length; i++)
        {
            Color col = i == 0 ? cRed : (i == 2 ? cGreen : cHand);
            MakeButton(drawer, gCodes[i], gLabels[i], new Vector2(-550f + i * 220f, -340f), new Vector2(208f, 48f), col, 13);
        }

        special.drawerRoot = drawer.gameObject;
        drawer.gameObject.SetActive(false);

        // Attach tactile HMI button FX to all interactive buttons under HUD root
        hmi.AttachButtonEffectsUnder(root.transform);

        // Ensure OperationsPanel liveSections excludes our non-blocking toast and keeps drawer clean
        var opsPanel = root.GetComponent<VResQ.Core.VResqOperationsPanel>();
        if (opsPanel != null && opsPanel.liveSections != null)
        {
            var list = new System.Collections.Generic.List<GameObject>(opsPanel.liveSections);
            if (!list.Contains(ribbon.gameObject)) list.Add(ribbon.gameObject);
            opsPanel.liveSections = list.ToArray();
            EditorUtility.SetDirty(opsPanel);
        }

        // Ensure all new Button targetGraphics have raycastTarget = true
        foreach (var sel in root.GetComponentsInChildren<Selectable>(true))
        {
            if (sel.targetGraphic != null) sel.targetGraphic.raycastTarget = true;
        }

        EditorUtility.SetDirty(hmi);
        EditorUtility.SetDirty(hands);
        EditorUtility.SetDirty(special);
        EditorUtility.SetDirty(hud);
    }

    private static RectTransform Rect(string name, Transform parent, Vector2 pos, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        return rt;
    }

    private static TextMeshProUGUI Label(Transform parent, string text, Vector2 pos, Vector2 size, float fontSize, Color color, TextAlignmentOptions align)
    {
        var rt = Rect("Lbl_" + text.Substring(0, System.Math.Min(16, text.Length)), parent, pos, size);
        var tmp = rt.gameObject.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.color = color;
        tmp.alignment = align;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.overflowMode = TextOverflowModes.Ellipsis;
        tmp.raycastTarget = false;
        return tmp;
    }

    private static Button MakeButton(Transform parent, string name, string labelText, Vector2 pos, Vector2 size, Color bg, float fontSize)
    {
        var rt = Rect(name, parent, pos, size);
        var img = rt.gameObject.AddComponent<Image>();
        img.color = bg;
        img.raycastTarget = true;
        var btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        var colors = btn.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.18f, 1.24f, 1.28f, 1f);
        colors.pressedColor = new Color(0.82f, 0.90f, 0.95f, 1f);
        btn.colors = colors;

        var lbl = Label(rt, labelText, Vector2.zero, new Vector2(size.x - 10f, size.y - 6f), fontSize, Color.white, TextAlignmentOptions.Center);
        lbl.fontStyle = FontStyles.Bold;
        lbl.textWrappingMode = TextWrappingModes.Normal;
        return btn;
    }

    private static void AddBorder(Transform parent, Vector2 size, Color color, float thickness)
    {
        MakeEdge(parent, "B_Top", new Vector2(0f, size.y * 0.5f), new Vector2(size.x, thickness), color);
        MakeEdge(parent, "B_Bot", new Vector2(0f, -size.y * 0.5f), new Vector2(size.x, thickness), color);
        MakeEdge(parent, "B_Left", new Vector2(-size.x * 0.5f, 0f), new Vector2(thickness, size.y), color);
        MakeEdge(parent, "B_Right", new Vector2(size.x * 0.5f, 0f), new Vector2(thickness, size.y), color);
    }

    private static void MakeEdge(Transform parent, string name, Vector2 pos, Vector2 size, Color color)
    {
        var img = Rect(name, parent, pos, size).gameObject.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
    }
}
