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
        private bool _magnetometerActive;
        private bool _lightSensorActive;
        private bool _isDeviceConnected; // Track connection state manually (cross-platform)
        private bool _notificationHandlerAttached; // Track if notification handler is attached

        public event EventHandler<string> DeviceDisconnected;
        public event EventHandler<MetaWearAccelerometerData> AccelerometerDataReceived;
        public event EventHandler<MetaWearGyroscopeData> GyroscopeDataReceived;
        public event EventHandler<MetaWearMagnetometerData> MagnetometerDataReceived;
        public event EventHandler<MetaWearLightSensorData> LightSensorDataReceived;

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
                _notificationHandlerAttached = false;
                
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
                
                // Attach handler BEFORE starting updates to avoid missing data
                // Remove handler first to prevent duplicate attachments
                if (_notificationHandlerAttached)
                {
                    System.Diagnostics.Debug.WriteLine("Removing existing notification handler...");
                    _notificationCharacteristic.ValueUpdated -= OnNotificationReceived;
                    _notificationHandlerAttached = false;
                }
                
                _notificationCharacteristic.ValueUpdated += OnNotificationReceived;
                _notificationHandlerAttached = true;
                
                // Start receiving notifications
                await _notificationCharacteristic.StartUpdatesAsync();
                
                System.Diagnostics.Debug.WriteLine("Notifications enabled successfully");

                // Stop any active sensors from previous sessions (light sensor, magnetometer, etc.)
                await StopAllSensorsAsync();

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
                    if (_notificationHandlerAttached)
                    {
                        _notificationCharacteristic.ValueUpdated -= OnNotificationReceived;
                        _notificationHandlerAttached = false;
                    }
                    
                    try
                    {
                        await _notificationCharacteristic.StopUpdatesAsync();
                        System.Diagnostics.Debug.WriteLine("Notifications stopped successfully");
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
                _magnetometerActive = false;
                _lightSensorActive = false;
                _notificationHandlerAttached = false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in DisconnectAsync: {ex.Message}");
            }
        }

        /// <summary>
        /// Stops all sensors (light sensor, magnetometer, accelerometer, gyroscope) that might be active from previous sessions.
        /// Called immediately after connection to ensure a clean state.
        /// </summary>
        private async Task StopAllSensorsAsync()
        {
            // Note: We check _commandCharacteristic directly since _isDeviceConnected hasn't been set yet
            if (_commandCharacteristic == null || _device == null)
            {
                System.Diagnostics.Debug.WriteLine("[Connection] Cannot stop sensors - command characteristic or device not available");
                return;
            }

            try
            {
                System.Diagnostics.Debug.WriteLine("[Connection] Stopping all sensors from previous sessions...");

                // Stop light sensor (module 0x14) - it's often active by default
                try
                {
                    System.Diagnostics.Debug.WriteLine("[Connection] Stopping light sensor (module 0x14)...");
                    // Stop command: [module_id, register_0x01]
                    byte[] stopLightSensor = new byte[] { 0x14, 0x01 };
                    await _commandCharacteristic.WriteAsync(stopLightSensor);
                    await Task.Delay(100); // Wait for command to process
                    
                    // Also try stopping the data producer if it's using register 0x03
                    byte[] stopLightSensorProducer = new byte[] { 0x14, 0x03 };
                    await _commandCharacteristic.WriteAsync(stopLightSensorProducer);
                    await Task.Delay(100);
                    System.Diagnostics.Debug.WriteLine("[Connection] Light sensor stop commands sent");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Connection] Error stopping light sensor: {ex.Message}");
                }

                // Stop magnetometer (module 0x15) - it might also be active
                try
                {
                    System.Diagnostics.Debug.WriteLine("[Connection] Stopping magnetometer (module 0x15)...");
                    // Stop command: [module_id, register_0x01]
                    byte[] stopMagnetometer = new byte[] { 0x15, 0x01 };
                    await _commandCharacteristic.WriteAsync(stopMagnetometer);
                    await Task.Delay(100);
                    
                    // Also try stopping the data producer if it's using register 0x05
                    byte[] stopMagnetometerProducer = new byte[] { 0x15, 0x05 };
                    await _commandCharacteristic.WriteAsync(stopMagnetometerProducer);
                    await Task.Delay(100);
                    System.Diagnostics.Debug.WriteLine("[Connection] Magnetometer stop commands sent");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Connection] Error stopping magnetometer: {ex.Message}");
                }

                // Stop accelerometer if active (shouldn't be, but just in case)
                if (_accelerometerActive)
                {
                    try
                    {
                        await StopAccelerometerAsync();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[Connection] Error stopping accelerometer: {ex.Message}");
                    }
                }

                // Stop gyroscope if active (shouldn't be, but just in case)
                if (_gyroscopeActive)
                {
                    try
                    {
                        await StopGyroscopeAsync();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[Connection] Error stopping gyroscope: {ex.Message}");
                    }
                }

                // Stop magnetometer if active
                if (_magnetometerActive)
                {
                    try
                    {
                        await StopMagnetometerAsync();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[Connection] Error stopping magnetometer: {ex.Message}");
                    }
                }

                // Stop light sensor if active
                if (_lightSensorActive)
                {
                    try
                    {
                        await StopLightSensorAsync();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[Connection] Error stopping light sensor: {ex.Message}");
                    }
                }

                System.Diagnostics.Debug.WriteLine("[Connection] All sensors stopped");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Connection] Error in StopAllSensorsAsync: {ex.Message}");
                // Don't throw - connection should still succeed even if we can't stop sensors
            }
        }

        private void OnNotificationReceived(object? sender, Plugin.BLE.Abstractions.EventArgs.CharacteristicUpdatedEventArgs e)
        {
            try
            {
                // Get data from characteristic value 
                byte[]? data = e.Characteristic.Value;
                if (data == null || data.Length < 2)
                {
                    System.Diagnostics.Debug.WriteLine($"[Notification] Received null or too small data (length: {data?.Length ?? 0})");
                    return;
                }

                // Parse MetaWear notification data
                // Format: [module_id, register_id, ...data]
                byte moduleId = data[0];
                byte registerId = data[1];

                // Reduced logging - only log occasionally to avoid performance issues
                // Data comes at 50-100Hz, so logging every single packet causes lag
                if (data.Length <= 20 && (DateTime.Now.Millisecond % 1000) < 10) // Only log ~1% of packets
                {
                    System.Diagnostics.Debug.WriteLine($"[Notification] Module: 0x{moduleId:X2}, Reg: 0x{registerId:X2}, Len: {data.Length}");
                }

                // MetaWear module IDs (correct mapping for MetaMotionS - MMS):
                // Accelerometer: 0x03 (BMI270 on MMS, or BMA255 on other devices)
                // Gyroscope: 0x13 (BMI270 on MMS, or BMI160 on other devices)
                // Light Sensor: 0x14 (LTR-329ALS-01)
                // Magnetometer: 0x15 (BMM150)
                // Note: 0x14 and 0x15 are NOT accelerometer/gyroscope - they're light sensor and magnetometer!
                // Reference: https://mbientlab.com/tutorials/MetaMotionS.html
                bool isAccelerometer = (moduleId == 0x03);
                bool isGyroscope = (moduleId == 0x13);
                bool isLightSensor = (moduleId == 0x14);
                bool isMagnetometer = (moduleId == 0x15);
                
                if (isAccelerometer)
                {
                    // Always log accelerometer notifications for debugging (even if not active) to verify we're receiving data
                    System.Diagnostics.Debug.WriteLine($"[Accelerometer] DEBUG: Received accelerometer notification! Module: 0x{data[0]:X2}, Reg: 0x{data[1]:X2}, Len: {data.Length}, Active: {_accelerometerActive}");
                    System.Diagnostics.Debug.WriteLine($"[Accelerometer] DEBUG: Raw data: [{string.Join(", ", data.Select(b => $"0x{b:X2}"))}]");
                    
                    if (_accelerometerActive)
                    {
                        // Parse without logging every packet (too verbose at 100Hz)
                        ParseAccelerometerData(data);
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"[Accelerometer] DEBUG: Accelerometer not active, ignoring data");
                    }
                }
                else if (isGyroscope)
                {
                    if (_gyroscopeActive)
                    {
                        // Parse without logging every packet (too verbose at 100Hz)
                        ParseGyroscopeData(data);
                    }
                    // Silently ignore if not active
                }
                else if (isMagnetometer)
                {
                    if (_magnetometerActive)
                    {
                        ParseMagnetometerData(data);
                    }
                    // Silently ignore if not active
                }
                else if (isLightSensor)
                {
                    if (_lightSensorActive)
                    {
                        ParseLightSensorData(data);
                    }
                    // Silently ignore if not active
                }
                else
                {
                    // Only log unknown module IDs occasionally (not every packet)
                    if ((DateTime.Now.Millisecond % 5000) < 10) // Log once per 5 seconds max
                    {
                        System.Diagnostics.Debug.WriteLine($"[Notification] Unknown module ID: 0x{moduleId:X2}, Register: 0x{registerId:X2}, Length: {data.Length}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Notification] Error processing notification: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }

        private void ParseAccelerometerData(byte[] data)
        {
            try
            {
                // MetaWear accelerometer data format can vary:
                // Format 1 (8 bytes): [module_id, register_id, x_low, x_high, y_low, y_high, z_low, z_high]
                // Format 2 (6 bytes): [module_id, register_id, x_low, x_high, y_low, y_high] (might be 2-axis or compressed)
                // Format 3 (other): Device-specific formats
                
                byte moduleId = data[0];
                short x, y, z;
                
                if (data.Length >= 8)
                {
                    // Standard 3-axis format: [module_id, register_id, x_low, x_high, y_low, y_high, z_low, z_high]
                    x = BitConverter.ToInt16(data, 2);
                    y = BitConverter.ToInt16(data, 4);
                    z = BitConverter.ToInt16(data, 6);
                }
                else if (data.Length >= 6)
                {
                    // 2-axis format or different byte order: [module_id, register_id, x_low, x_high, y_low, y_high]
                    // Some MetaWear devices send data in a compressed format
                    // Try parsing as 2-axis first, then attempt 3-axis with different byte order
                    x = BitConverter.ToInt16(data, 2);
                    y = BitConverter.ToInt16(data, 4);
                    z = 0; // Z axis not available in 6-byte format
                }
                else
                {
                    // Data too short - silently skip (logging causes performance issues)
                    return;
                }

                // Convert to G (assuming 16G range, adjust based on actual configuration)
                // MetaWear typically uses 4096 LSB/g for ±16G range (BMA255)
                // BMI160 might use different scaling, so we'll use 4096 as default
                float xG = x / 4096.0f;
                float yG = y / 4096.0f;
                float zG = z / 4096.0f;

                // Reduced logging - only log occasionally (every 2 seconds) to avoid performance issues
                if ((DateTime.Now.Millisecond % 2000) < 10)
                {
                    System.Diagnostics.Debug.WriteLine($"[Accelerometer] Data - X: {xG:F3}g, Y: {yG:F3}g, Z: {zG:F3}g");
                }

                AccelerometerDataReceived?.Invoke(this, new MetaWearAccelerometerData
                {
                    X = xG,
                    Y = yG,
                    Z = zG,
                    Timestamp = DateTime.Now
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Accelerometer] Error parsing data: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[Accelerometer] Raw data: [{string.Join(", ", data.Select(b => $"0x{b:X2}"))}]");
            }
        }

        private void ParseGyroscopeData(byte[] data)
        {
            try
            {
                // MetaWear gyroscope data format:
                // Format 1 (8 bytes): [module_id, register_id, x_low, x_high, y_low, y_high, z_low, z_high]
                // Format 2 (other): Device-specific formats
                
                byte moduleId = data[0];
                short x, y, z;
                
                if (data.Length >= 8)
                {
                    // Standard 3-axis format: [module_id, register_id, x_low, x_high, y_low, y_high, z_low, z_high]
                    x = BitConverter.ToInt16(data, 2);
                    y = BitConverter.ToInt16(data, 4);
                    z = BitConverter.ToInt16(data, 6);
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[Gyroscope] Data too short: {data.Length} bytes (need at least 8)");
                    System.Diagnostics.Debug.WriteLine($"[Gyroscope] Raw data: [{string.Join(", ", data.Select(b => $"0x{b:X2}"))}]");
                    return;
                }

                // Convert to degrees/sec (assuming 2000 dps range, adjust based on actual configuration)
                // MetaWear BMI160 typically uses 16 LSB/(°/s) for ±2000 dps range
                float xDps = x / 16.0f;
                float yDps = y / 16.0f;
                float zDps = z / 16.0f;

                // Reduced logging - only log occasionally (every 2 seconds) to avoid performance issues
                if ((DateTime.Now.Millisecond % 2000) < 10)
                {
                    System.Diagnostics.Debug.WriteLine($"[Gyroscope] Data - X: {xDps:F2}°/s, Y: {yDps:F2}°/s, Z: {zDps:F2}°/s");
                }

                GyroscopeDataReceived?.Invoke(this, new MetaWearGyroscopeData
                {
                    X = xDps,
                    Y = yDps,
                    Z = zDps,
                    Timestamp = DateTime.Now
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Gyroscope] Error parsing data: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[Gyroscope] Raw data: [{string.Join(", ", data.Select(b => $"0x{b:X2}"))}]");
            }
        }

        public async Task StartAccelerometerAsync(float sampleRate = 50f, float range = 16f)
        {
            if (_commandCharacteristic == null || !IsConnected)
                throw new InvalidOperationException("Device not connected");

            try
            {
                System.Diagnostics.Debug.WriteLine($"[Accelerometer] Starting accelerometer - SampleRate: {sampleRate}Hz, Range: {range}G");
                
                // Stop accelerometer first if already running
                if (_accelerometerActive)
                {
                    System.Diagnostics.Debug.WriteLine($"[Accelerometer] Stopping existing accelerometer first...");
                    await StopAccelerometerAsync();
                    await Task.Delay(100); // Small delay between stop and start
                }
                
                // Stop light sensor (0x14) if it's interfering - light sensor might be sending data
                try
                {
                    System.Diagnostics.Debug.WriteLine($"[Accelerometer] Stopping light sensor (module 0x14) to prevent interference...");
                    // Try stopping by disabling data route (register 0x02 or 0x04 might disable routes)
                    byte[] stopLightSensor = new byte[] { 0x14, 0x02 }; // Module 0x14, Stop/disable routes
                    await _commandCharacteristic.WriteAsync(stopLightSensor);
                    await Task.Delay(50);
                    // Also try register 0x01 (stop)
                    byte[] stopLightSensor2 = new byte[] { 0x14, 0x01 };
                    await _commandCharacteristic.WriteAsync(stopLightSensor2);
                    await Task.Delay(50);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Accelerometer] Error stopping light sensor: {ex.Message}");
                }

                // MetaWear command to configure and enable accelerometer
                // Module ID: 0x03 (Accelerometer - BMI270 on MMS, BMA255 on other devices)
                // Reference: https://mbientlab.com/tutorials/MetaMotionS.html
                
                // Step 1: Configure the accelerometer (register 0x04 - configuration register)
                // Configuration byte format depends on sensor type
                // For BMI270 (MMS) or BMA255 (other devices): [odr (4 bits), range (2 bits), unused (2 bits)]
                // ODR: 0 = 125Hz, 1 = 250Hz, 2 = 500Hz, 3 = 1000Hz, 4 = 2000Hz
                // Range: 0 = ±2G, 1 = ±4G, 2 = ±8G, 3 = ±16G
                // Reference: https://mbientlab.com/tutorials/MetaMotionS.html
                byte odr = sampleRate switch
                {
                    <= 125f => 0,
                    <= 250f => 1,
                    <= 500f => 2,
                    <= 1000f => 3,
                    _ => 4
                };
                byte rangeConfig = range switch
                {
                    <= 2f => 0,
                    <= 4f => 1,
                    <= 8f => 2,
                    _ => 3
                };
                byte configByte = (byte)((odr << 4) | (rangeConfig << 2));
                
                byte[] configCommand = new byte[]
                {
                    0x03, 0x04, // Module ID: 0x03 (Accelerometer - BMI270 on MMS), Register ID: 0x04 (Configuration)
                    configByte
                };
                
                System.Diagnostics.Debug.WriteLine($"[Accelerometer] Sending config command: [{string.Join(", ", configCommand.Select(b => $"0x{b:X2}"))}] (ODR={odr}, Range={rangeConfig})");
                
                // Write configuration first
                await _commandCharacteristic.WriteAsync(configCommand);
                await Task.Delay(50); // Allow configuration to take effect
                
                // Step 2: Enable the accelerometer sensor module first (power on)
                // For BMI270, we need to enable the sensor module before creating routes
                try
                {
                    // Enable accelerometer module (register 0x01 = enable/power on)
                    byte[] enableModule = new byte[] { 0x03, 0x01, 0x01 }; // Module 0x03, Register 0x01, Enable
                    System.Diagnostics.Debug.WriteLine($"[Accelerometer] Enabling accelerometer module: [{string.Join(", ", enableModule.Select(b => $"0x{b:X2}"))}]");
                    await _commandCharacteristic.WriteAsync(enableModule);
                    await Task.Delay(100); // Give it time to power on
                }
                catch (Exception enableEx)
                {
                    System.Diagnostics.Debug.WriteLine($"[Accelerometer] Error enabling module: {enableEx.Message}");
                }
                
                // Step 3: Create route using Route Manager (module 0x12) for BMI270
                // For BMI270, we need to create a route first, then enable the data producer
                // Route Manager: module 0x12, register 0x03 (create route)
                // Format: [0x12, 0x03, producer_module, producer_register, route_id, endpoint_type, endpoint_id]
                // For BMI270 accelerometer, the route uses register 0x03 as the producer (same pattern as gyroscope uses 0x03)
                // Note: Even though notifications show register 0x04, the route creation uses 0x03 for the producer
                try
                {
                    System.Diagnostics.Debug.WriteLine($"[Accelerometer] Creating route using Route Manager...");
                    // Create route: accelerometer (0x03) acceleration data (0x03 = acceleration data producer for BMI270) -> route 0x01 -> stream to notifications
                    // Route Manager command format: [0x12, 0x03, producer_module, producer_register, route_id, endpoint_type, endpoint_id]
                    // Endpoint type: 0x01 = stream, endpoint_id: 0x00 = default notifications
                    // Pattern matches gyroscope: gyro route uses 0x13, 0x03 even though notifications show 0x13, 0x04
                    byte[] createRouteCommand = new byte[]
                    {
                        0x12, 0x03,  // Route Manager (module 0x12, register 0x03 - create route)
                        0x03, 0x03,  // Producer: accelerometer (0x03), data producer register (0x03 = acceleration data producer for BMI270)
                        0x01,        // Route ID: 0x01
                        0x01,        // Endpoint type: 0x01 = stream (notifications)
                        0x00         // Endpoint ID: 0x00 = default
                    };
                    
                    System.Diagnostics.Debug.WriteLine($"[Accelerometer] Creating route: [{string.Join(", ", createRouteCommand.Select(b => $"0x{b:X2}"))}]");
                    await _commandCharacteristic.WriteAsync(createRouteCommand);
                    await Task.Delay(200); // Wait longer for route creation
                }
                catch (Exception routeEx)
                {
                    System.Diagnostics.Debug.WriteLine($"[Accelerometer] Error creating route: {routeEx.Message}");
                    throw; // Don't continue if route creation fails
                }
                
                // Step 4: Enable the accelerometer data producer (register 0x03 for BMI270 acceleration data)
                // For BMI270, register 0x03 is the data producer that generates acceleration data
                // After creating the route, we need to subscribe to/enable the data producer
                // Pattern matches gyroscope: gyro enable uses 0x13, 0x03 even though notifications show 0x13, 0x04
                byte[] enableProducer = new byte[]
                {
                    0x03, 0x03, 0x01  // Module ID: 0x03 (Accelerometer), Register ID: 0x03 (Acceleration data producer), Route ID: 0x01
                };

                System.Diagnostics.Debug.WriteLine($"[Accelerometer] Enabling accelerometer data producer: [{string.Join(", ", enableProducer.Select(b => $"0x{b:X2}"))}]");
                
                // Add debug logging to check for notifications
                System.Diagnostics.Debug.WriteLine($"[Accelerometer] DEBUG: Accelerometer active flag will be set to true");
                System.Diagnostics.Debug.WriteLine($"[Accelerometer] DEBUG: Notification handler attached: {_notificationHandlerAttached}");
                
                // Verify notification handler is still attached
                if (!_notificationHandlerAttached && _notificationCharacteristic != null)
                {
                    System.Diagnostics.Debug.WriteLine($"[Accelerometer] WARNING: Notification handler not attached! Re-attaching...");
                    _notificationCharacteristic.ValueUpdated -= OnNotificationReceived;
                    _notificationCharacteristic.ValueUpdated += OnNotificationReceived;
                    _notificationHandlerAttached = true;
                    
                    // Re-enable notifications if needed
                    try
                    {
                        await _notificationCharacteristic.StartUpdatesAsync();
                        System.Diagnostics.Debug.WriteLine($"[Accelerometer] Notifications re-enabled");
                    }
                    catch (Exception notifEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"[Accelerometer] Error re-enabling notifications: {notifEx.Message}");
                    }
                }
                
                // Write enable producer command 
                await _commandCharacteristic.WriteAsync(enableProducer);
                
                // Small delay to allow command to process
                await Task.Delay(100); // Increased delay for device to process command
                
                _accelerometerActive = true;
                System.Diagnostics.Debug.WriteLine($"[Accelerometer] Accelerometer started successfully");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Accelerometer] Error starting accelerometer: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                _accelerometerActive = false;
                throw;
            }
        }

        public async Task StopAccelerometerAsync()
        {
            if (_commandCharacteristic == null || !IsConnected)
            {
                _accelerometerActive = false;
                return;
            }

            try
            {
                System.Diagnostics.Debug.WriteLine($"[Accelerometer] Stopping accelerometer...");
                
                // Stop accelerometer - try multiple methods
                // Method 1: Stop data producer (register 0x05 - same as start)
                byte[] stopProducerCommand = new byte[] { 0x03, 0x05 }; // Stop acceleration data producer
                System.Diagnostics.Debug.WriteLine($"[Accelerometer] Sending stop producer command (0x05): [{string.Join(", ", stopProducerCommand.Select(b => $"0x{b:X2}"))}]");
                await _commandCharacteristic.WriteAsync(stopProducerCommand);
                await Task.Delay(50);
                
                // Method 2: Stop command (register 0x01) - general stop
                byte[] stopCommand1 = new byte[] { 0x03, 0x01 };
                System.Diagnostics.Debug.WriteLine($"[Accelerometer] Sending stop command (0x01): [{string.Join(", ", stopCommand1.Select(b => $"0x{b:X2}"))}]");
                await _commandCharacteristic.WriteAsync(stopCommand1);
                await Task.Delay(50);
                
                // Method 3: Remove route using Route Manager (module 0x12, register 0x02)
                try
                {
                    byte[] removeRouteCommand = new byte[] { 0x12, 0x02, 0x01 }; // Route Manager, remove route 0x01
                    System.Diagnostics.Debug.WriteLine($"[Accelerometer] Removing route: [{string.Join(", ", removeRouteCommand.Select(b => $"0x{b:X2}"))}]");
                    await _commandCharacteristic.WriteAsync(removeRouteCommand);
                    await Task.Delay(50);
                }
                catch (Exception ex2)
                {
                    System.Diagnostics.Debug.WriteLine($"[Accelerometer] Error removing route: {ex2.Message}");
                }
                
                _accelerometerActive = false;
                System.Diagnostics.Debug.WriteLine($"[Accelerometer] Accelerometer stopped successfully");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Accelerometer] Error stopping accelerometer: {ex.Message}");
                _accelerometerActive = false; // Reset flag even if write fails
            }
        }

        public async Task StartGyroscopeAsync(float sampleRate = 100f, float range = 2000f)
        {
            if (_commandCharacteristic == null || !IsConnected)
                throw new InvalidOperationException("Device not connected");

            try
            {
                System.Diagnostics.Debug.WriteLine($"[Gyroscope] Starting gyroscope - SampleRate: {sampleRate}Hz, Range: {range} dps");
                
                // Stop gyroscope first if already running
                if (_gyroscopeActive)
                {
                    System.Diagnostics.Debug.WriteLine($"[Gyroscope] Stopping existing gyroscope first...");
                    await StopGyroscopeAsync();
                    await Task.Delay(100); // Small delay between stop and start
                }
                
                // Stop magnetometer (0x15) if it's interfering - magnetometer might be sending data
                try
                {
                    System.Diagnostics.Debug.WriteLine($"[Gyroscope] Stopping magnetometer (module 0x15) to prevent interference...");
                    // Try stopping by disabling data route (register 0x02 or 0x04 might disable routes)
                    byte[] stopMagnetometer = new byte[] { 0x15, 0x02 }; // Module 0x15, Stop/disable routes
                    await _commandCharacteristic.WriteAsync(stopMagnetometer);
                    await Task.Delay(50);
                    // Also try register 0x01 (stop)
                    byte[] stopMagnetometer2 = new byte[] { 0x15, 0x01 };
                    await _commandCharacteristic.WriteAsync(stopMagnetometer2);
                    await Task.Delay(50);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Gyroscope] Error stopping magnetometer: {ex.Message}");
                }

                // MetaWear command to configure and enable gyroscope
                // Module ID: 0x13 (Gyroscope - BMI270 on MMS, BMI160 on other devices)
                // Reference: https://mbientlab.com/tutorials/MetaMotionS.html
                
                // Step 1: Configure the gyroscope (register 0x04 - configuration register)
                // For BMI270 (MMS) or BMI160 (other devices): [odr (4 bits), range (3 bits), ...]
                // ODR: 0 = 25Hz, 1 = 50Hz, 2 = 100Hz, 3 = 200Hz, 4 = 400Hz, 5 = 800Hz, 6 = 1600Hz, 7 = 3200Hz
                // Range: 0 = ±125°/s, 1 = ±250°/s, 2 = ±500°/s, 3 = ±1000°/s, 4 = ±2000°/s
                // Reference: https://mbientlab.com/tutorials/MetaMotionS.html
                byte odr = sampleRate switch
                {
                    <= 25f => 0,
                    <= 50f => 1,
                    <= 100f => 2,
                    <= 200f => 3,
                    <= 400f => 4,
                    <= 800f => 5,
                    <= 1600f => 6,
                    _ => 7
                };
                byte rangeConfig = range switch
                {
                    <= 125f => 0,
                    <= 250f => 1,
                    <= 500f => 2,
                    <= 1000f => 3,
                    _ => 4
                };
                byte configByte = (byte)((odr << 4) | rangeConfig);
                
                byte[] configCommand = new byte[]
                {
                    0x13, 0x04, // Module ID: 0x13 (Gyroscope - BMI270 on MMS), Register ID: 0x04 (Configuration)
                    configByte
                };
                
                System.Diagnostics.Debug.WriteLine($"[Gyroscope] Sending config command: [{string.Join(", ", configCommand.Select(b => $"0x{b:X2}"))}] (ODR={odr}, Range={rangeConfig})");
                
                // Write configuration first
                await _commandCharacteristic.WriteAsync(configCommand);
                await Task.Delay(50); // Allow configuration to take effect
                
                // Step 2: Create route using Route Manager (module 0x12) for BMI270
                // For BMI270 gyroscope, we need to create a route first
                try
                {
                    System.Diagnostics.Debug.WriteLine($"[Gyroscope] Creating route using Route Manager...");
                    // Create route: gyroscope (0x13) angular velocity data (0x03 = data producer) -> route 0x02 -> stream to notifications
                    // For BMI270, the angular velocity data comes from register 0x03 (data producer), not 0x05
                    // Route Manager command format: [0x12, 0x03, producer_module, producer_register, route_id, endpoint_type, endpoint_id]
                    byte[] createRouteCommand = new byte[]
                    {
                        0x12, 0x03,  // Route Manager (module 0x12, register 0x03 - create route)
                        0x13, 0x03,  // Producer: gyroscope (0x13), data producer (0x03) - NOT 0x05 for BMI270
                        0x02,        // Route ID: 0x02 (different from accelerometer route)
                        0x01,        // Endpoint type: 0x01 = stream (notifications)
                        0x00         // Endpoint ID: 0x00 = default
                    };
                    
                    System.Diagnostics.Debug.WriteLine($"[Gyroscope] Creating route: [{string.Join(", ", createRouteCommand.Select(b => $"0x{b:X2}"))}]");
                    await _commandCharacteristic.WriteAsync(createRouteCommand);
                    await Task.Delay(150); // Wait for route creation
                }
                catch (Exception routeEx)
                {
                    System.Diagnostics.Debug.WriteLine($"[Gyroscope] Error creating route: {routeEx.Message}");
                    // Continue anyway
                }
                
                // Step 3: Enable the gyroscope sensor module first (power on)
                // For BMI270, we need to enable the sensor module before enabling the data producer
                try
                {
                    // Enable gyroscope module (register 0x01 = enable/power on)
                    byte[] enableModule = new byte[] { 0x13, 0x01, 0x01 }; // Module 0x13, Register 0x01, Enable
                    System.Diagnostics.Debug.WriteLine($"[Gyroscope] Enabling gyroscope module: [{string.Join(", ", enableModule.Select(b => $"0x{b:X2}"))}]");
                    await _commandCharacteristic.WriteAsync(enableModule);
                    await Task.Delay(50);
                }
                catch (Exception enableEx)
                {
                    System.Diagnostics.Debug.WriteLine($"[Gyroscope] Error enabling module (might not be needed): {enableEx.Message}");
                }
                
                // Step 4: Enable the gyroscope data producer (register 0x03 for BMI270 angular velocity data)
                // For BMI270, register 0x03 is the data producer that generates angular velocity data
                // After creating the route, we need to subscribe to/enable the data producer
                byte[] enableProducer = new byte[]
                {
                    0x13, 0x03, 0x02  // Module ID: 0x13 (Gyroscope), Register ID: 0x03 (Data producer), Route ID: 0x02
                };

                System.Diagnostics.Debug.WriteLine($"[Gyroscope] Enabling gyroscope data producer: [{string.Join(", ", enableProducer.Select(b => $"0x{b:X2}"))}]");
                
                // Verify notification handler is still attached
                if (!_notificationHandlerAttached && _notificationCharacteristic != null)
                {
                    System.Diagnostics.Debug.WriteLine($"[Gyroscope] WARNING: Notification handler not attached! Re-attaching...");
                    _notificationCharacteristic.ValueUpdated -= OnNotificationReceived;
                    _notificationCharacteristic.ValueUpdated += OnNotificationReceived;
                    _notificationHandlerAttached = true;
                    
                    // Re-enable notifications if needed
                    try
                    {
                        await _notificationCharacteristic.StartUpdatesAsync();
                        System.Diagnostics.Debug.WriteLine($"[Gyroscope] Notifications re-enabled");
                    }
                    catch (Exception notifEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"[Gyroscope] Error re-enabling notifications: {notifEx.Message}");
                    }
                }
                
                // Write enable producer command 
                await _commandCharacteristic.WriteAsync(enableProducer);
                
                // Small delay to allow command to process
                await Task.Delay(100); // Increased delay for device to process command
                
                _gyroscopeActive = true;
                System.Diagnostics.Debug.WriteLine($"[Gyroscope] Gyroscope started successfully");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Gyroscope] Error starting gyroscope: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                _gyroscopeActive = false;
                throw;
            }
        }

        public async Task StopGyroscopeAsync()
        {
            if (_commandCharacteristic == null || !IsConnected)
            {
                _gyroscopeActive = false;
                return;
            }

            try
            {
                System.Diagnostics.Debug.WriteLine($"[Gyroscope] Stopping gyroscope...");
                
                // Stop gyroscope - try multiple methods
                // Method 1: Stop data producer (register 0x05 - same as start)
                byte[] stopProducerCommand = new byte[] { 0x13, 0x05 }; // Stop angular velocity data producer
                System.Diagnostics.Debug.WriteLine($"[Gyroscope] Sending stop producer command (0x05): [{string.Join(", ", stopProducerCommand.Select(b => $"0x{b:X2}"))}]");
                await _commandCharacteristic.WriteAsync(stopProducerCommand);
                await Task.Delay(50);
                
                // Method 2: Stop command (register 0x01) - general stop
                byte[] stopCommand1 = new byte[] { 0x13, 0x01 };
                System.Diagnostics.Debug.WriteLine($"[Gyroscope] Sending stop command (0x01): [{string.Join(", ", stopCommand1.Select(b => $"0x{b:X2}"))}]");
                await _commandCharacteristic.WriteAsync(stopCommand1);
                await Task.Delay(50);
                
                // Method 3: Remove route using Route Manager (module 0x12, register 0x02)
                try
                {
                    byte[] removeRouteCommand = new byte[] { 0x12, 0x02, 0x02 }; // Route Manager, remove route 0x02
                    System.Diagnostics.Debug.WriteLine($"[Gyroscope] Removing route: [{string.Join(", ", removeRouteCommand.Select(b => $"0x{b:X2}"))}]");
                    await _commandCharacteristic.WriteAsync(removeRouteCommand);
                    await Task.Delay(50);
                }
                catch (Exception ex2)
                {
                    System.Diagnostics.Debug.WriteLine($"[Gyroscope] Error removing route: {ex2.Message}");
                }
                
                _gyroscopeActive = false;
                System.Diagnostics.Debug.WriteLine($"[Gyroscope] Gyroscope stopped successfully");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Gyroscope] Error stopping gyroscope: {ex.Message}");
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

        private void ParseMagnetometerData(byte[] data)
        {
            try
            {
                // MetaWear magnetometer (BMM150) data format:
                // Format: [module_id, register_id, x_low, x_high, y_low, y_high, z_low, z_high]
                // BMM150 uses 16-bit signed values
                
                if (data.Length < 8)
                {
                    return;
                }

                short x = BitConverter.ToInt16(data, 2);
                short y = BitConverter.ToInt16(data, 4);
                short z = BitConverter.ToInt16(data, 6);

                // Convert to microtesla (µT) - BMM150 typically uses 16 LSB/µT for ±1300µT range
                float xUt = x / 16.0f;
                float yUt = y / 16.0f;
                float zUt = z / 16.0f;

                // Reduced logging - only log occasionally (every 2 seconds) to avoid performance issues
                if ((DateTime.Now.Millisecond % 2000) < 10)
                {
                    System.Diagnostics.Debug.WriteLine($"[Magnetometer] Data - X: {xUt:F2}µT, Y: {yUt:F2}µT, Z: {zUt:F2}µT");
                }

                MagnetometerDataReceived?.Invoke(this, new MetaWearMagnetometerData
                {
                    X = xUt,
                    Y = yUt,
                    Z = zUt,
                    Timestamp = DateTime.Now
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Magnetometer] Error parsing data: {ex.Message}");
            }
        }

        private void ParseLightSensorData(byte[] data)
        {
            try
            {
                // MetaWear light sensor (LTR-329ALS-01) data format:
                // Format: [module_id, register_id, visible_low, visible_high, ir_low, ir_high]
                // 6 bytes total
                
                if (data.Length < 6)
                {
                    return;
                }

                ushort visible = BitConverter.ToUInt16(data, 2);
                ushort infrared = BitConverter.ToUInt16(data, 4);

                // Reduced logging - only log occasionally (every 2 seconds) to avoid performance issues
                if ((DateTime.Now.Millisecond % 2000) < 10)
                {
                    System.Diagnostics.Debug.WriteLine($"[LightSensor] Data - Visible: {visible}, IR: {infrared}");
                }

                LightSensorDataReceived?.Invoke(this, new MetaWearLightSensorData
                {
                    Visible = visible,
                    Infrared = infrared,
                    Timestamp = DateTime.Now
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LightSensor] Error parsing data: {ex.Message}");
            }
        }

        public async Task StartMagnetometerAsync(float sampleRate = 25f)
        {
            if (_commandCharacteristic == null || !IsConnected)
                throw new InvalidOperationException("Device not connected");

            try
            {
                System.Diagnostics.Debug.WriteLine($"[Magnetometer] Starting magnetometer - SampleRate: {sampleRate}Hz");
                
                // Stop magnetometer first if already running
                if (_magnetometerActive)
                {
                    System.Diagnostics.Debug.WriteLine($"[Magnetometer] Stopping existing magnetometer first...");
                    await StopMagnetometerAsync();
                    await Task.Delay(100);
                }

                // For BMM150 magnetometer (module 0x15):
                // Step 1: Configure the magnetometer
                // Register 0x04: Configuration (ODR, power mode, etc.)
                // ODR: 0 = 10Hz, 1 = 2Hz, 2 = 6Hz, 3 = 8Hz, 4 = 15Hz, 5 = 20Hz, 6 = 25Hz, 7 = 30Hz
                byte odr = sampleRate switch
                {
                    <= 10f => 0,
                    <= 15f => 4,
                    <= 20f => 5,
                    <= 25f => 6,
                    _ => 7
                };

                byte[] configCommand = new byte[]
                {
                    0x15, 0x04, // Module ID: 0x15 (Magnetometer - BMM150), Register ID: 0x04 (Configuration)
                    odr // ODR setting
                };
                
                System.Diagnostics.Debug.WriteLine($"[Magnetometer] Sending config command: [{string.Join(", ", configCommand.Select(b => $"0x{b:X2}"))}] (ODR={odr})");
                await _commandCharacteristic.WriteAsync(configCommand);
                await Task.Delay(50);

                // Step 2: Create route using Route Manager
                try
                {
                    System.Diagnostics.Debug.WriteLine($"[Magnetometer] Creating route using Route Manager...");
                    byte[] createRouteCommand = new byte[]
                    {
                        0x12, 0x03,  // Route Manager (module 0x12, register 0x03 - create route)
                        0x15, 0x05,  // Producer: magnetometer (0x15), data producer (0x05 for BMM150)
                        0x03,        // Route ID: 0x03
                        0x01,        // Endpoint type: 0x01 = stream (notifications)
                        0x00         // Endpoint ID: 0x00 = default
                    };
                    
                    System.Diagnostics.Debug.WriteLine($"[Magnetometer] Creating route: [{string.Join(", ", createRouteCommand.Select(b => $"0x{b:X2}"))}]");
                    await _commandCharacteristic.WriteAsync(createRouteCommand);
                    await Task.Delay(150);
                }
                catch (Exception routeEx)
                {
                    System.Diagnostics.Debug.WriteLine($"[Magnetometer] Error creating route: {routeEx.Message}");
                }

                // Step 3: Enable the magnetometer module
                try
                {
                    byte[] enableModule = new byte[] { 0x15, 0x01, 0x01 }; // Module 0x15, Register 0x01, Enable
                    System.Diagnostics.Debug.WriteLine($"[Magnetometer] Enabling magnetometer module: [{string.Join(", ", enableModule.Select(b => $"0x{b:X2}"))}]");
                    await _commandCharacteristic.WriteAsync(enableModule);
                    await Task.Delay(50);
                }
                catch (Exception enableEx)
                {
                    System.Diagnostics.Debug.WriteLine($"[Magnetometer] Error enabling module: {enableEx.Message}");
                }

                // Step 4: Enable the magnetometer data producer
                byte[] enableProducer = new byte[]
                {
                    0x15, 0x05, 0x03  // Module ID: 0x15 (Magnetometer), Register ID: 0x05 (Data producer), Route ID: 0x03
                };

                System.Diagnostics.Debug.WriteLine($"[Magnetometer] Enabling magnetometer data producer: [{string.Join(", ", enableProducer.Select(b => $"0x{b:X2}"))}]");
                await _commandCharacteristic.WriteAsync(enableProducer);
                await Task.Delay(100);

                _magnetometerActive = true;
                System.Diagnostics.Debug.WriteLine($"[Magnetometer] Magnetometer started successfully");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Magnetometer] Error starting magnetometer: {ex.Message}");
                _magnetometerActive = false;
                throw;
            }
        }

        public async Task StopMagnetometerAsync()
        {
            if (_commandCharacteristic == null || !IsConnected)
            {
                _magnetometerActive = false;
                return;
            }

            try
            {
                System.Diagnostics.Debug.WriteLine($"[Magnetometer] Stopping magnetometer...");
                
                // Stop magnetometer - try multiple methods
                byte[] stopProducerCommand = new byte[] { 0x15, 0x05 }; // Stop data producer
                System.Diagnostics.Debug.WriteLine($"[Magnetometer] Sending stop producer command (0x05): [{string.Join(", ", stopProducerCommand.Select(b => $"0x{b:X2}"))}]");
                await _commandCharacteristic.WriteAsync(stopProducerCommand);
                await Task.Delay(50);
                
                byte[] stopCommand1 = new byte[] { 0x15, 0x01 }; // General stop
                System.Diagnostics.Debug.WriteLine($"[Magnetometer] Sending stop command (0x01): [{string.Join(", ", stopCommand1.Select(b => $"0x{b:X2}"))}]");
                await _commandCharacteristic.WriteAsync(stopCommand1);
                await Task.Delay(50);
                
                // Remove route using Route Manager
                try
                {
                    byte[] removeRouteCommand = new byte[] { 0x12, 0x02, 0x03 }; // Route Manager, remove route 0x03
                    System.Diagnostics.Debug.WriteLine($"[Magnetometer] Removing route: [{string.Join(", ", removeRouteCommand.Select(b => $"0x{b:X2}"))}]");
                    await _commandCharacteristic.WriteAsync(removeRouteCommand);
                    await Task.Delay(50);
                }
                catch (Exception ex2)
                {
                    System.Diagnostics.Debug.WriteLine($"[Magnetometer] Error removing route: {ex2.Message}");
                }

                _magnetometerActive = false;
                System.Diagnostics.Debug.WriteLine($"[Magnetometer] Magnetometer stopped successfully");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Magnetometer] Error stopping magnetometer: {ex.Message}");
                _magnetometerActive = false;
            }
        }

        public async Task StartLightSensorAsync(float sampleRate = 10f)
        {
            if (_commandCharacteristic == null || !IsConnected)
                throw new InvalidOperationException("Device not connected");

            try
            {
                System.Diagnostics.Debug.WriteLine($"[LightSensor] Starting light sensor - SampleRate: {sampleRate}Hz");
                
                // Stop light sensor first if already running
                if (_lightSensorActive)
                {
                    System.Diagnostics.Debug.WriteLine($"[LightSensor] Stopping existing light sensor first...");
                    await StopLightSensorAsync();
                    await Task.Delay(100);
                }

                // For LTR-329ALS-01 light sensor (module 0x14):
                // Step 1: Configure the light sensor
                // Register 0x04: Configuration (gain, integration time, measurement rate)
                // For simplicity, use default settings
                byte[] configCommand = new byte[]
                {
                    0x14, 0x04, // Module ID: 0x14 (Light Sensor), Register ID: 0x04 (Configuration)
                    0x01, 0x01  // Gain and integration time settings (default)
                };
                
                System.Diagnostics.Debug.WriteLine($"[LightSensor] Sending config command: [{string.Join(", ", configCommand.Select(b => $"0x{b:X2}"))}]");
                await _commandCharacteristic.WriteAsync(configCommand);
                await Task.Delay(50);

                // Step 2: Create route using Route Manager
                try
                {
                    System.Diagnostics.Debug.WriteLine($"[LightSensor] Creating route using Route Manager...");
                    byte[] createRouteCommand = new byte[]
                    {
                        0x12, 0x03,  // Route Manager (module 0x12, register 0x03 - create route)
                        0x14, 0x03,  // Producer: light sensor (0x14), data producer (0x03)
                        0x04,        // Route ID: 0x04
                        0x01,        // Endpoint type: 0x01 = stream (notifications)
                        0x00         // Endpoint ID: 0x00 = default
                    };
                    
                    System.Diagnostics.Debug.WriteLine($"[LightSensor] Creating route: [{string.Join(", ", createRouteCommand.Select(b => $"0x{b:X2}"))}]");
                    await _commandCharacteristic.WriteAsync(createRouteCommand);
                    await Task.Delay(150);
                }
                catch (Exception routeEx)
                {
                    System.Diagnostics.Debug.WriteLine($"[LightSensor] Error creating route: {routeEx.Message}");
                }

                // Step 3: Enable the light sensor module
                try
                {
                    byte[] enableModule = new byte[] { 0x14, 0x01, 0x01 }; // Module 0x14, Register 0x01, Enable
                    System.Diagnostics.Debug.WriteLine($"[LightSensor] Enabling light sensor module: [{string.Join(", ", enableModule.Select(b => $"0x{b:X2}"))}]");
                    await _commandCharacteristic.WriteAsync(enableModule);
                    await Task.Delay(50);
                }
                catch (Exception enableEx)
                {
                    System.Diagnostics.Debug.WriteLine($"[LightSensor] Error enabling module: {enableEx.Message}");
                }

                // Step 4: Enable the light sensor data producer
                byte[] enableProducer = new byte[]
                {
                    0x14, 0x03, 0x04  // Module ID: 0x14 (Light Sensor), Register ID: 0x03 (Data producer), Route ID: 0x04
                };

                System.Diagnostics.Debug.WriteLine($"[LightSensor] Enabling light sensor data producer: [{string.Join(", ", enableProducer.Select(b => $"0x{b:X2}"))}]");
                await _commandCharacteristic.WriteAsync(enableProducer);
                await Task.Delay(100);

                _lightSensorActive = true;
                System.Diagnostics.Debug.WriteLine($"[LightSensor] Light sensor started successfully");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LightSensor] Error starting light sensor: {ex.Message}");
                _lightSensorActive = false;
                throw;
            }
        }

        public async Task StopLightSensorAsync()
        {
            if (_commandCharacteristic == null || !IsConnected)
            {
                _lightSensorActive = false;
                return;
            }

            try
            {
                System.Diagnostics.Debug.WriteLine($"[LightSensor] Stopping light sensor...");
                
                // Stop light sensor - try multiple methods
                byte[] stopProducerCommand = new byte[] { 0x14, 0x03 }; // Stop data producer
                System.Diagnostics.Debug.WriteLine($"[LightSensor] Sending stop producer command (0x03): [{string.Join(", ", stopProducerCommand.Select(b => $"0x{b:X2}"))}]");
                await _commandCharacteristic.WriteAsync(stopProducerCommand);
                await Task.Delay(50);
                
                byte[] stopCommand1 = new byte[] { 0x14, 0x01 }; // General stop
                System.Diagnostics.Debug.WriteLine($"[LightSensor] Sending stop command (0x01): [{string.Join(", ", stopCommand1.Select(b => $"0x{b:X2}"))}]");
                await _commandCharacteristic.WriteAsync(stopCommand1);
                await Task.Delay(50);
                
                // Remove route using Route Manager
                try
                {
                    byte[] removeRouteCommand = new byte[] { 0x12, 0x02, 0x04 }; // Route Manager, remove route 0x04
                    System.Diagnostics.Debug.WriteLine($"[LightSensor] Removing route: [{string.Join(", ", removeRouteCommand.Select(b => $"0x{b:X2}"))}]");
                    await _commandCharacteristic.WriteAsync(removeRouteCommand);
                    await Task.Delay(50);
                }
                catch (Exception ex2)
                {
                    System.Diagnostics.Debug.WriteLine($"[LightSensor] Error removing route: {ex2.Message}");
                }

                _lightSensorActive = false;
                System.Diagnostics.Debug.WriteLine($"[LightSensor] Light sensor stopped successfully");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LightSensor] Error stopping light sensor: {ex.Message}");
                _lightSensorActive = false;
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

