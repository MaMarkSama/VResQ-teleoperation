using UnityEngine;

public class TacticalHUDFollow : MonoBehaviour
{
    [Header("Camera Reference")]
    [Tooltip("ลาก Main Camera มาใส่ที่นี่")]
    public Transform targetCamera;

    [Header("Follow Distance & Height")]
    [Tooltip("ระยะห่างจากตัวผู้ใช้ (เมตร)")]
    public float followDistance = 1f;

    [Tooltip("ชดเชยระดับความสูงจากสายตา")]
    public float heightOffset = -0.05f;

    [Header("Smooth Dynamics")]
    [Tooltip("ความนุ่มนวลในการเคลื่อนที่ตามตัว (ลุก/นั่ง/เดิน)")]
    public float positionSmoothTime = 0.15f;

    [Tooltip("ความนุ่มนวลในการหมุนตามสายตา (ยิ่งค่าน้อยยิ่งเร็ว ยิ่งค่ามากยิ่งสมูท แนะนำ 0.2 - 0.35)")]
    public float rotationSmoothTime = 0.25f;

    [Header("Deadzone (ระยะมองอิสระ)")]
    [Tooltip("มุมที่มองกวาดสายตาได้โดยที่จอไม่ขยับ (องศา)")]
    public float deadzoneAngle = 180.0f;

    private Vector3 currentVelocity;
    private Quaternion targetRotation;
    private float currentYawVelocity;

    void Start()
    {
        if (targetCamera == null && Camera.main != null)
        {
            targetCamera = Camera.main.transform;
        }

        if (targetCamera != null)
        {
            ResetHUDOrientation();
        }
    }

    void LateUpdate()
    {
        if (targetCamera == null) return;

        // 1. คำนวณทิศทางหันของกล้อง (ตัดแกนก้ม-เงยออกเพื่อรักษาแนวระนาบ)
        Vector3 camForward = targetCamera.forward;
        camForward.y = 0;

        if (camForward.sqrMagnitude > 0.001f)
        {
            camForward.Normalize();
            float targetYaw = Quaternion.LookRotation(camForward).eulerAngles.y;
            float currentYaw = transform.eulerAngles.y;

            // ตรวจสอบระยะมุมหันเทียบกับ Deadzone
            float angleDiff = Mathf.DeltaAngle(currentYaw, targetYaw);

            // เมื่อหันหน้าเกินระยะ Deadzone ให้คำนวณเป้าหมายมุมหมุนใหม่
            if (Mathf.Abs(angleDiff) > deadzoneAngle)
            {
                float excessAngle = angleDiff > 0 ? angleDiff - deadzoneAngle : angleDiff + deadzoneAngle;
                targetRotation = Quaternion.Euler(0, currentYaw + excessAngle, 0);
            }

            // หมุนหน้าจอตามแบบ Smooth Damping (ไร้รอยต่อ ไม่กระตุก)
            float smoothYaw = Mathf.SmoothDampAngle(transform.eulerAngles.y, targetRotation.eulerAngles.y, ref currentYawVelocity, rotationSmoothTime);
            transform.rotation = Quaternion.Euler(0, smoothYaw, 0);
        }

        // 2. คำนวณตำแหน่ง HUD ให้อยู่ข้างหน้าตามแนวการหมุนปัจจุบันแบบนุ่มนวล
        Vector3 hudForward = transform.forward;
        Vector3 targetPosition = targetCamera.position + (hudForward * followDistance);
        targetPosition.y = targetCamera.position.y + heightOffset;

        // อัปเดตตำแหน่งตามการเดิน/ลุก/นั่ง
        transform.position = Vector3.SmoothDamp(transform.position, targetPosition, ref currentVelocity, positionSmoothTime);
    }

    public void ResetHUDOrientation()
    {
        Vector3 camForward = targetCamera.forward;
        camForward.y = 0;
        if (camForward == Vector3.zero) camForward = Vector3.forward;
        camForward.Normalize();

        targetRotation = Quaternion.LookRotation(camForward);
        transform.rotation = targetRotation;
        transform.position = targetCamera.position + (camForward * followDistance) + new Vector3(0, heightOffset, 0);
    }
}