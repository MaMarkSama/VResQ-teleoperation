using System;
using UnityEngine;

namespace VResQ.Core
{
    [Serializable]
    public class VResqFrameMetadata
    {
        public int schema_version;
        public string robot_id;
        public string camera_id;
        public string frame_id;
        public string source_session_id;
        public long sequence;
        public long capture_timestamp_ns;
        public string timestamp_source;
        public string clock_sync;
        public bool valid;
        public string error;
        public RobotStateSnapshot telemetry;

        public static VResqFrameMetadata Parse(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            var value = JsonUtility.FromJson<VResqFrameMetadata>(json);
            if (value != null && !value.valid)
                throw new FormatException(string.IsNullOrEmpty(value.error) ? "Camera reports an invalid frame" : value.error);
            if (value == null || value.schema_version != 1 ||
                string.IsNullOrEmpty(value.frame_id) || string.IsNullOrEmpty(value.robot_id) ||
                string.IsNullOrEmpty(value.camera_id) || string.IsNullOrEmpty(value.source_session_id) ||
                value.sequence < 0 || value.capture_timestamp_ns <= 0)
                throw new FormatException("Invalid frame identity, timestamp or camera health");
            return value;
        }
    }

    public static class VResqTelemetryQuality
    {
        public static string Validate(TelemetryData value)
        {
            if (value == null) return "Telemetry missing";
            if (!InRange(value.temp_c, -100, 200)) return "Temperature out of range";
            if (!InRange(value.humidity, 0, 100)) return "Humidity out of range";
            if (!InRange(value.ultrasonic_cm, -1, 10000)) return "Distance out of range";
            if (!InRange(value.pitch_deg, -180, 180) || !InRange(value.roll_deg, -180, 180) ||
                !InRange(value.yaw_deg, -360, 360)) return "Orientation out of range";
            if (value.servo_deg < 0 || value.servo_deg > 180) return "Servo angle out of range";
            return "";
        }

        private static bool InRange(float value, float min, float max)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value) && value >= min && value <= max;
        }
    }
}
