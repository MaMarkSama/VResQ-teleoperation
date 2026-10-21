using UnityEngine;
using VResQ.Core;

[ExecuteAlways]
public class TacticalHUDFollow : MonoBehaviour
{
    [Header("Camera Reference")]
    [Tooltip("Main Camera inside XR Origin")]
    public Transform targetCamera;

    [Header("Follow Distance & Height")]
    [Tooltip("Distance from user eyes (meters)")]
    public float followDistance = 1.45f;

    [Tooltip("Vertical offset from eye level (meters)")]
    public float heightOffset = -0.04f;

    [Header("Smooth Dynamics")]
    public float positionSmoothTime = 0.14f;
    public float rotationSmoothTime = 0.22f;

    [Header("Deadzone (Free Look Angle)")]
    public float deadzoneAngle = 35.0f;

    [Header("Lock Mode")]
    public bool enableSmoothFollow = true;

    private Vector3 currentVelocity;
    private Quaternion targetRotation;
    private float currentYawVelocity;

    void OnEnable()
    {
        if (targetCamera == null && Camera.main != null)
        {
            targetCamera = Camera.main.transform;
        }
        SyncFromConfig();
        if (targetCamera != null)
        {
            ResetHUDOrientation();
        }
    }

    void Start()
    {
        if (targetCamera == null && Camera.main != null)
        {
            targetCamera = Camera.main.transform;
        }

        SyncFromConfig();
        if (VResqFrameworkConfig.Instance != null)
        {
            VResqFrameworkConfig.Instance.OnConfigChanged.AddListener(SyncFromConfig);
        }

        if (targetCamera != null)
        {
            ResetHUDOrientation();
        }
    }

    public void SyncFromConfig()
    {
        var cfg = VResqFrameworkConfig.Instance;
        if (cfg != null)
        {
            followDistance = cfg.Profile.hudDistance;
            heightOffset = cfg.Profile.hudHeightOffset;
            enableSmoothFollow = cfg.Profile.smoothFollowHead;
            deadzoneAngle = cfg.Profile.followDeadzoneAngle;
            float s = Mathf.Clamp(cfg.Profile.hudScale, 0.0004f, 0.002f);
            transform.localScale = new Vector3(s, s, s);
        }
    }

    void LateUpdate()
    {
        if (targetCamera == null)
        {
            if (Camera.main != null) targetCamera = Camera.main.transform;
            else return;
        }

        if (!Application.isPlaying)
        {
            ResetHUDOrientation();
            return;
        }

        if (!enableSmoothFollow)
        {
            return;
        }

        Vector3 camForward = targetCamera.forward;
        camForward.y = 0;

        if (camForward.sqrMagnitude > 0.001f)
        {
            camForward.Normalize();
            float targetYaw = Quaternion.LookRotation(camForward).eulerAngles.y;
            float currentYaw = transform.eulerAngles.y;

            float angleDiff = Mathf.DeltaAngle(currentYaw, targetYaw);
            if (Mathf.Abs(angleDiff) > deadzoneAngle)
            {
                float excessAngle = angleDiff > 0 ? angleDiff - deadzoneAngle : angleDiff + deadzoneAngle;
                targetRotation = Quaternion.Euler(0, currentYaw + excessAngle, 0);
            }

            float smoothYaw = Mathf.SmoothDampAngle(transform.eulerAngles.y, targetRotation.eulerAngles.y, ref currentYawVelocity, rotationSmoothTime);
            transform.rotation = Quaternion.Euler(0, smoothYaw, 0);
        }

        Vector3 hudForward = transform.forward;
        Vector3 targetPosition = targetCamera.position + (hudForward * followDistance);
        targetPosition.y = targetCamera.position.y + heightOffset;

        transform.position = Vector3.SmoothDamp(transform.position, targetPosition, ref currentVelocity, positionSmoothTime);
    }

    public void ResetHUDOrientation()
    {
        if (targetCamera == null) return;
        Vector3 camForward = targetCamera.forward;
        camForward.y = 0;
        if (camForward.sqrMagnitude < 0.001f) camForward = Vector3.forward;
        camForward.Normalize();

        targetRotation = Quaternion.LookRotation(camForward, Vector3.up);
        transform.rotation = targetRotation;
        transform.position = targetCamera.position + (camForward * followDistance) + new Vector3(0, heightOffset, 0);
    }
}
