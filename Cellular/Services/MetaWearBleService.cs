using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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
        // Prevents concurrent sensor start/stop operations that interleave BLE commands and crash firmware.
        // Uses Interlocked for atomic test-and-set so it's safe across async continuations.
        // 0 = free, 1 = busy. Start/Stop methods CAS from 0→1 on entry and reset to 0 on exit.
        private int _sensorBusy = 0;
        private Guid _savedDeviceId = Guid.Empty;      // Saved before clearing on disconnect, used for reconnect
        private bool _intentionalDisconnect = false;    // Set true in DisconnectAsync to suppress auto-reconnect
        private CancellationTokenSource? _reconnectCts; // Cancels in-progress reconnect on intentional disconnect
        // Current accelerometer LSB/g (set from range in Start; auto-corrected for MMS 2g in parser)
        private float _accelLsbPerG = ACC_SCALE_16G;
        // True when connected device is MMS (BMI270 accel/gyro, no magnetometer); false = MMC (BMI160, has BMM150)
        private bool _isMms = false;

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
                _isMms = false;
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

                // Negotiate a fast BLE connection interval before sending any sensor commands.
                // MetaBase app does this (Settings.editBleConnParams + 1000 ms delay) to prevent
                // GATT congestion when starting multiple sensors on Android.
                await RequestFastConnectionAsync();

                // Quiesce any sensors that may have been left running from a previous session.
                // Each block sends the correct 3-byte stop sequence; errors are swallowed so one
                // failed stop doesn't prevent the others from running.
                try { await WriteCommandAsync(new byte[] { 0x03, 0x04, 0x00 }); await Task.Delay(30); } catch { } // accel stream off
                try { await WriteCommandAsync(new byte[] { 0x03, 0x02, 0x00, 0x01 }); await Task.Delay(30); } catch { } // accel unsub
                try { await WriteCommandAsync(new byte[] { 0x03, 0x01, 0x00 }); await Task.Delay(30); } catch { } // accel power off
                _accelerometerActive = false;

                try { await WriteCommandAsync(new byte[] { 0x13, 0x04, 0x00 }); await Task.Delay(30); } catch { } // gyro stream off
                try { await WriteCommandAsync(new byte[] { 0x13, 0x02, 0x00, 0x01 }); await Task.Delay(30); } catch { } // gyro unsub
                try { await WriteCommandAsync(new byte[] { 0x13, 0x01, 0x00 }); await Task.Delay(30); } catch { } // gyro power off
                _gyroscopeActive = false;

                try { await WriteCommandAsync(new byte[] { 0x15, 0x05, 0x00 }); await Task.Delay(30); } catch { } // mag stream off
                try { await WriteCommandAsync(new byte[] { 0x15, 0x02, 0x00, 0x01 }); await Task.Delay(30); } catch { } // mag unsub
                try { await WriteCommandAsync(new byte[] { 0x15, 0x01, 0x00 }); await Task.Delay(30); } catch { } // mag power off
                _magnetometerActive = false;

                try { await WriteCommandAsync(new byte[] { 0x14, 0x03, 0x00 }); await Task.Delay(30); } catch { } // light stream off
                try { await WriteCommandAsync(new byte[] { 0x14, 0x01, 0x00 }); await Task.Delay(30); } catch { } // light power off
                _lightSensorActive = false;

                DebugLog("[Connection] All sensors quiesced");

                DebugLog("MetaWear device connected successfully!");

                // Load device info and detect model — wrapped so a BLE read failure never aborts the connection
                try
                {
                    _ = await GetDeviceInfoAsync();
                }
                catch (Exception devEx)
                {
                    DebugLog($"[Device] GetDeviceInfoAsync failed (non-fatal): {devEx.Message}");
                }

                // Fall back to BLE advertised name if Device Information Service didn't return a model
                if (string.IsNullOrEmpty(_cachedDeviceInfo?.Model) || _cachedDeviceInfo?.Model == "Unknown")
                {
                    var deviceName = _device?.Name ?? string.Empty;
                    _isMms = deviceName.Contains("MMS", StringComparison.OrdinalIgnoreCase)
                          || deviceName.Contains("MetaMotion S", StringComparison.OrdinalIgnoreCase);
                }
                else
                {
                    _isMms = _cachedDeviceInfo!.Model.Contains("MetaMotion S", StringComparison.OrdinalIgnoreCase)
                          || _cachedDeviceInfo!.Model.Contains("MMS", StringComparison.OrdinalIgnoreCase);
                }
                DebugLog($"[Device] Model: '{_cachedDeviceInfo?.Model}' Name: '{_device?.Name}' → {(_isMms ? "MMS (BMI270, no mag)" : "MMC (BMI160, has mag)")}");

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
                _isMms = false;
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

                DebugLog($"[Notification] Module: 0x{moduleId:X2}, Reg: 0x{registerId:X2}, Len: {data.Length}");

                // Module IDs from C++ SDK core/module.h (MBL_MW_MODULE_*)
                bool isAccelerometer = (moduleId == MW_MODULE_ACCELEROMETER);
                bool isGyroscope = (moduleId == MW_MODULE_GYRO);
                bool isLightSensor = (moduleId == MW_MODULE_AMBIENT_LIGHT);
                bool isMagnetometer = (moduleId == MW_MODULE_MAGNETOMETER);
                
                if (isAccelerometer)
                {
                    if (_accelerometerActive)
                        ParseAccelerometerData(data);
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

            if (Interlocked.CompareExchange(ref _sensorBusy, 1, 0) != 0)
            {
                DebugLog("[Accelerometer] Start ignored — sensor operation already in progress");
                return;
            }
            try
            {
                DebugLog($"[Accelerometer] Starting accelerometer - SampleRate: {sampleRate}Hz, Range: {range}G");
                // Set LSB/g from range (C++ SDK: BMI160_FSR_SCALE / BMI270_FSR_SCALE)
                _accelLsbPerG = range switch { <= 2f => ACC_SCALE_2G, <= 4f => ACC_SCALE_4G, <= 8f => ACC_SCALE_8G, _ => ACC_SCALE_16G };

                // Stop accelerometer first if already running
                if (_accelerometerActive)
                {
                    DebugLog($"[Accelerometer] Stopping existing accelerometer first...");
                    await StopAccelerometerCoreAsync();
                    await Task.Delay(100); // Small delay between stop and start
                }
                
                // Step 1: Configure — register 0x03.
                // ODR byte (Bosch BMI160/BMI270): 0x05=12.5Hz, 0x06=25Hz, 0x07=50Hz, 0x08=100Hz, 0x09=200Hz
                // Config byte = bwp_bits | odr: BMI160 bwp=normal→0x20; BMI270 performance→0xA0
                byte odrByte = sampleRate switch { <= 12.5f => 0x05, <= 25f => 0x06, <= 50f => 0x07, <= 100f => 0x08, _ => 0x09 };
                byte configByte0 = _isMms ? (byte)(0xA0 | odrByte) : (byte)(0x20 | odrByte);
                // BMI160 FSR: 2g=0x3, 4g=0x5, 8g=0x8, 16g=0xC.  BMI270 FSR: 2g=0, 4g=1, 8g=2, 16g=3
                int rangeIndex = range <= 2f ? 0 : range <= 4f ? 1 : range <= 8f ? 2 : 3;
                byte rangeByte = _isMms
                    ? (byte)rangeIndex
                    : (byte)(rangeIndex == 0 ? 0x3 : rangeIndex == 1 ? 0x5 : rangeIndex == 2 ? 0x8 : 0xC);
                await WriteCommandAsync(new byte[] { 0x03, 0x03, configByte0, rangeByte }); // config
                await Task.Delay(100);
                await WriteCommandAsync(new byte[] { 0x03, 0x04, 0x01 }); // enable stream
                await Task.Delay(100);
                await WriteCommandAsync(new byte[] { 0x03, 0x02, 0x01, 0x00 }); // subscribe
                await Task.Delay(100);
                await WriteCommandAsync(new byte[] { 0x03, 0x01, 0x01 }); // power on
                await Task.Delay(200);

                _accelerometerActive = true;
                DebugLog($"[Accelerometer] Started {sampleRate}Hz {range}G {(_isMms ? "BMI270" : "BMI160")}");
            }
            catch (Exception ex)
            {
                DebugLog($"[Accelerometer] Error starting accelerometer: {ex.Message}");
                DebugLog($"Stack trace: {ex.StackTrace}");
                _accelerometerActive = false;
                throw;
            }
            finally
            {
                Interlocked.Exchange(ref _sensorBusy, 0);
            }
        }

        public async Task StopAccelerometerAsync()
        {
            if (Interlocked.CompareExchange(ref _sensorBusy, 1, 0) != 0)
            {
                DebugLog("[Accelerometer] Stop ignored — sensor operation already in progress");
                return;
            }
            try { await StopAccelerometerCoreAsync(); }
            finally { Interlocked.Exchange(ref _sensorBusy, 0); }
        }

        /// <summary>
        /// Negotiates a faster BLE connection interval so the Android stack can keep up with
        /// multi-sensor command sequences. Must be called after notifications are enabled.
        /// Two-pronged: Android CONNECTION_PRIORITY_HIGH + MetaWear firmware BLE conn-params command.
        /// </summary>
        private async Task RequestFastConnectionAsync()
        {
            // Step 1: Android-side — ask the BT stack for a high-priority (low-latency) connection.
            // Plugin.BLE doesn't expose this directly so we reach into its internal BluetoothGatt
            // via reflection. This is safe to skip if reflection fails — the MetaWear command below
            // also negotiates the interval from the peripheral side.
#if __ANDROID__
            try
            {
                var gattField = _device?.GetType().GetField("_gatt",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (gattField?.GetValue(_device) is Android.Bluetooth.BluetoothGatt gatt)
                {
                    gatt.RequestConnectionPriority(Android.Bluetooth.GattConnectionPriority.High);
                    DebugLog("[BLE] Android CONNECTION_PRIORITY_HIGH requested");
                }
                else
                {
                    DebugLog("[BLE] Could not get BluetoothGatt via reflection — skipping Android priority request");
                }
            }
            catch (Exception ex)
            {
                DebugLog($"[BLE] Android priority request failed (non-fatal): {ex.Message}");
            }
#endif

            // Step 2: MetaWear-side — tell the firmware to request a 7.5 ms connection interval.
            // SETTINGS module (0x11), register CONNECTION_PARAMS (0x09).
            // Payload (little-endian): minInterval=6, maxInterval=6, slaveLatency=0, timeout=600
            //   6 × 1.25 ms = 7.5 ms connection interval (fastest Android allows).
            //   600 × 10 ms = 6000 ms supervision timeout.
            // MetaBase app sends this then waits 1000 ms before any sensor commands.
            try
            {
                byte[] connParams = new byte[]
                {
                    0x11, 0x09,       // SETTINGS module, CONNECTION_PARAMS register
                    0x06, 0x00,       // minConnectionInterval = 6 (7.5 ms)
                    0x06, 0x00,       // maxConnectionInterval = 6 (7.5 ms)
                    0x00, 0x00,       // slaveLatency = 0
                    0x58, 0x02        // supervisorTimeout = 0x0258 = 600 (6000 ms)
                };
                DebugLog($"[BLE] Sending MetaWear BLE conn-params (7.5 ms interval): [{string.Join(", ", connParams.Select(b => $"0x{b:X2}"))}]");
                await WriteCommandAsync(connParams);
            }
            catch (Exception ex)
            {
                DebugLog($"[BLE] MetaWear conn-params command failed (non-fatal): {ex.Message}");
            }

            // Wait for BLE stack to complete parameter negotiation before any sensor commands.
            DebugLog("[BLE] Waiting 1000 ms for connection parameter negotiation...");
            await Task.Delay(1000);
            DebugLog("[BLE] Connection parameter negotiation complete");
        }

        private async Task StopAccelerometerCoreAsync()
        {
            if (_commandCharacteristic == null || !IsConnected)
            {
                _accelerometerActive = false;
                return;
            }

            try
            {
                DebugLog("[Accelerometer] Stopping...");
                try { await WriteCommandAsync(new byte[] { 0x03, 0x04, 0x00 }); await Task.Delay(50); } catch { }
                try { await WriteCommandAsync(new byte[] { 0x03, 0x02, 0x00, 0x01 }); await Task.Delay(50); } catch { }
                try { await WriteCommandAsync(new byte[] { 0x03, 0x01, 0x00 }); await Task.Delay(50); } catch { }
                _accelerometerActive = false;
                DebugLog("[Accelerometer] Stopped");
            }
            catch (Exception ex)
            {
                DebugLog($"[Accelerometer] Stop error: {ex.Message}");
                _accelerometerActive = false;
            }
        }

        /// <summary>
        /// Starts accelerometer + light sensor together following the MetaBase Android app pattern:
        ///   Phase 1 — configure + subscribe both sensors (no power-on yet)
        ///   Phase 2 — power on both sensors
        /// This prevents the MMC firmware from silently dropping the LTR-329 stream when the
        /// BMI160 DATA_INTERRUPT is registered while the sensor is already running.
        /// </summary>
        public async Task StartAccelAndLightAsync(
            float accelSampleRate = 50f, float accelRange = 16f,
            float lightSampleRate = 10f, byte lightGain = 0,
            byte lightIntegrationTime = 0, byte lightMeasurementRate = 1)
        {
            if (_commandCharacteristic == null || !IsConnected)
                throw new InvalidOperationException("Device not connected");

            if (Interlocked.CompareExchange(ref _sensorBusy, 1, 0) != 0)
            {
                DebugLog("[AccelLight] Start ignored — sensor operation already in progress");
                return;
            }
            try
            {
                // Stop both cleanly before reconfiguring
                if (_accelerometerActive) await StopAccelerometerCoreAsync();
                if (_lightSensorActive) await StopLightSensorCoreAsync();
                await Task.Delay(150);

                // ── Phase 1: configure + subscribe both sensors ──────────────────────────
                // Accel config
                _accelLsbPerG = accelRange switch { <= 2f => ACC_SCALE_2G, <= 4f => ACC_SCALE_4G, <= 8f => ACC_SCALE_8G, _ => ACC_SCALE_16G };
                byte odrByte = accelSampleRate switch { <= 12.5f => 0x05, <= 25f => 0x06, <= 50f => 0x07, <= 100f => 0x08, _ => 0x09 };
                byte configByte0 = _isMms ? (byte)(0xA0 | odrByte) : (byte)(0x20 | odrByte);
                int rangeIndex = accelRange <= 2f ? 0 : accelRange <= 4f ? 1 : accelRange <= 8f ? 2 : 3;
                byte rangeByte = _isMms
                    ? (byte)rangeIndex
                    : (byte)(rangeIndex == 0 ? 0x3 : rangeIndex == 1 ? 0x5 : rangeIndex == 2 ? 0x8 : 0xC);
                await WriteCommandAsync(new byte[] { 0x03, 0x03, configByte0, rangeByte }); await Task.Delay(30);
                await WriteCommandAsync(new byte[] { 0x03, 0x04, 0x01 }); await Task.Delay(30); // accel enable stream
                await WriteCommandAsync(new byte[] { 0x03, 0x02, 0x01, 0x00 }); await Task.Delay(30); // accel subscribe

                // Light config — registered AFTER accel subscribe so it cannot be reset by it
                await WriteCommandAsync(new byte[] { 0x14, 0x02, (byte)(lightGain << 2), (byte)((lightIntegrationTime << 3) | (lightMeasurementRate & 0x7)) }); await Task.Delay(30);
                await WriteCommandAsync(new byte[] { 0x14, 0x03, 0x01 }); await Task.Delay(30); // light enable stream

                // ── Phase 2: power on both sensors ──────────────────────────────────────
                await WriteCommandAsync(new byte[] { 0x03, 0x01, 0x01 }); await Task.Delay(50); // accel power on
                await WriteCommandAsync(new byte[] { 0x14, 0x01, 0x01 }); await Task.Delay(50); // light power on

                _accelerometerActive = true;
                _lightSensorActive = true;
                DebugLog($"[AccelLight] Both sensors started: accel {accelSampleRate}Hz {accelRange}G, light {lightSampleRate}Hz");
            }
            catch (Exception ex)
            {
                DebugLog($"[AccelLight] Error: {ex.Message}");
                _accelerometerActive = false;
                _lightSensorActive = false;
                throw;
            }
            finally
            {
                Interlocked.Exchange(ref _sensorBusy, 0);
            }
        }

        public async Task StartGyroscopeAsync(float sampleRate = 100f, float range = 2000f)
        {
            if (_commandCharacteristic == null || !IsConnected)
                throw new InvalidOperationException("Device not connected");

            if (Interlocked.CompareExchange(ref _sensorBusy, 1, 0) != 0)
            {
                DebugLog("[Gyroscope] Start ignored — sensor operation already in progress");
                return;
            }
            try
            {
                DebugLog($"[Gyroscope] Starting {sampleRate}Hz {range}dps");

                if (_gyroscopeActive)
                {
                    await StopGyroscopeCoreAsync();
                    await Task.Delay(100);
                }

                // ODR byte: BMI160 normal-BWP = 0x20|odr, BMI270 performance = 0xA0|odr
                // odr: 0x07=100Hz, 0x06=50Hz
                byte odrByte = sampleRate <= 50f ? (byte)0x06 : (byte)0x07;
                byte gyroConfigByte = _isMms ? (byte)(0xA0 | odrByte) : (byte)(0x20 | odrByte);
                await WriteCommandAsync(new byte[] { 0x13, 0x03, gyroConfigByte, 0x00 });
                await Task.Delay(100);
                await WriteCommandAsync(new byte[] { 0x13, 0x04, 0x01 }); // enable stream
                await Task.Delay(100);
                await WriteCommandAsync(new byte[] { 0x13, 0x02, 0x01, 0x00 }); // subscribe
                await Task.Delay(100);
                await WriteCommandAsync(new byte[] { 0x13, 0x01, 0x01 }); // power on
                await Task.Delay(200);

                _gyroscopeActive = true;
                DebugLog("[Gyroscope] Started");
            }
            catch (Exception ex)
            {
                DebugLog($"[Gyroscope] Error starting gyroscope: {ex.Message}");
                DebugLog($"Stack trace: {ex.StackTrace}");
                _gyroscopeActive = false;
                throw;
            }
            finally
            {
                Interlocked.Exchange(ref _sensorBusy, 0);
            }
        }

        public async Task StopGyroscopeAsync()
        {
            if (Interlocked.CompareExchange(ref _sensorBusy, 1, 0) != 0)
            {
                DebugLog("[Gyroscope] Stop ignored — sensor operation already in progress");
                return;
            }
            try { await StopGyroscopeCoreAsync(); }
            finally { Interlocked.Exchange(ref _sensorBusy, 0); }
        }

        private async Task StopGyroscopeCoreAsync()
        {
            if (_commandCharacteristic == null || !IsConnected)
            {
                _gyroscopeActive = false;
                return;
            }

            try
            {
                DebugLog("[Gyroscope] Stopping...");
                try { await WriteCommandAsync(new byte[] { 0x13, 0x04, 0x00 }); await Task.Delay(50); } catch { }
                try { await WriteCommandAsync(new byte[] { 0x13, 0x02, 0x00, 0x01 }); await Task.Delay(50); } catch { }
                try { await WriteCommandAsync(new byte[] { 0x13, 0x01, 0x00 }); await Task.Delay(50); } catch { }
                _gyroscopeActive = false;
                DebugLog("[Gyroscope] Stopped");
            }
            catch (Exception ex)
            {
                DebugLog($"[Gyroscope] Stop error: {ex.Message}");
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

            // MMS uses BMI270 and has no BMM150 magnetometer — skip silently
            if (_isMms)
            {
                DebugLog("[Magnetometer] MMS detected — no magnetometer hardware, skipping");
                return;
            }

            if (Interlocked.CompareExchange(ref _sensorBusy, 1, 0) != 0)
            {
                DebugLog("[Magnetometer] Start ignored — sensor operation already in progress");
                return;
            }
            try
            {
                DebugLog($"[Magnetometer] Starting magnetometer (MMC/BMM150)");

                if (_magnetometerActive)
                {
                    await StopMagnetometerCoreAsync();
                    await Task.Delay(150);
                }

                // SDK configure().commit() always calls stop() first to put BMM150 in sleep mode
                // before writing to configuration registers (required by BMM150 hardware)
                byte[] sleepCommand = new byte[] { 0x15, 0x01, 0x00 };
                DebugLog($"[Magnetometer] Putting BMM150 in sleep mode before config: [{string.Join(", ", sleepCommand.Select(b => $"0x{b:X2}"))}]");
                await WriteCommandAsync(sleepCommand);
                await Task.Delay(100);

                // Regular preset: 9 XY reps, 15 Z reps
                // DATA_REPETITIONS register 0x04: xy_byte=(9-1)/2=4, z_byte=15-1=14(0x0E)
                byte[] repsCommand = new byte[] { 0x15, 0x04, 0x04, 0x0E };
                DebugLog($"[Magnetometer] Setting repetitions (Regular preset): [{string.Join(", ", repsCommand.Select(b => $"0x{b:X2}"))}]");
                await WriteCommandAsync(repsCommand);
                await Task.Delay(100);

                // DATA_RATE register 0x03: 0x00 = 10Hz (correct max for Regular preset)
                byte[] odrCommand = new byte[] { 0x15, 0x03, 0x00 };
                DebugLog($"[Magnetometer] Setting ODR (10Hz): [{string.Join(", ", odrCommand.Select(b => $"0x{b:X2}"))}]");
                await WriteCommandAsync(odrCommand);
                await Task.Delay(100);

                // MAG_DATA register 0x05: enable data producer (same pattern as accel 0x04 and gyro 0x05)
                byte[] enableProducer = new byte[] { 0x15, 0x05, 0x01 };
                DebugLog($"[Magnetometer] Enabling data producer (register 0x05): [{string.Join(", ", enableProducer.Select(b => $"0x{b:X2}"))}]");
                await WriteCommandAsync(enableProducer);
                await Task.Delay(100);

                // DATA_INTERRUPT_ENABLE register 0x02: subscribe to notifications
                byte[] subscribeCommand = new byte[] { 0x15, 0x02, 0x01, 0x00 };
                DebugLog($"[Magnetometer] Subscribing to data (register 0x02): [{string.Join(", ", subscribeCommand.Select(b => $"0x{b:X2}"))}]");
                await WriteCommandAsync(subscribeCommand);
                await Task.Delay(100);

                // POWER_MODE register 0x01: 0x01 = start sampling
                byte[] startCommand = new byte[] { 0x15, 0x01, 0x01 };
                DebugLog($"[Magnetometer] Starting power: [{string.Join(", ", startCommand.Select(b => $"0x{b:X2}"))}]");
                await WriteCommandAsync(startCommand);
                await Task.Delay(150);

                _magnetometerActive = true;
                DebugLog("[Magnetometer] Magnetometer started successfully");
            }
            catch (Exception ex)
            {
                DebugLog($"[Magnetometer] Error starting magnetometer: {ex.Message}");
                _magnetometerActive = false;
                // Do not re-throw — a magnetometer failure must not abort StartBufferingAsync
                // before StartLightSensorAsync is reached (light sensor triggers the recording).
            }
            finally
            {
                Interlocked.Exchange(ref _sensorBusy, 0);
            }
        }

        public async Task StopMagnetometerAsync()
        {
            if (Interlocked.CompareExchange(ref _sensorBusy, 1, 0) != 0)
            {
                DebugLog("[Magnetometer] Stop ignored — sensor operation already in progress");
                return;
            }
            try { await StopMagnetometerCoreAsync(); }
            finally { Interlocked.Exchange(ref _sensorBusy, 0); }
        }

        private async Task StopMagnetometerCoreAsync()
        {
            if (_isMms) { _magnetometerActive = false; return; }
            if (_commandCharacteristic == null || !IsConnected)
            {
                _magnetometerActive = false;
                return;
            }

            try
            {
                DebugLog("[Magnetometer] Stopping magnetometer...");

                // Disable data producer: MAG_DATA register 0x05
                try { await WriteCommandAsync(new byte[] { 0x15, 0x05, 0x00 }); await Task.Delay(50); } catch { }

                // Disable subscribe: DATA_INTERRUPT_ENABLE register 0x02 (enable_mask=0x00, disable_mask=0x01)
                try { await WriteCommandAsync(new byte[] { 0x15, 0x02, 0x00, 0x01 }); await Task.Delay(50); } catch { }

                // Power off: POWER_MODE register 0x01
                try { await WriteCommandAsync(new byte[] { 0x15, 0x01, 0x00 }); await Task.Delay(50); } catch { }

                _magnetometerActive = false;
                DebugLog("[Magnetometer] Magnetometer stopped successfully");
            }
            catch (Exception ex)
            {
                DebugLog($"[Magnetometer] Error stopping magnetometer: {ex.Message}");
                _magnetometerActive = false;
            }
        }

        public async Task StartLightSensorAsync(float sampleRate = 10f, byte gain = 0, byte integrationTime = 0, byte measurementRate = 1)
        {
            if (_commandCharacteristic == null || !IsConnected)
                throw new InvalidOperationException("Device not connected");

            if (Interlocked.CompareExchange(ref _sensorBusy, 1, 0) != 0)
            {
                DebugLog("[LightSensor] Start ignored — sensor operation already in progress");
                return;
            }
            try
            {
                DebugLog($"[LightSensor] Starting light sensor - SampleRate: {sampleRate}Hz");

                // Stop light sensor first if already running
                if (_lightSensorActive)
                {
                    DebugLog($"[LightSensor] Stopping existing light sensor first...");
                    await StopLightSensorCoreAsync();
                    await Task.Delay(100);
                }

                // LTR-329ALS-01 (module 0x14) startup sequence.
                // SDK order (AmbientLightLtr329Impl + StreamedDataConsumer):
                //   1. Configure (register 0x02): gain, integration time, measurement rate
                //   2. Enable stream (register 0x03, 0x01): registers the BLE notification consumer
                //   3. Start module (register 0x01, 0x01): begins sampling
                // IMPORTANT: stream enable MUST come before module start. If the module starts
                // before the stream consumer is registered the firmware drops readings silently,
                // which is the root cause of "light sensor stops working when accelerometer is on."

                // Step 1 — Configure
                // gain bitmask: bits [4:2] of ALS_CONTR (gain: 0=1x,1=2x,2=4x,3=8x,6=48x,7=96x)
                // measurementRate: 0=50ms,1=100ms,2=200ms,3=500ms,4=1000ms,5=2000ms
                // integrationTime: 0=100ms,1=50ms,2=200ms,3=400ms,4=150ms,5=250ms,6=300ms,7=350ms
                byte[] configCommand = new byte[]
                {
                    0x14, 0x02,
                    (byte)(gain << 2),
                    (byte)((integrationTime << 3) | (measurementRate & 0x7))
                };
                DebugLog($"[LightSensor] Config: [{string.Join(", ", configCommand.Select(b => $"0x{b:X2}"))}]");
                await WriteCommandAsync(configCommand);
                await Task.Delay(50);

                // Step 2 — Enable stream (register stream consumer before the sensor starts)
                byte[] enableStream = new byte[] { 0x14, 0x03, 0x01 };
                DebugLog($"[LightSensor] Enable stream: [{string.Join(", ", enableStream.Select(b => $"0x{b:X2}"))}]");
                await WriteCommandAsync(enableStream);
                await Task.Delay(50);

                // Step 3 — Start module (sensor begins sampling)
                byte[] enableModule = new byte[] { 0x14, 0x01, 0x01 };
                DebugLog($"[LightSensor] Start module: [{string.Join(", ", enableModule.Select(b => $"0x{b:X2}"))}]");
                await WriteCommandAsync(enableModule);
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
            finally
            {
                Interlocked.Exchange(ref _sensorBusy, 0);
            }
        }

        public async Task StopLightSensorAsync()
        {
            if (Interlocked.CompareExchange(ref _sensorBusy, 1, 0) != 0)
            {
                DebugLog("[LightSensor] Stop ignored — sensor operation already in progress");
                return;
            }
            try { await StopLightSensorCoreAsync(); }
            finally { Interlocked.Exchange(ref _sensorBusy, 0); }
        }

        private async Task StopLightSensorCoreAsync()
        {
            if (_commandCharacteristic == null || !IsConnected)
            {
                _lightSensorActive = false;
                return;
            }

            try
            {
                DebugLog($"[LightSensor] Stopping light sensor...");

                // Disable data stream (reverse of enableStream { 0x14, 0x03, 0x01 })
                try
                {
                    byte[] disableStream = new byte[] { 0x14, 0x03, 0x00 };
                    DebugLog($"[LightSensor] Disabling data stream (register 0x03): [{string.Join(", ", disableStream.Select(b => $"0x{b:X2}"))}]");
                    await WriteCommandAsync(disableStream);
                    await Task.Delay(50);
                }
                catch (Exception ex2)
                {
                    DebugLog($"[LightSensor] Error disabling stream: {ex2.Message}");
                }

                // Power off (reverse of { 0x14, 0x01, 0x01 })
                try
                {
                    byte[] disableModule = new byte[] { 0x14, 0x01, 0x00 };
                    DebugLog($"[LightSensor] Disabling module (register 0x01): [{string.Join(", ", disableModule.Select(b => $"0x{b:X2}"))}]");
                    await WriteCommandAsync(disableModule);
                    await Task.Delay(50);
                }
                catch (Exception ex3)
                {
                    DebugLog($"[LightSensor] Error disabling module: {ex3.Message}");
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
                // Debug module (0xfe), register 0x01 = mbl_mw_debug_reset
                byte[] resetCommand = new byte[] { 0xfe, 0x01 };
                await WriteCommandAsync(resetCommand);
                DebugLog("[Device] Reset command sent");
            }
            catch (Exception ex)
            {
                DebugLog($"Error resetting device: {ex.Message}");
                throw;
            }
        }

        public async Task SleepAsync()
        {
            if (_commandCharacteristic == null || !IsConnected)
                throw new InvalidOperationException("Device not connected");

            try
            {
                // Deep sleep sequence per MetaWear Android SDK (debug.enablePowersave + resetAsync):
                //   1. mbl_mw_debug_enable_power_save  → { 0xfe, 0x07 }
                //      Marks the device to enter low-power sleep on the next reset.
                //      Wake-up requires pressing the physical button or plugging in USB.
                //   2. mbl_mw_debug_reset              → { 0xfe, 0x01 }
                //      Triggers the reset; device immediately goes into deep sleep.
                await WriteCommandAsync(new byte[] { 0xfe, 0x07 });
                await Task.Delay(100); // brief pause so the flag is committed
                await WriteCommandAsync(new byte[] { 0xfe, 0x01 });
                DebugLog("[Device] Deep sleep command sequence sent (enable_power_save + reset)");
            }
            catch (Exception ex)
            {
                DebugLog($"Error sending sleep command: {ex.Message}");
                throw;
            }
        }

        public Task StartBarometerAsync(byte oversampling = 3, byte iirFilter = 0, byte standbyTime = 0)
            => Task.CompletedTask; // Not used for MMC/MMS sensor recording

        public Task StopBarometerAsync()
            => Task.CompletedTask;

        public async Task ProbeDeviceAsync()
        {
            if (_commandCharacteristic == null || !IsConnected)
            {
                System.Diagnostics.Debug.WriteLine("[Probe] Cannot probe — not connected");
                return;
            }

            // Read device info and log BLE connection state for diagnostics
            System.Diagnostics.Debug.WriteLine("[Probe] ---- Device probe ----");
            System.Diagnostics.Debug.WriteLine($"[Probe] Model: {_cachedDeviceInfo?.Model ?? "unknown"}  MMS={_isMms}");
            System.Diagnostics.Debug.WriteLine($"[Probe] Accel={_accelerometerActive}  Gyro={_gyroscopeActive}  Mag={_magnetometerActive}  Light={_lightSensorActive}");
            System.Diagnostics.Debug.WriteLine("[Probe] Done");
            await Task.CompletedTask;
        }
    }
}

