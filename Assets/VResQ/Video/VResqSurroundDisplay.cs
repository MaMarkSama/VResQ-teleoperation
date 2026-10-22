using System;
using UnityEngine;
using UnityEngine.InputSystem;
using VResQ.Core;

namespace VResQ.Video
{
    /// <summary>
    /// Surround View Modes:
    /// - ExternalWorld (มองเห็นรอบนอก): See-through 3D environment all around the user.
    /// - Surround360 (ภาพจากกล้องรอบตัว 360°): Full 360° panoramic cylinder/sphere camera wrap.
    /// - Surround220 (ภาพจากกล้องรอบตัว 220°): Ultra-wide 220° curved surround camera wrap.
    /// - Surround180 (ภาพจากกล้องรอบตัว 180°): Front hemisphere 180° curved surround camera wrap.
    /// </summary>
    public enum SurroundViewMode
    {
        ExternalWorld = 0,
        Surround360 = 1,
        Surround220 = 2,
        Surround180 = 3,

        // Legacy compatibility aliases
        CenterPanelOnly = ExternalWorld,
        Dome360 = Surround360
    }

    /// <summary>
    /// Generates and manages a 3D Panoramic Surround Camera Dome wrapping around the user.
    /// Supports instant switching between seeing the outside 3D world (ExternalWorld / มองเห็นรอบนอก)
    /// and full surround camera immersion (Surround360 / 220 / 180 / ภาพจากกล้องรอบตัว) with NO flat monitor box.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(VResqCameraReceiver))]
    public class VResqSurroundDisplay : MonoBehaviour
    {
        public static VResqSurroundDisplay Instance { get; private set; }

        [Header("--- Surround Camera Dome Dimensions ---")]
        [Tooltip("Distance from user eyes to the surround camera dome (meters)")]
        [Range(1.5f, 6.0f)] public float radius = 2.8f;

        [Tooltip("Total height of the surround camera dome (meters)")]
        [Range(1.2f, 5.0f)] public float screenHeight = 2.8f;

        [Tooltip("Vertical height offset relative to camera")]
        public float heightOffset = 0.0f;

        [Tooltip("Horizontal Field of View arc (degrees): 360 = full wrap, 220 = ultra-wide, 180 = hemisphere")]
        [Range(90f, 360f)] public float arcAngle = 360f;

        [Header("--- Mesh Resolution ---")]
        [Range(24, 96)] public int horizontalSegments = 64;
        [Range(6, 24)] public int verticalSegments = 16;

        [Header("--- Active View Mode ---")]
        public SurroundViewMode currentViewMode = SurroundViewMode.Surround360;

        [Header("--- Last Active Camera Mode (for 1-button toggle) ---")]
        public SurroundViewMode lastCameraSurroundMode = SurroundViewMode.Surround360;

        [Header("--- Material & Shader ---")]
        public Material surroundMaterial;

        [Header("--- Camera Reference ---")]
        public Transform targetCamera;

        private GameObject _screenObject;
        private MeshFilter _meshFilter;
        private MeshRenderer _meshRenderer;
        private Mesh _screenMesh;
        private VResqCameraReceiver _cameraReceiver;
        private Material _runtimeMaterial;

        private void OnEnable()
        {
            Instance = this;
            _cameraReceiver = GetComponent<VResqCameraReceiver>();
        }

        private void Awake()
        {
            Instance = this;
            _cameraReceiver = GetComponent<VResqCameraReceiver>();
        }

        private void Start()
        {
            if (targetCamera == null && Camera.main != null)
            {
                targetCamera = Camera.main.transform;
            }

            SetupSurroundScreen();
            SyncFromFrameworkConfig();

            if (VResqFrameworkConfig.Instance != null)
            {
                VResqFrameworkConfig.Instance.OnConfigChanged.AddListener(SyncFromFrameworkConfig);
            }
        }

        public void SyncFromFrameworkConfig()
        {
            var cfg = VResqFrameworkConfig.Instance;
            if (cfg != null)
            {
                currentViewMode = cfg.Profile.viewMode;
                radius = cfg.Profile.surroundRadius;
                screenHeight = cfg.Profile.surroundHeight;
                arcAngle = cfg.Profile.surroundArcAngle;
                heightOffset = cfg.Profile.surroundVerticalOffset;
            }
            ApplyGeometryAndMode();
        }

        private void SetupSurroundScreen()
        {
            if (_screenObject != null && _meshFilter != null && _meshRenderer != null) return;

            Transform existing = transform.Find("Curved_Surround_Screen");
            if (existing != null)
            {
                _screenObject = existing.gameObject;
                _meshFilter = _screenObject.GetComponent<MeshFilter>() ?? _screenObject.AddComponent<MeshFilter>();
                _meshRenderer = _screenObject.GetComponent<MeshRenderer>() ?? _screenObject.AddComponent<MeshRenderer>();
            }
            else
            {
                _screenObject = new GameObject("Curved_Surround_Screen");
                _screenObject.transform.SetParent(transform, false);
                _meshFilter = _screenObject.AddComponent<MeshFilter>();
                _meshRenderer = _screenObject.AddComponent<MeshRenderer>();
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ??
                            Shader.Find("Unlit/Texture") ??
                            Shader.Find("Sprites/Default");

            _runtimeMaterial = surroundMaterial != null ? new Material(surroundMaterial) : new Material(shader);
            _runtimeMaterial.name = "SurroundScreen_Material";

            if (_runtimeMaterial.HasProperty("_Cull"))
            {
                _runtimeMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            }

            _meshRenderer.sharedMaterial = _runtimeMaterial;
            _meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _meshRenderer.receiveShadows = false;

            GenerateCurvedMesh(arcAngle, radius, screenHeight);
        }

        private void Update()
        {
            if (targetCamera == null && Camera.main != null)
            {
                targetCamera = Camera.main.transform;
            }

            if (targetCamera != null && _screenObject != null)
            {
                Vector3 camPos = targetCamera.position;
                camPos.y += heightOffset;
                _screenObject.transform.position = camPos;

                // Keep the surround dome aligned with robot/world forward so the user can look 360° around inside it!
                _screenObject.transform.rotation = Quaternion.identity;
            }

            if (Application.isPlaying && _cameraReceiver != null && _cameraReceiver.VideoTexture != null && _runtimeMaterial != null)
            {
                if (_runtimeMaterial.mainTexture != _cameraReceiver.VideoTexture)
                {
                    _runtimeMaterial.mainTexture = _cameraReceiver.VideoTexture;
                }
                if (_runtimeMaterial.HasProperty("_BaseMap") && _runtimeMaterial.GetTexture("_BaseMap") != _cameraReceiver.VideoTexture)
                {
                    _runtimeMaterial.SetTexture("_BaseMap", _cameraReceiver.VideoTexture);
                }
            }
        }

        /// <summary>
        /// Instant 1-button toggle between มองเห็นรอบนอก (ExternalWorld) and ภาพจากกล้องรอบตัว (Surround Camera).
        /// </summary>
        public void ToggleExternalVsCamera()
        {
            if (currentViewMode == SurroundViewMode.ExternalWorld)
            {
                if (lastCameraSurroundMode == SurroundViewMode.ExternalWorld)
                    lastCameraSurroundMode = SurroundViewMode.Surround360;
                SetViewMode(lastCameraSurroundMode);
            }
            else
            {
                lastCameraSurroundMode = currentViewMode;
                SetViewMode(SurroundViewMode.ExternalWorld);
            }
        }

        /// <summary>
        /// Cycles through all 4 modes: Surround360 -> Surround220 -> Surround180 -> ExternalWorld (มองเห็นรอบนอก).
        /// </summary>
        public void CycleNextViewMode()
        {
            SurroundViewMode next = currentViewMode switch
            {
                SurroundViewMode.Surround360 => SurroundViewMode.Surround220,
                SurroundViewMode.Surround220 => SurroundViewMode.Surround180,
                SurroundViewMode.Surround180 => SurroundViewMode.ExternalWorld,
                SurroundViewMode.ExternalWorld => SurroundViewMode.Surround360,
                _ => SurroundViewMode.Surround360
            };
            SetViewMode(next);
        }

        public void SetViewMode(SurroundViewMode mode)
        {
            currentViewMode = mode;
            if (mode != SurroundViewMode.ExternalWorld)
            {
                lastCameraSurroundMode = mode;
            }

            switch (mode)
            {
                case SurroundViewMode.Surround360:
                    arcAngle = 360f;
                    break;
                case SurroundViewMode.Surround220:
                    arcAngle = 220f;
                    break;
                case SurroundViewMode.Surround180:
                    arcAngle = 180f;
                    break;
            }

            var cfg = VResqFrameworkConfig.Instance;
            if (cfg != null)
            {
                cfg.Profile.viewMode = currentViewMode;
                if (mode != SurroundViewMode.ExternalWorld)
                {
                    cfg.Profile.surroundArcAngle = arcAngle;
                }
                cfg.NotifyConfigChanged();
            }
            else
            {
                ApplyGeometryAndMode();
            }

            Debug.Log($"<color=cyan>[SurroundDisplay]</color> View Mode -> {currentViewMode} ({arcAngle}°)");
        }

        public void ApplyGeometryAndMode()
        {
            if (_screenObject == null) SetupSurroundScreen();
            if (_screenObject == null) return;

            if (currentViewMode == SurroundViewMode.ExternalWorld)
            {
                // มองเห็นรอบนอก (See-through 3D external world)
                _screenObject.SetActive(false);
            }
            else
            {
                // ภาพจากกล้องรอบตัว (Surround Camera 360° / 220° / 180°)
                _screenObject.SetActive(true);
                GenerateCurvedMesh(arcAngle, radius, screenHeight);
            }
        }

        public void GenerateCurvedMesh(float totalArcDeg, float r, float h)
        {
            if (_meshFilter == null) return;

            if (_screenMesh == null)
            {
                _screenMesh = new Mesh();
                _screenMesh.name = "Procedural_Curved_Surround_Mesh";
            }
            else
            {
                _screenMesh.Clear();
            }

            int hCount = Mathf.Max(16, horizontalSegments);
            int vCount = Mathf.Max(4, verticalSegments);

            int vertexCount = (hCount + 1) * (vCount + 1);
            Vector3[] vertices = new Vector3[vertexCount];
            Vector3[] normals = new Vector3[vertexCount];
            Vector2[] uvs = new Vector2[vertexCount];
            int[] triangles = new int[hCount * vCount * 6];

            float halfArcRad = (totalArcDeg * 0.5f) * Mathf.Deg2Rad;
            float startAngleRad = -halfArcRad;
            float angleStepRad = (totalArcDeg * Mathf.Deg2Rad) / hCount;

            float halfH = h * 0.5f;
            float yStep = h / vCount;

            int vIdx = 0;
            for (int y = 0; y <= vCount; y++)
            {
                float currentY = -halfH + (y * yStep);
                float v = (float)y / vCount;

                for (int x = 0; x <= hCount; x++)
                {
                    float currentAngleRad = startAngleRad + (x * angleStepRad);
                    float u = (float)x / hCount;

                    // Slight spherical vertical curvature for natural surround immersion
                    float vNorm = (v - 0.5f) * 2f; // -1 to +1
                    float sphericalRadius = r * Mathf.Lerp(1.0f, 0.92f, vNorm * vNorm);

                    float sin = Mathf.Sin(currentAngleRad);
                    float cos = Mathf.Cos(currentAngleRad);

                    Vector3 pos = new Vector3(sin * sphericalRadius, currentY, cos * sphericalRadius);
                    vertices[vIdx] = pos;
                    normals[vIdx] = -new Vector3(sin, 0f, cos).normalized;
                    uvs[vIdx] = new Vector2(u, v);
                    vIdx++;
                }
            }

            int tIdx = 0;
            for (int y = 0; y < vCount; y++)
            {
                for (int x = 0; x < hCount; x++)
                {
                    int row1 = y * (hCount + 1);
                    int row2 = (y + 1) * (hCount + 1);

                    triangles[tIdx++] = row1 + x;
                    triangles[tIdx++] = row1 + x + 1;
                    triangles[tIdx++] = row2 + x;

                    triangles[tIdx++] = row1 + x + 1;
                    triangles[tIdx++] = row2 + x + 1;
                    triangles[tIdx++] = row2 + x;
                }
            }

            _screenMesh.vertices = vertices;
            _screenMesh.normals = normals;
            _screenMesh.uv = uvs;
            _screenMesh.triangles = triangles;
            _screenMesh.RecalculateBounds();

            _meshFilter.sharedMesh = _screenMesh;
        }

        private void OnDestroy()
        {
            if (Application.isPlaying)
            {
                if (_runtimeMaterial != null) Destroy(_runtimeMaterial);
                if (_screenMesh != null) Destroy(_screenMesh);
            }
        }
    }
}
