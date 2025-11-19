using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Plugin.BLE;
using Plugin.BLE.Abstractions.Contracts;
using Plugin.BLE.Abstractions.Extensions;
using Plugin.BLE.Abstractions;
using System.Text.Json;

namespace Cellular.Services
{
    /// <summary>
    /// BLE-based implementation of IMetaWearService using Plugin.BLE
    /// This implements the MetaWear protocol directly via BLE GATT
    /// </summary>
    public class MetaWearBleService : IMetaWearService
    {
        
        // MetaWear GATT Service UUIDs
        private static readonly Guid MetaWearServiceUuid = Guid.Parse("326A9000-85CB-9195-D9DD-464CFBBAE75A");
        private static readonly Guid MetaWearCommandCharacteristicUuid = Guid.Parse("326A9001-85CB-9195-D9DD-464CFBBAE75A");
        private static readonly Guid MetaWearNotificationCharacteristicUuid = Guid.Parse("326A9002-85CB-9195-D9DD-464CFBBAE75A");
        private static readonly Guid MetaWearNotificationCharacteristicUuidAlt = Guid.Parse("326A9006-85CB-9195-D9DD-464CFBBAE75A");
        
        // Custom Watch GATT Service UUIDs
        private static readonly Guid WatchServiceUuid = Guid.Parse("a3c94f10-7b47-4c8e-b88f-0e4b2f7c2a91");
        private static readonly Guid WatchCommandCharacteristicUuid = Guid.Parse("a3c94f11-7b47-4c8e-b88f-0e4b2f7c2a91");
        private static readonly Guid WatchNotifyCharacteristicUuid = Guid.Parse("a3c94f12-7b47-4c8e-b88f-0e4b2f7c2a91");
        
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
        private IService? _gattService;
        private ICharacteristic? _commandCharacteristic;
        private ICharacteristic? _notificationCharacteristic;
        private DeviceInfo? _cachedDeviceInfo;
        private bool _isDeviceConnected;
        private bool _isWatchDevice;

        public event EventHandler<string> DeviceDisconnected;
        public event EventHandler<string>? WatchJsonReceived;

        public bool IsConnected => _isDeviceConnected && _device != null;
        public string MacAddress => _device?.Id.ToString() ?? string.Empty;

        public MetaWearBleService()
        {
            _bluetoothLE = CrossBluetoothLE.Current;
            _adapter = CrossBluetoothLE.Current.Adapter;
            
            _adapter.DeviceDisconnected += OnDeviceDisconnected;
            
            LogDebug("MetaWearBleService initialized");
        }
        private void LogDebug(string message) //for debuggin in logcat
        {
#if ANDROID
            Android.Util.Log.Debug("METAWEAR_BLE", message);
#endif
            System.Diagnostics.Debug.WriteLine($"[METAWEAR] {message}");
            Console.WriteLine($"[METAWEAR] {message}");
        }

        private void LogError(string message)
        {
#if ANDROID
            Android.Util.Log.Error("METAWEAR_BLE", message);
#endif
            System.Diagnostics.Debug.WriteLine($"[METAWEAR ERROR] {message}");
            Console.WriteLine($"[METAWEAR ERROR] {message}");
        }

        private void OnDeviceDisconnected(object? sender, Plugin.BLE.Abstractions.EventArgs.DeviceEventArgs e)
        {
            if (e.Device.Id == _device?.Id)
            {
                LogDebug($"Device disconnected: {e.Device.Name}");
                _isDeviceConnected = false;
                _device = null;
                _gattService = null;
                _commandCharacteristic = null;
                _notificationCharacteristic = null;
                _cachedDeviceInfo = null;
                _isWatchDevice = false;
                
                DeviceDisconnected?.Invoke(this, MacAddress);
            }
        }

        public async Task<bool> ConnectAsync(object device)
        {
            try
            {
                LogDebug("=== ConnectAsync called ===");
                
                if (device == null)
                {
                    LogError("Device parameter is null");
                    return false;
                }
                
                LogDebug($"Device type: {device.GetType().FullName}");
                
                if (device is IDevice bleDevice)
                {
                    _device = bleDevice;
                    LogDebug($"Device cast successful: {bleDevice.Name ?? "Unknown"} ({bleDevice.Id})");
                    return await ConnectToDeviceAsync();
                }
                
                LogError($"Invalid device object type: {device.GetType()}");
                return false;
            }
            catch (Exception ex)
            {
                LogError($"Exception in ConnectAsync: {ex.Message}");
                LogError($"Stack trace: {ex.StackTrace}");
                return false;
            }
        }

        private async Task<bool> ConnectToDeviceAsync()
        {
            if (_device == null)
            {
                LogError("Device is null in ConnectToDeviceAsync");
                return false;
            }

            try
            {
                LogDebug($"=== Starting connection to: {_device.Name ?? "Unknown"} ({_device.Id}) ===");
                if (_adapter.IsScanning)
                {
                    LogDebug("Adapter is scanning, stopping scan before connect...");
                    try { await _adapter.StopScanningForDevicesAsync(); } catch { }
                }
                var parameters = new ConnectParameters(
                    autoConnect: false,
                    forceBleTransport: true  // important for some devices
                );

                LogDebug("Attempting BLE connection with explicit ConnectParameters...");
                await _adapter.ConnectToDeviceAsync(_device, parameters);
        
                LogDebug("BLE connected, waiting for services to stabilize...");
                await Task.Delay(1500);
                LogDebug("Discovering services...");
                var services = await _device.GetServicesAsync();
                
                LogDebug($"Found {services.Count()} service(s):");
                foreach (var service in services)
                {
                    LogDebug($"  • Service: {service.Id}");
                }
                
                // Try to find Watch service first, then MetaWear service
                _gattService = services.FirstOrDefault(s => s.Id == WatchServiceUuid);
                
                if (_gattService != null)
                {
                    LogDebug($"✓ Found Watch service: {_gattService.Id}");
                    _isWatchDevice = true;
                }
                else
                {
                    _gattService = services.FirstOrDefault(s => s.Id == MetaWearServiceUuid);
                    if (_gattService != null)
                    {
                        LogDebug($"✓ Found MetaWear service: {_gattService.Id}");
                        _isWatchDevice = false;
                    }
                }

                if (_gattService == null)
                {
                    LogError("✗ ERROR: Neither Watch nor MetaWear service found!");
                    LogError($"  Expected Watch UUID: {WatchServiceUuid}");
                    LogError($"  Expected MetaWear UUID: {MetaWearServiceUuid}");
                    _isDeviceConnected = false;
                    await _adapter.DisconnectDeviceAsync(_device);
                    return false;
                }
                LogDebug("Discovering characteristics...");
                var characteristics = await _gattService.GetCharacteristicsAsync();
                
                LogDebug($"Found {characteristics.Count()} characteristic(s):");
                foreach (var characteristic in characteristics)
                {
                    LogDebug($"  • Characteristic: {characteristic.Id}");
                    LogDebug($"    Properties: Read={characteristic.CanRead}, Write={characteristic.CanWrite}, Notify={characteristic.CanUpdate}");
                }
                if (_isWatchDevice)
                {
                    LogDebug("Looking for Watch characteristics...");
                    _commandCharacteristic = characteristics.FirstOrDefault(c => c.Id == WatchCommandCharacteristicUuid);
                    _notificationCharacteristic = characteristics.FirstOrDefault(c => c.Id == WatchNotifyCharacteristicUuid);
                    
                    LogDebug($"  Command char (write): {(_commandCharacteristic != null ? "✓ Found" : "✗ Not found")}");
                    LogDebug($"  Notify char (notify): {(_notificationCharacteristic != null ? "✓ Found" : "✗ Not found")}");
                }
                else
                {
                    LogDebug("Looking for MetaWear characteristics...");
                    _commandCharacteristic = characteristics.FirstOrDefault(c => c.Id == MetaWearCommandCharacteristicUuid);
                    _notificationCharacteristic = characteristics.FirstOrDefault(c => 
                        c.Id == MetaWearNotificationCharacteristicUuid || 
                        c.Id == MetaWearNotificationCharacteristicUuidAlt);
                    
                    LogDebug($"  Command char: {(_commandCharacteristic != null ? "✓ Found" : "✗ Not found")}");
                    LogDebug($"  Notify char: {(_notificationCharacteristic != null ? "✓ Found" : "✗ Not found")}");
                }

                if (_commandCharacteristic == null)
                {
                    LogError("✗ ERROR: Command characteristic not found!");
                    if (_isWatchDevice)
                    {
                        LogError($"  Expected UUID: {WatchCommandCharacteristicUuid}");
                    }
                    else
                    {
                        LogError($"  Expected UUID: {MetaWearCommandCharacteristicUuid}");
                    }
                    _isDeviceConnected = false;
                    await _adapter.DisconnectDeviceAsync(_device);
                    return false;
                }
                
                if (_notificationCharacteristic == null)
                {
                    LogError("✗ ERROR: Notification characteristic not found!");
                    if (_isWatchDevice)
                    {
                        LogError($"  Expected UUID: {WatchNotifyCharacteristicUuid}");
                    }
                    else
                    {
                        LogError($"  Expected UUIDs: {MetaWearNotificationCharacteristicUuid} or {MetaWearNotificationCharacteristicUuidAlt}");
                    }
                    _isDeviceConnected = false;
                    await _adapter.DisconnectDeviceAsync(_device);
                    return false;
                }
                LogDebug("Enabling notifications...");
                _notificationCharacteristic.ValueUpdated += OnNotificationReceived;
                await _notificationCharacteristic.StartUpdatesAsync();
                LogDebug("✓ Notifications enabled");

                _isDeviceConnected = true;
                LogDebug($"=== Successfully connected to {(_isWatchDevice ? "Watch" : "MetaWear")} device ===");
                return true;
            }
            catch (Exception ex)
            {
                LogError($"✗ EXCEPTION in ConnectToDeviceAsync: {ex.Message}");
                LogError($"Stack trace: {ex.StackTrace}");
                _isDeviceConnected = false;
                try
                {
                    if (_device != null)
                    {
                        await _adapter.DisconnectDeviceAsync(_device);
                    }
                }
                catch { }
                
                return false;
            }
        }
        public async Task DisconnectAsync()
        {
            try
            {
                LogDebug("Disconnecting device...");
                
                // Stop notifications first
                if (_notificationCharacteristic != null)
                {
                    _notificationCharacteristic.ValueUpdated -= OnNotificationReceived;
                    try
                    {
                        await _notificationCharacteristic.StopUpdatesAsync();
                        LogDebug("Notifications stopped");
                    }
                    catch (Exception ex)
                    {
                        LogError($"Error stopping notifications: {ex.Message}");
                    }
                }

                // Disconnect device
                if (_device != null && _isDeviceConnected)
                {
                    try
                    {
                        await _adapter.DisconnectDeviceAsync(_device);
                        LogDebug("Device disconnected");
                    }
                    catch (Exception ex)
                    {
                        LogError($"Error disconnecting device: {ex.Message}");
                    }
                }

                // Clear all references
                _isDeviceConnected = false;
                _device = null;
                _gattService = null;
                _commandCharacteristic = null;
                _notificationCharacteristic = null;
                _cachedDeviceInfo = null;
                _isWatchDevice = false;
            }
            catch (Exception ex)
            {
                LogError($"Error in DisconnectAsync: {ex.Message}");
            }
        }

        private void OnNotificationReceived(object? sender, Plugin.BLE.Abstractions.EventArgs.CharacteristicUpdatedEventArgs e)
        {
            byte[]? data = e.Characteristic.Value;
            if (data == null || data.Length == 0)
                return;

            if (_isWatchDevice)
            {
                // Handle Watch JSON data
                try
                {
                    string jsonString = System.Text.Encoding.UTF8.GetString(data);
                    LogDebug($"Received from Watch: {jsonString}");
                    WatchJsonReceived?.Invoke(this, jsonString);
                }
                catch (Exception ex)
                {
                    LogError($"Error parsing Watch notification: {ex.Message}");
                }
            }
            else
            {
                // Handle MetaWear data
                if (data.Length < 2)
                    return;
                    
                byte moduleId = data[0];
                byte registerId = data[1];
                LogDebug($"MetaWear notification: Module={moduleId:X2}, Register={registerId:X2}");
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
                    _cachedDeviceInfo = new DeviceInfo 
                    { 
                        Manufacturer = _isWatchDevice ? "RevMetrix" : "MbientLab",
                        Model = _isWatchDevice ? "Watch" : "MetaWear",
                        FirmwareVersion = "Unknown",
                        HardwareVersion = "Unknown",
                        SerialNumber = _device.Id.ToString()
                    };
                    return _cachedDeviceInfo;
                }

                var characteristics = await deviceInfoService.GetCharacteristicsAsync();
                
                var modelNumber = await ReadStringCharacteristic(characteristics, ModelNumberCharacteristicUuid);
                var serialNumber = await ReadStringCharacteristic(characteristics, SerialNumberCharacteristicUuid);
                var firmwareVersion = await ReadStringCharacteristic(characteristics, FirmwareRevisionCharacteristicUuid);
                var hardwareVersion = await ReadStringCharacteristic(characteristics, HardwareRevisionCharacteristicUuid);
                var manufacturer = await ReadStringCharacteristic(characteristics, ManufacturerNameCharacteristicUuid);

                _cachedDeviceInfo = new DeviceInfo
                {
                    Model = modelNumber ?? (_isWatchDevice ? "Watch" : "MetaWear"),
                    SerialNumber = serialNumber ?? _device.Id.ToString(),
                    FirmwareVersion = firmwareVersion ?? "Unknown",
                    HardwareVersion = hardwareVersion ?? "Unknown",
                    Manufacturer = manufacturer ?? (_isWatchDevice ? "RevMetrix" : "MbientLab")
                };

                return _cachedDeviceInfo;
            }
            catch (Exception ex)
            {
                LogError($"Error reading device info: {ex.Message}");
                _cachedDeviceInfo = new DeviceInfo 
                { 
                    Manufacturer = _isWatchDevice ? "RevMetrix" : "MbientLab",
                    Model = _isWatchDevice ? "Watch" : "MetaWear",
                    FirmwareVersion = "Unknown",
                    SerialNumber = _device.Id.ToString()
                };
                return _cachedDeviceInfo;
            }
        }

        private async Task<string?> ReadStringCharacteristic(IEnumerable<ICharacteristic> characteristics, Guid uuid)
        {
            try
            {
                var characteristic = characteristics.FirstOrDefault(c => c.Id == uuid);
                if (characteristic == null || !characteristic.CanRead)
                    return null;

                var (data, resultCode) = await characteristic.ReadAsync();
                
                if (resultCode != 0 || data == null || data.Length == 0)
                    return null;

                var result = System.Text.Encoding.UTF8.GetString(data);
                return result.TrimEnd('\0');
            }
            catch (Exception ex)
            {
                LogError($"Error reading characteristic {uuid}: {ex.Message}");
                return null;
            }
        }
        public async Task<bool> SendJsonToWatch(object json)
        {
            try
            {
                if (_commandCharacteristic == null || !IsConnected)
                {
                    LogError("Cannot send JSON: No command characteristic or not connected");
                    return false;
                }

                // Serialize using System.Text.Json
                string payload = JsonSerializer.Serialize(json);

                byte[] bytes = System.Text.Encoding.UTF8.GetBytes(payload);

                LogDebug($"Sending to watch: {payload}");

                await _commandCharacteristic.WriteAsync(bytes);

                return true;
            }
            catch (Exception ex)
            {
                LogError($"SendJsonToWatch error: {ex.Message}");
                return false;
            }
        }
        public async Task ResetAsync()
        {
            if (_commandCharacteristic == null || !IsConnected)
                throw new InvalidOperationException("Device not connected");

            try
            {
                if (_isWatchDevice)
                {
                    LogDebug("Reset not implemented for Watch device");
                }
                else
                {
                    // MetaWear reset command
                    byte[] resetCommand = new byte[] { 0x0F, 0x0A };
                    await _commandCharacteristic.WriteAsync(resetCommand);
                }
            }
            catch (Exception ex)
            {
                LogError($"Error resetting device: {ex.Message}");
                throw;
            }
        }

    }
}