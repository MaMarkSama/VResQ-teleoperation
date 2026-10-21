using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace VResQ.Core
{
    /// <summary>
    /// Interactive 2D Tactical Radar & Coordinate Map for LeftPanel_Map.
    /// Plots camera FOV cone, ultrasonic distance, rotating sweep beam, robot heading, and swarm fleet nodes.
    /// </summary>
    public class VResqRadarMap : MonoBehaviour
    {
        [Header("Target Display")]
        public RawImage radarRawImage;
        public TextMeshProUGUI mapCoordsText;

        [Header("Radar Configuration")]
        public int textureSize = 256;
        public float maxRangeMeters = 3.0f;
        public float sweepSpeedDegPerSec = 110f;

        private Texture2D _radarTexture;
        private Color32[] _clearBuffer;
        private Color32[] _renderBuffer;

        private float _currentSweepAngle = 0f;
        private float _lastUltrasonicCm = 0f;
        private float _lastHeadingDeg = 0f;
        private int _lastServoDeg = 90;
        private float _poseX = 0f;
        private float _poseY = 0f;

        private readonly Color32 _bgColor = new Color32(5, 13, 24, 245);
        private readonly Color32 _gridColor = new Color32(0, 175, 230, 58);
        private readonly Color32 _axisColor = new Color32(0, 225, 255, 115);
        private readonly Color32 _fovColor = new Color32(0, 210, 255, 95);
        private readonly Color32 _sweepColor = new Color32(0, 255, 195, 190);
        private readonly Color32 _robotColor = new Color32(0, 255, 120, 255);
        private readonly Color32 _alertObstacleColor = new Color32(255, 45, 55, 255);
        private readonly Color32 _cautionObstacleColor = new Color32(255, 195, 20, 255);
        private readonly Color32 _safeObstacleColor = new Color32(0, 235, 255, 255);
        private readonly Color32 _peerOnlineColor = new Color32(150, 110, 255, 255);

        private void Awake()
        {
            InitializeRadar();
        }

        private void Start()
        {
            if (VResqClient.Instance != null)
            {
                VResqClient.Instance.OnStateReceived.AddListener(OnRobotStateReceived);
            }
        }

        private void OnDestroy()
        {
            if (VResqClient.Instance != null)
            {
                VResqClient.Instance.OnStateReceived.RemoveListener(OnRobotStateReceived);
            }
            if (_radarTexture != null) Destroy(_radarTexture);
        }

        private void InitializeRadar()
        {
            _radarTexture = new Texture2D(textureSize, textureSize, TextureFormat.RGBA32, false);
            _radarTexture.filterMode = FilterMode.Bilinear;

            _clearBuffer = new Color32[textureSize * textureSize];
            _renderBuffer = new Color32[textureSize * textureSize];

            for (int i = 0; i < _clearBuffer.Length; i++)
            {
                _clearBuffer[i] = _bgColor;
            }

            if (radarRawImage != null)
            {
                radarRawImage.texture = _radarTexture;
                radarRawImage.color = Color.white;
            }
        }

        private void OnRobotStateReceived(RobotStateSnapshot state)
        {
            if (state == null) return;

            if (state.telemetry != null)
            {
                _lastUltrasonicCm = state.telemetry.ultrasonic_cm;
                _lastHeadingDeg = state.telemetry.yaw_deg;
                _lastServoDeg = state.telemetry.servo_deg > 0 ? state.telemetry.servo_deg : state.servo_deg;
            }

            _poseX = state.pose_x;
            _poseY = state.pose_y;

            if (mapCoordsText != null)
            {
                string rName = (state.node_info != null && !string.IsNullOrEmpty(state.node_info.node_name))
                    ? state.node_info.node_name
                    : (state.identity != null ? state.identity.node_name : "VRESQ-UNIT");
                float normHdg = ((_lastHeadingDeg % 360f) + 360f) % 360f;
                mapCoordsText.text = $"X: {_poseX:+0.00;-0.00}m   Y: {_poseY:+0.00;-0.00}m\nHDG: {normHdg:000}°  |  CAM: {_lastServoDeg:000}°  |  {rName.ToUpper()}";
            }
        }

        private void Update()
        {
            if (_radarTexture == null) return;

            _currentSweepAngle = (_currentSweepAngle + (sweepSpeedDegPerSec * Time.deltaTime)) % 360f;
            Array.Copy(_clearBuffer, _renderBuffer, _clearBuffer.Length);

            int cx = textureSize / 2;
            int cy = textureSize / 2;
            int maxRadius = (textureSize / 2) - 10;

            // 1. Range Rings
            DrawCircle(cx, cy, (int)(maxRadius * 0.25f), _gridColor);
            DrawCircle(cx, cy, (int)(maxRadius * 0.50f), _gridColor);
            DrawCircle(cx, cy, (int)(maxRadius * 0.75f), _gridColor);
            DrawCircle(cx, cy, maxRadius, _axisColor);

            // 2. Crosshairs
            DrawLine(cx - maxRadius, cy, cx + maxRadius, cy, _gridColor);
            DrawLine(cx, cy - maxRadius, cx, cy + maxRadius, _gridColor);

            // 3. Camera Servo Gimbal FOV Cone (90 = straight up +Y)
            float servoOffset = _lastServoDeg - 90f;
            float centerCamAngle = 90f - servoOffset;
            float leftRay = (centerCamAngle + 28f) * Mathf.Deg2Rad;
            float rightRay = (centerCamAngle - 28f) * Mathf.Deg2Rad;
            DrawLine(cx, cy, cx + (int)(Mathf.Cos(leftRay) * maxRadius * 0.85f), cy + (int)(Mathf.Sin(leftRay) * maxRadius * 0.85f), _fovColor);
            DrawLine(cx, cy, cx + (int)(Mathf.Cos(rightRay) * maxRadius * 0.85f), cy + (int)(Mathf.Sin(rightRay) * maxRadius * 0.85f), _fovColor);

            // 4. Rotating Radar Sweep Beam
            float sweepRad = _currentSweepAngle * Mathf.Deg2Rad;
            int sweepX = cx + (int)(Mathf.Cos(sweepRad) * maxRadius);
            int sweepY = cy + (int)(Mathf.Sin(sweepRad) * maxRadius);
            DrawLine(cx, cy, sweepX, sweepY, _sweepColor);

            // 5. Ultrasonic Obstacle Blip
            if (_lastUltrasonicCm > 2f && _lastUltrasonicCm < (maxRangeMeters * 100f))
            {
                float distRatio = Mathf.Clamp01((_lastUltrasonicCm / 100f) / maxRangeMeters);
                int rDist = (int)(distRatio * maxRadius);
                float obsAngle = centerCamAngle * Mathf.Deg2Rad;

                int ox = cx + (int)(Mathf.Cos(obsAngle) * rDist);
                int oy = cy + (int)(Mathf.Sin(obsAngle) * rDist);

                Color32 obsCol = (_lastUltrasonicCm < 25f) ? _alertObstacleColor :
                                 (_lastUltrasonicCm < 60f) ? _cautionObstacleColor : _safeObstacleColor;

                DrawSolidCircle(ox, oy, 5, obsCol);
            }

            // 6. Swarm Peer Markers
            var cfg = VResqFrameworkConfig.Instance;
            if (cfg != null && cfg.Profile.fleetNodes != null)
            {
                for (int i = 0; i < cfg.Profile.fleetNodes.Length; i++)
                {
                    var node = cfg.Profile.fleetNodes[i];
                    if (VResqClient.Instance != null && i == VResqClient.Instance.ActiveNodeIndex) continue;
                    if (!node.isOnline) continue;

                    float peerAngle = (45f + i * 95f) * Mathf.Deg2Rad;
                    int px = cx + (int)(Mathf.Cos(peerAngle) * (maxRadius * 0.65f));
                    int py = cy + (int)(Mathf.Sin(peerAngle) * (maxRadius * 0.65f));
                    DrawSolidCircle(px, py, 4, _peerOnlineColor);
                }
            }

            // 7. Robot Center Marker
            DrawSolidCircle(cx, cy, 4, _robotColor);
            DrawLine(cx, cy, cx, cy + 14, _robotColor);

            _radarTexture.SetPixels32(_renderBuffer);
            _radarTexture.Apply();

            if (radarRawImage != null && radarRawImage.texture != _radarTexture)
            {
                radarRawImage.texture = _radarTexture;
                radarRawImage.color = Color.white;
            }
        }

        private void DrawCircle(int cx, int cy, int r, Color32 color)
        {
            int x = 0, y = r;
            int d = 3 - 2 * r;
            DrawCirclePoints(cx, cy, x, y, color);
            while (y >= x)
            {
                x++;
                if (d > 0)
                {
                    y--;
                    d = d + 4 * (x - y) + 10;
                }
                else
                {
                    d = d + 4 * x + 6;
                }
                DrawCirclePoints(cx, cy, x, y, color);
            }
        }

        private void DrawCirclePoints(int cx, int cy, int x, int y, Color32 color)
        {
            SetPixelSafe(cx + x, cy + y, color);
            SetPixelSafe(cx - x, cy + y, color);
            SetPixelSafe(cx + x, cy - y, color);
            SetPixelSafe(cx - x, cy - y, color);
            SetPixelSafe(cx + y, cy + x, color);
            SetPixelSafe(cx - y, cy + x, color);
            SetPixelSafe(cx + y, cy - x, color);
            SetPixelSafe(cx - y, cy - x, color);
        }

        private void DrawSolidCircle(int cx, int cy, int r, Color32 color)
        {
            for (int y = -r; y <= r; y++)
            {
                for (int x = -r; x <= r; x++)
                {
                    if (x * x + y * y <= r * r)
                    {
                        SetPixelSafe(cx + x, cy + y, color);
                    }
                }
            }
        }

        private void DrawLine(int x0, int y0, int x1, int y1, Color32 color)
        {
            int dx = Math.Abs(x1 - x0);
            int dy = Math.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1;
            int sy = y0 < y1 ? 1 : -1;
            int err = dx - dy;

            while (true)
            {
                SetPixelSafe(x0, y0, color);
                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * err;
                if (e2 > -dy)
                {
                    err -= dy;
                    x0 += sx;
                }
                if (e2 < dx)
                {
                    err += dx;
                    y0 += sy;
                }
            }
        }

        private void SetPixelSafe(int x, int y, Color32 color)
        {
            if (x >= 0 && x < textureSize && y >= 0 && y < textureSize)
            {
                _renderBuffer[y * textureSize + x] = color;
            }
        }
    }
}
