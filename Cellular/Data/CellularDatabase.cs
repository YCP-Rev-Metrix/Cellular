using SQLite;
using System;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Maui.Storage;
using Cellular.ViewModel;
using Frame = Microsoft.Maui.Controls.Frame;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using CellularCore;

namespace Cellular.Data
{
    public class CellularDatabase
    {
        private readonly SQLiteAsyncConnection _database;

        public CellularDatabase()
        {
            var dbPath = Path.Combine(FileSystem.AppDataDirectory, "appdata.db");
            _database = new SQLiteAsyncConnection(dbPath);
        }

        public async Task InitializeAsync()
        {
            await _database.CreateTableAsync<User>();
            await ImportUsersFromCsvAsync();
            await _database.CreateTableAsync<Ball>();
            await ImportBallsFromCsvAsync(1);
            await _database.CreateTableAsync<Event>();
            await ImportEventsFromCsvAsync(1);
            await _database.CreateTableAsync<Establishment>();
            await ImportEstabishmentsFromCsvAsync(1);
            await _database.CreateTableAsync<Session>();
            await _database.CreateTableAsync<Game>();
            await _database.CreateTableAsync<BowlingFrame>();
            await _database.CreateTableAsync<Shot>();
            await ImportHakesBowlingDataAsync();

            // Local-only device profile table — never synced to cloud
            await _database.CreateTableAsync<SmartDotDevice>();

            await EnsureCloudIdColumnsAsync(_database);
            await BackfillSessionEstablishmentsAsync();
        }

        /// <summary>Adds CloudID to existing installs (SQLite CreateTable does not add new columns).</summary>
        private static async Task EnsureCloudIdColumnsAsync(SQLiteAsyncConnection db)
        {
            await EnsureColumnAsync(db, "ball", "CloudID");
            await EnsureColumnAsync(db, "establishment", "CloudID");
            await EnsureColumnAsync(db, "event", "CloudID");
            await EnsureColumnAsync(db, "session", "CloudID");
            await EnsureColumnAsync(db, "game", "CloudID");
            await EnsureColumnAsync(db, "bowlingFrame", "CloudID");
            await EnsureColumnAsync(db, "shot", "CloudID");
            await EnsureColumnAsync(db, "shot", "PlayerId");
        }

        private static async Task EnsureColumnAsync(SQLiteAsyncConnection db, string table, string column)
        {
            var info = await db.GetTableInfoAsync(table);
            if (info.Any(c => string.Equals(c.Name, column, StringComparison.OrdinalIgnoreCase)))
                return;
            await db.ExecuteAsync($"ALTER TABLE [{table}] ADD COLUMN {column} INTEGER NULL");
        }

        /// <summary>
        /// One-time backfill: for every session whose Establishment is null, resolve it from
        /// Event.Location → Establishment.NickName and update the row.
        /// Safe to run on every launch — skips sessions that are already set.
        /// </summary>
        private async Task BackfillSessionEstablishmentsAsync()
        {
            try
            {
                var sessions = await _database.Table<Session>()
                    .Where(s => s.Establishment == null)
                    .ToListAsync();

                if (sessions.Count == 0) return;

                var allEvents = await _database.Table<Event>().ToListAsync();
                var eventById = allEvents.ToDictionary(e => e.EventId, e => e);

                var allEstabs = await _database.Table<Establishment>().ToListAsync();
                // Build lookup by NickName (Event.Location stores NickName)
                var estabByNickName = allEstabs
                    .Where(e => !string.IsNullOrWhiteSpace(e.NickName))
                    .ToDictionary(e => e.NickName!.Trim(), e => e, StringComparer.OrdinalIgnoreCase);

                int updated = 0;
                foreach (var session in sessions)
                {
                    if (!eventById.TryGetValue(session.EventId, out var ev)) continue;
                    if (string.IsNullOrWhiteSpace(ev.Location)) continue;

                    if (estabByNickName.TryGetValue(ev.Location.Trim(), out var estab))
                    {
                        session.Establishment = estab.EstaID;
                        await _database.UpdateAsync(session);
                        updated++;
                    }
                    else
                    {
                        Debug.WriteLine($"BackfillSession: no match for location '{ev.Location}'");
                    }
                }

                if (updated > 0)
                    Debug.WriteLine($"BackfillSessionEstablishments: updated {updated} session(s).");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"BackfillSessionEstablishments error: {ex.Message}");
            }
        }

        private async Task ImportEstabishmentsFromCsvAsync(int userId)
        {
            var csvFileName = "establishments3.csv"; // File in Resources/Raw

            try
            {
                using var stream = await FileSystem.OpenAppPackageFileAsync(csvFileName);
                using var reader = new StreamReader(stream);

                var establishments = new List<Establishment>();
                while (!reader.EndOfStream)
                {
                    var line = await reader.ReadLineAsync();
                    var data = line?.Split(',');

                    if (data == null || data.Length < 4) continue; // Ensure valid data

                    var esta = new Establishment
                    {
                        UserId = userId,
                        FullName = data[1].Trim(),
                        NickName = data[2].Trim(),
                        Address = data[3].Trim(),
                        PhoneNumber = data[4].Trim(),
                        Lanes = data[5].Trim(),
                        Type = data[6].Trim(),
                        Enabled = true
                    };
                    var existingUser = await _database.Table<Establishment>().FirstOrDefaultAsync(u => u.NickName == esta.NickName);
                    if (existingUser == null)
                    {
                        establishments.Add(esta);
                    }

                }

                if (establishments.Count > 0)
                {
                    await _database.InsertAllAsync(establishments);
                    Console.WriteLine("Establishments imported successfully.");
                }
                else
                {
                    Console.WriteLine("No new establishments to import.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading CSV: {ex.Message}");
            }
        }
        private async Task ImportEventsFromCsvAsync(int userId)
        {
            var csvFileName = "events3.csv"; // File in Resources/Raw

            try
            {
                using var stream = await FileSystem.OpenAppPackageFileAsync(csvFileName);
                using var reader = new StreamReader(stream);

                var events = new List<Event>();
                while (!reader.EndOfStream)
                {
                    var line = await reader.ReadLineAsync();
                    var data = line?.Split(',');

                    if (data == null || data.Length < 6) continue; // Ensure valid data

                    var event_ = new Event
                    {
                        UserId = userId,
                        LongName = data[1].Trim(),
                        NickName = data[2].Trim(),
                        Type = data[3].Trim(),
                        Location = data[4].Trim(),
                        StartDate = data[10].Trim(),
                        EndDate = data[11].Trim(),
                        WeekDay = data[8].Trim(),
                        StartTime = data[9].Trim(),
                        NumGamesPerSession = int.TryParse(data[12].Trim(), out int numGames) ? numGames : 0,
                    };
                    var existingUser = await _database.Table<Event>().FirstOrDefaultAsync(u => u.LongName == event_.LongName);
                    if (existingUser == null)
                    {
                        events.Add(event_);
                    }
                    
                }

                if (events.Count > 0)
                {
                    await _database.InsertAllAsync(events);
                    Console.WriteLine("Events imported successfully.");
                }
                else
                {
                    Console.WriteLine("No new events to import.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading CSV: {ex.Message}");
            }
        }
        private async Task ImportHakesBowlingDataAsync()
        {
            try
            {
                // Guard: if sessions already exist, assume import already ran and skip.
                // Use CountAsync() to avoid ambiguity with AnyAsync extension overloads.
                if (await _database.Table<Session>().CountAsync() > 0)
                {
                    Console.WriteLine("Hakes bowling data already imported — skipping import.");
                    return;
                }
                var csvFileName = "Fa25-LeagueScores(JoshMods-4-13-26).csv";
                //var csvFileName = "lessLineScores.csv";
                using var stream = await FileSystem.OpenAppPackageFileAsync(csvFileName);
                using var reader = new StreamReader(stream);

                //Skip first 2 lines
                await reader.ReadLineAsync();
                await reader.ReadLineAsync();

                // 1. PRE-LOAD EVENTS (Essential to avoid async calls inside transaction)
                var allEvents = await _database.Table<Event>().ToListAsync();
                var eventMap = allEvents
                    .Where(e => !string.IsNullOrWhiteSpace(e.NickName))
                    .ToDictionary(e => e.NickName!.Trim(), e => e);

                // PRE-LOAD ESTABLISHMENTS keyed by NickName so we can resolve
                // Event.Location → Establishment.EstaID when creating sessions.
                var allEstablishments = await _database.Table<Establishment>().ToListAsync();
                var estabMap = allEstablishments
                    .Where(e => !string.IsNullOrWhiteSpace(e.NickName))
                    .ToDictionary(e => e.NickName!.Trim(), e => e, StringComparer.OrdinalIgnoreCase);

                // PRE-LOAD BALLS so we can map ball name -> BallId when saving shots
                var allBalls = await _database.Table<Ball>().ToListAsync();
                var ballMap = allBalls
                    .Where(b => !string.IsNullOrWhiteSpace(b.Name))
                    .ToDictionary(b => b.Name!.Trim(), b => b, StringComparer.OrdinalIgnoreCase);

                // No pre-skip of lines required — Read7RowBlockAsync will seek the next COUNT row

                // Read entire file into memory as blocks using async reads to avoid cross-thread StreamReader access
                var blocks = new List<BowlingBlock>();
                while (!reader.EndOfStream)
                {
                    var block = await Read7RowBlockAsync(reader);
                    if (block == null) continue;
                    if (block.Count == null || block.Score == null) continue;
                    if (!string.Equals(block.Count[0], "COUNT", StringComparison.OrdinalIgnoreCase)) continue;
                    blocks.Add(block);
                }

                // 2. Process each block in its own transaction so a failing block can be skipped
                string lastWeekIdentifier = string.Empty;
                string lastDateIdentifier = string.Empty;
                int currentSessionId = 0;

                for (int bi = 0; bi < blocks.Count; bi++)
                {
                    var block = blocks[bi];

                    // 1. Grab identifiers from the CSV (do this outside the transaction)
                    string currentWeek = block.Count.Length > 3 ? block.Count[4].Trim() : "";
                    string currentDate = block.Count.Length > 4 ? block.Count[5].Trim() : "";
                    string eventName = (block.Count.Length > 1) ? block.Count[1].Trim() : string.Empty;
                    eventMap.TryGetValue(eventName, out var eventRecord);

                    bool isNewSession = (currentWeek != lastWeekIdentifier || currentDate != lastDateIdentifier);

                    Session? newSession = null;
                    if (isNewSession)
                    {
                        // Resolve the establishment from Event.Location.
                        // EventPopupViewModel stores NickName; CSV imports may store FullName — try both.
                        int? establishmentId = null;
                        if (!string.IsNullOrWhiteSpace(eventRecord?.Location))
                        {
                            var loc = eventRecord.Location.Trim();
                            if (!estabMap.TryGetValue(loc, out var estab))
                            {
                                // Fallback: match by nickname
                                estab = allEstablishments.FirstOrDefault(e =>
                                    string.Equals(e.NickName, loc, StringComparison.OrdinalIgnoreCase));
                            }

                            if (estab != null)
                            {
                                establishmentId = estab.EstaID;
                                Debug.WriteLine($"Import: matched '{loc}' → EstaID {estab.EstaID} ('{estab.NickName}')");
                            }
                            else
                            {
                                Debug.WriteLine($"Import: no establishment match for '{loc}'");
                            }
                        }

                        newSession = new Session
                        {
                            EventId = eventRecord?.EventId ?? 0,
                            SessionNumber = int.TryParse(currentWeek, out var weekNum) ? weekNum : 0,
                            DateTime = DateTime.TryParse(currentDate, out var dt) ? dt : DateTime.Now,
                            UserId = eventRecord?.UserId ?? 0,
                            Establishment = establishmentId
                        };
                    }

                    try
                    {
                        // Run each block inside its own transaction so failures rollback only that block
                        await _database.RunInTransactionAsync(conn =>
                        {
                            if (isNewSession && newSession != null)
                            {
                                conn.Insert(newSession);
                            }

                            var sessionIdForGame = (isNewSession && newSession != null) ? newSession.SessionId : currentSessionId;

                            // 3. CREATE GAME (Always happens for every block)
                            var lanesA = SafeGet(block.Lane, 10);
                            var lanesB = SafeGet(block.Lane, 12);
                            var game = new Game
                            {
                                SessionId = sessionIdForGame,
                                Lanes = string.IsNullOrWhiteSpace(lanesA) && string.IsNullOrWhiteSpace(lanesB) ? string.Empty : ($"{lanesA},{lanesB}"),
                                GameNumber = int.TryParse(SafeGet(block.Count, 6), out var gn) ? gn : 0,
                                StartingLane = int.TryParse(SafeGet(block.Count, 7), out var sl) ? sl : 0,
                                Score = int.TryParse(SafeGet(block.Score, 28), out var s) ? s : 0
                            };
                            conn.Insert(game);

                            // 4. PROCESS FRAMES & SHOTS
                            int[] shot1Indices = { 10, 12, 14, 16, 18, 20, 22, 24, 26, 28, 30, 32 };

                            for (int i = 0; i < shot1Indices.Length; i++)
                            {
                                int s1Col = shot1Indices[i];
                                int s2Col = s1Col + 1;
                                int frameNumber = i + 1;

                                var s1Raw = SafeGet(block.Count, s1Col);
                                if (string.IsNullOrWhiteSpace(s1Raw)) break;

                                var ballName1 = SafeGet(block.Ball, s1Col);
                                var shot1 = new Shot
                                {
                                    ShotNumber = 1,
                                    LeaveType = ParseLeaveToShort(SafeGet(block.Leave, s1Col), 1),
                                    Count = ParsePinCount(s1Raw),
                                    Position = SafeGet(block.Board, s1Col),
                                    Ball = (!string.IsNullOrWhiteSpace(ballName1) && ballMap.TryGetValue(ballName1.Trim(), out var b1)) ? b1.BallId : (int?)null,
                                };
                                conn.Insert(shot1);

                                int shot2Id = -1;
                                var s2Raw = SafeGet(block.Count, s2Col);
                                if (!string.IsNullOrWhiteSpace(s2Raw))
                                {
                                    int s2Count;
                                    if (s2Raw == "/")
                                    {
                                        int s1Count = ParsePinCount(s1Raw);
                                        s2Count = 10 - s1Count;
                                    }
                                    else
                                    {
                                        s2Count = ParsePinCount(s2Raw);
                                    }

                                    var ballName2 = SafeGet(block.Ball, s2Col);
                                    var shot2 = new Shot
                                    {
                                        ShotNumber = 2,
                                        LeaveType = ParseLeaveToShort(SafeGet(block.Leave, s2Col), 2),
                                        Count = s2Count,
                                        Position = SafeGet(block.Board, s2Col),
                                        Ball = (!string.IsNullOrWhiteSpace(ballName2) && ballMap.TryGetValue(ballName2.Trim(), out var b2)) ? b2.BallId : (int?)null,
                                    };
                                    conn.Insert(shot2);
                                    shot2Id = shot2.ShotId;
                                }
                                string frameResult = null;
                                if (shot1.Count == 10)
                                {
                                    frameResult = "Strike";
                                }
                                else if (shot2Id != -1)
                                {
                                    if (s2Raw == "/")
                                    {
                                        frameResult = "Spare";
                                    }
                                    else
                                    {
                                        frameResult = "Open";
                                    }
                                }
                                else
                                {
                                    frameResult = "Open";
                                }

                                var frame = new BowlingFrame
                                {
                                    GameId = game.GameId,
                                    FrameNumber = frameNumber,
                                    Shot1 = shot1.ShotId,
                                    Shot2 = shot2Id,
                                    Result = frameResult,
                                    Lane = int.TryParse(SafeGet(block.Lane, s1Col), out var l) ? (l > 0 ? l : 0) : 0
                                };
                                conn.Insert(frame);
                            }
                        });

                        // If we get here the block transaction succeeded — update session trackers
                        if (isNewSession && newSession != null)
                        {
                            currentSessionId = newSession.SessionId;
                            lastWeekIdentifier = currentWeek;
                            lastDateIdentifier = currentDate;
                            Debug.WriteLine($"Created New Session: Week {currentWeek} on {currentDate}");
                        }
                    }
                    catch (Exception ex)
                    {
                        // Log and continue with next block
                        Console.WriteLine($"Import: skipping block #{bi} (Event='{eventName}', Week='{currentWeek}', Date='{currentDate}'): {ex.Message}");
                    }
                }

                Console.WriteLine("Import Success!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Import Error: {ex.Message}");
            }
        }
        private async Task ImportBallsFromCsvAsync(int userId)
        {
            var csvFileName = "balls3.csv"; // File in Resources/Raw

            try
            {
                using var stream = await FileSystem.OpenAppPackageFileAsync(csvFileName);
                using var reader = new StreamReader(stream);

                var balls = new List<Ball>();
                while (!reader.EndOfStream)
                {
                    var line = await reader.ReadLineAsync();
                    var data = line?.Split(',');

                    if (data == null || data.Length < 4) continue; // Ensure valid data

                    var ball = new Ball
                    {
                        UserId = userId,
                        Name = data[2].Trim(),
                        BallMFG = data[3].Trim(),
                        BallMFGName = data[1].Trim(),
                        SerialNumber = data[10].Trim(),
                        Weight = int.TryParse(data[11].Trim(), out int weight) ? weight : 0,
                        Core = data[5].Trim(),
                        Coverstock = data[6].Trim(),
                        ColorString = data[7].Trim()
                    };

                    var existingUser = await _database.Table<Ball>().FirstOrDefaultAsync(u => u.Name == ball.Name);
                    if (existingUser == null)
                    {
                        balls.Add(ball);
                    }
                }

                if (balls.Count > 0)
                {
                    await _database.InsertAllAsync(balls);
                    Console.WriteLine("Balls imported successfully.");
                }
                else
                {
                    Console.WriteLine("No new balls to import.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading CSV: {ex.Message}");
            }
        }
    
        private async Task ImportUsersFromCsvAsync()
        {
            var csvFileName = "users.csv"; // File in Resources/Raw

            try
            {
                using var stream = await FileSystem.OpenAppPackageFileAsync(csvFileName);
                using var reader = new StreamReader(stream);

                var users = new List<User>();
                while (!reader.EndOfStream)
                {
                    var line = await reader.ReadLineAsync();
                    var data = line?.Split(',');

                    if (data == null || data.Length < 6) continue; // Ensure valid data

                    var user = new User
                    {
                        UserName = data[0].Trim(),
                        PasswordHash = HashPassword(data[1].Trim()),
                        FirstName = data[2].Trim(),
                        LastName = data[3].Trim(),
                        Email = data[4].Trim(),
                        LastLogin = DateTime.TryParse(data[5].Trim(), out DateTime lastLogin) ? lastLogin : DateTime.Now,
                        PhoneNumber = data[6].Trim(),
                        Hand = data[7].Trim()
                    };

                    var existingUser = await _database.Table<User>().FirstOrDefaultAsync(u => u.UserName == user.UserName);
                    if (existingUser == null)
                    {
                        users.Add(user);
                    }
                }

                if (users.Count > 0)
                {
                    await _database.InsertAllAsync(users);
                    Console.WriteLine("Users imported successfully.");
                }
                else
                {
                    Console.WriteLine("No new users to import.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading CSV: {ex.Message}");
            }
        }

        
        public static string HashPassword(string password)
        {
            return BCrypt.Net.BCrypt.HashPassword(password);
        }

        public SQLiteAsyncConnection GetConnection() => _database;

        //CSV Helpers
        private async Task<BowlingBlock?> Read7RowBlockAsync(StreamReader reader)
        {
            string? line;
            // 1. Seek the next "COUNT" row
            while ((line = await reader.ReadLineAsync()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (line.StartsWith("COUNT", StringComparison.OrdinalIgnoreCase)) break;
            }

            if (line == null) return null; // Reached EOF

            // 2. Read the next 6 lines safely and ensure arrays are padded to avoid index issues
            const int MinColumns = 32; // accommodate indices up to 31 used by the importer

            // Read the next six lines; if any are missing then treat this as EOF/incomplete block
            string? leaveLine = await reader.ReadLineAsync();
            string? scoreLine = await reader.ReadLineAsync();
            string? typeLine = await reader.ReadLineAsync();
            string? boardLine = await reader.ReadLineAsync();
            string? laneLine = await reader.ReadLineAsync();
            string? ballLine = await reader.ReadLineAsync();

            if (leaveLine == null || scoreLine == null || typeLine == null || boardLine == null || laneLine == null || ballLine == null)
            {
                // Incomplete block at EOF — skip it
                Debug.WriteLine("Read7RowBlockAsync: encountered incomplete block at EOF; skipping remaining lines.");
                return null;
            }

            var count = EnsureLength(line.Split(','), MinColumns);
            var leave = EnsureLength(leaveLine.Split(','), MinColumns);
            var score = EnsureLength(scoreLine.Split(','), MinColumns);
            var type = EnsureLength(typeLine.Split(','), MinColumns);
            var board = EnsureLength(boardLine.Split(','), MinColumns);
            var lane = EnsureLength(laneLine.Split(','), MinColumns);
            var ball = EnsureLength(ballLine.Split(','), MinColumns);

            return new BowlingBlock
            {
                Count = count,
                Leave = leave,
                Score = score,
                Type = type,
                Board = board,
                Lane = lane,
                Ball = ball
            };
        }

        private static string SafeGet(string[] arr, int index)
        {
            if (arr == null) return string.Empty;
            return (index >= 0 && index < arr.Length) ? arr[index] : string.Empty;
        }

        private static string[] EnsureLength(string[] arr, int minLength)
        {
            if (arr == null) return Enumerable.Repeat(string.Empty, minLength).ToArray();
            if (arr.Length >= minLength) return arr;
            var result = new string[minLength];
            for (int i = 0; i < minLength; i++)
            {
                result[i] = i < arr.Length ? arr[i] : string.Empty;
            }
            return result;
        }

        private int ParsePinCount(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return 0;

            // Explicitly handle common Excel/CSV error strings
            if (value.Contains("#DIV") || value.Contains("NaN")) return 0;

            if (value.Equals("X", StringComparison.OrdinalIgnoreCase)) return 10;
            if (value == "/") return 0; // Return 0 here, let the Loop handle the subtraction

            if (int.TryParse(value, out int result))
            {
                return result;
            }
            return 0;
        }

        private short ParseLeaveToShort(string leaveString, int shotNumber)
        {
            // Start with 0 (All pins are DOWN/0)
            int pinMask = 0;

            if (string.IsNullOrWhiteSpace(leaveString)) return (short)pinMask;

            // If it's a strike (X) on shot 1, all pins are down, so we return 0
            if (leaveString.ToUpper() == "X") return 0;

            // 1. Handle "10" first 
            if (leaveString.Contains("10"))
            {
                pinMask |= (1 << 9); // Set bit 9 (Pin 10) to 1 (Standing)
                leaveString = leaveString.Replace("10", "");
            }

            // 2. Handle remaining pins 1-9
            foreach (char c in leaveString)
            {
                if (char.IsDigit(c))
                {
                    int pin = int.Parse(c.ToString());
                    if (pin >= 1 && pin <= 9)
                    {
                        pinMask |= (1 << (pin - 1)); // Set the bit to 1 (Standing)
                    }
                }
            }

            return ShotCalculator.CalculateShotType((short)pinMask, shotNumber);
        }
    }
}


