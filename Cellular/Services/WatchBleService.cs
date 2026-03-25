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

                // Request larger MTU to support packets > 20 bytes
                await RequestLargerMtuAsync(device);

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
                byte[] data = e.Characteristic.Value;

                // Log raw packet for debugging
                string hexString = BitConverter.ToString(data).Replace("-", " ");
                Debug.WriteLine($"PHONE BLE RECEIVED → {hexString}");

                // Check if this is a binary packet or JSON command
                // Binary packets have known type bytes (0x01 = user data, 0x03 = shot)
                if (data.Length > 0 && (data[0] == 0x01 || data[0] == 0x03))
                {
                    // This is a binary packet
                    ParseShotPacket(data);
                }
                else
                {
                    // Try to parse as JSON command
                    try
                    {
                        string jsonStr = System.Text.Encoding.UTF8.GetString(data);
                        Debug.WriteLine($"PHONE BLE RECEIVED (JSON) → {jsonStr}");

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

                        WatchJsonReceived?.Invoke(this, jsonStr);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"JSON parsing error: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"OnWatchNotification error: {ex.Message}");
            }
        }

        private void ParseShotPacket(byte[] data)
        {
            if (data.Length < 3)
            {
                Debug.WriteLine("ParseShotPacket: Packet too short");
                return;
            }

            try
            {
                int offset = 0;

                // Parse Packet Type (1 byte)
                byte packetType = data[offset];
                offset += 1;

                // Parse Version (2 bytes)
                if (data.Length < offset + 2)
                {
                    Debug.WriteLine("ParseShotPacket: Not enough data for version (2 bytes)");
                    return;
                }
                byte versionByte1 = data[offset];
                byte versionByte2 = data[offset + 1];
                offset += 2;

                Debug.WriteLine($"ParseShotPacket: PacketType=0x{packetType:X2}, Version=0x{versionByte1:X2} 0x{versionByte2:X2}");

                // Parse Length (1 byte) - represents total packet size
                byte length = data[offset];
                offset += 1;

                if (data.Length < length)
                {
                    Debug.WriteLine($"ParseShotPacket: Warning - packet may be truncated. Length field says {length} bytes, received {data.Length} bytes");
                }

                // Parse shot packet based on type
                if (packetType == 0x03) // Shot packet
                {
                    ParseShotData(data, offset);
                }
                else
                {
                    Debug.WriteLine($"ParseShotPacket: Unknown packet type 0x{packetType:X2}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ParseShotPacket error: {ex.Message}");
            }
        }

        private void ParseShotData(byte[] data, int offset)
        {
            try
            {
                // Payload should be 19 bytes (after 4-byte header: type + version + length)
                if (data.Length < offset + 19)
                {
                    Debug.WriteLine($"ParseShotData: Not enough data for shot packet. Expected at least {offset + 19} bytes, got {data.Length}");
                    return;
                }

                int shotDataOffset = offset;

                // Parse Session ID (4 bytes, big-endian)
                uint sessionId = (uint)((data[shotDataOffset] << 24) | (data[shotDataOffset + 1] << 16) | (data[shotDataOffset + 2] << 8) | data[shotDataOffset + 3]);
                shotDataOffset += 4;

                // Parse Game/Frame/Shot/Ball ID packed fields (2 bytes, big-endian)
                ushort packedId = (ushort)((data[shotDataOffset] << 8) | data[shotDataOffset + 1]);
                int gameNumber = (packedId >> 11) & 0x1F;          // 5 bits (bits 15-11)
                int frameNumber = (packedId >> 7) & 0x0F;           // 4 bits (bits 10-7)
                int shotNumber = ((packedId >> 6) & 0x01) + 1;      // 1 bit (bit 6), convert 0/1 to 1/2
                int ballId = packedId & 0x3F;                       // 6 bits (bits 5-0)
                shotDataOffset += 2;

                // Parse Pins + Foul (2 bytes, big-endian, 10 bits pins + 1 bit foul)
                ushort pinData = (ushort)((data[shotDataOffset] << 8) | data[shotDataOffset + 1]);
                int pinsStanding = pinData & 0x3FF;                 // 10 bits (bits 9-0) - pins standing
                int foul = (pinData >> 10) & 0x01;                  // 1 bit (bit 10)

                // Convert pin bits to human-readable pin numbers
                List<int> standingPins = new List<int>();
                for (int i = 0; i < 10; i++)
                {
                    if ((pinsStanding & (1 << i)) != 0)
                    {
                        standingPins.Add(i + 1); // Pin numbering starts at 1
                    }
                }
                string standingPinsStr = standingPins.Count > 0 ? string.Join(", ", standingPins) : "None";

                shotDataOffset += 2;

                // Parse Stance (1 byte, divide by 2)
                double stance = data[shotDataOffset] / 2.0;
                shotDataOffset += 1;

                // Parse Target (1 byte, divide by 2)
                double target = data[shotDataOffset] / 2.0;
                shotDataOffset += 1;

                // Parse Break Point (1 byte, divide by 2)
                double breakPoint = data[shotDataOffset] / 2.0;
                shotDataOffset += 1;

                // Parse Impact/Board (1 byte, divide by 2)
                double impact = data[shotDataOffset] / 2.0;
                shotDataOffset += 1;

                // Parse Ball Speed (2 bytes, big-endian, value / 10 = mph)
                ushort ballSpeed = (ushort)((data[shotDataOffset] << 8) | data[shotDataOffset + 1]);
                double ballSpeedMph = ballSpeed / 10.0;
                shotDataOffset += 2;

                // Parse Lane # (1 byte)
                byte laneNumber = data[shotDataOffset];
                shotDataOffset += 1;

                // Parse Game Score After (3 bytes, little-endian)
                uint gameScore = (uint)(data[shotDataOffset] | (data[shotDataOffset + 1] << 8) | (data[shotDataOffset + 2] << 16));
                shotDataOffset += 3;

                // Parse Padding (1 byte)
                byte padding = data[shotDataOffset];

                // Log parsed shot data
                Debug.WriteLine($"ParseShotData: Shot packet parsed successfully:");
                Debug.WriteLine($"  Session ID: {sessionId}");
                Debug.WriteLine($"  Game: {gameNumber}, Frame: {frameNumber}, Shot: {shotNumber}, Ball ID: {ballId}");
                Debug.WriteLine($"  Pins Standing: {standingPinsStr} (0x{pinsStanding:X3}), Foul: {foul}");
                Debug.WriteLine($"  Stance: {stance:F1}, Target: {target:F1}, Break Point: {breakPoint:F1}, Impact: {impact:F1}");
                Debug.WriteLine($"  Ball Speed: {ballSpeedMph} mph");
                Debug.WriteLine($"  Lane: {laneNumber}");
                Debug.WriteLine($"  Game Score: {gameScore}");

                // TODO: Save shot data to database
                // TODO: Update UI with shot information
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ParseShotData error: {ex.Message}");
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

        private async Task<byte[]> BuildUserDataPacket(int userId, SessionRepository? sessionRepo, BallRepository? ballRepo, EventRepository? eventRepo, GameRepository? gameRepo, User? user, FrameRepository? frameRepo, ShotRepository? shotRepo)
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
            ushort previousShotPins = 0x3FF; // Default: all pins standing (1111111111 in binary)
            int currentGameScore = 0;
            ushort gameCount = 0; // Number of games in the session

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
                                        gameCount = (ushort)games.Count; // Store the number of games in this session
                                        // Get the most recent game
                                        var mostRecentGame = games.OrderByDescending(g => g.GameId).FirstOrDefault();
                                        if (mostRecentGame != null)
                                        {
                                            gameNumber = (ushort)(mostRecentGame.GameNumber ?? 0);
                                            currentGameScore = mostRecentGame.Score ?? 0;
                                            frameNumber = 0; // Default to frame 0 for now
                                            shotNumber = 0;  // Default to shot 0 for now
                                            Debug.WriteLine($"BuildUserDataPacket: Found active game {gameNumber} ({gameCount} total games in session)");

                                            // Query for current frame and previous shot data
                                            if (frameRepo != null && shotRepo != null)
                                            {
                                                try
                                                {
                                                    // Get all frames for this game to find the current frame
                                                    var frameIds = await frameRepo.GetFrameIdsByGameIdAsync(mostRecentGame.GameId);
                                                    if (frameIds.Count > 0)
                                                    {
                                                        frameNumber = (uint)(frameIds.Count); // Current frame is next one to play

                                                        // Get the last frame to check for previous shot
                                                        var lastFrameId = frameIds.Last();
                                                        var lastFrame = await frameRepo.GetFrameById(lastFrameId);

                                                        if (lastFrame != null)
                                                        {
                                                            // Determine which shot to get (Shot2 if it exists, else Shot1)
                                                            Shot? previousShot = null;

                                                            if (lastFrame.Shot2.HasValue)
                                                            {
                                                                // Frame has 2 shots, get the second one
                                                                previousShot = await shotRepo.GetShotById(lastFrame.Shot2.Value);
                                                                shotNumber = 1; // Next shot in the current frame would be 1
                                                            }
                                                            else if (lastFrame.Shot1.HasValue)
                                                            {
                                                                // Frame has only 1 shot, get it
                                                                previousShot = await shotRepo.GetShotById(lastFrame.Shot1.Value);
                                                                shotNumber = 2; // Next shot would be 2
                                                            }

                                                            // Extract previous shot pin data
                                                            if (previousShot != null && previousShot.LeaveType.HasValue)
                                                            {
                                                                // LeaveType contains the pin state: bit=1 => standing, bit=0 => down
                                                                // Bits 0-9 are the 10 pins, bit 10 is foul
                                                                previousShotPins = (ushort)previousShot.LeaveType.Value;
                                                                Debug.WriteLine($"BuildUserDataPacket: Previous shot pin state = 0x{previousShotPins:X}");
                                                            }
                                                        }
                                                    }
                                                }
                                                catch (Exception ex)
                                                {
                                                    Debug.WriteLine($"BuildUserDataPacket: Error querying frame/shot data: {ex.Message}");
                                                }
                                            }
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

            // Write game count (number of games in this session)
            writer.Write(gameCount);    // 2 bytes

            // Write previous shot pin data (11 bits: pins + foul)
            writer.Write(previousShotPins);  // 2 bytes (bits 0-9 = pins, bit 10 = foul)

            // Write current game score
            writer.Write(currentGameScore);  // 4 bytes

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

        public async Task<bool> SendJsonToWatch(int userId, SessionRepository? sessionRepo, BallRepository? ballRepo, EventRepository? eventRepo, GameRepository? gameRepo, User? user, FrameRepository? frameRepo, ShotRepository? shotRepo)
        {
            if (!IsConnected || _commandChar == null)
            {
                Debug.WriteLine("SendJsonToWatch: Not connected or no characteristic");
                return false;
            }

            byte[] bytes = await BuildUserDataPacket(userId, sessionRepo, ballRepo, eventRepo, gameRepo, user, frameRepo, shotRepo);

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

        private async Task RequestLargerMtuAsync(IDevice device)
        {
            try
            {
#if __ANDROID__
                // Android-specific MTU negotiation
                var mtuRequested = await device.RequestMtuAsync(512);
                Debug.WriteLine($"MTU negotiated: {mtuRequested} bytes");
#else
                Debug.WriteLine("MTU negotiation not implemented for this platform");
#endif
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to request larger MTU: {ex.Message}");
                // Not critical - will continue with default 20-byte MTU
            }
        }
    }
}
