using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VResQ.Control;

public static class VResqControlsBuilder
{
    public static GameObject Configure(Transform parent)
    {
        var root = Rect("Operator_Controls", parent, Vector2.zero, new Vector2(1610, 740));
        var control = root.gameObject.AddComponent<VResqControlPanel>();
        control.speed = Slider(root, "Speed limit", -420, 215, .2f, 1);
        control.deadzone = Slider(root, "Stick deadzone", -420, 95, .05f, .5f);
        control.steering = Slider(root, "Steering gain", -420, -25, .2f, 1.5f);
        control.servo = Slider(root, "Servo pan", -420, -145, 20, 160);
        control.grip = Toggle(root, "Hold left grip to drive", -420, -235);
        control.invertDrive = Toggle(root, "Invert forward / reverse", -420, -295);
        control.invertSteering = Toggle(root, "Invert steering", -420, -355);
        control.mapping = new TMP_Dropdown[4];
        string[] names = { "A", "X", "Y", "Right stick click" };
        for (int i = 0; i < 4; i++)
        {
            Label(root, names[i], new Vector2(430, 250 - i * 110), new Vector2(650, 38));
            control.mapping[i] = Dropdown(root, "Mapping_" + i, new Vector2(430, 200 - i * 110), 650);
        }
        control.arm = Button(root, "Arm and return Live", new Vector2(400, -255), 660);
        control.stop = Button(root, "E-STOP", new Vector2(220, -330), 300);
        control.stop.image.color = new Color(.65f, .06f, .12f);
        control.reset = Button(root, "Reset E-stop", new Vector2(580, -330), 300);
        control.save = Button(root, "Save profile", new Vector2(400, -445), 660);
        control.headsetTest = Button(root, "Headset-only test", new Vector2(-420, -445), 680);
        control.live = Label(root, "DISARMED", new Vector2(0, 365), new Vector2(1610, 138));
        control.live.fontSize = 23;
        return root.gameObject;
    }

    public static TMP_Dropdown Dropdown(Transform parent, string name, Vector2 position, float width)
    {
        var root = TMP_DefaultControls.CreateDropdown(new TMP_DefaultControls.Resources());
        root.name = name;
        var rect = root.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(width, 52);
        var dropdown = root.GetComponent<TMP_Dropdown>();
        root.AddComponent<VResQ.Core.VResqDropdownXR>();
        dropdown.captionText.fontSize = 24;
        dropdown.itemText.fontSize = 24;
        dropdown.captionText.richText = false;
        dropdown.itemText.richText = false;
        dropdown.template.sizeDelta = new Vector2(0, 290);
        dropdown.template.Find("Viewport/Content/Item").GetComponent<RectTransform>().sizeDelta = new Vector2(0, 48);
        foreach (var text in root.GetComponentsInChildren<TMP_Text>(true)) text.color = new Color(.03f, .12f, .15f);
        return dropdown;
    }

    public static Button Button(Transform parent, string name, Vector2 position, float width)
    {
        var root = Rect(name, parent, position, new Vector2(width, 54));
        var image = root.gameObject.AddComponent<Image>();
        image.color = new Color(.035f, .3f, .33f);
        var button = root.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        Label(root, name, Vector2.zero, new Vector2(width - 24, 48)).alignment = TextAlignmentOptions.Center;
        return button;
    }

    private static Toggle Toggle(Transform parent, string name, float x, float y)
    {
        var root = Rect(name, parent, new Vector2(x, y), new Vector2(680, 48));
        var background = Rect("Checkbox", root, new Vector2(-310, 0), new Vector2(32, 32));
        var image = background.gameObject.AddComponent<Image>();
        image.color = new Color(.15f, .3f, .33f);
        var mark = Rect("Mark", background, Vector2.zero, new Vector2(22, 22)).gameObject.AddComponent<Image>();
        mark.color = new Color(.1f, 1f, .7f);
        mark.raycastTarget = false;
        var toggle = root.gameObject.AddComponent<Toggle>();
        toggle.targetGraphic = image; toggle.graphic = mark;
        Label(root, name, new Vector2(30, 0), new Vector2(580, 48));
        return toggle;
    }

    private static Slider Slider(Transform parent, string name, float x, float y, float min, float max)
    {
        Label(parent, name, new Vector2(x, y + 40), new Vector2(680, 38));
        var root = Rect(name, parent, new Vector2(x, y), new Vector2(680, 44));
        root.gameObject.AddComponent<Image>().color = new Color(.12f, .25f, .27f);
        var handle = Rect("Handle", root, Vector2.zero, new Vector2(28, 44));
        var image = handle.gameObject.AddComponent<Image>();
        image.color = new Color(.2f, 1, .7f);
        var slider = root.gameObject.AddComponent<Slider>();
        slider.minValue = min; slider.maxValue = max; slider.targetGraphic = image; slider.handleRect = handle;
        return slider;
    }

    private static RectTransform Rect(string name, Transform parent, Vector2 position, Vector2 size)
    {
        var root = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        root.SetParent(parent, false); root.anchoredPosition = position; root.sizeDelta = size;
        return root;
    }

    private static TextMeshProUGUI Label(Transform parent, string text, Vector2 position, Vector2 size)
    {
        var label = Rect("Label", parent, position, size).gameObject.AddComponent<TextMeshProUGUI>();
        label.text = text; label.fontSize = 26; label.color = new Color(.9f, .97f, .97f);
        label.alignment = TextAlignmentOptions.MidlineLeft; label.raycastTarget = false;
        return label;
    }
}
