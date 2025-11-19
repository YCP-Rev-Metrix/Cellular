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
        event EventHandler<string> WatchJsonReceived;

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
        /// Reads device information
        /// </summary>
        Task<DeviceInfo> GetDeviceInfoAsync();

        /// <summary>
        /// Resets the device
        /// </summary>
        Task ResetAsync();
        
        Task<bool> SendJsonToWatch(object json);
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