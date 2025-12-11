using System;
using System.Threading.Tasks;

namespace Cellular.Services
{
    /// <summary>
    /// Platform-agnostic interface for MetaWear device operations
    /// </summary>
    public interface IMetaWearService
    {
        /// <summary>
        /// Event fired when device is disconnected
        /// </summary>
        event EventHandler<string> DeviceDisconnected;

        /// <summary>
        /// Event fired when accelerometer data is received
        /// </summary>
        event EventHandler<MetaWearAccelerometerData> AccelerometerDataReceived;

        /// <summary>
        /// Event fired when gyroscope data is received
        /// </summary>
        event EventHandler<MetaWearGyroscopeData> GyroscopeDataReceived;

        /// <summary>
        /// Event fired when magnetometer data is received
        /// </summary>
        event EventHandler<MetaWearMagnetometerData> MagnetometerDataReceived;

        /// <summary>
        /// Event fired when light sensor data is received
        /// </summary>
        event EventHandler<MetaWearLightSensorData> LightSensorDataReceived;

        /// <summary>
        /// Gets whether the device is connected
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// Gets the MAC address of the connected device
        /// </summary>
        string MacAddress { get; }

        /// <summary>
        /// Connects to a MetaWear device using a device object
        /// This is the recommended cross-platform method
        /// </summary>
        /// <param name="device">The BLE device object (IDevice from Plugin.BLE)</param>
        Task<bool> ConnectAsync(object device);

        /// <summary>
        /// Disconnects from the device
        /// </summary>
        Task DisconnectAsync();

        /// <summary>
        /// Starts accelerometer data streaming
        /// </summary>
        /// <param name="sampleRate">Sample rate in Hz (1-100)</param>
        /// <param name="range">Accelerometer range in G (2, 4, 8, 16)</param>
        Task StartAccelerometerAsync(float sampleRate = 50f, float range = 16f);

        /// <summary>
        /// Stops accelerometer data streaming
        /// </summary>
        Task StopAccelerometerAsync();

        /// <summary>
        /// Starts gyroscope data streaming
        /// </summary>
        /// <param name="sampleRate">Sample rate in Hz (25-200)</param>
        /// <param name="range">Gyroscope range in degrees/sec (125, 250, 500, 1000, 2000)</param>
        Task StartGyroscopeAsync(float sampleRate = 100f, float range = 2000f);

        /// <summary>
        /// Stops gyroscope data streaming
        /// </summary>
        Task StopGyroscopeAsync();

        /// <summary>
        /// Reads device information
        /// </summary>
        Task<DeviceInfo> GetDeviceInfoAsync();

        /// <summary>
        /// Starts magnetometer data streaming
        /// </summary>
        /// <param name="sampleRate">Sample rate in Hz (10-25)</param>
        Task StartMagnetometerAsync(float sampleRate = 25f);

        /// <summary>
        /// Stops magnetometer data streaming
        /// </summary>
        Task StopMagnetometerAsync();

        /// <summary>
        /// Starts light sensor data streaming
        /// </summary>
        /// <param name="sampleRate">Sample rate in Hz (1-10)</param>
        Task StartLightSensorAsync(float sampleRate = 10f);

        /// <summary>
        /// Stops light sensor data streaming
        /// </summary>
        Task StopLightSensorAsync();

        /// <summary>
        /// Resets the device
        /// </summary>
        Task ResetAsync();

        /// <summary>
        /// Probes the device to discover available modules and registers
        /// </summary>
        Task ProbeDeviceAsync();
    }

    /// <summary>
    /// MetaWear accelerometer data structure
    /// </summary>
    public class MetaWearAccelerometerData
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// MetaWear gyroscope data structure
    /// </summary>
    public class MetaWearGyroscopeData
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// MetaWear magnetometer data structure
    /// </summary>
    public class MetaWearMagnetometerData
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// MetaWear light sensor data structure
    /// </summary>
    public class MetaWearLightSensorData
    {
        public float Visible { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Device information structure
    /// </summary>
    public class DeviceInfo
    {
        public string Model { get; set; }
        public string SerialNumber { get; set; }
        public string FirmwareVersion { get; set; }
        public string HardwareVersion { get; set; }
        public string Manufacturer { get; set; }
    }
}

