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
        /// Event fired when device is disconnected unexpectedly and all reconnect attempts have failed
        /// </summary>
        event EventHandler<string> DeviceDisconnected;

        /// <summary>
        /// Event fired when the device successfully auto-reconnects after an unexpected disconnect
        /// </summary>
        event EventHandler<string> DeviceReconnected;

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
        /// <param name="gain">LTR329 gain: 0=1x, 1=2x, 2=4x, 3=8x, 6=48x, 7=96x</param>
        /// <param name="integrationTime">LTR329 integration time: 0=100ms, 1=50ms, 2=200ms, 3=400ms, 4=150ms, 5=250ms, 6=300ms, 7=350ms</param>
        /// <param name="measurementRate">LTR329 measurement rate: 0=50ms, 1=100ms, 2=200ms, 3=500ms, 4=1000ms, 5=2000ms</param>
        Task StartLightSensorAsync(float sampleRate = 10f, byte gain = 0, byte integrationTime = 0, byte measurementRate = 1);

        /// <summary>
        /// Stops light sensor data streaming
        /// </summary>
        Task StopLightSensorAsync();

        /// <summary>
        /// Starts barometer (BMP280) with the given configuration
        /// </summary>
        /// <param name="oversampling">Pressure oversampling: 0=ULP, 1=LP, 2=Standard, 3=High, 4=Ultra High</param>
        /// <param name="iirFilter">IIR filter coefficient: 0=Off, 1=2, 2=4, 3=8, 4=16</param>
        /// <param name="standbyTime">Standby time index: 0=0.5ms, 1=62.5ms, 2=125ms, 3=250ms, 4=500ms, 5=1000ms, 6=2000ms, 7=4000ms</param>
        Task StartBarometerAsync(byte oversampling = 3, byte iirFilter = 0, byte standbyTime = 0);

        /// <summary>
        /// Stops barometer streaming
        /// </summary>
        Task StopBarometerAsync();

        /// <summary>
        /// Resets the device
        /// </summary>
        Task ResetAsync();

        /// <summary>
        /// Puts the device into sleep/low-power mode
        /// </summary>
        Task SleepAsync();

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
        public int? BatteryPercentage { get; set; }
    }
}

