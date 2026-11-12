using System;
using UnityEngine;
using UnityEngine.InputSystem;
using VResQ.Core;

namespace VResQ.Video
{
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
    /// Bulletproof mesh and material lifecycle across Edit Mode and Play Mode so live video is ALWAYS visible.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(VResqCameraReceiver))]
    public class VResqSurroundDisplay : MonoBehaviour
    {
        public static VResqSurroundDisplay Instance { get; private set; }

        [Header("--- Surround Camera Dome Dimensions ---")]
        [Range(1.5f, 6.0f)] public float radius = 2.6f;
        [Range(1.2f, 5.0f)] public float screenHeight = 2.7f;
        public float heightOffset = 0.0f;
        [Range(90f, 360f)] public float arcAngle = 360f;

        [Header("--- Mesh Resolution ---")]
        [Range(24, 96)] public int horizontalSegments = 64;
        [Range(6, 24)] public int verticalSegments = 16;

        [Header("--- Active View Mode ---")]
        public SurroundViewMode currentViewMode = SurroundViewMode.Surround360;
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

            EnsureSurroundScreenReady(forceRebuildMesh: true);
            SyncFromFrameworkConfig();

            if (VResqFrameworkConfig.Instance != null)
            {
                VResqFrameworkConfig.Instance.OnConfigChanged.AddListener(SyncFromFrameworkConfig);
            }
        }

        private void OnDestroy()
        {
            if (VResqFrameworkConfig.Instance != null)
            {
                VResqFrameworkConfig.Instance.OnConfigChanged.RemoveListener(SyncFromFrameworkConfig);
            }
            if (_runtimeMaterial != null)
            {
                if (Application.isPlaying) Destroy(_runtimeMaterial);
                else DestroyImmediate(_runtimeMaterial);
                _runtimeMaterial = null;
            }
            if (_screenMesh != null)
            {
                if (Application.isPlaying) Destroy(_screenMesh);
                else DestroyImmediate(_screenMesh);
                _screenMesh = null;
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

        public void EnsureSurroundScreenReady(bool forceRebuildMesh = false)
        {
            if (_screenObject == null)
            {
                Transform existing = transform.Find("Curved_Surround_Screen");
                if (existing != null)
                {
                    _screenObject = existing.gameObject;
                }
                else
                {
                    _screenObject = new GameObject("Curved_Surround_Screen");
                    _screenObject.transform.SetParent(transform, false);
                }
            }

            if (_meshFilter == null)
            {
                _meshFilter = _screenObject.GetComponent<MeshFilter>();
                if (_meshFilter == null)
                {
                    _meshFilter = _screenObject.AddComponent<MeshFilter>();
                }
            }
            if (_meshRenderer == null)
            {
                _meshRenderer = _screenObject.GetComponent<MeshRenderer>();
                if (_meshRenderer == null)
                {
                    _meshRenderer = _screenObject.AddComponent<MeshRenderer>();
                }
            }

            if (_runtimeMaterial == null || _meshRenderer.sharedMaterial == null)
            {
                Shader shader = Shader.Find("Unlit/Texture");
                if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null) shader = Shader.Find("Sprites/Default");
                if (shader == null) shader = Shader.Find("UI/Default");
                if (shader == null) return;

                _runtimeMaterial = new Material(shader);
                _runtimeMaterial.name = "SurroundScreen_RuntimeMaterial";
                _runtimeMaterial.color = Color.white;

                if (_runtimeMaterial.HasProperty("_Color"))
                {
                    _runtimeMaterial.SetColor("_Color", Color.white);
                }
                if (_runtimeMaterial.HasProperty("_BaseColor"))
                {
                    _runtimeMaterial.SetColor("_BaseColor", Color.white);
                }
                if (_runtimeMaterial.HasProperty("_RendererColor"))
                {
                    _runtimeMaterial.SetColor("_RendererColor", Color.white);
                }
                if (_runtimeMaterial.HasProperty("_Cull"))
                {
                    _runtimeMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
                }

                _meshRenderer.sharedMaterial = _runtimeMaterial;
                _meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _meshRenderer.receiveShadows = false;
            }

            if (forceRebuildMesh || _screenMesh == null || _meshFilter.sharedMesh == null || _screenMesh.vertexCount == 0)
            {
                GenerateCurvedMesh(arcAngle, radius, screenHeight);
            }
        }

        private void Update()
        {
            EnsureSurroundScreenReady(forceRebuildMesh: false);

            if (targetCamera == null && Camera.main != null)
            {
                targetCamera = Camera.main.transform;
            }

            if (targetCamera != null && _screenObject != null)
            {
                Vector3 camPos = targetCamera.position;
                camPos.y += heightOffset;
                _screenObject.transform.position = camPos;
                _screenObject.transform.rotation = Quaternion.identity;
            }

            if (_cameraReceiver == null)
            {
                _cameraReceiver = GetComponent<VResqCameraReceiver>();
            }

            if (_cameraReceiver != null && _cameraReceiver.VideoTexture != null && _runtimeMaterial != null)
            {
                Texture2D tex = _cameraReceiver.VideoTexture;
                if (_runtimeMaterial.mainTexture != tex)
                {
                    _runtimeMaterial.mainTexture = tex;
                }
                if (_runtimeMaterial.HasProperty("_MainTex") && _runtimeMaterial.GetTexture("_MainTex") != tex)
                {
                    _runtimeMaterial.SetTexture("_MainTex", tex);
                }
                if (_runtimeMaterial.HasProperty("_BaseMap") && _runtimeMaterial.GetTexture("_BaseMap") != tex)
                {
                    _runtimeMaterial.SetTexture("_BaseMap", tex);
                }
            }
        }

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
        }

        public void ApplyGeometryAndMode()
        {
            EnsureSurroundScreenReady(forceRebuildMesh: false);
            if (_screenObject == null) return;

            if (currentViewMode == SurroundViewMode.ExternalWorld)
            {
                _screenObject.SetActive(false);
            }
            else
            {
                _screenObject.SetActive(true);
                GenerateCurvedMesh(arcAngle, radius, screenHeight);
            }
        }

        /// <summary>
        /// Generates a double-sided cylindrical/spherical curved screen mesh so it is 100% visible from inside.
        /// For 360° mode, maps the front 200° directly and mirrors the rear 160° seamlessly so the camera image
        /// is never overly stretched and has no hard seam behind the user.
        /// </summary>
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

            int hCount = Mathf.Max(24, horizontalSegments);
            int vCount = Mathf.Max(6, verticalSegments);

            int vertexCount = (hCount + 1) * (vCount + 1);
            Vector3[] vertices = new Vector3[vertexCount];
            Vector3[] normals = new Vector3[vertexCount];
            Color32[] colors = new Color32[vertexCount];
            Vector2[] uvs = new Vector2[vertexCount];
            // Double-sided triangles (12 indices per quad) so culling never hides the mesh!
            int[] triangles = new int[hCount * vCount * 12];

            float halfArcRad = (totalArcDeg * 0.5f) * Mathf.Deg2Rad;
            float startAngleRad = -halfArcRad;
            float angleStepRad = (totalArcDeg * Mathf.Deg2Rad) / hCount;

            float halfH = h * 0.5f;
            float yStep = h / vCount;
            Color32 white = new Color32(255, 255, 255, 255);

            int vIdx = 0;
            for (int y = 0; y <= vCount; y++)
            {
                float currentY = -halfH + (y * yStep);
                float v = (float)y / vCount;
                float vNorm = (v - 0.5f) * 2f;
                float sphericalRadius = r * Mathf.Lerp(1.0f, 0.93f, vNorm * vNorm);

                for (int x = 0; x <= hCount; x++)
                {
                    float currentAngleRad = startAngleRad + (x * angleStepRad);
                    float angleDeg = currentAngleRad * Mathf.Rad2Deg;

                    float u;
                    if (totalArcDeg >= 300f)
                    {
                        // Seamless 360° wrap: front [-100°..+100°] maps 0..1, rear mirrors smoothly 1..0
                        if (angleDeg >= -100f && angleDeg <= 100f)
                        {
                            u = Mathf.InverseLerp(-100f, 100f, angleDeg);
                        }
                        else if (angleDeg > 100f)
                        {
                            u = 1.0f - Mathf.InverseLerp(100f, 180f, angleDeg) * 0.5f;
                        }
                        else
                        {
                            u = Mathf.InverseLerp(-180f, -100f, angleDeg) * 0.5f;
                        }
                    }
                    else
                    {
                        u = (float)x / hCount;
                    }

                    float sin = Mathf.Sin(currentAngleRad);
                    float cos = Mathf.Cos(currentAngleRad);

                    vertices[vIdx] = new Vector3(sin * sphericalRadius, currentY, cos * sphericalRadius);
                    normals[vIdx] = -new Vector3(sin, 0f, cos).normalized;
                    colors[vIdx] = white;
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

                    int i0 = row1 + x;
                    int i1 = row1 + x + 1;
                    int i2 = row2 + x;
                    int i3 = row2 + x + 1;

                    // Side A (Inward facing)
                    triangles[tIdx++] = i0;
                    triangles[tIdx++] = i1;
                    triangles[tIdx++] = i2;

                    triangles[tIdx++] = i1;
                    triangles[tIdx++] = i3;
                    triangles[tIdx++] = i2;

                    // Side B (Outward facing - guarantees visibility regardless of shader Cull mode)
                    triangles[tIdx++] = i0;
                    triangles[tIdx++] = i2;
                    triangles[tIdx++] = i1;

                    triangles[tIdx++] = i1;
                    triangles[tIdx++] = i2;
                    triangles[tIdx++] = i3;
                }
            }

            _screenMesh.vertices = vertices;
            _screenMesh.normals = normals;
            _screenMesh.colors32 = colors;
            _screenMesh.uv = uvs;
            _screenMesh.triangles = triangles;
            _screenMesh.RecalculateBounds();

            _meshFilter.sharedMesh = _screenMesh;
        }
    }
}
