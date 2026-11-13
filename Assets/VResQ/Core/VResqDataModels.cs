using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace VResQ.Core
{
    [Serializable]
    public class TelemetryAlerts
    {
        public bool impact;
        public bool rollover;
        public bool overheat;
        public bool obstacle;
        public bool vibration;
    }

    [Serializable]
    public class SensorStatusFlags
    {
        public bool ultrasonic;
        public bool bme280;
        public bool mpu6050;
        public bool oled;
    }

    [Serializable]
    public class SensorStatusDetail
    {
        public bool esp32_online;
        public bool serial_port_open;
        public bool ultrasonic;
        public bool bme280;
        public bool mpu6050;
        public bool oled;
        public string temp_source = "host_board";
        public string heading_source = "odometry";
    }

    [Serializable]
    public class HostSummaryData
    {
        public float cpu_temp_c;
        public float gpu_temp_c;
        public float board_temp_c;
        public float max_temp_c;
        public float ram_pct;
        public float disk_pct;
        public float load_1m;
        public float uptime_s;
    }

    [Serializable]
    public class EnvironmentData
    {
        public float temp_c;
        public float humidity_pct;
        public float pressure_hpa;
        public float heat_index_c;
        public float board_temp_c;
        public float cpu_temp_c;
        public float display_temp_c;
        public string source = "host_board";
        public string sensor_source = "host_board";
    }

    /// <summary>
    /// Telemetry metrics received from ESP32 & Sensors (MPU6050, BME280, Ultrasonic, OLED)
    /// Matches the exact JSON structure of the V-ResQ Web Dashboard (/api/state & /ws).
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
        public TelemetryAlerts alerts = new TelemetryAlerts();
        public int fsm_state;
        public int servo_deg = 90;
        public int flags;
        public int sensor_flags;
        public int oled_page;
        public SensorStatusFlags sensors = new SensorStatusFlags();
        public int rx_ok;
        public int crc_errors;
        public int esp32_cmd_rx_ok;
        public int esp32_crc_errors;
        public int esp32_last_cmd_age_ms;
        public int motor_status;
        public bool motor_driver_ok;
        public string motor_driver = "legacy";
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
        public int score = -1;
        public string category = "NOMINAL";
        public string level = "SAFE";
        public string label = "NORMAL";
    }

    [Serializable]
    public class TractionAnalyticsData
    {
        public string status = "TRACTION_NOMINAL";
        public string label_th = "สภาพพื้นผิวปกติ";
        public string color = "ok";
    }

    [Serializable]
    public class AnalyticsData
    {
        public string floor_name;
        public AirQualityData air = new AirQualityData();
        public SurvivabilityData survivability = new SurvivabilityData();
        public TractionAnalyticsData traction = new TractionAnalyticsData();
        public string fire_risk;
        public string collapse_risk;
    }

    [Serializable]
    public class AIDetection
    {
        public string frame_id;
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
        public string uid;
        public int robot_id = 1;
        public int cluster_id = 0;
        public string node_name = "vresq-node";
        public string name = "vresq-node";
        public string role = "t2-dashboard";
        public string protocol_version = "node-zero";
        public string source;
    }

    [Serializable]
    public class DiscoveredPeerProperties
    {
        public string node_uid;
        public string uid;
        public string robot_id = "1";
        public string cluster_id = "0";
        public string node_name = "vresq-node";
        public string name = "vresq-node";
        public string role = "t2-dashboard";
        public string protocol_version = "5";
    }

    [Serializable]
    public class DiscoveredPeerData
    {
        public string node_uid;
        public string node_name;
        public string name;
        public int robot_id = 1;
        public string role;
        public string server;
        public string ip;
        public string[] ips;
        public int port = 8000;
        public DiscoveredPeerProperties properties;
        public float latency_ms;
        public bool online = true;
        public string discovery_mode;
        public string hw_mode = "LIVE-HW";
        public int health_score = 95;
    }

    [Serializable]
    public class SwarmApiResponse
    {
        public NodeIdentityData local_node;
        public DiscoveredPeerData[] discovered;
    }

    /// <summary>
    /// Master Robot State snapshot received via WebSocket (/ws) at 20Hz or HTTP (/api/state).
    /// </summary>
    [Serializable]
    public class RobotStateSnapshot
    {
        public NodeIdentityData identity = new NodeIdentityData();
        public NodeIdentityData node_info;
        public TelemetryData telemetry = new TelemetryData();
        public AnalyticsData analytics = new AnalyticsData();
        public AIDetection[] ai_detections;
        public AIConfigData ai_config = new AIConfigData();
        public int servo_deg = 90;
        public int left_pwm;
        public int right_pwm;
        public float speed_scale = 0.75f;
        public int oled_page = 0;
        public int oled_fx = 0;
        public bool oled_auto = false;
        public bool estop;
        public bool buzzer;
        public bool serial_connected;
        public bool esp32_online;
        public bool camera_ready;
        public bool motion_authorized;
        public string serial_port;
        public string hw_mode = "LIVE-HW";
        public string mode_name = "READY";
        public float battery_v = 12.2f;
        public float last_command_age_ms;
        public float last_telem_age_ms;
        public string active_action;
        public int mode = 1;
        public float pose_x;
        public float pose_y;
        public float pose_heading;
        public SwarmPose pose = new SwarmPose();
        public HostSummaryData host = new HostSummaryData();
        public SensorStatusDetail sensor_status = new SensorStatusDetail();
        public EnvironmentData environment = new EnvironmentData();
        public bool radar_active;

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

    /// <summary>
    /// Wrapper envelope for WebSocket {"type": "state", "data": { ... }} messages.
    /// </summary>
    [Serializable]
    public class WsStateEnvelope
    {
        public string type;
        public RobotStateSnapshot data;
    }

    [Serializable]
    public class ControlPayload
    {
        public string type = "control";
        public int sequence = 1;
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

    // =========================================================================
    // SWARM FRAME v1 & MULTI-ROBOT C2 CONTRACTS (3.5.0-swarm-unified)
    // =========================================================================

    [Serializable]
    public class SwarmPose
    {
        public float x;
        public float y;
        public float theta;
        public float heading;
        public float heading_deg;
    }

    [Serializable]
    public class SwarmActuators
    {
        public int left_pwm;
        public int right_pwm;
        public int servo_deg;
        public float speed_scale;
    }

    [Serializable]
    public class SwarmFrame
    {
        public string schema = "swarm-frame-v1";
        public string build_version = "3.5.0-swarm-unified";
        public string node_uid;
        public string node_id;
        public int robot_id;
        public string node_name;
        public string name;
        public string role;
        public string role_category;
        public string swarm_role;
        public bool is_leader;
        public bool is_self;
        public int leader_id;
        public string ip;
        public string[] ips;
        public int port = 8000;
        public double ts;
        public string hw_mode = "LIVE-HW";
        public bool serial_connected;
        public bool esp32_online;
        public int health_score = 95;
        public float battery_v = 12.2f;
        public string sim_scenario;
        public string mode_name = "READY";
        public string fsm_state;
        public string action;
        public bool estop;
        public SwarmPose pose = new SwarmPose();
        public SwarmActuators actuators;
        public TelemetryData telemetry;
        public string state = "ALIVE";
        public string link_state = "ALIVE";
        public string status;
        public float age_sec;
        public bool online = true;
        public bool has_state;
        public string transport;
        public string video_url;
        public string dashboard_url;
    }

    [Serializable]
    public class SwarmSummaryBlock
    {
        public int total_nodes;
        public int alive_nodes;
        public int stale_nodes;
        public int lost_nodes;
        public string leader_uid;
        public string leader_name;
        public int leader_robot_id;
        public int swarm_health_score;
        public bool version_synced;
        public string build_version;
        public string formation = "relay_chain";
        public string mission = "cooperative_search";
        public string scenario = "normal";
        public int confirmed_survivors;
        public int active_hazards;
    }

    [Serializable]
    public class SwarmSummaryResponse
    {
        public string schema;
        public string build_version;
        public int leader_id;
        public string leader_ip;
        public string formation;
        public string scenario;
        public int total_nodes;
        public int alive_nodes;
        public int stale_nodes;
        public int lost_nodes;
        public bool version_synced;
        public SwarmSummaryBlock swarm_summary = new SwarmSummaryBlock();
        public SwarmFrame local_node;
        public SwarmFrame self;
        public SwarmFrame[] nodes;
        public SwarmFrame[] peers;
    }

    [Serializable]
    public class FusedMapRobot
    {
        public string node_uid;
        public int robot_id;
        public string node_name;
        public string name;
        public string ip;
        public string role;
        public string role_category;
        public string swarm_role;
        public bool is_leader;
        public bool is_self;
        public string link_state = "ALIVE";
        public string status;
        public string hw_mode = "LIVE-HW";
        public int health_score = 95;
        public SwarmPose pose = new SwarmPose();
        public float x;
        public float y;
        public float heading;
        public string fsm_state;
        public string action;
    }

    [Serializable]
    public class FusedMapRelayLink
    {
        public int from_robot_id;
        public int to_robot_id;
        public string from_name;
        public string to_name;
        public SwarmPose from_pose = new SwarmPose();
        public SwarmPose to_pose = new SwarmPose();
        public float x1;
        public float y1;
        public float x2;
        public float y2;
        public float distance_m;
        public float distance_cm;
        public int link_quality_pct;
        public int quality_pct;
    }

    [Serializable]
    public class FusedMapObstacle
    {
        public float x;
        public float y;
        public int source_robot_id;
        public float dist_cm;
    }

    [Serializable]
    public class FusedMapHazard
    {
        public float x;
        public float y;
        public int robot_id;
        public float temp_c;
        public string fire_risk;
        public string collapse_risk;
        public int survivability_score;
    }

    [Serializable]
    public class FusedMapSurvivor
    {
        public string cluster_id;
        public float x;
        public float y;
        public int[] confirmed_by;
        public int confirm_count;
        public float fused_confidence;
        public string status;
    }

    [Serializable]
    public class FusedMapResponse
    {
        public double ts;
        public string formation;
        public string scenario;
        public int leader_id;
        public FusedMapRobot[] robots;
        public FusedMapRelayLink[] relay_links;
        public FusedMapObstacle[] obstacles;
        public FusedMapHazard[] hazards;
        public FusedMapSurvivor[] survivors;
    }

    /// <summary>
    /// Payload sent from Unity Bare-Board 3D SIL Simulator to POST /api/sim/inject
    /// when physical boards run without an attached ESP32.
    /// </summary>
    [Serializable]
    public class SimInjectPayload
    {
        public float pose_x;
        public float pose_y;
        public float pose_heading;
        public float ultrasonic_cm = 120.0f;
        public float temp_c = 29.5f;
        public float humidity = 55.0f;
        public float pressure_hpa = 1011.2f;
        public float pitch_deg = 0.0f;
        public float roll_deg = 0.0f;
        public float yaw_deg = 0.0f;
        public float ax_g = 0.0f;
        public float ay_g = 0.0f;
        public float az_g = 1.0f;
        public string scenario = "normal";
        public AIDetection[] ai_detections;

        public string ToJson()
        {
            return JsonUtility.ToJson(this);
        }
    }

    [Serializable]
    public class SimActuatorsResponse
    {
        public int left_pwm;
        public int right_pwm;
        public int servo_deg;
        public float speed_scale;
        public string fsm_state;
        public string action;
    }

    [Serializable]
    public class SimInjectResponse
    {
        public bool ok;
        public string mode;
        public SimActuatorsResponse actuators;
    }

    [Serializable]
    public class SwarmMissionRequest
    {
        public string action;
        public string command;
        public string formation;
        public string scenario;
        public int robot_id;
        public int target_robot_id;
        public float x;
        public float y;

        public string ToJson()
        {
            if (string.IsNullOrEmpty(action) && !string.IsNullOrEmpty(command))
            {
                action = command == "estop_all" ? "estop" : command;
            }
            if (robot_id == 0 && target_robot_id != 0)
            {
                robot_id = target_robot_id;
            }
            return JsonUtility.ToJson(this);
        }
    }
}
