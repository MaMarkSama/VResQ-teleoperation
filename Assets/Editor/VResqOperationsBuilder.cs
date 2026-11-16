using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using VResQ.Core;

public static class VResqOperationsBuilder
{
    public static void Configure(RescueRobotVRHUD hud)
    {
        var view = hud.GetComponent<VResqOperationsPanel>() ?? hud.gameObject.AddComponent<VResqOperationsPanel>();
        ConfigureSettings(hud);
        if (hud.GetComponent<VResQ.Control.VResqQuestInput>() == null) hud.gameObject.AddComponent<VResQ.Control.VResqQuestInput>();
        foreach (string name in new[] { "Operations_Navigation", "Operations_Data", "Operations_Feedback" })
        {
            var old = hud.transform.Find(name);
            if (old != null) Object.DestroyImmediate(old.gameObject);
        }
        var live = new List<GameObject>();
        foreach (Transform child in hud.transform)
            if (child.name != "Curved_Surround_Screen" && child.name != "Video_Data_Controls") live.Add(child.gameObject);
        view.liveSections = live.ToArray();
        var feedback = hud.GetComponent<VResqUIFeedback>() ?? hud.gameObject.AddComponent<VResqUIFeedback>();
        feedback.output = Text(hud.transform, "", new Vector2(0, -656), new Vector2(1740, 36), 20);
        feedback.output.name = "Operations_Feedback";
        var navigation = Rect("Operations_Navigation", hud.transform, new Vector2(0, 600), new Vector2(1740, 62));
        navigation.gameObject.AddComponent<Image>().color = new Color(.015f, .045f, .055f, .98f);
        view.tabs = new Button[6];
        string[] names = { "Live", "Telemetry", "Protocol", "Swarm", "System", "Controls" };
        for (int i = 0; i < names.Length; i++) view.tabs[i] = Button(navigation, names[i], -740 + i * 245, 0, 230);
        Button(navigation, "Settings", 740, 0, 220);
        // Bind this button persistently because scene reconstruction runs outside Play mode.
        var settingsButton = navigation.Find("Settings").GetComponent<Button>();
        UnityEditor.Events.UnityEventTools.AddPersistentListener(settingsButton.onClick, hud.ToggleSettingsModal);
        var panel = Rect("Operations_Data", hud.transform, Vector2.zero, new Vector2(1740, 1020));
        panel.localPosition = new Vector3(0, 0, -8);
        panel.gameObject.AddComponent<Image>().color = new Color(.015f, .04f, .055f, .99f);
        view.panel = panel.gameObject;
        view.heading = Text(panel, "Telemetry", new Vector2(-570, 450), new Vector2(480, 65), 36);
        view.summary = Text(panel, "Waiting for response", new Vector2(0, 350), new Vector2(1610, 92), 24);
        view.status = Text(panel, "No payload", new Vector2(0, 275), new Vector2(1610, 40), 18);
        var viewport = Rect("Payload_Viewport", panel, new Vector2(-15, -100), new Vector2(1590, 600));
        viewport.gameObject.AddComponent<Image>().color = new Color(.025f, .065f, .075f);
        viewport.gameObject.AddComponent<RectMask2D>();
        view.body = Text(viewport, "", Vector2.zero, new Vector2(1540, 720), 26);
        var body = view.body.rectTransform;
        body.anchorMin = new Vector2(0, 1);
        body.anchorMax = new Vector2(1, 1);
        body.pivot = new Vector2(.5f, 1);
        body.anchoredPosition = Vector2.zero;
        body.sizeDelta = new Vector2(-40, 720);
        view.body.richText = false;
        view.body.overflowMode = TextOverflowModes.Overflow;
        view.body.textWrappingMode = TextWrappingModes.Normal;
        view.body.alignment = TextAlignmentOptions.TopLeft;
        view.scroll = panel.gameObject.AddComponent<ScrollRect>();
        view.scroll.viewport = viewport;
        view.scroll.content = body;
        view.scroll.horizontal = false;
        view.scroll.movementType = ScrollRect.MovementType.Clamped;
        view.scroll.scrollSensitivity = 45;
        view.refreshButton = Button(panel, "Refresh", -650, -460, 210);
        view.exportButton = Button(panel, "Save payload", -380, -460, 240);
        view.pageUpButton = Button(panel, "Page up", 360, -460, 220);
        view.pageDownButton = Button(panel, "Page down", 650, -460, 220);
        view.restartXrButton = Button(panel, "Restart XR", -65, -460, 250);
        view.rawButton = Button(panel, "Raw JSON", -65, -460, 250);
        view.controlsPanel = VResqControlsBuilder.Configure(panel);
        view.controlsPanel.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, -30);
        var tools = Rect("Swarm_Tools", panel, new Vector2(0, 225), new Vector2(1610, 52));
        view.swarmTools = tools.gameObject;
        view.nodeSelector = VResqControlsBuilder.Dropdown(tools, "Node selector", new Vector2(-240, 0), 1010);
        view.connectNodeButton = Button(tools, "Connect node", 400, 0, 240);
        view.connectNodeButton.interactable = false;
        view.scanButton = Button(tools, "Rescan LAN", 675, 0, 240);
        var plotBackground = Rect("Server_Pose_Map", panel, new Vector2(480, 350), new Vector2(600, 170));
        plotBackground.gameObject.AddComponent<Image>().color = new Color(.05f, .13f, .14f);
        var plot = Rect("Positions", plotBackground, Vector2.zero, new Vector2(570, 130));
        plot.gameObject.AddComponent<CanvasRenderer>();
        view.swarmPlot = plot.gameObject.AddComponent<VResqSwarmPlot>();
        view.swarmPlot.raycastTarget = false;
        // Keep the background on the same visibility switch as the plot.
        plotBackground.GetComponent<Image>().enabled = false;
        Text(plot, "Server poses (unvalidated) / green live / amber sim", new Vector2(0, 75), new Vector2(590, 30), 18);
        view.controlsPanel.SetActive(false);
        view.swarmTools.SetActive(false);
        view.swarmPlot.gameObject.SetActive(false);
        view.rawButton.gameObject.SetActive(false);
        panel.gameObject.SetActive(false);
        hud.hudDensity = RescueRobotVRHUD.HudDensityMode.TacticalExpert;
        EditorUtility.SetDirty(view);
        EditorUtility.SetDirty(hud);
    }

    private static void ConfigureSettings(RescueRobotVRHUD hud)
    {
        if (hud.settingsModalRoot == null) return;
        hud.settingsModalRoot.transform.localScale = Vector3.one * 1.45f;
        var labels = new Dictionary<string, string>
        {
            ["CFG_CLOSE"] = "CLOSE",
            ["CFG_EXT_TOGGLE"] = "EXTERNAL / ROBOT VIEW",
            ["CFG_VIEW_CYCLE"] = "CAMERA ARC 360 / 220 / 180",
            ["CFG_THEME_CYCLE"] = "HUD THEME",
            ["CFG_RADIUS_MINUS"] = "DOME -0.2m",
            ["CFG_RADIUS_PLUS"] = "DOME +0.2m",
            ["CFG_RECENTER"] = "RECENTER",
            ["CFG_SAVE"] = "SAVE PROFILE",
            ["CFG_RESET"] = "RESET PROFILE"
        };
        foreach (var button in hud.settingsModalRoot.GetComponentsInChildren<Button>(true))
            if (labels.TryGetValue(button.name, out string text))
                button.GetComponentInChildren<TextMeshProUGUI>().text = text;
        foreach (var label in hud.settingsModalRoot.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            if (label.name == "Modal_Header") label.text = "V-RESQ OPERATOR SETTINGS";
            if (label.name == "Modal_Footer") label.text = "";
        }
    }

    private static RectTransform Rect(string name, Transform parent, Vector2 position, Vector2 size)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        return rect;
    }

    private static TextMeshProUGUI Text(Transform parent, string text, Vector2 position, Vector2 size, float fontSize)
    {
        var label = Rect(text.Length == 0 ? "Payload" : text, parent, position, size).gameObject.AddComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = fontSize;
        label.color = new Color(.9f, .96f, .97f);
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Ellipsis;
        label.raycastTarget = false;
        return label;
    }

    private static Button Button(Transform parent, string name, float x, float y, float width)
    {
        var root = Rect(name, parent, new Vector2(x, y), new Vector2(width, 52));
        var image = root.gameObject.AddComponent<Image>();
        image.color = new Color(.05f, .12f, .17f);
        var button = root.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        Text(root, name, Vector2.zero, new Vector2(width - 20, 48), 24).alignment = TextAlignmentOptions.Center;
        return button;
    }
}
