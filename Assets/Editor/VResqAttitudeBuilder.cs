using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VResQ.Video;

public static class VResqAttitudeBuilder
{
    public static void Configure(RescueRobotVRHUD hud)
    {
        if (hud.imuHorizonBall == null) return;
        var mask = hud.imuHorizonBall.parent as RectTransform;
        Object.DestroyImmediate(hud.imuHorizonBall.gameObject);
        var oldLabel = mask.Find("IMU_Status");
        if (oldLabel != null) Object.DestroyImmediate(oldLabel.gameObject);
        mask.sizeDelta = new Vector2(148, 148);
        var instrument = mask.GetComponent<VResqAttitudeDisplay>() ?? mask.gameObject.AddComponent<VResqAttitudeDisplay>();
        var ball = Rect("IMU_Horizon", mask, Vector2.zero, new Vector2(480, 480));
        instrument.horizon = ball;
        instrument.horizonVisibility = ball.gameObject.AddComponent<CanvasGroup>();
        Image(ball, "Sky", new Vector2(0, 120), new Vector2(480, 240), new Color(.06f, .34f, .45f));
        Image(ball, "Ground", new Vector2(0, -120), new Vector2(480, 240), new Color(.22f, .32f, .23f));
        Image(ball, "Horizon", Vector2.zero, new Vector2(480, 2), Color.white);
        for (int degrees = -30; degrees <= 30; degrees += 10)
        {
            if (degrees == 0) continue;
            float y = -degrees * 1.8f;
            Image(ball, "Pitch_" + degrees, new Vector2(0, y), new Vector2(46, 1), new Color(1, 1, 1, .7f));
        }
        foreach (string marker in new[] { "Aircraft_Left", "Aircraft_Right", "Aircraft_Center" })
        {
            var old = mask.Find(marker);
            if (old != null) Object.DestroyImmediate(old.gameObject);
        }
        Image(mask, "Aircraft_Left", new Vector2(-30, 0), new Vector2(32, 3), new Color(1f, .8f, .25f));
        Image(mask, "Aircraft_Right", new Vector2(30, 0), new Vector2(32, 3), new Color(1f, .8f, .25f));
        Image(mask, "Aircraft_Center", Vector2.zero, new Vector2(4, 4), new Color(1f, .8f, .25f));
        var label = Rect("IMU_Status", mask, new Vector2(0, -60), new Vector2(142, 24)).gameObject.AddComponent<TextMeshProUGUI>();
        label.fontSize = 12;
        label.alignment = TextAlignmentOptions.Center;
        label.text = "IMU UNAVAILABLE";
        label.raycastTarget = false;
        instrument.stateLabel = label;
        instrument.pitchLabel = hud.pitchText;
        instrument.rollLabel = hud.rollText;
        instrument.yawLabel = hud.yawText;
        hud.imuHorizonBall = ball;
    }

    private static RectTransform Rect(string name, Transform parent, Vector2 position, Vector2 size)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        return rect;
    }

    private static void Image(Transform parent, string name, Vector2 position, Vector2 size, Color color)
    {
        var image = Rect(name, parent, position, size).gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
    }
}
