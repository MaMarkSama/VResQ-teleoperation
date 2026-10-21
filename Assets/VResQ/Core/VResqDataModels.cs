using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace VResQ.Core
{
    /// <summary>
    /// Telemetry metrics received from ESP32 & Sensors (MPU6050, BME280, Ultrasonic)
    /// Non-nullable types are used for reliable Unity JsonUtility deserialization.
    /// </summary>
    [Serializable]
    public class TelemetryData
    {
        public float ultrasonic_cm;
        public float temp_c;
        public float pressure_hpa;
        public float humidity;
        public float altitude_m;
        public float pitch_deg;
        public float roll_deg;
        public float yaw_deg;
        public float terrain_roughness;
        public float heat_index_c;
        public int alert_flags;
        public int fsm_state;
        public int servo_deg = 90;
        public int flags;
        public int sensor_flags;
        public int oled_page;
        public int rx_ok;
        public int crc_errors;
        public int esp32_cmd_rx_ok;
        public int esp32_crc_errors;
        public int esp32_last_cmd_age_ms;
        public int motor_status;
        public bool motor_driver_ok;
        public int motor_left_cmd;
        public int motor_right_cmd;
    }

    [Serializable]
    public class AirQualityData
    {
        public float po2_kpa;
        public float o2_percent;
        public string air_density_category;
        public float density_kg_m3;
    }

    [Serializable]
    public class SurvivabilityData
    {
        public int score;
        public string level;
        public string label;
    }

    [Serializable]
    public class AnalyticsData
    {
        public string floor_name;
        public AirQualityData air;
        public SurvivabilityData survivability;
        public string fire_risk;
        public string collapse_risk;
    }

    [Serializable]
    public class AIDetection
    {
        public string id;
        public string label;
        public float score;
        public float confidence;
        public float[] box; // [x1, y1, x2, y2]
        public float[] bbox; // [x1, y1, x2, y2]
        public string source;
    }

    [Serializable]
    public class AIConfigData
    {
        public string mode = "jetson";
        public float confidence_threshold = 0.65f;
        public string[] target_classes;
        public int total_detections;
        public float detector_fps;
    }

    [Serializable]
    public class NodeIdentityData
    {
        public string node_uid;
        public int robot_id = 1;
        public int cluster_id = 0;
        public string node_name = "vresq-node";
        public string role = "t2-dashboard";
        public string protocol_version = "node-zero";
    }

    [Serializable]
    public class DiscoveredPeerData
    {
        public string name;
        public string[] ips;
        public int port = 8000;
        public NodeIdentityData properties;
        public float latency_ms;
        public bool online = true;
        public string discovery_mode;
    }

    [Serializable]
    public class SwarmApiResponse
    {
        public NodeIdentityData local_node;
        public DiscoveredPeerData[] discovered;
    }

    /// <summary>
    /// Master Robot State snapshot received via WebSocket at 20Hz
    /// </summary>
    [Serializable]
    public class RobotStateSnapshot
    {
        public NodeIdentityData identity;
        public NodeIdentityData node_info;
        public TelemetryData telemetry = new TelemetryData();
        public AnalyticsData analytics;
        public AIDetection[] ai_detections;
        public AIConfigData ai_config = new AIConfigData();
        public int servo_deg = 90;
        public int left_pwm;
        public int right_pwm;
        public float speed_scale = 0.7f;
        public bool estop;
        public bool buzzer;
        public bool serial_connected;
        public string serial_port;
        public string active_action;
        public int mode = 1;
        public float pose_x;
        public float pose_y;
        public float pose_heading;

        [NonSerialized]
        public Dictionary<int, float> radarPoints = new Dictionary<int, float>();

        public void ParseRadarPoints(string rawJson)
        {
            radarPoints.Clear();
            if (string.IsNullOrEmpty(rawJson)) return;

            int idx = rawJson.IndexOf("\"radar_points\"");
            if (idx < 0) return;

            int openBrace = rawJson.IndexOf('{', idx);
            if (openBrace < 0) return;

            int closeBrace = rawJson.IndexOf('}', openBrace);
            if (closeBrace < 0) return;

            string segment = rawJson.Substring(openBrace, closeBrace - openBrace + 1);
            MatchCollection matches = Regex.Matches(segment, "\"(\\d+)\"\\s*:\\s*([0-9.]+)");
            foreach (Match m in matches)
            {
                if (m.Groups.Count >= 3)
                {
                    if (int.TryParse(m.Groups[1].Value, out int deg) &&
                        float.TryParse(m.Groups[2].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out float dist))
                    {
                        radarPoints[deg] = dist;
                    }
                }
            }
        }
    }

    [Serializable]
    public class ControlPayload
    {
        public string type = "control";
        public float linear;
        public float angular;
        public int servo = 90;
        public float speed = 0.7f;
        public bool estop = false;
        public bool buzzer = false;

        public string ToJson()
        {
            return JsonUtility.ToJson(this);
        }
    }

    [Serializable]
    public class ActionPayload
    {
        public string type = "action";
        public string action;

        public string ToJson()
        {
            return JsonUtility.ToJson(this);
        }
    }
}
