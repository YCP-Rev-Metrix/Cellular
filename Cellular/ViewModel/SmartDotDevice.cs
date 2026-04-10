using SQLite;
using System;

namespace Cellular.ViewModel
{
    [Table("smartdot_device")]
    public class SmartDotDevice
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        [Unique]
        public string MacAddress { get; set; } = string.Empty;

        public string DeviceName { get; set; } = string.Empty;

        public DateTime LastConnected { get; set; } = DateTime.Now;

        // ── Light Sensor ──────────────────────────────────────────────
        public float LightSensorHighThreshold { get; set; } = 25000f;
        public double ContinuousSaveDurationSeconds { get; set; } = 4.0;
        public float LightSampleRate { get; set; } = 10f;
        public int LightGain { get; set; } = 0;
        public int LightIntegrationTime { get; set; } = 0;
        public int LightMeasurementRate { get; set; } = 1;

        // ── Accelerometer ─────────────────────────────────────────────
        public float AccelSampleRate { get; set; } = 50f;
        public float AccelRange { get; set; } = 16f;

        // ── Gyroscope ─────────────────────────────────────────────────
        public float GyroSampleRate { get; set; } = 100f;
        public float GyroRange { get; set; } = 2000f;

        // ── Magnetometer ──────────────────────────────────────────────
        public float MagSampleRate { get; set; } = 25f;

        // ── Barometer ─────────────────────────────────────────────────
        public int BaroOversampling { get; set; } = 3;
        public int BaroIirFilter { get; set; } = 0;
        public int BaroStandbyTime { get; set; } = 0;
    }
}
