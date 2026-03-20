using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.IO;
using Plugin.BLE;
using Plugin.BLE.Abstractions.Contracts;
using Plugin.BLE.Abstractions.EventArgs;
using Plugin.BLE.Abstractions;
using System.Diagnostics;
using Cellular.Data;
using Cellular.ViewModel;
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
        public event EventHandler? WatchStartRecordingRequested;
        public event EventHandler? WatchStopRecordingRequested;

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
                    string cmd = cmdProp.GetString() ?? "";

                    if (cmd == "startRec")
                    {
                        Debug.WriteLine("PHONE BLE → Watch requested start recording");
                        WatchStartRecordingRequested?.Invoke(this, EventArgs.Empty);
                    }
                    else if (cmd == "stopRec")
                    {
                        Debug.WriteLine("PHONE BLE → Watch requested stop recording");
                        WatchStopRecordingRequested?.Invoke(this, EventArgs.Empty);
                    }
                }

                // Keep original callback for debugging or logging
                WatchJsonReceived?.Invoke(this, jsonStr);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"OnWatchNotification error: {ex.Message}");
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

        private async Task<byte[]> BuildUserDataPacket(int userId, SessionRepository? sessionRepo, BallRepository? ballRepo, EventRepository? eventRepo, GameRepository? gameRepo, User? user)
        {
            using var ms = new System.IO.MemoryStream();
            using var writer = new System.IO.BinaryWriter(ms);

            // Header
            byte packetType = 0x01; // User Data packet
            byte version = 0x01;    // V1 (bit 7 = 0)
            writer.Write(packetType);
            writer.Write(version);

            // Placeholder for length (will be calculated and filled in later)
            long lengthPosition = ms.Position;
            writer.Write((byte)0);

            // Session Data - query database for most recent session
            uint sessionId = 0;
            string eventName = "";
            bool sessionActive = false;
            uint frameNumber = 0;
            ushort gameNumber = 0;
            ushort shotNumber = 0;

            if (sessionRepo != null)
            {
                try
                {
                    var sessions = await sessionRepo.GetSessionsByUserIdAsync(userId);
                    if (sessions != null && sessions.Count > 0)
                    {
                        var mostRecent = sessions.OrderByDescending(s => s.SessionId).FirstOrDefault();
                        if (mostRecent != null)
                        {
                            sessionId = (uint)mostRecent.SessionId;

                            // Get the actual event name from EventRepository
                            if (eventRepo != null)
                            {
                                var eventData = await eventRepo.GetEventByIdAsync(mostRecent.EventId);
                                eventName = eventData?.Name ?? "";
                            }

                            // Check if session has games (active session)
                            if (gameRepo != null)
                            {
                                try
                                {
                                    var games = await gameRepo.GetGamesListBySessionAsync((int)sessionId, userId);
                                    if (games != null && games.Count > 0)
                                    {
                                        sessionActive = true;
                                        // Get the most recent game
                                        var mostRecentGame = games.OrderByDescending(g => g.GameId).FirstOrDefault();
                                        if (mostRecentGame != null)
                                        {
                                            gameNumber = (ushort)(mostRecentGame.GameNumber ?? 0);
                                            frameNumber = 0; // Default to frame 0 for now
                                            shotNumber = 0;  // Default to shot 0 for now
                                            Debug.WriteLine($"BuildUserDataPacket: Found active game {gameNumber}");
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Debug.WriteLine($"BuildUserDataPacket: Error querying games: {ex.Message}");
                                }
                            }

                            Debug.WriteLine($"BuildUserDataPacket: Found session {sessionId}, event {eventName}, active={sessionActive}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"BuildUserDataPacket: Error querying sessions: {ex.Message}");
                }
            }

            // Write Session ID
            writer.Write(sessionId);

            // Write Event Name (null-terminated string)
            var eventNameBytes = System.Text.Encoding.UTF8.GetBytes(eventName);
            writer.Write(eventNameBytes);
            writer.Write((byte)0); // null terminator

            // Write session active flag and conditional fields
            byte primaryHand = 0;
            if (user?.Hand?.ToLower() == "right")
                primaryHand = 1;
            else if (user?.Hand?.ToLower() == "left")
                primaryHand = 0;

            writer.Write(primaryHand);

            // Write game/frame/shot data (all 0s if session not active)
            writer.Write(frameNumber);  // 4 bytes
            writer.Write(gameNumber);   // 2 bytes
            writer.Write(shotNumber);   // 2 bytes

            // Ball Data
            List<Ball> balls = new List<Ball>();
            if (ballRepo != null)
            {
                try
                {
                    balls = await ballRepo.GetBallsByUserIdAsync(userId);
                    if (balls == null) balls = new List<Ball>();
                    Debug.WriteLine($"BuildUserDataPacket: Found {balls.Count} balls");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"BuildUserDataPacket: Error querying balls: {ex.Message}");
                }
            }

            byte ballCount = (byte)Math.Min(balls.Count, 255);
            writer.Write(ballCount);

            foreach (var ball in balls.Take(ballCount))
            {
                writer.Write((uint)ball.BallId);
                var ballNameBytes = System.Text.Encoding.UTF8.GetBytes(ball.Name ?? "");
                writer.Write(ballNameBytes);
                writer.Write((byte)0); // null terminator
            }

            // User Data
            var username = user?.UserName ?? "";
            var usernameBytes = System.Text.Encoding.UTF8.GetBytes(username);
            writer.Write(usernameBytes);
            writer.Write((byte)0); // null terminator

            writer.Write((uint)userId);

            // Calculate and write packet length (excluding the header itself)
            long endPosition = ms.Position;
            byte packetLength = (byte)(endPosition - 3); // Length excludes header bytes

            // Seek back to length position and write it
            ms.Seek(lengthPosition, System.IO.SeekOrigin.Begin);
            writer.Write(packetLength);

            byte[] packet = ms.ToArray();
            Debug.WriteLine($"BuildUserDataPacket: Built packet of {packet.Length} bytes, sessionId={sessionId}, gameNumber={gameNumber}, frameNumber={frameNumber}, shotNumber={shotNumber}");

            return packet;
        }

        public async Task<bool> SendJsonToWatch(int userId, SessionRepository? sessionRepo, BallRepository? ballRepo, EventRepository? eventRepo, GameRepository? gameRepo, User? user)
        {
            if (!IsConnected || _commandChar == null)
            {
                Debug.WriteLine("SendJsonToWatch: Not connected or no characteristic");
                return false;
            }

            byte[] bytes = await BuildUserDataPacket(userId, sessionRepo, ballRepo, eventRepo, gameRepo, user);

            Debug.WriteLine($"SendJsonToWatch: Payload size = {bytes.Length} bytes");

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
