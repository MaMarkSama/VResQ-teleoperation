using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using VResQ.Core;

namespace VResQ.Robotics
{
    /// <summary>
    /// Unity 3D Bare-Board Software-in-the-Loop (SIL) Physics & Sensor Simulator.
    /// When the physical Jetson / Raspberry Pi boards (172.20.10.4, 172.20.10.2, 172.20.10.3)
    /// are running without a physical ESP32 attached ("บอร์ดเปล่ายังไม่มี esp32"),
    /// attach this script to each virtual robot prefab in a Unity 3D rescue arena.
    ///
    /// Closed-Loop SIL Flow (10-20 Hz):
    /// 1. Unity raycasts ultrasonic distance from the camera/sensor head, reads transform pose
    ///    and IMU orientation, and checks proximity to FireZone / Survivor tags in the 3D scene.
    /// 2. Sends POST http://<node-ip>:8080/api/sim/inject with SimInjectPayload.
    /// 3. Physical Linux board runs its real AnalyticsEngine, SafetyController, and SwarmCoordinator,
    ///    and returns { left_pwm, right_pwm, servo_deg } in SimInjectResponse.
    /// 4. This component drives the Unity 3D robot chassis and servo mount according to the
    ///    returned actuator commands!
    /// </summary>
    public class VResqBareBoardSimulator : MonoBehaviour
    {
        [Header("Target Physical Board (Bare Board without ESP32)")]
        public int robotId = 1;
        public string boardEndpoint = "http://172.20.10.4:8000";
        [Range(2f, 20f)]
        public float syncRateHz = 10f;

        [Header("Robot 3D Transforms & Raycast Sensor")]
        public Transform chassisTransform;
        public Transform sensorHeadTransform;
        public LayerMask obstacleLayers = ~0;
        public float maxUltrasonicRangeMeters = 2.6f;

        [Header("Differential Drive Kinematics")]
        public float maxLinearSpeedMetersPerSec = 0.55f;
        public float wheelTrackWidthMeters = 0.24f;

        [Header("Simulated Environmental Zones (Optional)")]
        public float ambientTempC = 29.8f;
        public float ambientHumidity = 56.0f;
        public float ambientPressureHpa = 1011.2f;
        public Transform[] fireHazardSources;
        public Transform[] survivorTargets;

        [Header("Live Closed-Loop Actuator Feedback (Read-Only)")]
        public int receivedLeftPwm = 0;
        public int receivedRightPwm = 0;
        public int receivedServoDeg = 90;
        public string activeMode = "UNITY-SIM";
        public string activeAction = "idle";

        private bool _running;

        private void Awake()
        {
            if (chassisTransform == null) chassisTransform = transform;
            if (sensorHeadTransform == null) sensorHeadTransform = transform;
        }

        private void OnEnable()
        {
            _running = true;
            StartCoroutine(SilSimulationLoop());
        }

        private void OnDisable()
        {
            _running = false;
            StopAllCoroutines();
        }

        private void Update()
        {
            ApplyDifferentialDriveKinematics(Time.deltaTime);
            ApplyServoHeadRotation(Time.deltaTime);
        }

        private void ApplyDifferentialDriveKinematics(float dt)
        {
            float vLeft = Mathf.Clamp(receivedLeftPwm / 255f, -1f, 1f) * maxLinearSpeedMetersPerSec;
            float vRight = Mathf.Clamp(receivedRightPwm / 255f, -1f, 1f) * maxLinearSpeedMetersPerSec;
            float vCenter = 0.5f * (vLeft + vRight);
            float omegaRad = (vRight - vLeft) / Mathf.Max(0.08f, wheelTrackWidthMeters);

            chassisTransform.Translate(Vector3.forward * (vCenter * dt), Space.Self);
            chassisTransform.Rotate(Vector3.up, omegaRad * Mathf.Rad2Deg * dt, Space.Self);
        }

        private void ApplyServoHeadRotation(float dt)
        {
            if (sensorHeadTransform == null || sensorHeadTransform == chassisTransform) return;
            float targetYawOffset = -(receivedServoDeg - 90f);
            Quaternion targetLocalRot = Quaternion.Euler(0f, targetYawOffset, 0f);
            sensorHeadTransform.localRotation = Quaternion.Slerp(sensorHeadTransform.localRotation, targetLocalRot, dt * 10f);
        }

        private float MeasureUltrasonicCm()
        {
            Vector3 origin = sensorHeadTransform.position + Vector3.up * 0.08f;
            Vector3 dir = sensorHeadTransform.forward;
            if (Physics.Raycast(origin, dir, out RaycastHit hit, maxUltrasonicRangeMeters, obstacleLayers))
            {
                return Mathf.Clamp(hit.distance * 100f, 3f, maxUltrasonicRangeMeters * 100f);
            }
            return maxUltrasonicRangeMeters * 100f;
        }

        private float ComputeLocalTemperatureC()
        {
            float temp = ambientTempC;
            if (fireHazardSources != null)
            {
                foreach (Transform fire in fireHazardSources)
                {
                    if (fire == null) continue;
                    float dist = Vector3.Distance(chassisTransform.position, fire.position);
                    if (dist < 3.0f)
                    {
                        temp += Mathf.Lerp(26.0f, 0f, dist / 3.0f);
                    }
                }
            }
            return temp;
        }

        private AIDetection[] DetectSimulatedSurvivors()
        {
            if (survivorTargets == null || survivorTargets.Length == 0)
                return new AIDetection[0];

            var detections = new System.Collections.Generic.List<AIDetection>();
            foreach (Transform target in survivorTargets)
            {
                if (target == null) continue;
                Vector3 toTarget = target.position - sensorHeadTransform.position;
                float dist = toTarget.magnitude;
                if (dist <= 3.5f)
                {
                    float angle = Vector3.Angle(sensorHeadTransform.forward, toTarget.normalized);
                    if (angle <= 42f)
                    {
                        detections.Add(new AIDetection
                        {
                            id = $"unity-surv-{robotId}",
                            label = "person",
                            score = Mathf.Clamp(0.95f - (dist * 0.06f), 0.72f, 0.96f),
                            box = new float[] { 0.35f, 0.22f, 0.65f, 0.84f },
                            source = "unity-sil"
                        });
                    }
                }
            }
            return detections.ToArray();
        }

        private IEnumerator SilSimulationLoop()
        {
            string url = boardEndpoint.TrimEnd('/') + "/api/sim/inject";
            while (_running)
            {
                Vector3 pos = chassisTransform.position;
                Vector3 euler = chassisTransform.eulerAngles;
                float headingDeg = euler.y;
                float pitchDeg = NormalizeAngle180(euler.x);
                float rollDeg = NormalizeAngle180(euler.z);

                SimInjectPayload payload = new SimInjectPayload
                {
                    pose_x = pos.x,
                    pose_y = pos.z,
                    pose_heading = headingDeg,
                    ultrasonic_cm = MeasureUltrasonicCm(),
                    temp_c = ComputeLocalTemperatureC(),
                    humidity = ambientHumidity,
                    pressure_hpa = ambientPressureHpa,
                    pitch_deg = pitchDeg,
                    roll_deg = rollDeg,
                    yaw_deg = headingDeg,
                    ax_g = 0.0f,
                    ay_g = 0.0f,
                    az_g = 1.0f,
                    scenario = "normal",
                    ai_detections = DetectSimulatedSurvivors()
                };

                byte[] body = Encoding.UTF8.GetBytes(payload.ToJson());
                using (UnityWebRequest req = new UnityWebRequest(url, "POST"))
                {
                    req.uploadHandler = new UploadHandlerRaw(body);
                    req.downloadHandler = new DownloadHandlerBuffer();
                    req.SetRequestHeader("Content-Type", "application/json");
                    req.timeout = 2;
                    yield return req.SendWebRequest();

                    if (req.result == UnityWebRequest.Result.Success)
                    {
                        SimInjectResponse resp = JsonUtility.FromJson<SimInjectResponse>(req.downloadHandler.text);
                        if (resp != null && resp.ok && resp.actuators != null)
                        {
                            activeMode = resp.mode;
                            receivedLeftPwm = resp.actuators.left_pwm;
                            receivedRightPwm = resp.actuators.right_pwm;
                            receivedServoDeg = resp.actuators.servo_deg;
                            activeAction = resp.actuators.action;
                        }
                    }
                }

                yield return new WaitForSeconds(1f / Mathf.Max(1f, syncRateHz));
            }
        }

        private static float NormalizeAngle180(float angle)
        {
            angle %= 360f;
            if (angle > 180f) angle -= 360f;
            if (angle < -180f) angle += 360f;
            return angle;
        }
    }
}
