using UnityEngine;

public class HUDTelemetrySimulator : MonoBehaviour
{
    [Tooltip("Disabled in LIVE-HW production mode so fake ping-pong values never overwrite real robot telemetry.")]
    public bool enableFakeSimulation = false;

    private RescueRobotVRHUD hud;
    private float timer = 0f;

    void Awake()
    {
        if (!enableFakeSimulation)
        {
            enabled = false;
        }
    }

    void Start()
    {
        if (!enableFakeSimulation)
        {
            enabled = false;
            return;
        }
        hud = GetComponent<RescueRobotVRHUD>();
    }

    void Update()
    {
        if (!enableFakeSimulation || hud == null) return;
        timer += Time.deltaTime;

        // จำลอง Deadman Switch
        bool simulatedGrip = (Mathf.PingPong(timer, 4f) > 1.5f);
        if (hud.deadmanStatusText) hud.deadmanStatusText.text = simulatedGrip ? "ACTIVE" : "STANDBY";
        if (hud.deadmanIndicatorDot) hud.deadmanIndicatorDot.color = simulatedGrip ? Color.green : Color.red;

        // จำลอง Speed และ Motor L/R
        float speed = Mathf.PingPong(timer * 20f, 100f);
        if (hud.speedPercentText) hud.speedPercentText.text = $"SPD {speed:F0}%";
        if (hud.motorLeftText) hud.motorLeftText.text = $"L +{(int)(speed * 1.8f)}";
        if (hud.motorRightText) hud.motorRightText.text = $"R +{(int)(speed * 1.75f)}";

        // จำลองเข็มทิศ
        float heading = (timer * 15f) % 360f;
        if (hud.compassHeadingText) hud.compassHeadingText.text = $"{heading:000}° N";

        // จำลอง Pitch & Roll
        float roll = Mathf.Sin(timer * 1.5f) * 6f;
        float pitch = Mathf.Cos(timer * 1.2f) * 4f;
        if (hud.pitchText) hud.pitchText.text = $"PITCH {pitch:+0;-#}°";
        if (hud.rollText) hud.rollText.text = $"ROLL {roll:+0;-#}°";
        if (hud.imuHorizonBall)
        {
            hud.imuHorizonBall.localEulerAngles = new Vector3(0, 0, -roll);
            hud.imuHorizonBall.anchoredPosition = new Vector2(0, pitch * 2.5f);
        }

        // จำลองระยะ Ultrasonic
        float distance = 40f + Mathf.PingPong(timer * 10f, 60f);
        if (hud.ultrasonicDistanceText) hud.ultrasonicDistanceText.text = $"{distance:F0} cm";
    }
}