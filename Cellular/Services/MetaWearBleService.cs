using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Plugin.BLE;
using Plugin.BLE.Abstractions.Contracts;
using Plugin.BLE.Abstractions.Extensions;

namespace Cellular.Services
{
    /// <summary>
    /// BLE-based implementation of IMetaWearService using Plugin.BLE
    /// This implements the MetaWear protocol directly via BLE GATT
    /// Assume this is cross-platform if not stated otherwise
    /// </summary>
    public class MetaWearBleService : IMetaWearService
    {
        // MetaWear GATT Service UUIDs
        private static readonly Guid MetaWearServiceUuid = Guid.Parse("326A9000-85CB-9195-D9DD-464CFBBAE75A");
        private static readonly Guid MetaWearCommandCharacteristicUuid = Guid.Parse("326A9001-85CB-9195-D9DD-464CFBBAE75A");
        // Notification characteristic can be either 326A9002 or 326A9006 depending on device/firmware
        private static readonly Guid MetaWearNotificationCharacteristicUuid = Guid.Parse("326A9002-85CB-9195-D9DD-464CFBBAE75A");
        private static readonly Guid MetaWearNotificationCharacteristicUuidAlt = Guid.Parse("326A9006-85CB-9195-D9DD-464CFBBAE75A");
        
        // Device Information Service
        private static readonly Guid DeviceInformationServiceUuid = Guid.Parse("0000180a-0000-1000-8000-00805f9b34fb");
        private static readonly Guid ModelNumberCharacteristicUuid = Guid.Parse("00002a24-0000-1000-8000-00805f9b34fb");
        private static readonly Guid SerialNumberCharacteristicUuid = Guid.Parse("00002a25-0000-1000-8000-00805f9b34fb");
        private static readonly Guid FirmwareRevisionCharacteristicUuid = Guid.Parse("00002a26-0000-1000-8000-00805f9b34fb");
        private static readonly Guid HardwareRevisionCharacteristicUuid = Guid.Parse("00002a27-0000-1000-8000-00805f9b34fb");
        private static readonly Guid ManufacturerNameCharacteristicUuid = Guid.Parse("00002a29-0000-1000-8000-00805f9b34fb");

        private readonly IBluetoothLE _bluetoothLE;
        private readonly IAdapter _adapter;
        private IDevice? _device;
        private IService? _metaWearService;
        private ICharacteristic? _commandCharacteristic;
        private ICharacteristic? _notificationCharacteristic;
        private DeviceInfo? _cachedDeviceInfo;
        private bool _accelerometerActive;
        private bool _gyroscopeActive;
        private bool _isDeviceConnected; // Track connection state manually (cross-platform)

        public event EventHandler<string> DeviceDisconnected;
        public event EventHandler<MetaWearAccelerometerData> AccelerometerDataReceived;
        public event EventHandler<MetaWearGyroscopeData> GyroscopeDataReceived;

        // Cross-platform connection check - use manual tracking instead of DeviceState enum
        // DeviceState enum may not be accessible or may have different values across platforms
        public bool IsConnected => _isDeviceConnected && _device != null;
        public string MacAddress => _device?.Id.ToString() ?? string.Empty;

        public MetaWearBleService()
        {
            _bluetoothLE = CrossBluetoothLE.Current;
            _adapter = CrossBluetoothLE.Current.Adapter;
            
            _adapter.DeviceDisconnected += OnDeviceDisconnected;
        }

        private void OnDeviceDisconnected(object? sender, Plugin.BLE.Abstractions.EventArgs.DeviceEventArgs e)
        {
            if (e.Device.Id == _device?.Id)
            {
                _isDeviceConnected = false;
                _device = null;
                _metaWearService = null;
                _commandCharacteristic = null;
                _notificationCharacteristic = null;
                _cachedDeviceInfo = null;
                _accelerometerActive = false;
                _gyroscopeActive = false;
                
                DeviceDisconnected?.Invoke(this, MacAddress);
            }
        }


        public async Task<bool> ConnectAsync(object device)
        {
            try
            {
                if (device is IDevice bleDevice)
                {
                    _device = bleDevice;
                    return await ConnectToDeviceAsync();
                }
                
                System.Diagnostics.Debug.WriteLine($"Invalid device object type: {device?.GetType()}");
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error connecting to MetaWear device: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> ConnectToDeviceAsync()
        {
            if (_device == null)
                return false;

            try
            {
                System.Diagnostics.Debug.WriteLine($"Connecting to device: {_device.Name} ({_device.Id})");
                
                // Connect to device 
                // Plugin.BLE handles platform differences automatically
                // Don't rely on DeviceState enum - just connect and verify by accessing services
                await _adapter.ConnectToDeviceAsync(_device);
                
                System.Diagnostics.Debug.WriteLine("Device connected, waiting for services to stabilize...");
                
                // Wait a bit for connection to stabilize 
                await Task.Delay(1000); // Increased delay for service discovery

                // Discover services - this will fail if not connected
                System.Diagnostics.Debug.WriteLine("Discovering services...");
                var services = await _device.GetServicesAsync();
                
                System.Diagnostics.Debug.WriteLine($"Found {services.Count()} service(s)");
                
                // Log all available services for debugging
                foreach (var service in services)
                {
                    System.Diagnostics.Debug.WriteLine($"  - Service UUID: {service.Id}");
                }
                
                _metaWearService = services.FirstOrDefault(s => s.Id == MetaWearServiceUuid);

                if (_metaWearService == null)
                {
                    System.Diagnostics.Debug.WriteLine($"MetaWear service not found. Expected UUID: {MetaWearServiceUuid}");
                    System.Diagnostics.Debug.WriteLine("Available services listed above. MetaWear MMS may use different UUIDs.");
                    _isDeviceConnected = false;
                    return false;
                }

                System.Diagnostics.Debug.WriteLine($"MetaWear service found: {_metaWearService.Id}");

                // Get characteristics 
                System.Diagnostics.Debug.WriteLine("Discovering characteristics...");
                var characteristics = await _metaWearService.GetCharacteristicsAsync();
                
                System.Diagnostics.Debug.WriteLine($"Found {characteristics.Count()} characteristic(s)");
                
                // Log all available characteristics for debugging
                foreach (var characteristic in characteristics)
                {
                    System.Diagnostics.Debug.WriteLine($"  - Characteristic UUID: {characteristic.Id}");
                }
                
                _commandCharacteristic = characteristics.FirstOrDefault(c => c.Id == MetaWearCommandCharacteristicUuid);
                
                // Try to find notification characteristic - it can be either UUID depending on device/firmware
                _notificationCharacteristic = characteristics.FirstOrDefault(c => 
                    c.Id == MetaWearNotificationCharacteristicUuid || 
                    c.Id == MetaWearNotificationCharacteristicUuidAlt);

                if (_commandCharacteristic == null)
                {
                    System.Diagnostics.Debug.WriteLine($"MetaWear command characteristic not found.");
                    System.Diagnostics.Debug.WriteLine($"  Expected Command UUID: {MetaWearCommandCharacteristicUuid}");
                    System.Diagnostics.Debug.WriteLine("Available characteristics listed above.");
                    _isDeviceConnected = false;
                    return false;
                }
                
                if (_notificationCharacteristic == null)
                {
                    System.Diagnostics.Debug.WriteLine($"MetaWear notification characteristic not found.");
                    System.Diagnostics.Debug.WriteLine($"  Expected Notification UUID: {MetaWearNotificationCharacteristicUuid} or {MetaWearNotificationCharacteristicUuidAlt}");
                    System.Diagnostics.Debug.WriteLine("Available characteristics listed above.");
                    _isDeviceConnected = false;
                    return false;
                }

                System.Diagnostics.Debug.WriteLine("MetaWear characteristics found");
                System.Diagnostics.Debug.WriteLine($"  Command: {_commandCharacteristic.Id}");
                System.Diagnostics.Debug.WriteLine($"  Notification: {_notificationCharacteristic.Id}");

                // Enable notifications 
                System.Diagnostics.Debug.WriteLine("Enabling notifications...");
                await _notificationCharacteristic.StartUpdatesAsync();
                _notificationCharacteristic.ValueUpdated += OnNotificationReceived;

                System.Diagnostics.Debug.WriteLine("MetaWear device connected successfully!");

                // Mark as connected only after successful service/characteristic discovery
                _isDeviceConnected = true;
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in ConnectToDeviceAsync: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                _isDeviceConnected = false;
                return false;
            }
        }

        public async Task DisconnectAsync()
        {
            try
            {
                // Stop notifications first 
                if (_notificationCharacteristic != null)
                {
                    _notificationCharacteristic.ValueUpdated -= OnNotificationReceived;
                    try
                    {
                        await _notificationCharacteristic.StopUpdatesAsync();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error stopping notifications: {ex.Message}");
                    }
                }

                // Disconnect device 
                if (_device != null && _isDeviceConnected)
                {
                    try
                    {
                        await _adapter.DisconnectDeviceAsync(_device);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error disconnecting device: {ex.Message}");
                    }
                }

                // Clear all references
                _isDeviceConnected = false;
                _device = null;
                _metaWearService = null;
                _commandCharacteristic = null;
                _notificationCharacteristic = null;
                _cachedDeviceInfo = null;
                _accelerometerActive = false;
                _gyroscopeActive = false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in DisconnectAsync: {ex.Message}");
            }
        }

        private void OnNotificationReceived(object? sender, Plugin.BLE.Abstractions.EventArgs.CharacteristicUpdatedEventArgs e)
        {
            // Get data from characteristic value 
            byte[]? data = e.Characteristic.Value;
            if (data == null || data.Length < 2)
                return;

            // Parse MetaWear notification data
            // Format: [module_id, register_id, ...data]
            byte moduleId = data[0];
            byte registerId = data[1];

            // Accelerometer module ID: 0x03
            // Gyroscope module ID: 0x13
            if (moduleId == 0x03 && _accelerometerActive)
            {
                ParseAccelerometerData(data);
            }
            else if (moduleId == 0x13 && _gyroscopeActive)
            {
                ParseGyroscopeData(data);
            }
        }

        private void ParseAccelerometerData(byte[] data)
        {
            if (data.Length < 8)
                return;

            // Parse accelerometer data (typically 3-axis, 16-bit signed integers)
            short x = BitConverter.ToInt16(data, 2);
            short y = BitConverter.ToInt16(data, 4);
            short z = BitConverter.ToInt16(data, 6);

            // Convert to G (assuming 16G range, adjust based on actual configuration)
            float xG = x / 4096.0f;
            float yG = y / 4096.0f;
            float zG = z / 4096.0f;

            AccelerometerDataReceived?.Invoke(this, new MetaWearAccelerometerData
            {
                X = xG,
                Y = yG,
                Z = zG,
                Timestamp = DateTime.Now
            });
        }

        private void ParseGyroscopeData(byte[] data)
        {
            if (data.Length < 8)
                return;

            // Parse gyroscope data (typically 3-axis, 16-bit signed integers)
            short x = BitConverter.ToInt16(data, 2);
            short y = BitConverter.ToInt16(data, 4);
            short z = BitConverter.ToInt16(data, 6);

            // Convert to degrees/sec (assuming 2000 dps range, adjust based on actual configuration)
            float xDps = x / 16.0f;
            float yDps = y / 16.0f;
            float zDps = z / 16.0f;

            GyroscopeDataReceived?.Invoke(this, new MetaWearGyroscopeData
            {
                X = xDps,
                Y = yDps,
                Z = zDps,
                Timestamp = DateTime.Now
            });
        }

        public async Task StartAccelerometerAsync(float sampleRate = 50f, float range = 16f)
        {
            if (_commandCharacteristic == null || !IsConnected)
                throw new InvalidOperationException("Device not connected");

            try
            {
                // MetaWear command to configure accelerometer
                // Module ID: 0x03 (Accelerometer)
                // Register ID: 0x03 (Data)
                // This is a simplified implementation - actual commands depend on MetaWear API
                
                // Configure accelerometer range and sample rate
                byte[] configCommand = new byte[]
                {
                    0x03, 0x03, // Module ID, Register ID
                    0x00,       // Configuration bytes (simplified)
                };

                // Write command 
                await _commandCharacteristic.WriteAsync(configCommand);
                _accelerometerActive = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error starting accelerometer: {ex.Message}");
                throw;
            }
        }

        public async Task StopAccelerometerAsync()
        {
            if (_commandCharacteristic == null || !IsConnected)
                return;

            try
            {
                // Stop accelerometer 
                byte[] stopCommand = new byte[] { 0x03, 0x01 }; // Module ID, Stop command
                await _commandCharacteristic.WriteAsync(stopCommand);
                _accelerometerActive = false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error stopping accelerometer: {ex.Message}");
                _accelerometerActive = false; // Reset flag even if write fails
            }
        }

        public async Task StartGyroscopeAsync(float sampleRate = 100f, float range = 2000f)
        {
            if (_commandCharacteristic == null || !IsConnected)
                throw new InvalidOperationException("Device not connected");

            try
            {
                // MetaWear command to configure gyroscope
                // Module ID: 0x13 (Gyroscope)
                // Register ID: 0x03 (Data)
                
                byte[] configCommand = new byte[]
                {
                    0x13, 0x03, // Module ID, Register ID
                    0x00,       // Configuration bytes 
                };

                // Write command 
                await _commandCharacteristic.WriteAsync(configCommand);
                _gyroscopeActive = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error starting gyroscope: {ex.Message}");
                throw;
            }
        }

        public async Task StopGyroscopeAsync()
        {
            if (_commandCharacteristic == null || !IsConnected)
                return;

            try
            {
                // Stop gyroscope 
                byte[] stopCommand = new byte[] { 0x13, 0x01 }; // Module ID, Stop command
                await _commandCharacteristic.WriteAsync(stopCommand);
                _gyroscopeActive = false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error stopping gyroscope: {ex.Message}");
                _gyroscopeActive = false; // Reset flag even if write fails
            }
        }

        public async Task<DeviceInfo> GetDeviceInfoAsync()
        {
            if (_cachedDeviceInfo != null)
                return _cachedDeviceInfo;

            if (_device == null || !IsConnected)
                throw new InvalidOperationException("Device not connected");

            try
            {
                var services = await _device.GetServicesAsync();
                var deviceInfoService = services.FirstOrDefault(s => s.Id == DeviceInformationServiceUuid);

                if (deviceInfoService == null)
                {
                    return new DeviceInfo { Manufacturer = "MbientLab" };
                }

                var characteristics = await deviceInfoService.GetCharacteristicsAsync();
                
                var modelNumber = await ReadStringCharacteristic(characteristics, ModelNumberCharacteristicUuid);
                var serialNumber = await ReadStringCharacteristic(characteristics, SerialNumberCharacteristicUuid);
                var firmwareVersion = await ReadStringCharacteristic(characteristics, FirmwareRevisionCharacteristicUuid);
                var hardwareVersion = await ReadStringCharacteristic(characteristics, HardwareRevisionCharacteristicUuid);
                var manufacturer = await ReadStringCharacteristic(characteristics, ManufacturerNameCharacteristicUuid);

                _cachedDeviceInfo = new DeviceInfo
                {
                    Model = modelNumber ?? "Unknown",
                    SerialNumber = serialNumber ?? "Unknown",
                    FirmwareVersion = firmwareVersion ?? "Unknown",
                    HardwareVersion = hardwareVersion ?? "Unknown",
                    Manufacturer = manufacturer ?? "MbientLab"
                };

                return _cachedDeviceInfo;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error reading device info: {ex.Message}");
                return new DeviceInfo { Manufacturer = "MbientLab" };
            }
        }

        private async Task<string?> ReadStringCharacteristic(IEnumerable<ICharacteristic> characteristics, Guid uuid)
        {
            try
            {
                var characteristic = characteristics.FirstOrDefault(c => c.Id == uuid);
                if (characteristic == null)
                    return null;

                // Check if characteristic can be read
                if (!characteristic.CanRead)
                    return null;

                // ReadAsync returns Task<(byte[] data, int resultCode)> - tuple with data and result code
                // Plugin.BLE ReadAsync() returns a tuple: (byte[] data, int resultCode)
                var (data, resultCode) = await characteristic.ReadAsync();
                
                // Check result code (0 typically means success)
                if (resultCode != 0 || data == null)
                    return null;
                
                // Check if array is empty
                if (data.Length == 0)
                    return null;

                // Convert byte array to string
                // TrimEnd removes null terminators that are common in BLE string characteristics
                var result = System.Text.Encoding.UTF8.GetString(data);
                return result.TrimEnd('\0');
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error reading characteristic {uuid}: {ex.Message}");
                return null;
            }
        }

        public async Task ResetAsync()
        {
            if (_commandCharacteristic == null || !IsConnected)
                throw new InvalidOperationException("Device not connected");

            try
            {
                // MetaWear reset command 
                byte[] resetCommand = new byte[] { 0x0F, 0x0A }; // System module, Reset command
                await _commandCharacteristic.WriteAsync(resetCommand);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error resetting device: {ex.Message}");
                throw;
            }
        }
    }
}

