using System.Text;
using Newtonsoft.Json.Linq;

namespace VResQ.Core
{
    public static class VResqDashboardReport
    {
        public static string Value(JToken root, string path)
        {
            var value = root.SelectToken(path);
            return value == null || value.Type == JTokenType.Null ? "--" : value.ToString();
        }
        private static string Safe(string value) => value.Replace("<", "\\u003c");
        private static void Title(StringBuilder output, string title) => output.Append("\n<size=30><color=#5EF0C7><b>").Append(title).Append("</b></color></size>\n");
        private static void Row(StringBuilder output, string label, JToken root, string path) =>
            output.Append(label).Append("<pos=600><color=#FFFFFF>").Append(Safe(Value(root, path))).Append("</color>\n");

        public static string Protocol(JToken payload)
        {
            var root = payload["serial"] ?? payload;
            var output = new StringBuilder();
            Title(output, "PROTOCOL CONTRACT");
            foreach (var pair in new[] { ("Name", "protocol.name"), ("Baud", "protocol.baud"), ("Frame", "protocol.frame"), ("Command payload", "protocol.command_payload"), ("Telemetry payload", "protocol.telemetry_payload"), ("CRC rule", "protocol.crc") }) Row(output, pair.Item1, root, pair.Item2);
            Title(output, "LINK HEALTH");
            foreach (var pair in new[] { ("Connected", "link.connected"), ("Port", "link.port"), ("Reconnects", "link.reconnects"), ("RX bytes", "traffic.rx_bytes"), ("RX frames", "traffic.rx_frames"), ("TX frames", "traffic.tx_frames"), ("TX rate (Hz)", "traffic.tx_rate_hz"), ("RX rate (Hz)", "traffic.rx_rate_hz"), ("RX interval (ms)", "traffic.last_rx_interval_ms"), ("TX interval (ms)", "traffic.last_tx_interval_ms"), ("Payload length", "traffic.last_payload_len"), ("Frame length", "traffic.last_frame_len") }) Row(output, pair.Item1, root, pair.Item2);
            Title(output, "INTEGRITY / LATENCY");
            foreach (var pair in new[] { ("CRC checked", "integrity.crc_checked"), ("CRC errors", "integrity.crc_errors"), ("CRC error ratio", "integrity.crc_error_rate"), ("Parse errors", "integrity.parse_errors"), ("TX write errors", "integrity.tx_write_errors"), ("Telemetry age (ms)", "latency.telemetry_age_ms"), ("Command age (ms)", "latency.command_age_ms"), ("Serial time delta (ms, not RTT)", "latency.serial_turnaround_ms") }) Row(output, pair.Item1, root, pair.Item2);
            Title(output, "ROBOT STATE");
            foreach (var pair in new[] { ("FSM", "state.telemetry.fsm_state"), ("Left PWM", "state.left_pwm"), ("Right PWM", "state.right_pwm"), ("Servo (deg)", "state.servo_deg"), ("Mode", "state.mode"), ("E-stop", "state.estop"), ("Ultrasonic", "state.sensors.ultrasonic"), ("BME280", "state.sensors.bme280"), ("MPU6050", "state.sensors.mpu6050"), ("OLED", "state.sensors.oled") }) Row(output, pair.Item1, root, pair.Item2);
            Row(output, "ESP32 left command", root, "state.telemetry.motor_left_cmd");
            Row(output, "ESP32 right command", root, "state.telemetry.motor_right_cmd");
            var encoderStatus = Value(root, "encoder.status");
            Row(output, "Encoder link", root, "encoder.status");
            if (encoderStatus == "live")
            {
                Row(output, "Left encoder (ticks)", root, "state.telemetry.encoder_left_ticks");
                Row(output, "Right encoder (ticks)", root, "state.telemetry.encoder_right_ticks");
                Row(output, "Left travel (mm)", root, "encoder.left_mm");
                Row(output, "Right travel (mm)", root, "encoder.right_mm");
            }
            else output.AppendLine(encoderStatus == "stale" ? "No fresh encoder sample" : "Wheel encoders: unavailable");
            Title(output, "COMMAND PATH");
            foreach (var pair in new[] { ("Jetson TX active", "command_path.jetson_tx_active"), ("ESP32 telemetry active", "command_path.esp32_telemetry_active"), ("Command accepted (inferred)", "command_path.esp32_command_accepted_inferred"), ("ESP32 command RX count", "command_path.esp32_cmd_rx_ok"), ("ESP32 command age (ms)", "command_path.esp32_last_cmd_age_ms"), ("ESP32 CRC errors", "command_path.esp32_crc_errors"), ("Last TX hex", "traffic.last_frame_hex") }) Row(output, pair.Item1, root, pair.Item2);
            Title(output, "FAULT ANALYSIS");
            if (root["diagnosis"] is JArray diagnosis)
                foreach (var item in diagnosis) output.Append(Safe(Value(item, "level"))).Append(" / ").Append(Safe(Value(item, "area"))).Append("  ").Append(Safe(Value(item, "message"))).Append('\n');
            else output.Append("No diagnosis payload\n");
            Title(output, "DASHBOARD / CAMERA");
            foreach (var pair in new[] { ("Uptime (s)", "dashboard.uptime_s"), ("WebSockets active", "dashboard.ws_active"), ("WS messages", "dashboard.ws_messages"), ("Camera device", "camera.device"), ("Camera error", "camera.error") }) Row(output, pair.Item1, payload, pair.Item2);
            Row(output, "Last link error", root, "link.last_error");
            return output.ToString();
        }

        public static string Swarm(JToken root)
        {
            var output = new StringBuilder();
            Title(output, "SWARM OVERVIEW");
            foreach (var pair in new[] { ("Local node", "local_node.name"), ("Build", "build_version"), ("Alive", "swarm_summary.alive_nodes"), ("Total", "swarm_summary.total_nodes"), ("Health score", "swarm_summary.swarm_health_score"), ("Leader", "swarm_summary.leader_name"), ("Version synced", "swarm_summary.version_synced"), ("Formation", "swarm_summary.formation"), ("Scenario", "swarm_summary.scenario"), ("Confirmed survivors", "swarm_summary.confirmed_survivors"), ("Active hazards", "swarm_summary.active_hazards"), ("Control authority", "control_authority") }) Row(output, pair.Item1, root, pair.Item2);
            Title(output, "DISCOVERED NODES");
            var nodes = root["nodes"] as JArray ?? root["discovered"] as JArray;
            if (nodes == null || nodes.Count == 0) output.Append("No nodes reported by this server\n");
            else foreach (var node in nodes)
            {
                Title(output, Value(node, "node_name") != "--" ? Value(node, "node_name") : Value(node, "name"));
                foreach (var pair in new[] { ("Identity", "node_uid"), ("Robot ID", "robot_id"), ("IP", "ip"), ("Port", "port"), ("Role", "role"), ("State", "state"), ("Hardware mode", "hw_mode"), ("Health", "health_score"), ("Pose source", "pose_source"), ("Pose", "pose"), ("Telemetry", "telemetry") }) Row(output, pair.Item1, node, pair.Item2);
            }
            Title(output, "FUSED MAP");
            Row(output, "Snapshot", root, "_map");
            Title(output, "MISSION TIMELINE");
            Row(output, "Events", root, "_timeline");
            return output.ToString();
        }
    }
}
