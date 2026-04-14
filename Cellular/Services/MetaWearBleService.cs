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
        // ========================================================================================
        // DEBUG CONTROL: Set this to false to disable all debug logging. This Lags the Phone Hard.
        // ========================================================================================
        private const bool EnableDebugLogging = false;
        public static bool IsDebugLoggingEnabled => EnableDebugLogging;
        // ============================================================================

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

        // Battery Service (standard BLE)
        private static readonly Guid BatteryServiceUuid = Guid.Parse("0000180f-0000-1000-8000-00805f9b34fb");
        private static readonly Guid BatteryLevelCharacteristicUuid = Guid.Parse("00002a19-0000-1000-8000-00805f9b34fb");

        // MetaWear C++ SDK constants we use (mbientlab/MetaWear-SDK-Cpp)
        // Module IDs: route BLE notifications (first byte). Scales: raw int16 → g, °/s, µT.
        private const byte MW_MODULE_ACCELEROMETER = 3;
        private const byte MW_MODULE_GYRO = 19;
        private const byte MW_MODULE_AMBIENT_LIGHT = 20;
        private const byte MW_MODULE_MAGNETOMETER = 21;

        private const float ACC_SCALE_2G = 16384f;
        private const float ACC_SCALE_4G = 8192f;
        private const float ACC_SCALE_8G = 4096f;
        private const float ACC_SCALE_16G = 2048f;
        private const float GYRO_SCALE_2000DPS = 16.4f;
        private const float BMM150_SCALE = 16f;

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
        private DateTime _lastAccelerometerLogTime = DateTime.MinValue; // Track last log time for accelerometer
        private DateTime _lastMagnetometerLogTime = DateTime.MinValue; // Track last log time for magnetometer
        private bool _isDebugLogging = false; // Guard to prevent re-entrancy in DebugLog
        private const int GattWriteTimeoutMs = 3000;
        private Guid _savedDeviceId = Guid.Empty;      // Saved before clearing on disconnect, used for reconnect
        private bool _intentionalDisconnect = false;    // Set true in DisconnectAsync to suppress auto-reconnect
        private CancellationTokenSource? _reconnectCts; // Cancels in-progress reconnect on intentional disconnect
        // Current accelerometer LSB/g (set from range in Start; auto-corrected for MMS 2g in parser)
        private float _accelLsbPerG = ACC_SCALE_16G;

        /// <summary>
        /// Writes a command to the MetaWear command characteristic using WriteWithoutResponse.
        /// MetaWear does not ACK command writes; using WithResponse causes "prior command is not finished" on Android.
        /// </summary>
        private async Task WriteCommandAsync(byte[] data)
        {
            if (_commandCharacteristic == null)
                throw new InvalidOperationException("Command characteristic is null");
            _commandCharacteristic.WriteType = Plugin.BLE.Abstractions.CharacteristicWriteType.WithoutResponse;
            var writeTask = _commandCharacteristic.WriteAsync(data);
            var timeoutTask = Task.Delay(GattWriteTimeoutMs);
            var completed = await Task.WhenAny(writeTask, timeoutTask);
            if (completed == timeoutTask)
            {
                DebugLog($"[GATT] Write timed out after {GattWriteTimeoutMs}ms");
                throw new TimeoutException("GATT write timed out");
            }
            await writeTask;
        }

        /// <summary>
        /// Helper method to conditionally log debug messages based on EnableDebugLogging flag
        /// Includes re-entrancy guard to prevent stack overflow
        /// </summary>
        private void DebugLog(string message)
        {
            if (!EnableDebugLogging || _isDebugLogging)
                return;
            
            try
            {
                _isDebugLogging = true;
                System.Diagnostics.Debug.WriteLine(message);
            }
            finally
            {
                _isDebugLogging = false;
            }
        }

        public event EventHandler<string>? DeviceDisconnected;
        public event EventHandler<string>? DeviceReconnected;
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
                var disconnectedId = _device.Id; // Save before clearing
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

                if (!_intentionalDisconnect)
                {
                    System.Diagnostics.Debug.WriteLine($"[MetaWear] Unexpected disconnect — starting auto-reconnect for {disconnectedId}");
                    _reconnectCts?.Cancel();
                    _reconnectCts = new CancellationTokenSource();
                    _ = AutoReconnectAsync(disconnectedId, _reconnectCts.Token);
                }
                else
                {
                    DeviceDisconnected?.Invoke(this, disconnectedId.ToString());
                }
            }
        }

        /// <summary>
        /// Scans for and reconnects to the device after an unexpected disconnect.
        /// Fires DeviceReconnected on success or DeviceDisconnected after all attempts fail.
        /// Pattern from MetaWear Android SDK: call connectAsync() in onUnexpectedDisconnect handler.
        /// </summary>
        private async Task AutoReconnectAsync(Guid deviceId, CancellationToken ct)
        {
            int[] delaysMs = { 1500, 3000, 6000 };

            for (int attempt = 0; attempt < delaysMs.Length; attempt++)
            {
                if (ct.IsCancellationRequested) break;

                System.Diagnostics.Debug.WriteLine($"[MetaWear] Reconnect attempt {attempt + 1}/{delaysMs.Length} — waiting {delaysMs[attempt]}ms");

                try { await Task.Delay(delaysMs[attempt], ct); }
                catch (OperationCanceledException) { break; }

                if (ct.IsCancellationRequested) break;

                try
                {
                    System.Diagnostics.Debug.WriteLine($"[MetaWear] Scanning for {deviceId}...");

                    IDevice? found = null;
                    using var scanCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    scanCts.CancelAfter(5000);

                    void OnFound(object? s, Plugin.BLE.Abstractions.EventArgs.DeviceEventArgs de)
                    {
                        if (de.Device.Id == deviceId)
                        {
                            found = de.Device;
                            try { scanCts.Cancel(); } catch { }
                        }
                    }

                    _adapter.DeviceDiscovered += OnFound;
                    try { await _adapter.StartScanningForDevicesAsync(cancellationToken: scanCts.Token); }
                    catch (OperationCanceledException) { }
                    finally
                    {
                        _adapter.DeviceDiscovered -= OnFound;
                        try { await _adapter.StopScanningForDevicesAsync(); } catch { }
                    }

                    if (found == null || ct.IsCancellationRequested) continue;

                    _device = found;
                    System.Diagnostics.Debug.WriteLine($"[MetaWear] Device found — running GATT setup...");

                    bool success = await SetupGattAsync();
                    if (success)
                    {
                        System.Diagnostics.Debug.WriteLine($"[MetaWear] Auto-reconnect succeeded on attempt {attempt + 1}");
                        DeviceReconnected?.Invoke(this, deviceId.ToString());
                        return;
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[MetaWear] Reconnect attempt {attempt + 1} failed: {ex.Message}");
                }
            }

            System.Diagnostics.Debug.WriteLine("[MetaWear] Auto-reconnect failed after all attempts — firing DeviceDisconnected");
            DeviceDisconnected?.Invoke(this, deviceId.ToString());
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
                
                DebugLog($"Invalid device object type: {device?.GetType()}");
                return false;
            }
            catch (Exception ex)
            {
                DebugLog($"Error connecting to MetaWear device: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> ConnectToDeviceAsync()
        {
            if (_device == null)
                return false;

            try
            {
                DebugLog($"Connecting to device: {_device.Name} ({_device.Id})");
                await _adapter.ConnectToDeviceAsync(_device);
                return await SetupGattAsync();
            }
            catch (Exception ex)
            {
                DebugLog($"Error in ConnectToDeviceAsync: {ex.Message}");
                DebugLog($"Stack trace: {ex.StackTrace}");
                _isDeviceConnected = false;
                return false;
            }
        }

        /// <summary>
        /// Runs GATT service/characteristic discovery, enables notifications, and stops any
        /// stale sensors. Called after BLE connection is established (initial connect or auto-reconnect).
        /// </summary>
        private async Task<bool> SetupGattAsync()
        {
            if (_device == null)
                return false;

            try
            {
                DebugLog("Device connected, waiting for services to stabilize...");
                await Task.Delay(1000);

                // Discover services - this will fail if not connected
                DebugLog("Discovering services...");
                var services = await _device.GetServicesAsync();
                
                DebugLog($"Found {services.Count()} service(s)");
                
                // Log all available services for debugging
                foreach (var service in services)
                {
                    DebugLog($"  - Service UUID: {service.Id}");
                }
                
                _metaWearService = services.FirstOrDefault(s => s.Id == MetaWearServiceUuid);

                if (_metaWearService == null)
                {
                    DebugLog($"MetaWear service not found. Expected UUID: {MetaWearServiceUuid}");
                    DebugLog("Available services listed above. MetaWear MMS may use different UUIDs.");
                    _isDeviceConnected = false;
                    return false;
                }

                DebugLog($"MetaWear service found: {_metaWearService.Id}");

                // Get characteristics 
                DebugLog("Discovering characteristics...");
                var characteristics = await _metaWearService.GetCharacteristicsAsync();
                
                DebugLog($"Found {characteristics.Count()} characteristic(s)");
                
                // Log all available characteristics for debugging
                foreach (var characteristic in characteristics)
                {
                    DebugLog($"  - Characteristic UUID: {characteristic.Id}");
                }
                
                _commandCharacteristic = characteristics.FirstOrDefault(c => c.Id == MetaWearCommandCharacteristicUuid);
                
                // Try to find notification characteristic - it can be either UUID depending on device/firmware
                _notificationCharacteristic = characteristics.FirstOrDefault(c => 
                    c.Id == MetaWearNotificationCharacteristicUuid || 
                    c.Id == MetaWearNotificationCharacteristicUuidAlt);

                if (_commandCharacteristic == null)
                {
                    DebugLog($"MetaWear command characteristic not found.");
                    DebugLog($"  Expected Command UUID: {MetaWearCommandCharacteristicUuid}");
                    DebugLog("Available characteristics listed above.");
                    _isDeviceConnected = false;
                    return false;
                }

                // MetaWear does not ACK command writes; WithoutResponse avoids "prior command is not finished" (MMS/MMC).
                _commandCharacteristic.WriteType = Plugin.BLE.Abstractions.CharacteristicWriteType.WithoutResponse;

                if (_notificationCharacteristic == null)
                {
                    DebugLog($"MetaWear notification characteristic not found.");
                    DebugLog($"  Expected Notification UUID: {MetaWearNotificationCharacteristicUuid} or {MetaWearNotificationCharacteristicUuidAlt}");
                    DebugLog("Available characteristics listed above.");
                    _isDeviceConnected = false;
                    return false;
                }

                DebugLog("MetaWear characteristics found");
                DebugLog($"  Command: {_commandCharacteristic.Id}");
                DebugLog($"  Notification: {_notificationCharacteristic.Id}");

                // Enable notifications 
                DebugLog("Enabling notifications...");
                
                // Attach handler BEFORE starting updates to avoid missing data
                // Remove handler first to prevent duplicate attachments
                if (_notificationHandlerAttached)
                {
                    DebugLog("Removing existing notification handler...");
                    _notificationCharacteristic.ValueUpdated -= OnNotificationReceived;
                    _notificationHandlerAttached = false;
                }
                
                _notificationCharacteristic.ValueUpdated += OnNotificationReceived;
                _notificationHandlerAttached = true;
                
                // Start receiving notifications
                await _notificationCharacteristic.StartUpdatesAsync();
                
                DebugLog("Notifications enabled successfully");

                // Stop any active sensors from previous sessions
                // Stop light sensor (module 0x14) - it's often active by default
                try
                {
                    DebugLog("[Connection] Stopping light sensor (module 0x14)...");
                    byte[] stopLightSensor = new byte[] { 0x14, 0x01 };
                    await WriteCommandAsync(stopLightSensor);
                    await Task.Delay(100);
                    
                    byte[] stopLightSensorProducer = new byte[] { 0x14, 0x03 };
                    await WriteCommandAsync(stopLightSensorProducer);
                    await Task.Delay(100);
                    DebugLog("[Connection] Light sensor stop commands sent");
                }
                catch (Exception ex)
                {
                    DebugLog($"[Connection] Error stopping light sensor: {ex.Message}");
                }

                // Stop magnetometer (module 0x15)
                try
                {
                    DebugLog("[Connection] Stopping magnetometer (module 0x15)...");
                    try
                    {
                        byte[] stopProducerCommand = new byte[] { 0x15, 0x05 };
                        await WriteCommandAsync(stopProducerCommand);
                        await Task.Delay(50);
                    }
                    catch { }
                    
                    try
                    {
                        byte[] stopProducerCommand2 = new byte[] { 0x15, 0x06 };
                        await WriteCommandAsync(stopProducerCommand2);
                        await Task.Delay(50);
                    }
                    catch { }
                    
                    try
                    {
                        byte[] removeRouteCommand = new byte[] { 0x12, 0x02, 0x03 };
                        await WriteCommandAsync(removeRouteCommand);
                        await Task.Delay(50);
                    }
                    catch { }
                    
                    try
                    {
                        byte[] disableModule = new byte[] { 0x15, 0x01, 0x00 };
                        await WriteCommandAsync(disableModule);
                        await Task.Delay(50);
                    }
                    catch { }
                    
                    try
                    {
                        byte[] stopCommand1 = new byte[] { 0x15, 0x01 };
                        await WriteCommandAsync(stopCommand1);
                        await Task.Delay(50);
                    }
                    catch { }
                    
                    _magnetometerActive = false;
                    DebugLog("[Connection] Magnetometer stop commands sent");
                }
                catch (Exception ex)
                {
                    DebugLog($"[Connection] Error stopping magnetometer: {ex.Message}");
                    _magnetometerActive = false;
                }

                // Stop accelerometer (module 0x03)
                try
                {
                    DebugLog("[Connection] Stopping accelerometer (module 0x03)...");
                    byte[] disableProducer = new byte[] { 0x03, 0x04, 0x00 };
                    await WriteCommandAsync(disableProducer);
                    await Task.Delay(50);
                    
                    byte[] disableModule = new byte[] { 0x03, 0x01, 0x00 };
                    await WriteCommandAsync(disableModule);
                    await Task.Delay(50);
                    
                    try
                    {
                        byte[] removeRouteCommand12 = new byte[] { 0x12, 0x02, 0x01 };
                        await WriteCommandAsync(removeRouteCommand12);
                        await Task.Delay(50);
                    }
                    catch { }
                    
                    try
                    {
                        byte[] removeRouteCommand11 = new byte[] { 0x11, 0x02, 0x01 };
                        await WriteCommandAsync(removeRouteCommand11);
                        await Task.Delay(50);
                    }
                    catch { }
                    
                    byte[] fallbackStopCommand = new byte[] { 0x03, 0x05 };
                    await WriteCommandAsync(fallbackStopCommand);
                    await Task.Delay(50);
                    
                    _accelerometerActive = false;
                    DebugLog("[Connection] Accelerometer stop commands sent");
                }
                catch (Exception ex)
                {
                    DebugLog($"[Connection] Error stopping accelerometer: {ex.Message}");
                    _accelerometerActive = false;
                }

                // Stop gyroscope (module 0x13)
                try
                {
                    DebugLog("[Connection] Stopping gyroscope (module 0x13)...");
                    byte[] stopProducerCommand = new byte[] { 0x13, 0x05 };
                    await WriteCommandAsync(stopProducerCommand);
                    await Task.Delay(50);
                    
                    byte[] stopCommand1 = new byte[] { 0x13, 0x01 };
                    await WriteCommandAsync(stopCommand1);
                    await Task.Delay(50);
                    
                    byte[] disableProducer13 = new byte[] { 0x13, 0x04, 0x00 };
                    await WriteCommandAsync(disableProducer13);
                    await Task.Delay(50);
                    
                    byte[] disableModule13 = new byte[] { 0x13, 0x01, 0x00 };
                    await WriteCommandAsync(disableModule13);
                    await Task.Delay(50);
                    
                    try
                    {
                        byte[] removeRouteCommand = new byte[] { 0x12, 0x02, 0x02 };
                        await WriteCommandAsync(removeRouteCommand);
                        await Task.Delay(50);
                    }
                    catch { }
                    
                    try
                    {
                        byte[] removeRouteCommand11 = new byte[] { 0x11, 0x02, 0x02 };
                        await WriteCommandAsync(removeRouteCommand11);
                        await Task.Delay(50);
                    }
                    catch { }
                    
                    _gyroscopeActive = false;
                    DebugLog("[Connection] Gyroscope stop commands sent");
                }
                catch (Exception ex)
                {
                    DebugLog($"[Connection] Error stopping gyroscope: {ex.Message}");
                    _gyroscopeActive = false;
                }

                DebugLog("MetaWear device connected successfully!");

                // Mark as connected only after successful service/characteristic discovery
                _isDeviceConnected = true;
                return true;
            }
            catch (Exception ex)
            {
                DebugLog($"Error in SetupGattAsync: {ex.Message}");
                DebugLog($"Stack trace: {ex.StackTrace}");
                _isDeviceConnected = false;
                return false;
            }
        }

        public async Task DisconnectAsync()
        {
            // Cancel any pending auto-reconnect and mark this as intentional so
            // OnDeviceDisconnected does not try to reconnect again.
            _intentionalDisconnect = true;
            _reconnectCts?.Cancel();
            _reconnectCts = null;

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
                        DebugLog("Notifications stopped successfully");
                    }
                    catch (Exception ex)
                    {
                        DebugLog($"Error stopping notifications: {ex.Message}");
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
                        DebugLog($"Error disconnecting device: {ex.Message}");
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
                DebugLog($"Error in DisconnectAsync: {ex.Message}");
            }
            finally
            {
                _intentionalDisconnect = false;
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
                    DebugLog($"[Notification] Received null or too small data (length: {data?.Length ?? 0})");
                    return;
                }

                // Parse MetaWear notification data
                // Format: [module_id, register_id, ...data]
                byte moduleId = data[0];
                byte registerId = data[1];

                // Log ALL notifications for accelerometer, gyroscope, magnetometer, and light sensor to debug
                if (moduleId == MW_MODULE_ACCELEROMETER || moduleId == MW_MODULE_GYRO || moduleId == MW_MODULE_AMBIENT_LIGHT || moduleId == MW_MODULE_MAGNETOMETER)
                {
                    DebugLog($"[Notification] Module: 0x{moduleId:X2}, Reg: 0x{registerId:X2}, Len: {data.Length}, Raw: [{string.Join(", ", data.Select(b => $"0x{b:X2}"))}]");
                }
                else
                {
                    // Reduced logging for other modules - only log occasionally to avoid performance issues
                    // Data comes at 50-100Hz, so logging every single packet causes lag
                    if (data.Length <= 20 && (DateTime.Now.Millisecond % 1000) < 10) // Only log ~1% of packets
                    {
                        DebugLog($"[Notification] Module: 0x{moduleId:X2}, Reg: 0x{registerId:X2}, Len: {data.Length}");
                    }
                }

                // Module IDs from C++ SDK core/module.h (MBL_MW_MODULE_*)
                bool isAccelerometer = (moduleId == MW_MODULE_ACCELEROMETER);
                bool isGyroscope = (moduleId == MW_MODULE_GYRO);
                bool isLightSensor = (moduleId == MW_MODULE_AMBIENT_LIGHT);
                bool isMagnetometer = (moduleId == MW_MODULE_MAGNETOMETER);
                
                if (isAccelerometer)
                {
                    // Always log accelerometer notifications for debugging (even if not active) to verify we're receiving data
                    DebugLog($"[Accelerometer] DEBUG: Received accelerometer notification! Module: 0x{data[0]:X2}, Reg: 0x{data[1]:X2}, Len: {data.Length}, Active: {_accelerometerActive}");
                    DebugLog($"[Accelerometer] DEBUG: Raw data: [{string.Join(", ", data.Select(b => $"0x{b:X2}"))}]");
                    
                    if (_accelerometerActive)
                    {
                        // Parse without logging every packet (too verbose at 100Hz)
                        ParseAccelerometerData(data);
                    }
                    else
                    {
                        DebugLog($"[Accelerometer] DEBUG: Accelerometer not active, ignoring data");
                    }
                }
                else if (isGyroscope)
                {
                    if (_gyroscopeActive)
                    {
                        ParseGyroscopeData(data);
                    }
                }
                else if (isMagnetometer)
                {
                    if (_magnetometerActive)
                    {
                        ParseMagnetometerData(data);
                    }
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
                        DebugLog($"[Notification] Unknown module ID: 0x{moduleId:X2}, Register: 0x{registerId:X2}, Length: {data.Length}");
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLog($"[Notification] Error processing notification: {ex.Message}");
                DebugLog($"Stack trace: {ex.StackTrace}");
            }
        }

        private void ParseAccelerometerData(byte[] data)
        {
            try
            {
                // C++ SDK: convert_to_bosch_acceleration; payload = CartesianShort { int16_t x, y, z } (6 bytes LE).
                // BLE packet: [module_id, register_id, x_lo, x_hi, y_lo, y_hi, z_lo, z_hi]; register 4 = DATA_INTERRUPT.
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

                // Convert to G using Bosch LSB/g for configured range (MetaMotionS/BMI270 may use 2g = 16384 LSB/g)
                // Bosch: 2g=16384, 4g=8192, 8g=4096, 16g=2048. Auto-detect MMS 2g if magnitude > 2.5.
                float xG = x / _accelLsbPerG;
                float yG = y / _accelLsbPerG;
                float zG = z / _accelLsbPerG;
                float magSq = xG * xG + yG * yG + zG * zG;
                if (magSq > 6.25f) // magnitude > 2.5g → device likely at 2g range (MMS/BMI270)
                {
                    _accelLsbPerG = ACC_SCALE_2G;
                    xG = x / _accelLsbPerG;
                    yG = y / _accelLsbPerG;
                    zG = z / _accelLsbPerG;
                }

                // Log raw data every 1 second to reduce lag
                DateTime now = DateTime.Now;
                if ((now - _lastAccelerometerLogTime).TotalSeconds >= 1.0)
                {
                    DebugLog($"[Accelerometer] Raw data: [{string.Join(", ", data.Select(b => $"0x{b:X2}"))}] | X: {xG:F3}g, Y: {yG:F3}g, Z: {zG:F3}g");
                    _lastAccelerometerLogTime = now;
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
                DebugLog($"[Accelerometer] Error parsing data: {ex.Message}");
                DebugLog($"[Accelerometer] Raw data: [{string.Join(", ", data.Select(b => $"0x{b:X2}"))}]");
            }
        }

        private void ParseGyroscopeData(byte[] data)
        {
            try
            {
                // C++ SDK: convert_to_bosch_rotation; payload = CartesianShort { int16_t x, y, z } (6 bytes LE).
                // BLE packet: [module_id, register_id, ...]; BMI160 DATA=5, BMI270 DATA=4 (gyro_bosch_register.h).
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
                    DebugLog($"[Gyroscope] Data too short: {data.Length} bytes (need at least 8)");
                    DebugLog($"[Gyroscope] Raw data: [{string.Join(", ", data.Select(b => $"0x{b:X2}"))}]");
                    return;
                }

                // Convert to °/s (C++ SDK gyro_bosch.cpp: FSR_SCALE[MBL_MW_GYRO_BOSCH_RANGE_2000dps] = 16.4f)
                float xDps = x / GYRO_SCALE_2000DPS;
                float yDps = y / GYRO_SCALE_2000DPS;
                float zDps = z / GYRO_SCALE_2000DPS;

                // Reduced logging - only log occasionally (every 2 seconds) to avoid performance issues
                if ((DateTime.Now.Millisecond % 2000) < 10)
                {
                    DebugLog($"[Gyroscope] Data - X: {xDps:F2}°/s, Y: {yDps:F2}°/s, Z: {zDps:F2}°/s");
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
                DebugLog($"[Gyroscope] Error parsing data: {ex.Message}");
                DebugLog($"[Gyroscope] Raw data: [{string.Join(", ", data.Select(b => $"0x{b:X2}"))}]");
            }
        }

        public async Task StartAccelerometerAsync(float sampleRate = 50f, float range = 16f)
        {
            if (_commandCharacteristic == null || !IsConnected)
                throw new InvalidOperationException("Device not connected");

            try
            {
                DebugLog($"[Accelerometer] Starting accelerometer - SampleRate: {sampleRate}Hz, Range: {range}G");
                // Set LSB/g from range (C++ SDK: BMI160_FSR_SCALE / BMI270_FSR_SCALE)
                _accelLsbPerG = range switch { <= 2f => ACC_SCALE_2G, <= 4f => ACC_SCALE_4G, <= 8f => ACC_SCALE_8G, _ => ACC_SCALE_16G };

                // Ensure device info is loaded so we can choose BMI160 vs BMI270 config (MMS = BMI270)
                if (_cachedDeviceInfo == null)
                    _ = await GetDeviceInfoAsync();

                // Stop accelerometer first if already running
                if (_accelerometerActive)
                {
                    DebugLog($"[Accelerometer] Stopping existing accelerometer first...");
                    await StopAccelerometerAsync();
                    await Task.Delay(100); // Small delay between stop and start
                }
                
                // Stop light sensor (0x14) if it's interfering - light sensor might be sending data
                try
                {
                    DebugLog($"[Accelerometer] Stopping light sensor (module 0x14) to prevent interference...");
                    // Try stopping by disabling data route (register 0x02 or 0x04 might disable routes)
                    byte[] stopLightSensor = new byte[] { 0x14, 0x02 }; // Module 0x14, Stop/disable routes
                    await WriteCommandAsync(stopLightSensor);
                    await Task.Delay(50);
                    // Also try register 0x01 (stop)
                    byte[] stopLightSensor2 = new byte[] { 0x14, 0x01 };
                    await WriteCommandAsync(stopLightSensor2);
                    await Task.Delay(50);
                }
                catch (Exception ex)
                {
                    DebugLog($"[Accelerometer] Error stopping light sensor: {ex.Message}");
                }

                // Exact phyphox accelerometer sequence:
                // 0303xxxx  → Config on register 0x03 (payload depends on chip)
                // 030401    → Enable data producer on register 0x04
                // 03020100  → Subscribe on register 0x02
                // 030101    → Enable module on register 0x01

                // Step 1: Configure accelerometer - register 0x03. Build payload from range (C++ SDK: BMI160 vs BMI270).
                // BMI160 (MMC): FSR 2g=0x3, 4g=0x5, 8g=0x8, 16g=0xc; first byte 0x28 (ODR/bwp/us).
                // BMI270 (MMS): FSR 2g=0x0, 4g=0x1, 8g=0x2, 16g=0x3; first byte 0xa8 (SDK default).
                int rangeIndex = range <= 2f ? 0 : range <= 4f ? 1 : range <= 8f ? 2 : 3;
                bool isBmi270 = _cachedDeviceInfo?.Model?.Contains("MetaMotion S", StringComparison.OrdinalIgnoreCase) == true
                    || _cachedDeviceInfo?.Model?.Contains("MMS", StringComparison.OrdinalIgnoreCase) == true;
                byte configByte0 = isBmi270 ? (byte)0xa8 : (byte)0x28;
                byte rangeByte = isBmi270
                    ? (byte)rangeIndex
                    : (byte)(rangeIndex == 0 ? 0x3 : rangeIndex == 1 ? 0x5 : rangeIndex == 2 ? 0x8 : 0xc);
                byte[] configCommand = new byte[] { 0x03, 0x03, configByte0, rangeByte };
                DebugLog($"[Accelerometer] Config (register 0x03) {(isBmi270 ? "BMI270" : "BMI160")} {range}G: [{string.Join(", ", configCommand.Select(b => $"0x{b:X2}"))}]");
                await WriteCommandAsync(configCommand);
                await Task.Delay(100);

                // Step 2: Enable data producer - register 0x04 (phyphox: 030401)
                byte[] enableProducer = new byte[] { 0x03, 0x04, 0x01 };
                DebugLog($"[Accelerometer] Enable producer (register 0x04): [{string.Join(", ", enableProducer.Select(b => $"0x{b:X2}"))}]");
                await WriteCommandAsync(enableProducer);
                await Task.Delay(100);

                // Step 3: Subscribe to data - register 0x02 (phyphox: 03020100)
                byte[] setRoute = new byte[] { 0x03, 0x02, 0x01, 0x00 };
                DebugLog($"[Accelerometer] Subscribe (register 0x02): [{string.Join(", ", setRoute.Select(b => $"0x{b:X2}"))}]");
                await WriteCommandAsync(setRoute);
                await Task.Delay(100);
                
                // Enable module: 0x03, 0x01, 0x01
                byte[] enableModule = new byte[] { 0x03, 0x01, 0x01 };
                DebugLog($"[Accelerometer] Enabling module: [{string.Join(", ", enableModule.Select(b => $"0x{b:X2}"))}]");
                await WriteCommandAsync(enableModule);
                await Task.Delay(200);
                
                _accelerometerActive = true;
                DebugLog($"[Accelerometer] Accelerometer started successfully - expecting notifications with Module 0x03, Register 0x04 (phyphox pattern)");
            }
            catch (Exception ex)
            {
                DebugLog($"[Accelerometer] Error starting accelerometer: {ex.Message}");
                DebugLog($"Stack trace: {ex.StackTrace}");
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
                DebugLog($"[Accelerometer] Stopping accelerometer...");
                
                // Stop accelerometer using reverse of phyphox pattern
                // Start pattern: Enable producer (0x03, 0x04, 0x01) -> Set route (0x03, 0x02, 0x01, 0x00) -> Enable module (0x03, 0x01, 0x01)
                // Stop pattern: Disable producer -> Disable module -> Remove route
                
                // Step 1: Disable the data producer (reverse of 0x03, 0x04, 0x01)
                try
                {
                    byte[] disableProducer = new byte[] { 0x03, 0x04, 0x00 }; // Disable producer
                    DebugLog($"[Accelerometer] Disabling producer: [{string.Join(", ", disableProducer.Select(b => $"0x{b:X2}"))}]");
                    await WriteCommandAsync(disableProducer);
                    await Task.Delay(100);
                }
                catch (Exception ex1)
                {
                    DebugLog($"[Accelerometer] Error disabling producer: {ex1.Message}");
                }
                
                // Step 2: Disable the module (reverse of 0x03, 0x01, 0x01)
                try
                {
                    byte[] disableModule = new byte[] { 0x03, 0x01, 0x00 }; // Disable module
                    DebugLog($"[Accelerometer] Disabling module: [{string.Join(", ", disableModule.Select(b => $"0x{b:X2}"))}]");
                    await WriteCommandAsync(disableModule);
                    await Task.Delay(100);
                }
                catch (Exception ex2)
                {
                    DebugLog($"[Accelerometer] Error disabling module: {ex2.Message}");
                }
                
                // Step 3: Remove route using Route Manager (module 0x11 or 0x12, register 0x02)
                try
                {
                    // Try Route Manager 0x12 first (standard)
                    byte[] removeRouteCommand = new byte[] { 0x12, 0x02, 0x01 }; // Route Manager, remove route 0x01
                    DebugLog($"[Accelerometer] Removing route (0x12): [{string.Join(", ", removeRouteCommand.Select(b => $"0x{b:X2}"))}]");
                    await WriteCommandAsync(removeRouteCommand);
                    await Task.Delay(100);
                }
                catch (Exception ex3)
                {
                    DebugLog($"[Accelerometer] Error removing route (0x12): {ex3.Message}");
                    // Try alternative route manager 0x11
                    try
                    {
                        byte[] removeRouteCommand2 = new byte[] { 0x11, 0x02, 0x01 }; // Route Manager 0x11, remove route 0x01
                        DebugLog($"[Accelerometer] Removing route (0x11): [{string.Join(", ", removeRouteCommand2.Select(b => $"0x{b:X2}"))}]");
                        await WriteCommandAsync(removeRouteCommand2);
                        await Task.Delay(100);
                    }
                    catch (Exception ex4)
                    {
                        DebugLog($"[Accelerometer] Error removing route (0x11): {ex4.Message}");
                    }
                }
                
                // Step 4: Additional stop commands as fallback
                try
                {
                    byte[] stopCommand = new byte[] { 0x03, 0x05 }; // Stop command (register 0x05)
                    DebugLog($"[Accelerometer] Sending stop command (0x05): [{string.Join(", ", stopCommand.Select(b => $"0x{b:X2}"))}]");
                    await WriteCommandAsync(stopCommand);
                    await Task.Delay(50);
                }
                catch (Exception ex5)
                {
                    DebugLog($"[Accelerometer] Error sending stop command: {ex5.Message}");
                }
                
                _accelerometerActive = false;
                DebugLog($"[Accelerometer] Accelerometer stopped successfully");
            }
            catch (Exception ex)
            {
                DebugLog($"[Accelerometer] Error stopping accelerometer: {ex.Message}");
                _accelerometerActive = false; // Reset flag even if write fails
            }
        }

        public async Task StartGyroscopeAsync(float sampleRate = 100f, float range = 2000f)
        {
            if (_commandCharacteristic == null || !IsConnected)
                throw new InvalidOperationException("Device not connected");

            try
            {
                DebugLog($"[Gyroscope] Starting gyroscope - SampleRate: {sampleRate}Hz, Range: {range} dps");
                
                if (_gyroscopeActive)
                {
                    DebugLog($"[Gyroscope] Stopping existing gyroscope first...");
                    await StopGyroscopeAsync();
                    await Task.Delay(100);
                }
                
                try
                {
                    DebugLog($"[Gyroscope] Stopping magnetometer (module 0x15) to prevent interference...");
                    byte[] stopMagnetometer = new byte[] { 0x15, 0x02 };
                    await WriteCommandAsync(stopMagnetometer);
                    await Task.Delay(50);
                    byte[] stopMagnetometer2 = new byte[] { 0x15, 0x01 };
                    await WriteCommandAsync(stopMagnetometer2);
                    await Task.Delay(50);
                }
                catch (Exception ex)
                {
                    DebugLog($"[Gyroscope] Error stopping magnetometer: {ex.Message}");
                }

                byte[] configCommand = new byte[]
                {
                    0x13, 0x04, 0x28, 0x0C
                };
                
                DebugLog($"[Gyroscope] Sending config command (phyphox pattern): [{string.Join(", ", configCommand.Select(b => $"0x{b:X2}"))}]");
                await WriteCommandAsync(configCommand);
                await Task.Delay(100);
                
                try
                {
                    DebugLog($"[Gyroscope] Setting up route using phyphox pattern...");
                    byte[] routeManagerSetup = new byte[] { 0x11, 0x09, 0x06, 0x00, 0x06, 0x00, 0x00, 0x00, 0x58, 0x02 };
                    DebugLog($"[Gyroscope] Route manager setup: [{string.Join(", ", routeManagerSetup.Select(b => $"0x{b:X2}"))}]");
                    await WriteCommandAsync(routeManagerSetup);
                    await Task.Delay(200);
                }
                catch (Exception routeEx)
                {
                    DebugLog($"[Gyroscope] Error setting up route manager: {routeEx.Message}");
                }
                
                byte[] enableProducer = new byte[] { 0x13, 0x04, 0x01 };
                DebugLog($"[Gyroscope] Enabling producer (phyphox pattern): [{string.Join(", ", enableProducer.Select(b => $"0x{b:X2}"))}]");
                await WriteCommandAsync(enableProducer);
                await Task.Delay(100);
                
                byte[] setRoute = new byte[] { 0x13, 0x02, 0x01, 0x00 };
                DebugLog($"[Gyroscope] Setting route ID: [{string.Join(", ", setRoute.Select(b => $"0x{b:X2}"))}]");
                await WriteCommandAsync(setRoute);
                await Task.Delay(100);
                
                byte[] enableModule = new byte[] { 0x13, 0x01, 0x01 };
                DebugLog($"[Gyroscope] Enabling module: [{string.Join(", ", enableModule.Select(b => $"0x{b:X2}"))}]");
                await WriteCommandAsync(enableModule);
                await Task.Delay(200);
                
                _gyroscopeActive = true;
                DebugLog($"[Gyroscope] Gyroscope started successfully - expecting notifications with Module 0x13, Register 0x04 (phyphox pattern)");
            }
            catch (Exception ex)
            {
                DebugLog($"[Gyroscope] Error starting gyroscope: {ex.Message}");
                DebugLog($"Stack trace: {ex.StackTrace}");
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
                DebugLog($"[Gyroscope] Stopping gyroscope...");
                
                byte[] stopProducerCommand = new byte[] { 0x13, 0x05 };
                DebugLog($"[Gyroscope] Sending stop producer command (0x05): [{string.Join(", ", stopProducerCommand.Select(b => $"0x{b:X2}"))}]");
                await WriteCommandAsync(stopProducerCommand);
                await Task.Delay(50);
                
                byte[] stopCommand1 = new byte[] { 0x13, 0x01 };
                DebugLog($"[Gyroscope] Sending stop command (0x01): [{string.Join(", ", stopCommand1.Select(b => $"0x{b:X2}"))}]");
                await WriteCommandAsync(stopCommand1);
                await Task.Delay(50);
                
                try
                {
                    byte[] removeRouteCommand = new byte[] { 0x12, 0x02, 0x02 };
                    DebugLog($"[Gyroscope] Removing route: [{string.Join(", ", removeRouteCommand.Select(b => $"0x{b:X2}"))}]");
                    await WriteCommandAsync(removeRouteCommand);
                    await Task.Delay(50);
                }
                catch (Exception ex2)
                {
                    DebugLog($"[Gyroscope] Error removing route: {ex2.Message}");
                }
                
                _gyroscopeActive = false;
                DebugLog($"[Gyroscope] Gyroscope stopped successfully");
            }
            catch (Exception ex)
            {
                DebugLog($"[Gyroscope] Error stopping gyroscope: {ex.Message}");
                _gyroscopeActive = false;
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

                // Read battery level from Battery Service
                int? batteryLevel = await ReadBatteryLevelAsync();

                _cachedDeviceInfo = new DeviceInfo
                {
                    Model = modelNumber ?? "Unknown",
                    SerialNumber = serialNumber ?? "Unknown",
                    FirmwareVersion = firmwareVersion ?? "Unknown",
                    HardwareVersion = hardwareVersion ?? "Unknown",
                    Manufacturer = manufacturer ?? "MbientLab",
                    BatteryPercentage = batteryLevel
                };

                return _cachedDeviceInfo;
            }
            catch (Exception ex)
            {
                DebugLog($"Error reading device info: {ex.Message}");
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
                DebugLog($"Error reading characteristic {uuid}: {ex.Message}");
                return null;
            }
        }

        private async Task<int?> ReadBatteryLevelAsync()
        {
            try
            {
                if (_device == null || !IsConnected)
                    return null;

                var services = await _device.GetServicesAsync();
                var batteryService = services.FirstOrDefault(s => s.Id == BatteryServiceUuid);
                if (batteryService == null)
                {
                    DebugLog("Battery service not found on device");
                    return null;
                }

                var characteristics = await batteryService.GetCharacteristicsAsync();
                var batteryChar = characteristics.FirstOrDefault(c => c.Id == BatteryLevelCharacteristicUuid);
                if (batteryChar == null || !batteryChar.CanRead)
                {
                    DebugLog("Battery level characteristic not found or not readable");
                    return null;
                }

                var (data, resultCode) = await batteryChar.ReadAsync();
                if (resultCode != 0 || data == null || data.Length == 0)
                    return null;

                int level = data[0];
                DebugLog($"Battery level: {level}%");
                return level;
            }
            catch (Exception ex)
            {
                DebugLog($"Error reading battery level: {ex.Message}");
                return null;
            }
        }

        private void ParseMagnetometerData(byte[] data)
        {
            try
            {
                // MetaWear magnetometer (BMM150) data format:
                // Register 0x05 (without timestamp): [module_id, register_id, x_low, x_high, y_low, y_high, z_low, z_high] (8 bytes)
                // Register 0x06 (with timestamp): [module_id, register_id, timestamp_bytes..., x_low, x_high, y_low, y_high, z_low, z_high] (12 bytes typically)
                // BMM150 uses 16-bit signed values
                
                byte registerId = data.Length > 1 ? data[1] : (byte)0;
                int dataOffset = 2; // Start after module_id and register_id
                
                // If register 0x06 (with timestamp), skip 4-byte timestamp
                if (registerId == 0x06 && data.Length >= 12)
                {
                    dataOffset = 6; // Skip module_id (1), register_id (1), and timestamp (4)
                }
                else if (data.Length < 8)
                {
                    DebugLog($"[Magnetometer] Data too short: {data.Length} bytes (need at least 8), Register: 0x{registerId:X2}");
                    return;
                }

                short x = BitConverter.ToInt16(data, dataOffset);
                short y = BitConverter.ToInt16(data, dataOffset + 2);
                short z = BitConverter.ToInt16(data, dataOffset + 4);

                // Convert to µT (C++ SDK datainterpreter.cpp: BMM150_SCALE = 16.f)
                float xUt = x / BMM150_SCALE;
                float yUt = y / BMM150_SCALE;
                float zUt = z / BMM150_SCALE;

                // Log raw data every 1 second to reduce lag (similar to accelerometer)
                DateTime now = DateTime.Now;
                if ((now - _lastMagnetometerLogTime).TotalSeconds >= 1.0)
                {
                    DebugLog($"[Magnetometer] Raw data (Reg: 0x{registerId:X2}): [{string.Join(", ", data.Select(b => $"0x{b:X2}"))}] | X: {xUt:F2}µT, Y: {yUt:F2}µT, Z: {zUt:F2}µT");
                    _lastMagnetometerLogTime = now;
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
                DebugLog($"[Magnetometer] Error parsing data: {ex.Message}");
                DebugLog($"[Magnetometer] Raw data: [{string.Join(", ", data.Select(b => $"0x{b:X2}"))}]");
            }
        }

        private void ParseLightSensorData(byte[] data)
        {
            try
            {
                // MetaWear light sensor (LTR-329ALS-01) data format:
                // Format: [module_id, register_id, visible_low, visible_high, ir_low, ir_high]
                // 6 bytes total (we only use visible)
                
                if (data.Length < 4)
                {
                    return;
                }

                ushort visible = BitConverter.ToUInt16(data, 2);

                // Reduced logging - only log occasionally (every 2 seconds) to avoid performance issues
                if ((DateTime.Now.Millisecond % 2000) < 10)
                {
                    DebugLog($"[LightSensor] Data - Visible: {visible}");
                }

                LightSensorDataReceived?.Invoke(this, new MetaWearLightSensorData
                {
                    Visible = visible,
                    Timestamp = DateTime.Now
                });
            }
            catch (Exception ex)
            {
                DebugLog($"[LightSensor] Error parsing data: {ex.Message}");
            }
        }

        public async Task StartMagnetometerAsync(float sampleRate = 25f)
        {
            if (_commandCharacteristic == null || !IsConnected)
                throw new InvalidOperationException("Device not connected");

            try
            {
                DebugLog($"[Magnetometer] Starting magnetometer - SampleRate: {sampleRate}Hz");
                
                if (_magnetometerActive)
                {
                    DebugLog($"[Magnetometer] Stopping existing magnetometer first...");
                    await StopMagnetometerAsync();
                    await Task.Delay(150);
                }

                // Step 1: Set preset mode (Regular preset = 1)
                try
                {
                    byte[] presetCommand = new byte[] { 0x15, 0x03, 0x01 };
                    DebugLog($"[Magnetometer] Setting preset mode (Regular): [{string.Join(", ", presetCommand.Select(b => $"0x{b:X2}"))}]");
                    await WriteCommandAsync(presetCommand);
                    await Task.Delay(100);
                }
                catch (Exception presetEx)
                {
                    DebugLog($"[Magnetometer] Error setting preset (may not be needed): {presetEx.Message}");
                }

                // Step 2: Configure the magnetometer ODR
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
                    0x15, 0x04,
                    odr
                };
                
                DebugLog($"[Magnetometer] Sending config command: [{string.Join(", ", configCommand.Select(b => $"0x{b:X2}"))}] (ODR={odr})");
                await WriteCommandAsync(configCommand);
                await Task.Delay(150);

                // Step 3: Create route using Route Manager
                try
                {
                    DebugLog($"[Magnetometer] Creating route using Route Manager...");
                    byte[] createRouteCommand = new byte[]
                    {
                        0x12, 0x03,
                        0x15, 0x05,
                        0x03,
                        0x01,
                        0x00
                    };
                    
                    DebugLog($"[Magnetometer] Creating route: [{string.Join(", ", createRouteCommand.Select(b => $"0x{b:X2}"))}]");
                    await WriteCommandAsync(createRouteCommand);
                    await Task.Delay(200);
                }
                catch (Exception routeEx)
                {
                    DebugLog($"[Magnetometer] Error creating route: {routeEx.Message}");
                    throw;
                }

                // Step 4: Enable the magnetometer module
                try
                {
                    byte[] enableModule = new byte[] { 0x15, 0x01, 0x01 };
                    DebugLog($"[Magnetometer] Enabling magnetometer module: [{string.Join(", ", enableModule.Select(b => $"0x{b:X2}"))}]");
                    await WriteCommandAsync(enableModule);
                    await Task.Delay(100);
                }
                catch (Exception enableEx)
                {
                    DebugLog($"[Magnetometer] Error enabling module: {enableEx.Message}");
                    throw;
                }

                // Step 5: Enable the magnetometer data producer
                byte[] enableProducer = new byte[]
                {
                    0x15, 0x05, 0x03
                };

                DebugLog($"[Magnetometer] Enabling magnetometer data producer (0x05): [{string.Join(", ", enableProducer.Select(b => $"0x{b:X2}"))}]");
                await WriteCommandAsync(enableProducer);
                await Task.Delay(150);

                _magnetometerActive = true;
                DebugLog($"[Magnetometer] Magnetometer started successfully");
            }
            catch (Exception ex)
            {
                DebugLog($"[Magnetometer] Error starting magnetometer: {ex.Message}");
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
                DebugLog($"[Magnetometer] Stopping magnetometer...");
                
                try
                {
                    byte[] stopProducerCommand = new byte[] { 0x15, 0x05 };
                    DebugLog($"[Magnetometer] Sending stop producer command (0x05): [{string.Join(", ", stopProducerCommand.Select(b => $"0x{b:X2}"))}]");
                    await WriteCommandAsync(stopProducerCommand);
                    await Task.Delay(50);
                }
                catch (Exception ex1)
                {
                    DebugLog($"[Magnetometer] Error stopping producer (0x05): {ex1.Message}");
                }
                
                try
                {
                    byte[] stopProducerCommand2 = new byte[] { 0x15, 0x06 };
                    DebugLog($"[Magnetometer] Sending stop producer command (0x06): [{string.Join(", ", stopProducerCommand2.Select(b => $"0x{b:X2}"))}]");
                    await WriteCommandAsync(stopProducerCommand2);
                    await Task.Delay(50);
                }
                catch (Exception ex1b)
                {
                    DebugLog($"[Magnetometer] Error stopping producer (0x06): {ex1b.Message}");
                }
                
                try
                {
                    byte[] removeRouteCommand = new byte[] { 0x12, 0x02, 0x03 };
                    DebugLog($"[Magnetometer] Removing route: [{string.Join(", ", removeRouteCommand.Select(b => $"0x{b:X2}"))}]");
                    await WriteCommandAsync(removeRouteCommand);
                    await Task.Delay(50);
                }
                catch (Exception ex2)
                {
                    DebugLog($"[Magnetometer] Error removing route: {ex2.Message}");
                }
                
                try
                {
                    byte[] disableModule = new byte[] { 0x15, 0x01, 0x00 };
                    DebugLog($"[Magnetometer] Disabling module: [{string.Join(", ", disableModule.Select(b => $"0x{b:X2}"))}]");
                    await WriteCommandAsync(disableModule);
                    await Task.Delay(50);
                }
                catch (Exception ex3)
                {
                    DebugLog($"[Magnetometer] Error disabling module: {ex3.Message}");
                }
                
                try
                {
                    byte[] stopCommand1 = new byte[] { 0x15, 0x01 };
                    DebugLog($"[Magnetometer] Sending general stop command (0x01): [{string.Join(", ", stopCommand1.Select(b => $"0x{b:X2}"))}]");
                    await WriteCommandAsync(stopCommand1);
                    await Task.Delay(50);
                }
                catch (Exception ex4)
                {
                    DebugLog($"[Magnetometer] Error sending general stop: {ex4.Message}");
                }

                _magnetometerActive = false;
                DebugLog($"[Magnetometer] Magnetometer stopped successfully");
            }
            catch (Exception ex)
            {
                DebugLog($"[Magnetometer] Error stopping magnetometer: {ex.Message}");
                _magnetometerActive = false;
            }
        }

        public async Task StartLightSensorAsync(float sampleRate = 10f)
        {
            if (_commandCharacteristic == null || !IsConnected)
                throw new InvalidOperationException("Device not connected");

            try
            {
                DebugLog($"[LightSensor] Starting light sensor - SampleRate: {sampleRate}Hz");
                
                // Stop light sensor first if already running
                if (_lightSensorActive)
                {
                    DebugLog($"[LightSensor] Stopping existing light sensor first...");
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
                
                DebugLog($"[LightSensor] Sending config command: [{string.Join(", ", configCommand.Select(b => $"0x{b:X2}"))}]");
                await WriteCommandAsync(configCommand);
                await Task.Delay(50);

                // Step 2: Create route using Route Manager
                try
                {
                    DebugLog($"[LightSensor] Creating route using Route Manager...");
                    byte[] createRouteCommand = new byte[]
                    {
                        0x12, 0x03,  // Route Manager (module 0x12, register 0x03 - create route)
                        0x14, 0x03,  // Producer: light sensor (0x14), data producer (0x03)
                        0x04,        // Route ID: 0x04
                        0x01,        // Endpoint type: 0x01 = stream (notifications)
                        0x00         // Endpoint ID: 0x00 = default
                    };
                    
                    DebugLog($"[LightSensor] Creating route: [{string.Join(", ", createRouteCommand.Select(b => $"0x{b:X2}"))}]");
                    await WriteCommandAsync(createRouteCommand);
                    await Task.Delay(150);
                }
                catch (Exception routeEx)
                {
                    DebugLog($"[LightSensor] Error creating route: {routeEx.Message}");
                }

                // Step 3: Enable the light sensor module
                try
                {
                    byte[] enableModule = new byte[] { 0x14, 0x01, 0x01 }; // Module 0x14, Register 0x01, Enable
                    DebugLog($"[LightSensor] Enabling light sensor module: [{string.Join(", ", enableModule.Select(b => $"0x{b:X2}"))}]");
                    await WriteCommandAsync(enableModule);
                    await Task.Delay(50);
                }
                catch (Exception enableEx)
                {
                    DebugLog($"[LightSensor] Error enabling module: {enableEx.Message}");
                }

                // Step 4: Enable the light sensor data producer
                byte[] enableProducer = new byte[]
                {
                    0x14, 0x03, 0x04  // Module ID: 0x14 (Light Sensor), Register ID: 0x03 (Data producer), Route ID: 0x04
                };

                DebugLog($"[LightSensor] Enabling light sensor data producer: [{string.Join(", ", enableProducer.Select(b => $"0x{b:X2}"))}]");
                await WriteCommandAsync(enableProducer);
                await Task.Delay(100);

                _lightSensorActive = true;
                DebugLog($"[LightSensor] Light sensor started successfully");
            }
            catch (Exception ex)
            {
                DebugLog($"[LightSensor] Error starting light sensor: {ex.Message}");
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
                DebugLog($"[LightSensor] Stopping light sensor...");
                
                // Stop light sensor - try multiple methods
                byte[] stopProducerCommand = new byte[] { 0x14, 0x03 }; // Stop data producer
                DebugLog($"[LightSensor] Sending stop producer command (0x03): [{string.Join(", ", stopProducerCommand.Select(b => $"0x{b:X2}"))}]");
                await WriteCommandAsync(stopProducerCommand);
                await Task.Delay(50);
                
                byte[] stopCommand1 = new byte[] { 0x14, 0x01 }; // General stop
                DebugLog($"[LightSensor] Sending stop command (0x01): [{string.Join(", ", stopCommand1.Select(b => $"0x{b:X2}"))}]");
                await WriteCommandAsync(stopCommand1);
                await Task.Delay(50);
                
                // Remove route using Route Manager
                try
                {
                    byte[] removeRouteCommand = new byte[] { 0x12, 0x02, 0x04 }; // Route Manager, remove route 0x04
                    DebugLog($"[LightSensor] Removing route: [{string.Join(", ", removeRouteCommand.Select(b => $"0x{b:X2}"))}]");
                    await WriteCommandAsync(removeRouteCommand);
                    await Task.Delay(50);
                }
                catch (Exception ex2)
                {
                    DebugLog($"[LightSensor] Error removing route: {ex2.Message}");
                }

                _lightSensorActive = false;
                DebugLog($"[LightSensor] Light sensor stopped successfully");
            }
            catch (Exception ex)
            {
                DebugLog($"[LightSensor] Error stopping light sensor: {ex.Message}");
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
                await WriteCommandAsync(resetCommand);
            }
            catch (Exception ex)
            {
                DebugLog($"Error resetting device: {ex.Message}");
                throw;
            }
        }

        public async Task ProbeDeviceAsync()
        {
            if (_commandCharacteristic == null || !IsConnected)
            {
                DebugLog("[Probe] Cannot probe - not connected");
                return;
            }

            try
            {
                DebugLog("[Probe] ========================================");
                DebugLog("[Probe] Starting device probe based on phyphox pattern");
                DebugLog("[Probe] ========================================");
                
                // First, reset using phyphox pattern
                DebugLog("[Probe] Step 1: Reset sequence (phyphox pattern)...");
                await WriteCommandAsync(new byte[] { 0x0B, 0x84 });
                await Task.Delay(100);
                await WriteCommandAsync(new byte[] { 0x0F, 0x08 });
                await Task.Delay(100);
                await WriteCommandAsync(new byte[] { 0xFE, 0x05 });
                await Task.Delay(200);

                // Try phyphox accelerometer setup sequence exactly as documented
                DebugLog("[Probe] Step 2: Phyphox accelerometer setup sequence...");
                await WriteCommandAsync(new byte[] { 0x0B, 0x84 });
                await Task.Delay(100);
                // Route manager setup: 0x11, 0x09, 0x06, 0x00, 0x06, 0x00, 0x00, 0x00, 0x58, 0x02
                await WriteCommandAsync(new byte[] { 0x11, 0x09, 0x06, 0x00, 0x06, 0x00, 0x00, 0x00, 0x58, 0x02 });
                await Task.Delay(200);
                // Config: 0x03, 0x04, 0x28, 0x0C (ODR=0=100Hz, Range=3=16G)
                await WriteCommandAsync(new byte[] { 0x03, 0x04, 0x28, 0x0C });
                await Task.Delay(100);
                // Enable producer: 0x03, 0x04, 0x01 (module 0x03, register 0x04, enable)
                await WriteCommandAsync(new byte[] { 0x03, 0x04, 0x01 });
                await Task.Delay(100);
                // Set route: 0x03, 0x02, 0x01, 0x00 (module 0x03, register 0x02, route ID 0x0100)
                await WriteCommandAsync(new byte[] { 0x03, 0x02, 0x01, 0x00 });
                await Task.Delay(100);
                // Enable module: 0x03, 0x01, 0x01
                await WriteCommandAsync(new byte[] { 0x03, 0x01, 0x01 });
                await Task.Delay(200);

                DebugLog("[Probe] Accelerometer setup complete!");
                DebugLog("[Probe] Expecting notifications: Module 0x03, Register 0x04");
                DebugLog("[Probe] Waiting 5 seconds for accelerometer data...");
                await Task.Delay(5000);

                DebugLog("[Probe] ========================================");
                DebugLog("[Probe] Probe complete. Check logs above for notification patterns.");
                DebugLog("[Probe] ========================================");
            }
            catch (Exception ex)
            {
                DebugLog($"[Probe] Error during probe: {ex.Message}");
                DebugLog($"Stack trace: {ex.StackTrace}");
            }
        }
    }
}

