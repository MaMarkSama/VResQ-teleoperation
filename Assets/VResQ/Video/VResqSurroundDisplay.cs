using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using VResQ.Core;

namespace VResQ.Video
{
    public enum SurroundViewMode
    {
        Surround180,
        Surround220,
        Dome360,
        CenterPanelOnly
    }

    /// <summary>
    /// Generates and manages a procedural Curved Panoramic Surround Screen wrapping around the VR User.
    /// Maps the live Jetson MJPEG camera stream directly onto this immersive display.
    /// Provides dynamic view mode toggling (180° Surround, 220° Ultra-Wide, 360° Dome, or Center Monitor).
    /// </summary>
    [RequireComponent(typeof(VResqCameraReceiver))]
    public class VResqSurroundDisplay : MonoBehaviour
    {
        public static VResqSurroundDisplay Instance { get; private set; }

        [Header("--- Surround Screen Dimensions ---")]
        [Tooltip("Distance from user eyes to the curved screen (meters)")]
        [Range(1.5f, 6.0f)] public float radius = 2.6f;

        [Tooltip("Total height of the curved screen (meters)")]
        [Range(1.0f, 5.0f)] public float screenHeight = 2.4f;

        [Tooltip("Vertical height offset relative to camera/cockpit")]
        public float heightOffset = 0.0f;

        [Tooltip("Horizontal Field of View arc (degrees). 180 = half cylinder, 360 = full cylinder")]
        [Range(60f, 360f)] public float arcAngle = 180f;

        [Header("--- Mesh Resolution ---")]
        [Range(16, 96)] public int horizontalSegments = 48;
        [Range(4, 24)] public int verticalSegments = 12;

        [Header("--- View Mode ---")]
        public SurroundViewMode currentViewMode = SurroundViewMode.Surround180;

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

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
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
            ApplyViewMode(currentViewMode);
        }

        private void SetupSurroundScreen()
        {
            if (_screenObject != null) return;

            _screenObject = new GameObject("Curved_Surround_Screen");
            _screenObject.transform.SetParent(transform, false);

            _meshFilter = _screenObject.AddComponent<MeshFilter>();
            _meshRenderer = _screenObject.AddComponent<MeshRenderer>();

            // Setup Material (Unlit, Double-Sided)
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ??
                            Shader.Find("Unlit/Texture") ??
                            Shader.Find("Standard");

            _runtimeMaterial = surroundMaterial != null ? new Material(surroundMaterial) : new Material(shader);
            _runtimeMaterial.name = "SurroundScreen_Material";

            // Make sure texture doesn't tile or mirror incorrectly
            if (_runtimeMaterial.HasProperty("_Cull"))
            {
                _runtimeMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            }

            _meshRenderer.material = _runtimeMaterial;
            _meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _meshRenderer.receiveShadows = false;

            GenerateCurvedMesh(arcAngle, radius, screenHeight);
        }

        private void Update()
        {
            // 1. Hotkey 'V' for switching view modes
            HandleViewModeInput();

            // 2. Keep surround screen centered on camera position (horizontal tracking)
            if (targetCamera != null && _screenObject != null)
            {
                Vector3 camPos = targetCamera.position;
                camPos.y += heightOffset;
                _screenObject.transform.position = camPos;

                // Lock orientation to match forward yaw
                Vector3 forward = targetCamera.forward;
                forward.y = 0;
                if (forward.sqrMagnitude > 0.001f)
                {
                    _screenObject.transform.rotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
                }
            }

            // 3. Ensure live video texture is applied to the surround screen material
            if (_cameraReceiver != null && _cameraReceiver.VideoTexture != null && _runtimeMaterial != null)
            {
                if (_runtimeMaterial.mainTexture != _cameraReceiver.VideoTexture)
                {
                    _runtimeMaterial.mainTexture = _cameraReceiver.VideoTexture;
                }
            }
        }

        private void HandleViewModeInput()
        {
            bool toggleRequested = false;

            // New Input System
            Keyboard kb = Keyboard.current;
            if (kb != null && kb.vKey.wasPressedThisFrame)
            {
                toggleRequested = true;
            }

            // Gamepad North/Y button or DPad Up
            Gamepad gp = Gamepad.current;
            if (gp != null && gp.buttonNorth.wasPressedThisFrame)
            {
                toggleRequested = true;
            }

            // Legacy Input fallback
            try
            {
                if (UnityEngine.Input.GetKeyDown(KeyCode.V))
                {
                    toggleRequested = true;
                }
            }
            catch (InvalidOperationException) { }

            if (toggleRequested)
            {
                CycleNextViewMode();
            }
        }

        public void CycleNextViewMode()
        {
            int next = ((int)currentViewMode + 1) % 4;
            SetViewMode((SurroundViewMode)next);
        }

        public void SetViewMode(SurroundViewMode mode)
        {
            currentViewMode = mode;
            ApplyViewMode(currentViewMode);
            Debug.Log($"<color=cyan>[SurroundDisplay]</color> View Mode changed to: {currentViewMode}");
        }

        private void ApplyViewMode(SurroundViewMode mode)
        {
            if (_screenObject == null) return;

            switch (mode)
            {
                case SurroundViewMode.Surround180:
                    _screenObject.SetActive(true);
                    arcAngle = 180f;
                    radius = 2.6f;
                    screenHeight = 2.4f;
                    GenerateCurvedMesh(arcAngle, radius, screenHeight);
                    break;

                case SurroundViewMode.Surround220:
                    _screenObject.SetActive(true);
                    arcAngle = 220f;
                    radius = 2.4f;
                    screenHeight = 2.6f;
                    GenerateCurvedMesh(arcAngle, radius, screenHeight);
                    break;

                case SurroundViewMode.Dome360:
                    _screenObject.SetActive(true);
                    arcAngle = 360f;
                    radius = 2.8f;
                    screenHeight = 3.0f;
                    GenerateCurvedMesh(arcAngle, radius, screenHeight);
                    break;

                case SurroundViewMode.CenterPanelOnly:
                    _screenObject.SetActive(false);
                    break;
            }
        }

        /// <summary>
        /// Generates a smooth cylindrical curved screen mesh facing inward toward the origin (0,0,0).
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

            int hCount = Mathf.Max(8, horizontalSegments);
            int vCount = Mathf.Max(2, verticalSegments);

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

                    // Compute 3D cylinder point in front of origin (forward = +Z)
                    float sin = Mathf.Sin(currentAngleRad);
                    float cos = Mathf.Cos(currentAngleRad);

                    Vector3 pos = new Vector3(sin * r, currentY, cos * r);
                    vertices[vIdx] = pos;

                    // Inward facing normals (facing the user sitting at center)
                    normals[vIdx] = -new Vector3(sin, 0f, cos).normalized;

                    // UV mapping (U flipped if necessary so left/right matches real camera)
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

                    // Clockwise inward facing winding order
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
            if (_runtimeMaterial != null) Destroy(_runtimeMaterial);
            if (_screenMesh != null) Destroy(_screenMesh);
        }
    }
}
