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

        // Repository references for database operations
        private GameRepository? _gameRepo;
        private FrameRepository? _frameRepo;
        private ShotRepository? _shotRepo;

        // Sync context - stores repositories and user info needed to respond to sync commands
        private SessionRepository? _syncSessionRepo;
        private BallRepository? _syncBallRepo;
        private EventRepository? _syncEventRepo;
        private User? _syncUser;
        private int _syncUserId;

        // Cache for mapping watch anonymous session IDs to actual database session IDs
        private Dictionary<int, int> _anonymousSessionIdMapping = new();

        private IDevice? _device;
        private ICharacteristic? _commandChar;
        private ICharacteristic? _notifyChar;

        public bool IsConnected { get; private set; }
        public string MacAddress => _device?.Id.ToString() ?? "";

        public event EventHandler<string>? WatchDisconnected;
        public event EventHandler<string>? WatchJsonReceived;
        public event EventHandler? WatchStartRecordingRequested;
        public event EventHandler? WatchStopRecordingRequested;
        public event EventHandler<Shot>? WatchShotReceived;

        public WatchBleService()
        {
            _adapter.DeviceDisconnected += OnDisconnected;
        }

        /// <summary>
        /// Set the repositories needed for database operations when shot packets arrive.
        /// Call this after initializing the database and repositories.
        /// </summary>
        public void SetRepositories(GameRepository gameRepo, FrameRepository frameRepo, ShotRepository shotRepo, 
            SessionRepository? sessionRepo = null, BallRepository? ballRepo = null, EventRepository? eventRepo = null,
            User? user = null, int userId = 0)
        {
            _gameRepo = gameRepo;
            _frameRepo = frameRepo;
            _shotRepo = shotRepo;

            // Store sync context repositories
            _syncSessionRepo = sessionRepo;
            _syncBallRepo = ballRepo;
            _syncEventRepo = eventRepo;
            _syncUser = user;
            _syncUserId = userId;

            Debug.WriteLine("WatchBleService: Repositories initialized for shot processing and sync commands");
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
                            else if (cmd == "sync")
                            {
                                Debug.WriteLine("PHONE BLE → Watch requested sync");
                                _ = HandleSyncCommand();
                            }
                            else if (cmd == "disconn")
                            {
                                Debug.WriteLine("PHONE BLE → Watch requested disconnect");
                                _ = DisconnectAsync();
                            }
                            else if (cmd == "nextSession")
                            {
                                Debug.WriteLine("PHONE BLE → Watch requested next session");
                                if (root.TryGetProperty("sessionId", out var sessionIdProp))
                                {
                                    int completedSessionId = sessionIdProp.GetInt32();
                                    _ = HandleNextSessionCommand(completedSessionId);
                                }
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

        private async void ParseShotData(byte[] data, int offset)
        {
            try
            {
                // Payload should be 16 bytes (after 4-byte header: type + version + length)
                if (data.Length < offset + 16)
                {
                    Debug.WriteLine($"ParseShotData: Not enough data for shot packet. Expected at least {offset + 16} bytes, got {data.Length}");
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

                // Save shot data to database
                await SaveShotToDatabase(sessionId, gameNumber, frameNumber, shotNumber, ballId, 
                    pinsStanding, foul, stance, target, breakPoint, impact, ballSpeedMph, laneNumber);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ParseShotData error: {ex.Message}");
            }
        }

        private async Task SaveShotToDatabase(uint sessionId, int gameNumber, int frameNumber, int shotNumber, int ballId,
            int pinsStanding, int foul, double stance, double target, double breakPoint, double impact, double ballSpeedMph, int laneNumber)
        {
            try
            {
                // Check if repositories are available
                if (_gameRepo == null || _frameRepo == null || _shotRepo == null)
                {
                    Debug.WriteLine("SaveShotToDatabase: Repositories not initialized. Call SetRepositories() first.");
                    return;
                }

                Debug.WriteLine($"SaveShotToDatabase: Starting save for Session {sessionId}, Game {gameNumber}, Frame {frameNumber}, Shot {shotNumber}");

                // Check if this is an anonymous session (ID > 100,000)
                if (sessionId > 100000)
                {
                    // Get a unique anonymous session ID (may be different if collision detected)
                    sessionId = (uint)await GetUniqueAnonymousSessionIdAsync((int)sessionId);
                }

                // Step 1: Find or create the Game
                var game = await _gameRepo.GetOrCreateGame((int)sessionId, gameNumber);
                Debug.WriteLine($"SaveShotToDatabase: Game retrieved/created - GameId {game.GameId}, GameNumber {gameNumber}");


                // Step 2: Find or create the Frame
                var frames = await _frameRepo.GetFrameIdsByGameIdAsync(game.GameId);
                BowlingFrame? frame = null;

                foreach (var frameId in frames)
                {
                    var f = await _frameRepo.GetFrameById(frameId);
                    if (f != null && f.FrameNumber == frameNumber)
                    {
                        frame = f;
                        break;
                    }
                }

                // If frame doesn't exist, create it
                if (frame == null)
                {
                    frame = new BowlingFrame
                    {
                        GameId = game.GameId,
                        FrameNumber = frameNumber,
                        Lane = laneNumber
                    };
                    int newFrameId = await _frameRepo.AddFrame(frame);
                    frame.FrameId = newFrameId;
                    Debug.WriteLine($"SaveShotToDatabase: Created new frame - FrameId {newFrameId}, Frame {frameNumber}");
                }
                else
                {
                    Debug.WriteLine($"SaveShotToDatabase: Found existing frame - FrameId {frame.FrameId}, Frame {frameNumber}");
                }

                // Step 3: Create the Shot
                // For bowling, we need to calculate pins knocked down:
                // - Shot 1: Simple case - pins down = 10 - standing pins
                // - Shot 2: Complex case - pins down = what was standing before - what's standing now

                int pinsDown = 0;
                int standingCount = Enumerable.Range(0, 10).Count(i => (pinsStanding & (1 << i)) != 0);

                if (shotNumber == 1)
                {
                    // Shot 1 is straightforward
                    pinsDown = 10 - standingCount;
                    Debug.WriteLine($"SaveShotToDatabase: Shot 1 - pinsStanding=0x{pinsStanding:X3}, standing pins={standingCount}, pinsDown={pinsDown}");
                }
                else if (shotNumber == 2)
                {
                    // For shot 2, we need the state from shot 1 to calculate properly
                    // pins knocked in shot 2 = (pins standing before shot 2) - (pins standing after shot 2)
                    Shot? shot1 = null;
                    if (frame.Shot1.HasValue)
                    {
                        shot1 = await _shotRepo.GetShotById(frame.Shot1.Value);
                    }

                    if (shot1 != null && shot1.LeaveType.HasValue)
                    {
                        // Extract pins standing from shot1's LeaveType (bits 0-9)
                        int pinsStandingAfterShot1 = shot1.LeaveType.Value & 0x3FF;
                        int pinsStandingAfterShot1Count = Enumerable.Range(0, 10).Count(i => (pinsStandingAfterShot1 & (1 << i)) != 0);

                        // Pins knocked in shot 2 = what was standing - what remains
                        pinsDown = pinsStandingAfterShot1Count - standingCount;
                        Debug.WriteLine($"SaveShotToDatabase: Shot 2 - shot1.LeaveType=0x{pinsStandingAfterShot1:X3}, standing after shot1={pinsStandingAfterShot1Count}, standing now={standingCount}, pinsDown={pinsDown}");
                    }
                    else
                    {
                        // Fallback: treat as shot 1 (shouldn't happen in normal play)
                        pinsDown = 10 - standingCount;
                        Debug.WriteLine($"SaveShotToDatabase: Shot 2 - No shot 1 found, using fallback calculation, pinsDown={pinsDown}");
                    }
                }

                var shot = new Shot
                {
                    ShotNumber = shotNumber,
                    Ball = ballId,
                    LeaveType = (short)(pinsStanding | (foul << 10)),
                    Stance = (int)stance,
                    Speed = ballSpeedMph.ToString("F1"),
                    Frame = frame.FrameId,
                    // Position, Side, Comment left as null (can be added later if needed)
                    Count = pinsDown
                };

                int shotId = await _shotRepo.AddAsync(shot);
                if (shotId > 0)
                {
                    shot.ShotId = shotId;
                    Debug.WriteLine($"SaveShotToDatabase: Created shot - ShotId {shotId}, Shot {shotNumber}, Count={pinsDown}");

                    // Step 4: Update Frame to link the Shot
                    if (shotNumber == 1)
                    {
                        frame.Shot1 = shotId;
                    }
                    else if (shotNumber == 2)
                    {
                        frame.Shot2 = shotId;
                    }

                    await _frameRepo.UpdateFrameAsync(frame);
                    Debug.WriteLine($"SaveShotToDatabase: Updated frame to link shot {shotNumber}");

                    // Step 5: Fire event for UI update
                    WatchShotReceived?.Invoke(this, shot);
                    Debug.WriteLine($"SaveShotToDatabase: Shot saved successfully and UI event fired");
                }
                else
                {
                    Debug.WriteLine($"SaveShotToDatabase: Failed to save shot to database");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SaveShotToDatabase error: {ex.Message}");
                Debug.WriteLine($"SaveShotToDatabase stack trace: {ex.StackTrace}");
            }
        }

        private async Task<int> GetUniqueAnonymousSessionIdAsync(int requestedSessionId)
        {
            try
            {
                // Check if we've already mapped this watch session ID to a database session ID
                if (_anonymousSessionIdMapping.TryGetValue(requestedSessionId, out int mappedSessionId))
                {
                    Debug.WriteLine($"GetUniqueAnonymousSessionIdAsync: Watch session {requestedSessionId} already mapped to database session {mappedSessionId}");
                    return mappedSessionId;
                }

                if (_syncSessionRepo == null)
                {
                    Debug.WriteLine($"GetUniqueAnonymousSessionIdAsync: SessionRepository not available");
                    return requestedSessionId;
                }

                // Get all sessions for the current user
                var userSessions = await _syncSessionRepo.GetSessionsByUserIdAsync(_syncUserId);

                // Check if the requested ID already exists
                var existingSession = userSessions.FirstOrDefault(s => s.SessionId == requestedSessionId);

                if (existingSession == null)
                {
                    // ID is available - create new session with this ID
                    var newSession = new Session
                    {
                        SessionId = requestedSessionId,
                        SessionNumber = userSessions.Count + 1,
                        UserId = _syncUserId,
                        EventId = 0, // Anonymous sessions don't have an event
                        DateTime = DateTime.Now,
                        CloudID = null // Anonymous sessions won't be synced to cloud
                    };

                    await _syncSessionRepo.AddAsync(newSession);
                    Debug.WriteLine($"GetUniqueAnonymousSessionIdAsync: Created anonymous session {requestedSessionId} for user {_syncUserId}");

                    // Store the mapping
                    _anonymousSessionIdMapping[requestedSessionId] = requestedSessionId;
                    return requestedSessionId;
                }

                // ID collision detected - find next available anonymous ID
                Debug.WriteLine($"GetUniqueAnonymousSessionIdAsync: Session ID {requestedSessionId} already exists. Finding alternative ID...");

                int newId = requestedSessionId + 1;
                const int maxAttempts = 1000; // Prevent infinite loop
                int attempts = 0;

                while (attempts < maxAttempts)
                {
                    var checkSession = userSessions.FirstOrDefault(s => s.SessionId == newId);
                    if (checkSession == null)
                    {
                        // Found available ID
                        var newSession = new Session
                        {
                            SessionId = newId,
                            SessionNumber = userSessions.Count + 1,
                            UserId = _syncUserId,
                            EventId = 0,
                            DateTime = DateTime.Now,
                            CloudID = null
                        };

                        await _syncSessionRepo.AddAsync(newSession);
                        Debug.WriteLine($"GetUniqueAnonymousSessionIdAsync: Collision on {requestedSessionId}. Created new anonymous session {newId} for user {_syncUserId}");

                        // Store the mapping so subsequent shots use the correct ID
                        _anonymousSessionIdMapping[requestedSessionId] = newId;
                        return newId;
                    }

                    newId++;
                    attempts++;
                }

                Debug.WriteLine($"GetUniqueAnonymousSessionIdAsync: Could not find available anonymous session ID after {maxAttempts} attempts");
                return requestedSessionId; // Fallback to original (shouldn't happen in practice)
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetUniqueAnonymousSessionIdAsync error: {ex.Message}");
                return requestedSessionId;
            }
        }

        private async Task HandleNextSessionCommand(int completedSessionId)
        {
            try
            {
                if (!IsConnected)
                {
                    Debug.WriteLine("HandleNextSessionCommand: Not connected");
                    return;
                }

                if (_syncSessionRepo == null || _syncBallRepo == null || _syncEventRepo == null)
                {
                    Debug.WriteLine("HandleNextSessionCommand: Sync context not initialized");
                    return;
                }

                Debug.WriteLine($"HandleNextSessionCommand: Looking for next session after {completedSessionId}");

                // Find the next incomplete session after the completed one
                int? nextSessionId = await GetNextIncompleteSessionAsync(completedSessionId);

                if (nextSessionId == null)
                {
                    Debug.WriteLine("HandleNextSessionCommand: No more incomplete sessions available");
                    // Send packet indicating all sessions are complete (empty/minimal packet)
                    await SendJsonToWatch(_syncUserId, _syncSessionRepo, _syncBallRepo, _syncEventRepo,
                        _gameRepo, _syncUser, _frameRepo, _shotRepo, 0);
                    return;
                }

                Debug.WriteLine($"HandleNextSessionCommand: Found next incomplete session {nextSessionId}");

                // Send user data packet with the new session
                await SendJsonToWatch(_syncUserId, _syncSessionRepo, _syncBallRepo, _syncEventRepo,
                    _gameRepo, _syncUser, _frameRepo, _shotRepo, nextSessionId);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"HandleNextSessionCommand error: {ex.Message}");
            }
        }

        private async Task<int?> GetNextIncompleteSessionAsync(int currentSessionId)
        {
            try
            {
                if (_syncSessionRepo == null || _gameRepo == null || _frameRepo == null)
                    return null;

                // Get all sessions for the user, ordered by SessionId
                var allSessions = await _syncSessionRepo.GetSessionsByUserIdAsync(_syncUserId);
                var sessionsAfterCurrent = allSessions.Where(s => s.SessionId > currentSessionId).OrderBy(s => s.SessionId).ToList();

                // Check each session to see if it's incomplete
                foreach (var session in sessionsAfterCurrent)
                {
                    if (await IsSessionCompleteAsync(session.SessionId))
                        continue; // This session is complete, skip it

                    // Found an incomplete session
                    Debug.WriteLine($"GetNextIncompleteSessionAsync: Found incomplete session {session.SessionId}");
                    return session.SessionId;
                }

                Debug.WriteLine($"GetNextIncompleteSessionAsync: No incomplete sessions found after {currentSessionId}");
                return null; // No incomplete sessions found
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetNextIncompleteSessionAsync error: {ex.Message}");
                return null;
            }
        }

        private async Task<bool> IsSessionCompleteAsync(int sessionId)
        {
            try
            {
                if (_gameRepo == null || _frameRepo == null)
                    return false;

                var games = await _gameRepo.GetGamesListBySessionAsync(sessionId, _syncUserId);

                if (!games.Any())
                    return false; // No games = incomplete

                // Check each game to see if all frames are complete
                foreach (var game in games)
                {
                    var frameIds = await _frameRepo.GetFrameIdsByGameIdAsync(game.GameId);

                    if (frameIds.Count != 10)
                        return false; // Doesn't have all 10 frames

                    // Check if each frame has at least Shot1
                    foreach (var frameId in frameIds)
                    {
                        var frame = await _frameRepo.GetFrameById(frameId);
                        if (frame == null || !frame.Shot1.HasValue)
                            return false; // Frame missing Shot1
                    }
                }

                Debug.WriteLine($"IsSessionCompleteAsync: Session {sessionId} is complete");
                return true; // All games have all frames with shots
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"IsSessionCompleteAsync error: {ex.Message}");
                return false;
            }
        }

        private async Task HandleSyncCommand()
        {
            try
            {
                if (!IsConnected)
                {
                    Debug.WriteLine("HandleSyncCommand: Not connected, ignoring sync request");
                    return;
                }

                if (_syncSessionRepo == null || _syncBallRepo == null || _syncEventRepo == null)
                {
                    Debug.WriteLine("HandleSyncCommand: Sync context not initialized, cannot respond to sync");
                    return;
                }

                Debug.WriteLine("HandleSyncCommand: Responding to sync request with fresh user data");

                // Resend the user data packet with current state
                await SendJsonToWatch(_syncUserId, _syncSessionRepo, _syncBallRepo, _syncEventRepo, 
                    _gameRepo, _syncUser, _frameRepo, _shotRepo);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"HandleSyncCommand error: {ex.Message}");
            }
        }

        public async Task DisconnectAsync()
        {
            // Notify watch that phone is disconnecting
            await NotifyWatchDisconnectAsync();

            if (_notifyChar != null)
            {
                _notifyChar.ValueUpdated -= OnWatchNotification;
                try { await _notifyChar.StopUpdatesAsync(); } catch { }
            }

            if (_device != null && IsConnected)
            {
                try { await _adapter.DisconnectDeviceAsync(_device); } catch { }
            }

            // Clear the anonymous session ID mapping on disconnect
            _anonymousSessionIdMapping.Clear();

            IsConnected = false;

            // Notify UI that watch is disconnected
            WatchDisconnected?.Invoke(this, MacAddress);
        }

        private async Task NotifyWatchDisconnectAsync()
        {
            try
            {
                if (!IsConnected || _commandChar == null)
                {
                    Debug.WriteLine("NotifyWatchDisconnectAsync: Not connected or no characteristic, skipping notification");
                    return;
                }

                // Send disconnect command to watch
                string disconnectCmd = "{\"cmd\":\"disconn\"}";
                byte[] bytes = System.Text.Encoding.UTF8.GetBytes(disconnectCmd);

                await _commandChar.WriteAsync(bytes);
                Debug.WriteLine("NotifyWatchDisconnectAsync: Sent disconnect notification to watch");

                // Brief delay to ensure watch receives the message
                await Task.Delay(100);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"NotifyWatchDisconnectAsync error: {ex.Message}");
                // Don't throw - proceed with disconnect regardless
            }
        }

        private async Task<byte[]> BuildUserDataPacket(int userId, SessionRepository? sessionRepo, BallRepository? ballRepo, EventRepository? eventRepo, GameRepository? gameRepo, User? user, FrameRepository? frameRepo, ShotRepository? shotRepo, int? specificSessionId = null)
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
            ushort gameCount = 0; // Number of games in the session
            List<(ushort gameNumber, uint frameNumber, ushort shotNumber, ushort previousShotPins)> gameDataList = new();

            if (sessionRepo != null)
            {
                try
                {
                    var sessions = await sessionRepo.GetSessionsByUserIdAsync(userId);
                    if (sessions != null && sessions.Count > 0)
                    {
                        Session? sessionToUse = null;

                        if (specificSessionId.HasValue && specificSessionId.Value > 0)
                        {
                            // Use the specific session requested
                            sessionToUse = sessions.FirstOrDefault(s => s.SessionId == specificSessionId.Value);
                            Debug.WriteLine($"BuildUserDataPacket: Using specific session {specificSessionId}");
                        }
                        else
                        {
                            // Find the oldest incomplete session
                            var sortedSessions = sessions.OrderBy(s => s.SessionId).ToList();
                            foreach (var session in sortedSessions)
                            {
                                if (!await IsSessionCompleteAsync(session.SessionId))
                                {
                                    sessionToUse = session;
                                    Debug.WriteLine($"BuildUserDataPacket: Using oldest incomplete session {session.SessionId}");
                                    break;
                                }
                            }
                        }

                        if (sessionToUse != null)
                        {
                            sessionId = (uint)sessionToUse.SessionId;

                            // Get the actual event name from EventRepository
                            if (eventRepo != null)
                            {
                                var eventData = await eventRepo.GetEventByIdAsync(sessionToUse.EventId);
                                eventName = eventData?.NickName ?? "";
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
                                        Debug.WriteLine($"BuildUserDataPacket: Found {gameCount} games in session");

                                        // Build data for each game
                                        foreach (var game in games.OrderBy(g => g.GameNumber))
                                        {
                                            ushort gameNum = (ushort)(game.GameNumber ?? 0);
                                            uint frameNum = 0;
                                            ushort shotNum = 0;
                                            ushort prevShotPins = 0x3FF; // Default: all pins standing

                                            // Query for current frame and previous shot data for this game
                                            if (frameRepo != null && shotRepo != null)
                                            {
                                                try
                                                {
                                                    // Get all frames for this game
                                                    var frameIds = await frameRepo.GetFrameIdsByGameIdAsync(game.GameId);
                                                    if (frameIds.Count > 0)
                                                    {
                                                        // Get the last frame to check for previous shot
                                                        var lastFrameId = frameIds.Last();
                                                        var lastFrame = await frameRepo.GetFrameById(lastFrameId);

                                                        if (lastFrame != null)
                                                        {
                                                            // Determine which shot to get (Shot2 if it exists, else Shot1)
                                                            Shot? previousShot = null;

                                                            if (lastFrame.Shot2.HasValue)
                                                            {
                                                                // Frame has 2 shots - frame is complete
                                                                previousShot = await shotRepo.GetShotById(lastFrame.Shot2.Value);
                                                                frameNum = (uint)(frameIds.Count + 1); // Next frame to play
                                                                shotNum = 1; // Start with shot 1 of next frame
                                                            }
                                                            else if (lastFrame.Shot1.HasValue)
                                                            {
                                                                // Frame has only 1 shot - frame is incomplete
                                                                previousShot = await shotRepo.GetShotById(lastFrame.Shot1.Value);
                                                                frameNum = (uint)(frameIds.Count); // Stay in current frame
                                                                shotNum = 2; // Next shot is shot 2 of current frame
                                                            }

                                                            // Extract previous shot pin data
                                                            if (previousShot != null && previousShot.LeaveType.HasValue)
                                                            {
                                                                // LeaveType contains the pin state: bit=1 => standing, bit=0 => down
                                                                // Bits 0-9 are the 10 pins, bit 10 is foul
                                                                prevShotPins = (ushort)previousShot.LeaveType.Value;
                                                            }
                                                        }
                                                    }
                                                }
                                                catch (Exception ex)
                                                {
                                                    Debug.WriteLine($"BuildUserDataPacket: Error querying frame/shot data for game {gameNum}: {ex.Message}");
                                                }
                                            }

                                            gameDataList.Add((gameNum, frameNum, shotNum, prevShotPins));
                                            Debug.WriteLine($"BuildUserDataPacket: Game {gameNum} - Frame {frameNum}, Shot {shotNum}, PrevPins 0x{prevShotPins:X}");
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

            // Write game count (number of games in this session)
            writer.Write(gameCount);    // 2 bytes

            // Write game data for each game (similar to ball data structure)
            foreach (var (gameNum, frameNum, shotNum, prevShotPins) in gameDataList)
            {
                writer.Write(frameNum);      // 4 bytes - current frame number for this game
                writer.Write(gameNum);       // 2 bytes - game number
                writer.Write(shotNum);       // 2 bytes - next shot number for this game
                writer.Write(prevShotPins);  // 2 bytes - previous shot pin state (bits 0-9 = pins, bit 10 = foul)
            }

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
            Debug.WriteLine($"BuildUserDataPacket: Built packet of {packet.Length} bytes, sessionId={sessionId}, gameCount={gameCount}");

            return packet;
        }

        public async Task<bool> SendJsonToWatch(int userId, SessionRepository? sessionRepo, BallRepository? ballRepo, EventRepository? eventRepo, GameRepository? gameRepo, User? user, FrameRepository? frameRepo, ShotRepository? shotRepo, int? specificSessionId = null)
        {
            if (!IsConnected || _commandChar == null)
            {
                Debug.WriteLine("SendJsonToWatch: Not connected or no characteristic");
                return false;
            }

            byte[] bytes = await BuildUserDataPacket(userId, sessionRepo, ballRepo, eventRepo, gameRepo, user, frameRepo, shotRepo, specificSessionId);

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
