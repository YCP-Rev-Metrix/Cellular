using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Plugin.BLE;
using Plugin.BLE.Abstractions.Contracts;
using Plugin.BLE.Abstractions.EventArgs;
using Plugin.BLE.Abstractions;
using System.Diagnostics;
namespace Cellular.Services
{
    public class WatchBleService : IWatchBleService
    {
        private static readonly Guid WatchServiceUuid = Guid.Parse("a3c94f10-7b47-4c8e-b88f-0e4b2f7c2a91");
        private static readonly Guid WatchCommandCharacteristicUuid = Guid.Parse("a3c94f11-7b47-4c8e-b88f-0e4b2f7c2a91");
        private static readonly Guid WatchNotifyCharacteristicUuid = Guid.Parse("a3c94f12-7b47-4c8e-b88f-0e4b2f7c2a91");

        private readonly IBluetoothLE _ble = CrossBluetoothLE.Current;
        private readonly IAdapter _adapter = CrossBluetoothLE.Current.Adapter;

        private IDevice? _device;
        private ICharacteristic? _commandChar;
        private ICharacteristic? _notifyChar;

        public bool IsConnected { get; private set; }
        public string MacAddress => _device?.Id.ToString() ?? "";

        public event EventHandler<string>? WatchDisconnected;
        public event EventHandler<string>? WatchJsonReceived;

        public WatchBleService()
        {
            _adapter.DeviceDisconnected += OnDisconnected;
        }

        private void OnDisconnected(object? sender, DeviceEventArgs e)
        {
            if (_device != null && e.Device.Id == _device.Id)
            {
                IsConnected = false;
                WatchDisconnected?.Invoke(this, MacAddress);
            }
        }

        public async Task<bool> ConnectAsync(object deviceObj)
        {
            if (deviceObj is not IDevice device)
                return false;

            _device = device;

            try
            {
                // Stop scanning if needed
                if (_adapter.IsScanning)
                {
                    try { await _adapter.StopScanningForDevicesAsync(); } catch { }
                }

                // Use same parameters your MetaWear service uses
                var parameters = new ConnectParameters(
                    autoConnect: false,
                    forceBleTransport: true
                );

                // Connect
                await _adapter.ConnectToDeviceAsync(device, parameters);
                

                // Allow Android to stabilize GATT table
                await Task.Delay(1500);

                // Get services
                var services = await device.GetServicesAsync();
                var watchService = services.FirstOrDefault(s => s.Id == WatchServiceUuid);

                if (watchService == null)
                {
                    await _adapter.DisconnectDeviceAsync(device);
                    return false;
                }

                // Get characteristics
                var chars = await watchService.GetCharacteristicsAsync();
                _commandChar = chars.FirstOrDefault(c => c.Id == WatchCommandCharacteristicUuid);
                _notifyChar  = chars.FirstOrDefault(c => c.Id == WatchNotifyCharacteristicUuid);

                if (_commandChar == null || _notifyChar == null)
                {
                    await _adapter.DisconnectDeviceAsync(device);
                    return false;
                }

                // Enable notifications
                _notifyChar.ValueUpdated += OnWatchNotification;
                await _notifyChar.StartUpdatesAsync();

                IsConnected = true;
                return true;
            }
            catch (Exception ex)
            {
                try { if (_device != null) await _adapter.DisconnectDeviceAsync(_device); } catch { }
                return false;
            }
        }

        private void OnWatchNotification(object? sender, CharacteristicUpdatedEventArgs e)
        {
            if (e.Characteristic?.Value == null)
                return;

            try
            {
                string jsonStr = System.Text.Encoding.UTF8.GetString(e.Characteristic.Value);
                Debug.WriteLine($"PHONE BLE RECEIVED → {jsonStr}");
                using var doc = JsonDocument.Parse(jsonStr);
                var root = doc.RootElement;

                // Check for BLE commands from the Watch
                if (root.TryGetProperty("cmd", out var cmdProp))
                {
                    string cmd = cmdProp.GetString();

                    if (cmd == "startRec")
                    {
                        // Notify Video.xaml.cs to start recording
                        //MessagingCenter.Send<object>(this, "WatchStartRecording");
                    }

                    if (cmd == "stopRec")
                    {
                        // Notify Video.xaml.cs to stop recording
                        //MessagingCenter.Send<object>(this, "WatchStopRecording");
                    }
                }

                // Keep original callback for debugging or logging
                WatchJsonReceived?.Invoke(this, jsonStr);
            }
            catch
            {
                // Ignore malformed JSON or errors
            }
        }


        public async Task DisconnectAsync()
        {
            if (_notifyChar != null)
            {
                _notifyChar.ValueUpdated -= OnWatchNotification;
                try { await _notifyChar.StopUpdatesAsync(); } catch { }
            }

            if (_device != null && IsConnected)
            {
                try { await _adapter.DisconnectDeviceAsync(_device); } catch { }
            }

            IsConnected = false;
        }

        public async Task<bool> SendJsonToWatch(object json)
        {
            if (!IsConnected || _commandChar == null)
            {
                Debug.WriteLine("SendJsonToWatch: Not connected or no characteristic");
                return false;
            }

            string payload = JsonSerializer.Serialize(json);
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(payload);

            Debug.WriteLine($"SendJsonToWatch: Payload size = {bytes.Length} bytes");
            Debug.WriteLine($"SendJsonToWatch: Payload = {payload}");

            try
            {
                const int chunkSize = 20;
                int totalChunks = (bytes.Length + chunkSize - 1) / chunkSize;

                Debug.WriteLine($"SendJsonToWatch: Sending in {totalChunks} chunks of {chunkSize} bytes");

                for (int i = 0; i < bytes.Length; i += chunkSize)
                {
                    int length = Math.Min(chunkSize, bytes.Length - i);
                    byte[] chunk = new byte[length];
                    Array.Copy(bytes, i, chunk, 0, length);

                    await _commandChar.WriteAsync(chunk);
                    Debug.WriteLine($"SendJsonToWatch: Sent chunk {(i / chunkSize) + 1}/{totalChunks} ({length} bytes)");

                    if (i + chunkSize < bytes.Length)
                    {
                        await Task.Delay(50);
                    }
                }

                Debug.WriteLine("SendJsonToWatch: All chunks sent successfully");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SendJsonToWatch: Error = {ex.Message}");
                return false;
            }
        }
    }
}
