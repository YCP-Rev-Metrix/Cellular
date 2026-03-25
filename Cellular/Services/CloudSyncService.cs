using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text;
using System.Threading.Tasks;
using Cellular.Cloud_API;
using Cellular.Cloud_API.Models;
using Cellular.Data;
using Cellular.Services;
using Cellular.ViewModel;
using Microsoft.Maui.Storage;
using SQLite;

namespace Cellular.Services;

public enum SyncCheckResult
{
    NoConflict,
    HasConflict,
    Error
}

public class SyncResult
{
    public SyncCheckResult Result { get; set; }
    public string? ErrorMessage { get; set; }
}

public class CloudSyncService
{
    public const string SyncLastCheckedUtcKey = "SyncLastCheckedUtc";
    private const string DefaultCloudUsername = "Guest";
    private const string DefaultCloudPassword = "Guest";
    private const int DefaultCloudMobileId = 1;
    private static bool _guestBootstrapAttempted;

    private readonly ApiController _api;
    private readonly SQLiteAsyncConnection _conn;
    private readonly BallRepository _ballRepo;
    private readonly EstablishmentRepository _establishmentRepo;
    private readonly EventRepository _eventRepo;
    private readonly SessionRepository _sessionRepo;
    private readonly GameRepository _gameRepo;
    private readonly FrameRepository _frameRepo;
    private readonly ShotRepository _shotRepo;
    private readonly UserRepository _userRepo;

    public CloudSyncService()
    {
        var db = new CellularDatabase();
        _conn = db.GetConnection();
        _api = new ApiController();
        _ballRepo = new BallRepository(_conn);
        _establishmentRepo = new EstablishmentRepository(_conn);
        _eventRepo = new EventRepository(_conn);
        _sessionRepo = new SessionRepository(_conn);
        _gameRepo = new GameRepository(_conn);
        _frameRepo = new FrameRepository(_conn);
        _shotRepo = new ShotRepository(_conn);
        _userRepo = new UserRepository(_conn);
    }

    /// <summary>
    /// Returns the last sync check time from Preferences, or null if never synced.
    /// </summary>
    public static string? GetLastCheckedTime()
    {
        var s = Preferences.Get(SyncLastCheckedUtcKey, (string?)null);
        if (string.IsNullOrEmpty(s)) return null;
        if (DateTime.TryParse(s, null, System.Globalization.DateTimeStyles.RoundtripKind, out var utc))
            return utc.ToLocalTime().ToString("g");
        return s;
    }

    /// <summary>
    /// Check for new data: fetches cloud and local, compares. Returns HasConflict if different, NoConflict if same, Error on failure.
    /// </summary>
    public async Task<SyncResult> CheckForNewDataAsync(int userId, string? apiUsername = null, string? apiPassword = null)
    {
        try
        {
            var cloudCounts = await FetchCloudCountsAsync(apiUsername, apiPassword, userId).ConfigureAwait(false);
            if (cloudCounts.Error != null)
                return new SyncResult { Result = SyncCheckResult.Error, ErrorMessage = cloudCounts.Error };

            var localCounts = await GetLocalCountsAsync(userId);

            bool same = cloudCounts.Balls == localCounts.Balls
                && cloudCounts.Establishments == localCounts.Establishments
                && cloudCounts.Events == localCounts.Events
                && cloudCounts.Sessions == localCounts.Sessions
                && cloudCounts.Games == localCounts.Games
                && cloudCounts.Frames == localCounts.Frames
                && cloudCounts.Shots == localCounts.Shots;

            return new SyncResult
            {
                Result = same ? SyncCheckResult.NoConflict : SyncCheckResult.HasConflict,
                ErrorMessage = null
            };
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Sync check failed: " + ex);
            return new SyncResult { Result = SyncCheckResult.Error, ErrorMessage = ex.Message };
        }
    }

    /// <summary>
    /// Try to add new data: push local to cloud (POST each entity type). On success, updates last checked time.
    /// Event, Session, Game, Frame, Shot (and Establishment) POST bodies include "id" so the server can preserve
    /// the same IDs; that way when we pull, EventID on sessions still matches Event.id, etc.
    /// </summary>
    public async Task<string?> AddNewDataAsync(int userId, string? apiUsername = null, string? apiPassword = null, bool allowContentReplace = true)
    {
        try
        {
            // Avoid duplicate cloud rows on repeated sync: compare full payload content (not cloud id).
            var cloudData = await FetchCloudDataAsync(apiUsername, apiPassword, userId).ConfigureAwait(false);
            if (cloudData.Error != null)
                return cloudData.Error;

            var cloudBallsExisting = ParseJsonList<Cellular.Cloud_API.Models.Ball>(cloudData.Balls) ?? new List<Cellular.Cloud_API.Models.Ball>();
            var cloudEstExisting = ParseJsonList<Cellular.Cloud_API.Models.Establishment>(cloudData.Establishments) ?? new List<Cellular.Cloud_API.Models.Establishment>();
            var cloudEventsExisting = ParseJsonList<Cellular.Cloud_API.Models.Event>(cloudData.Events) ?? new List<Cellular.Cloud_API.Models.Event>();
            var cloudSessionsExisting = ParseJsonList<Cellular.Cloud_API.Models.Session>(cloudData.Sessions) ?? new List<Cellular.Cloud_API.Models.Session>();
            var cloudGamesExisting = ParseJsonList<Cellular.Cloud_API.Models.Game>(cloudData.Games) ?? new List<Cellular.Cloud_API.Models.Game>();
            var cloudFramesExisting = ParseJsonList<Cellular.Cloud_API.Models.Frames>(cloudData.Frames) ?? new List<Cellular.Cloud_API.Models.Frames>();
            var cloudShotsExisting = ParseJsonList<Cellular.Cloud_API.Models.Shot>(cloudData.Shots) ?? new List<Cellular.Cloud_API.Models.Shot>();

            var existingBallSignatures = cloudBallsExisting.Select(GetBallSignature).ToHashSet();
            var existingEstSignatures = cloudEstExisting.Select(GetEstablishmentSignature).ToHashSet();
            var existingEventSignatures = cloudEventsExisting.Select(GetEventSignature).ToHashSet();
            var existingSessionSignatures = cloudSessionsExisting.Select(GetSessionSignature).ToHashSet();
            var existingGameSignatures = cloudGamesExisting.Select(GetGameSignature).ToHashSet();
            var existingFrameSignatures = cloudFramesExisting.Select(GetFrameSignature).ToHashSet();
            var existingShotSignatures = cloudShotsExisting.Select(GetShotSignature).ToHashSet();

            // Detect "same MobileID but different content" updates. Cloud POST endpoints may not upsert;
            // if we detect updates for key entities, replace cloud with local to guarantee consistency.
            var cloudBallByMobileId = cloudBallsExisting.Where(x => x.MobileID.HasValue).GroupBy(x => x.MobileID!.Value).ToDictionary(g => g.Key, g => g.First());
            var cloudEventByMobileId = cloudEventsExisting.Where(x => x.MobileID.HasValue).GroupBy(x => x.MobileID!.Value).ToDictionary(g => g.Key, g => g.First());
            var cloudFrameByMobileId = cloudFramesExisting.Where(x => x.MobileID.HasValue).GroupBy(x => x.MobileID!.Value).ToDictionary(g => g.Key, g => g.First());

            var balls = await _ballRepo.GetBallsByUserIdAsync(userId);
            var establishments = await _establishmentRepo.GetEstablishmentsByUserIdAsync(userId);
            var events = await _eventRepo.GetEventsByUserIdAsync(userId);
            var sessions = await _sessionRepo.GetSessionsByUserIdAsync(userId);
            var games = await _gameRepo.GetGamesByUserIdAsync(userId);

            bool hasBallContentUpdates = balls.Any(b =>
                cloudBallByMobileId.TryGetValue(b.BallId, out var cb) &&
                GetBallSignature(cb) != GetBallSignature(b.ToCloudPost()));
            bool hasEventContentUpdates = events.Any(e =>
                cloudEventByMobileId.TryGetValue(e.EventId, out var ce) &&
                GetEventSignature(ce) != GetEventSignature(e.ToCloud(userId)));

            var gameIds = games.Select(g => g.GameId).ToList();
            var frameIds = new List<int>();
            foreach (var gid in gameIds)
                frameIds.AddRange(await _frameRepo.GetFrameIdsByGameIdAsync(gid));

            var frames = new List<BowlingFrame>();
            foreach (var fid in frameIds)
            {
                var f = await _frameRepo.GetFrameById(fid);
                if (f != null) frames.Add(f);
            }

            bool hasFrameContentUpdates = frames.Any(f =>
                cloudFrameByMobileId.TryGetValue(f.FrameId, out var cf) &&
                GetFrameSignature(cf) != GetFrameSignature(f.ToCloud(0)));

            if (hasBallContentUpdates || hasEventContentUpdates || hasFrameContentUpdates)
            {
                if (!allowContentReplace)
                {
                    Debug.WriteLine("[CloudSync] Content mismatch after cloud replace; aborting to avoid an infinite sync loop. Check delete scope (mobileID must match sync user) or server delete behavior.");
                    return "Sync could not finish: cloud rows still conflict after a replace. If this persists, the server may not be deleting data for your mobileID.";
                }
                Debug.WriteLine("[CloudSync] Detected cloud content updates for existing MobileIDs (Ball/Event/Frame). Replacing cloud with local.");
                return await ReplaceCloudWithLocalAsync(userId, apiUsername, apiPassword).ConfigureAwait(false);
            }
            var cloudBalls = balls
                .Select(b => b.ToCloudPost())
                .Where(b => !existingBallSignatures.Contains(GetBallSignature(b)))
                .DistinctBy(GetBallSignature)
                .ToList();
            var cloudEst = establishments
                .Select(e => e.ToCloud(0))
                .Where(e => !existingEstSignatures.Contains(GetEstablishmentSignature(e)))
                .DistinctBy(GetEstablishmentSignature)
                .ToList();
            var cloudEvents = events
                .Select(e => e.ToCloud(userId))
                .Where(e => !existingEventSignatures.Contains(GetEventSignature(e)))
                .DistinctBy(GetEventSignature)
                .ToList();
            var cloudSessions = sessions
                .Select(s => s.ToCloud(0))
                .Where(s => !existingSessionSignatures.Contains(GetSessionSignature(s)))
                .DistinctBy(GetSessionSignature)
                .ToList();
            var cloudGames = games
                .Select(g => g.ToCloud(0))
                .Where(g => !existingGameSignatures.Contains(GetGameSignature(g)))
                .DistinctBy(GetGameSignature)
                .ToList();

            if (cloudBalls.Count > 0)
            {
                var r = await _api.ExecuteRequest(EntityType.Ball, OperationType.Post, cloudBalls.Cast<object>().ToList(), -1, null, apiUsername, apiPassword);
                if (r != null && r.StartsWith("Request did not succeed", StringComparison.Ordinal))
                    return r;
                foreach (var b in cloudBalls) existingBallSignatures.Add(GetBallSignature(b));
            }
            if (cloudEst.Count > 0)
            {
                var r = await _api.ExecuteRequest(EntityType.Establishment, OperationType.Post, cloudEst.Cast<object>().ToList(), -1, null, apiUsername, apiPassword);
                if (r != null && r.StartsWith("Request did not succeed", StringComparison.Ordinal))
                    return r;
                foreach (var e in cloudEst) existingEstSignatures.Add(GetEstablishmentSignature(e));
            }
            if (cloudEvents.Count > 0)
            {
                var r = await _api.ExecuteRequest(EntityType.Event, OperationType.Post, cloudEvents.Cast<object>().ToList(), -1, null, apiUsername, apiPassword);
                if (r != null && r.StartsWith("Request did not succeed", StringComparison.Ordinal))
                    return r;
                foreach (var e in cloudEvents) existingEventSignatures.Add(GetEventSignature(e));
            }
            if (cloudSessions.Count > 0)
            {
                var r = await _api.ExecuteRequest(EntityType.Session, OperationType.Post, cloudSessions.Cast<object>().ToList(), -1, null, apiUsername, apiPassword);
                if (r != null && r.StartsWith("Request did not succeed", StringComparison.Ordinal))
                    return r;
                foreach (var s in cloudSessions) existingSessionSignatures.Add(GetSessionSignature(s));
            }
            if (cloudGames.Count > 0)
            {
                var r = await _api.ExecuteRequest(EntityType.Game, OperationType.Post, cloudGames.Cast<object>().ToList(), -1, null, apiUsername, apiPassword);
                if (r != null && r.StartsWith("Request did not succeed", StringComparison.Ordinal))
                    return r;
                foreach (var g in cloudGames) existingGameSignatures.Add(GetGameSignature(g));
            }

            if (frames.Count > 0)
            {
                var cloudFrames = frames
                    .Select(f => f.ToCloud(0))
                    .Where(f => !existingFrameSignatures.Contains(GetFrameSignature(f)))
                    .DistinctBy(GetFrameSignature)
                    .ToList();
                if (cloudFrames.Count > 0)
                {
                    var r = await _api.ExecuteRequest(EntityType.Frame, OperationType.Post, cloudFrames.Cast<object>().ToList(), -1, null, apiUsername, apiPassword);
                    if (r != null && r.StartsWith("Request did not succeed", StringComparison.Ordinal))
                        return r;
                    foreach (var f in cloudFrames) existingFrameSignatures.Add(GetFrameSignature(f));
                }
            }

            var shotsWithSession = new List<(Cellular.ViewModel.Shot shot, int sessionId)>();
            foreach (var fid in frameIds)
            {
                var frame = await _frameRepo.GetFrameById(fid);
                var game = frame?.GameId != null ? await _gameRepo.GetGameById(frame.GameId.Value) : null;
                var sessionId = game?.SessionId ?? 0;
                var shotIds = await _frameRepo.GetShotIdsByFrameIdAsync(fid);
                foreach (var sid in shotIds)
                {
                    var shot = await _shotRepo.GetShotById(sid);
                    if (shot != null) shotsWithSession.Add((shot, sessionId));
                }
            }
            if (shotsWithSession.Count > 0)
            {
                var cloudShots = shotsWithSession
                    .Select(x => x.shot.ToCloud(x.sessionId))
                    .Where(s => !existingShotSignatures.Contains(GetShotSignature(s)))
                    .DistinctBy(GetShotSignature)
                    .ToList();
                if (cloudShots.Count > 0)
                {
                    var r = await _api.ExecuteRequest(EntityType.Shot, OperationType.Post, cloudShots.Cast<object>().ToList(), -1, null, apiUsername, apiPassword);
                    if (r != null && r.StartsWith("Request did not succeed", StringComparison.Ordinal))
                        return r;
                    foreach (var s in cloudShots) existingShotSignatures.Add(GetShotSignature(s));
                }
            }

            await ApplyLocalCloudIdsFromCloudFetchAsync(userId, apiUsername, apiPassword).ConfigureAwait(false);

            Preferences.Set(SyncLastCheckedUtcKey, DateTime.UtcNow.ToString("O"));
            return null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Add new data failed: " + ex);
            return ex.Message;
        }
    }

    /// <summary>
    /// After a successful push, re-fetch cloud rows and store server <c>id</c> in local <see cref="Cellular.ViewModel.Ball.CloudID"/> (and peers)
    /// matched by <c>mobileID</c> (local PK). POST responses are often plain text; GET JSON is authoritative.
    /// </summary>
    private async Task ApplyLocalCloudIdsFromCloudFetchAsync(int userId, string? apiUsername, string? apiPassword)
    {
        try
        {
            var data = await FetchCloudDataAsync(apiUsername, apiPassword, userId).ConfigureAwait(false);
            if (data.Error != null)
                return;

            foreach (var c in ParseJsonList<Cellular.Cloud_API.Models.Ball>(data.Balls) ?? new List<Cellular.Cloud_API.Models.Ball>())
            {
                if (c.Id <= 0) continue;
                var localKey = c.MobileID ?? c.Id;
                var row = await _conn.Table<Cellular.ViewModel.Ball>().FirstOrDefaultAsync(b => b.BallId == localKey && b.UserId == userId);
                if (row != null)
                {
                    row.CloudID = c.Id;
                    await _conn.UpdateAsync(row);
                }
            }

            foreach (var c in ParseJsonList<Cellular.Cloud_API.Models.Establishment>(data.Establishments) ?? new List<Cellular.Cloud_API.Models.Establishment>())
            {
                if (c.ID <= 0) continue;
                var localKey = c.MobileID ?? c.ID;
                var row = await _conn.Table<Cellular.ViewModel.Establishment>().FirstOrDefaultAsync(e => e.EstaID == localKey && e.UserId == userId);
                if (row != null)
                {
                    row.CloudID = c.ID;
                    await _conn.UpdateAsync(row);
                }
            }

            foreach (var c in ParseJsonList<Cellular.Cloud_API.Models.Event>(data.Events) ?? new List<Cellular.Cloud_API.Models.Event>())
            {
                if (c.Id <= 0) continue;
                var localKey = c.MobileID ?? c.Id;
                var row = await _conn.Table<Cellular.ViewModel.Event>().FirstOrDefaultAsync(e => e.EventId == localKey && e.UserId == userId);
                if (row != null)
                {
                    row.CloudID = c.Id;
                    await _conn.UpdateAsync(row);
                }
            }

            foreach (var c in ParseJsonList<Cellular.Cloud_API.Models.Session>(data.Sessions) ?? new List<Cellular.Cloud_API.Models.Session>())
            {
                if (c.ID <= 0) continue;
                var localKey = c.MobileID ?? c.ID;
                var row = await _conn.Table<Cellular.ViewModel.Session>().FirstOrDefaultAsync(s => s.SessionId == localKey && s.UserId == userId);
                if (row != null)
                {
                    row.CloudID = c.ID;
                    await _conn.UpdateAsync(row);
                }
            }

            var userGameIds = (await _gameRepo.GetGamesByUserIdAsync(userId)).Select(g => g.GameId).ToHashSet();
            foreach (var c in ParseJsonList<Cellular.Cloud_API.Models.Game>(data.Games) ?? new List<Cellular.Cloud_API.Models.Game>())
            {
                if (c.ID <= 0) continue;
                var localKey = c.MobileID ?? c.ID;
                if (!userGameIds.Contains(localKey)) continue;
                var row = await _conn.Table<Cellular.ViewModel.Game>().FirstOrDefaultAsync(g => g.GameId == localKey);
                if (row != null)
                {
                    row.CloudID = c.ID;
                    await _conn.UpdateAsync(row);
                }
            }

            var userFrameIds = new HashSet<int>();
            foreach (var gid in userGameIds)
            {
                foreach (var fid in await _frameRepo.GetFrameIdsByGameIdAsync(gid).ConfigureAwait(false))
                    userFrameIds.Add(fid);
            }

            foreach (var c in ParseJsonList<Cellular.Cloud_API.Models.Frames>(data.Frames) ?? new List<Cellular.Cloud_API.Models.Frames>())
            {
                if (c.Id <= 0) continue;
                var localKey = c.MobileID ?? c.Id;
                if (!userFrameIds.Contains(localKey)) continue;
                var row = await _conn.Table<Cellular.ViewModel.BowlingFrame>().FirstOrDefaultAsync(f => f.FrameId == localKey);
                if (row != null)
                {
                    row.CloudID = c.Id;
                    await _conn.UpdateAsync(row);
                }
            }

            var userShotIds = new HashSet<int>();
            foreach (var fid in userFrameIds)
            {
                foreach (var sid in await _frameRepo.GetShotIdsByFrameIdAsync(fid).ConfigureAwait(false))
                    userShotIds.Add(sid);
            }

            foreach (var c in ParseJsonList<Cellular.Cloud_API.Models.Shot>(data.Shots) ?? new List<Cellular.Cloud_API.Models.Shot>())
            {
                if (c.ID <= 0) continue;
                var localKey = c.MobileID ?? c.ID;
                if (!userShotIds.Contains(localKey)) continue;
                var row = await _conn.Table<Cellular.ViewModel.Shot>().FirstOrDefaultAsync(s => s.ShotId == localKey);
                if (row != null)
                {
                    row.CloudID = c.ID;
                    await _conn.UpdateAsync(row);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine("[CloudSync] ApplyLocalCloudIdsFromCloudFetchAsync: " + ex);
        }
    }

    /// <summary>
    /// Make cloud match local: delete sync entities for the user (shots through balls), then push local (AddNewDataAsync).
    /// Call when user chooses "Keep local". Requires server delete endpoints for each entity type.
    /// </summary>
    public async Task<string?> ReplaceCloudWithLocalAsync(int userId, string? apiUsername = null, string? apiPassword = null)
    {
        var effectiveUsername = ResolveDeleteUsername(apiUsername);
        // Leaf -> root, then entities sessions reference (events, establishments), then balls (referenced by shots).
        var deleteOrder = new[]
        {
            EntityType.Shot, EntityType.Frame, EntityType.Game, EntityType.Session, EntityType.Event,
            EntityType.Establishment, EntityType.Ball
        };
        foreach (var entityType in deleteOrder)
        {
            var r = await DeleteCloudEntityWithFallbackAsync(entityType, userId, effectiveUsername, apiUsername, apiPassword).ConfigureAwait(false);
            if (r != null)
                return r;
        }
        Preferences.Remove(SyncLastCheckedUtcKey);
        return await AddNewDataAsync(userId, apiUsername, apiPassword, allowContentReplace: false).ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes cloud sync entities for the user (keeps users/auth data intact).
    /// </summary>
    public async Task<string?> ClearCloudDataAsync(int userId, string? apiUsername = null, string? apiPassword = null)
    {
        var effectiveUsername = ResolveDeleteUsername(apiUsername);
        var deleteOrder = new[]
        {
            EntityType.Shot, EntityType.Frame, EntityType.Game, EntityType.Session, EntityType.Event,
            EntityType.Establishment, EntityType.Ball
        };
        foreach (var entityType in deleteOrder)
        {
            var r = await DeleteCloudEntityWithFallbackAsync(entityType, userId, effectiveUsername, apiUsername, apiPassword).ConfigureAwait(false);
            if (r != null)
                return r;
        }
        Preferences.Remove(SyncLastCheckedUtcKey);
        return null;
    }

    /// <summary>
    /// Some cloud delete endpoints are strict about payload shape. Try common variants before failing.
    /// Returns null on success, or the last failure string.
    /// </summary>
    private async Task<string?> DeleteCloudEntityWithFallbackAsync(
        EntityType entityType,
        int userId,
        string effectiveUsername,
        string? apiUsername,
        string? apiPassword)
    {
        int effectiveMobileId = ResolveDeleteMobileId(userId);
        var attempts = new (string Name, List<object>? Body)[]
        {
            ("username+mobileID", new List<object> { new { username = effectiveUsername, mobileID = effectiveMobileId } }),
            ("username-only", new List<object> { new { username = effectiveUsername } }),
            ("mobileID-only", new List<object> { new { mobileID = effectiveMobileId } }),
            // Keep JSON content-type on every attempt to avoid 415 Unsupported Media Type.
            ("empty-json", new List<object> { new { } })
        };

        string? lastError = null;
        foreach (var attempt in attempts)
        {
            var r = await _api.ExecuteRequest(entityType, OperationType.Delete, attempt.Body, -1, null, apiUsername, apiPassword).ConfigureAwait(false);
            if (r == null || !r.StartsWith("Request did not succeed", StringComparison.Ordinal))
            {
                Debug.WriteLine($"[CloudSync] Delete {entityType} succeeded using {attempt.Name} payload.");
                return null;
            }

            lastError = r;
            Debug.WriteLine($"[CloudSync] Delete {entityType} failed using {attempt.Name}: {r}");
        }

        return lastError;
    }

    private async Task<string> ResolveApiUsernameAsync(int userId, string? apiUsername)
    {
        if (!string.IsNullOrWhiteSpace(apiUsername))
            return apiUsername;

        try
        {
            var user = await _userRepo.GetUserByIdAsync(userId).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(user?.UserName))
                return user.UserName!;
        }
        catch
        {
            // Fallback to legacy test username below.
        }

        return DefaultCloudUsername;
    }

    /// <summary>
    /// For delete payload filtering, match API tests/default auth username behavior.
    /// </summary>
    private static string ResolveDeleteUsername(string? apiUsername)
    {
        return !string.IsNullOrWhiteSpace(apiUsername) ? apiUsername : DefaultCloudUsername;
    }

    private static int ResolveDeleteMobileId(int userId)
    {
        // Must match GET ?mobileID= / sync user id from Preferences — not DefaultCloudMobileId when that differs,
        // or deletes return 200 but leave rows and sync loops forever.
        return userId > 0 ? userId : DefaultCloudMobileId;
    }

    /// <summary>
    /// Ensures the cloud has a Guest app user (mobileID=1). Uses string/string auth to create it if needed.
    /// Safe to call repeatedly; it runs once per app session.
    /// </summary>
    public static async Task EnsureGuestCloudUserExistsAsync()
    {
        if (_guestBootstrapAttempted) return;
        _guestBootstrapAttempted = true;

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

            // 1) Authorize with string/string as requested.
            var authBody = JsonSerializer.Serialize(new { username = "string", password = "string" });
            using var authReq = new HttpRequestMessage(HttpMethod.Post, RevMetrixApi.Posts("Authorize"))
            {
                Content = new StringContent(authBody, Encoding.UTF8, "application/json")
            };
            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes("string:string"));
            authReq.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);

            using var authResp = await client.SendAsync(authReq).ConfigureAwait(false);
            var authText = await authResp.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!authResp.IsSuccessStatusCode)
            {
                Debug.WriteLine($"[CloudSync] Guest bootstrap auth failed: {(int)authResp.StatusCode} {authText}");
                return;
            }

            string? token = null;
            try
            {
                using var doc = JsonDocument.Parse(authText);
                if (doc.RootElement.TryGetProperty("tokenA", out var p)) token = p.GetString();
            }
            catch { }
            if (string.IsNullOrWhiteSpace(token))
            {
                Debug.WriteLine("[CloudSync] Guest bootstrap auth returned no token.");
                return;
            }

            // 2) Create Guest app user (if exists already, server may return 400/409; treat as fine).
            var guestPayload = new
            {
                mobileID = DefaultCloudMobileId,
                firstname = "Guest",
                lastname = "User",
                username = DefaultCloudUsername,
                hashedPassword = "R3Vlc3Q=", // base64("Guest")
                email = "guest@revmetrix.io",
                phoneNumber = "0000000000",
                lastLogin = (string?)null,
                hand = (string?)null
            };
            var guestBody = JsonSerializer.Serialize(guestPayload);
            using var userReq = new HttpRequestMessage(HttpMethod.Post, RevMetrixApi.Posts("PostUserApp"))
            {
                Content = new StringContent(guestBody, Encoding.UTF8, "application/json")
            };
            userReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var userResp = await client.SendAsync(userReq).ConfigureAwait(false);
            var userText = await userResp.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!(userResp.IsSuccessStatusCode || userResp.StatusCode == System.Net.HttpStatusCode.BadRequest || userResp.StatusCode == System.Net.HttpStatusCode.Conflict))
                Debug.WriteLine($"[CloudSync] Guest bootstrap PostUserApp unexpected: {(int)userResp.StatusCode} {userText}");
            else
                Debug.WriteLine($"[CloudSync] Guest bootstrap done: {(int)userResp.StatusCode} {userText}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CloudSync] Guest bootstrap error: {ex.Message}");
        }
    }

    /// <summary>
    /// Best-effort cloud app-user creation for first-time registration flow.
    /// Uses string/string auth to call PostUserApp and treats already-exists style responses as success.
    /// </summary>
    public static async Task EnsureCloudAppUserExistsAsync(string username, string plainPassword, int mobileId)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

            // Authorize with service account used by cloud tests.
            var authBody = JsonSerializer.Serialize(new { username = "string", password = "string" });
            using var authReq = new HttpRequestMessage(HttpMethod.Post, RevMetrixApi.Posts("Authorize"))
            {
                Content = new StringContent(authBody, Encoding.UTF8, "application/json")
            };
            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes("string:string"));
            authReq.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);

            using var authResp = await client.SendAsync(authReq).ConfigureAwait(false);
            var authText = await authResp.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!authResp.IsSuccessStatusCode)
            {
                Debug.WriteLine($"[CloudSync] Register bootstrap auth failed: {(int)authResp.StatusCode} {authText}");
                return;
            }

            string? token = null;
            try
            {
                using var doc = JsonDocument.Parse(authText);
                if (doc.RootElement.TryGetProperty("tokenA", out var p)) token = p.GetString();
            }
            catch { }
            if (string.IsNullOrWhiteSpace(token))
                return;

            // Cloud test server accepts base64 bytes in hashedPassword.
            var hashedPassword = Convert.ToBase64String(Encoding.UTF8.GetBytes(plainPassword));
            var payload = new
            {
                mobileID = mobileId,
                firstname = username,
                lastname = "User",
                username = username,
                hashedPassword = hashedPassword,
                email = $"{username}@revmetrix.io",
                phoneNumber = "0000000000",
                lastLogin = (string?)null,
                hand = (string?)null
            };

            using var req = new HttpRequestMessage(HttpMethod.Post, RevMetrixApi.Posts("PostUserApp"))
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var resp = await client.SendAsync(req).ConfigureAwait(false);
            var text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!(resp.IsSuccessStatusCode || resp.StatusCode == System.Net.HttpStatusCode.BadRequest || resp.StatusCode == System.Net.HttpStatusCode.Conflict))
                Debug.WriteLine($"[CloudSync] Register PostUserApp unexpected: {(int)resp.StatusCode} {text}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CloudSync] Register cloud user ensure error: {ex.Message}");
        }
    }

    /// <summary>
    /// Deletes local sync entities for the user (keeps User table rows intact).
    /// </summary>
    public async Task<string?> ClearLocalDataAsync(int userId)
    {
        try
        {
            await DeleteUserDataInOrderAsync(userId).ConfigureAwait(false);
            Preferences.Remove(SyncLastCheckedUtcKey);
            return null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Clear local data failed: " + ex);
            return ex.Message;
        }
    }

    /// <summary>
    /// Overwrite local DB with cloud data. Call after user chooses "Use cloud". Then call AddNewDataAsync to align cloud.
    /// </summary>
    public async Task<string?> OverwriteLocalWithCloudAsync(
        string? ballsJson, string? establishmentsJson, string? eventsJson,
        string? sessionsJson, string? gamesJson, string? framesJson, string? shotsJson,
        int userId)
    {
        try
        {
            await DeleteUserDataInOrderAsync(userId);

            var balls = ParseJsonList<Cellular.Cloud_API.Models.Ball>(ballsJson);
            if (balls != null && balls.Count > 0)
            {
                var deduped = balls
                    .GroupBy(b => b.MobileID ?? b.Id)
                    .Select(g => g.First())
                    .ToList();
                foreach (var m in deduped)
                {
                    var loc = m.ToLocal(userId);
                    await _ballRepo.AddAsync(loc);
                }
            }

            var establishments = ParseJsonList<Cellular.Cloud_API.Models.Establishment>(establishmentsJson);
            if (establishments != null && establishments.Count > 0)
            {
                var deduped = establishments
                    .GroupBy(e => e.MobileID ?? e.ID)
                    .Select(g => g.First())
                    .ToList();
                foreach (var m in deduped)
                {
                    var e = m.ToLocal(userId);
                    await _establishmentRepo.AddAsync(e);
                }
            }

            var events = ParseJsonList<Cellular.Cloud_API.Models.Event>(eventsJson);
            if (events != null && events.Count > 0)
            {
                var deduped = events
                    .GroupBy(e => e.MobileID ?? e.Id)
                    .Select(g => g.First())
                    .ToList();
                foreach (var m in deduped)
                {
                    var e = m.ToLocal(userId);
                    await _eventRepo.AddAsync(e);
                }
            }

            var sessions = ParseJsonList<Cellular.Cloud_API.Models.Session>(sessionsJson);
            if (sessions != null && sessions.Count > 0)
            {
                var deduped = sessions
                    .GroupBy(s => s.MobileID ?? s.ID)
                    .Select(g => g.First())
                    .ToList();
                foreach (var m in deduped)
                {
                    var s = m.ToLocal(userId);
                    await _sessionRepo.AddAsync(s);
                }
            }

            var games = ParseJsonList<Cellular.Cloud_API.Models.Game>(gamesJson);
            if (games != null && games.Count > 0)
            {
                var deduped = games
                    .GroupBy(g => g.MobileID ?? g.ID)
                    .Select(gr => gr.First())
                    .ToList();
                foreach (var m in deduped)
                {
                    var g = m.ToLocal(0);
                    await _gameRepo.AddAsync(g);
                }
            }

            var frames = ParseJsonList<Cellular.Cloud_API.Models.Frames>(framesJson);
            if (frames != null && frames.Count > 0)
            {
                var deduped = frames
                    .GroupBy(f => f.MobileID ?? f.Id)
                    .Select(g => g.First())
                    .ToList();
                foreach (var m in deduped)
                {
                    var f = m.ToLocal(0);
                    await _frameRepo.AddFrame(f);
                }
            }

            var shots = ParseJsonList<Cellular.Cloud_API.Models.Shot>(shotsJson);
            if (shots != null && shots.Count > 0)
            {
                var deduped = shots
                    .GroupBy(s => s.MobileID ?? s.ID)
                    .Select(g => g.First())
                    .ToList();
                foreach (var m in deduped)
                {
                    var loc = m.ToLocal(0);
                    await _shotRepo.AddAsync(loc);
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Overwrite local failed: " + ex);
            return ex.Message;
        }
    }

    /// <summary>
    /// Fetches current cloud data as raw JSON strings (for conflict resolution). Returns error message or null.
    /// </summary>
    public async Task<(string? Error, string? Balls, string? Establishments, string? Events, string? Sessions, string? Games, string? Frames, string? Shots)> FetchCloudDataAsync(string? apiUsername = null, string? apiPassword = null, int? syncUserId = null)
    {
        string? err = null;
        var empty = new List<object>();
        var getFilter = BuildBallEventCloudGetQuery(ResolveSyncMobileId(syncUserId));
        var balls = await ExecuteGetWithOptionalQueryFallbackAsync(EntityType.Ball, empty, apiUsername, apiPassword, getFilter).ConfigureAwait(false);
        if (balls != null && balls.StartsWith("Request did not succeed", StringComparison.Ordinal)) err ??= balls;
        var est = await _api.ExecuteRequest(EntityType.Establishment, OperationType.Get, empty, -1, null, apiUsername, apiPassword);
        if (est != null && est.StartsWith("Request did not succeed", StringComparison.Ordinal)) err ??= est;
        var events = await ExecuteGetWithOptionalQueryFallbackAsync(EntityType.Event, empty, apiUsername, apiPassword, getFilter).ConfigureAwait(false);
        if (events != null && events.StartsWith("Request did not succeed", StringComparison.Ordinal)) err ??= events;
        var sessions = await _api.ExecuteRequest(EntityType.Session, OperationType.Get, empty, -1, null, apiUsername, apiPassword);
        if (sessions != null && sessions.StartsWith("Request did not succeed", StringComparison.Ordinal)) err ??= sessions;
        var games = await _api.ExecuteRequest(EntityType.Game, OperationType.Get, empty, -1, null, apiUsername, apiPassword);
        if (games != null && games.StartsWith("Request did not succeed", StringComparison.Ordinal)) err ??= games;
        var (frames, framesErr) = await FetchMergedFramesJsonAsync(empty, games, apiUsername, apiPassword).ConfigureAwait(false);
        if (framesErr != null) err ??= framesErr;
        var shots = await _api.ExecuteRequest(EntityType.Shot, OperationType.Get, empty, -1, null, apiUsername, apiPassword);
        if (shots != null && shots.StartsWith("Request did not succeed", StringComparison.Ordinal)) err ??= shots;

        return (err, balls, est, events, sessions, games, frames, shots);
    }

    /// <summary>Server returns camelCase (id, userId, name, ...); use case-insensitive so it maps to PascalCase models.</summary>
    private static readonly JsonSerializerOptions CloudJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = null
    };

    // Signatures for duplicate / content comparison: meaningful fields only — not cloud PK, and not userId
    // (GET returns combined-DB user id; local ToCloud uses app SQLite userId, so including userId caused endless replace loops).
    private static string N(string? s) => (s ?? string.Empty).Trim();
    private static string GetBallSignature(Cellular.Cloud_API.Models.Ball b) => $"{b.MobileID}|{N(b.Name)}|{N(b.Weight)}|{N(b.CoreType)}";
    private static string GetBallSignature(Cellular.Cloud_API.Models.BallPostRequest b) => $"{b.MobileID}|{N(b.Name)}|{N(b.Weight)}|{N(b.CoreType)}";
    private static string GetEstablishmentSignature(Cellular.Cloud_API.Models.Establishment e) => $"{e.MobileID}|{N(e.Name)}|{N(e.Lanes)}|{N(e.Type)}|{N(e.Location)}";
    private static string GetEventSignature(Cellular.Cloud_API.Models.Event e) => $"{e.MobileID}|{N(e.Name)}|{N(e.Type)}|{N(e.Location)}|{e.Average}|{e.Stats}|{N(e.Standings)}";
    private static string GetSessionSignature(Cellular.Cloud_API.Models.Session s) => $"{s.MobileID}|{s.SessionNumber}|{s.EstablishmentID}|{s.EventID}|{s.DateTime}|{N(s.TeamOpponent)}|{N(s.IndividualOpponent)}|{s.Score}|{s.Stats}|{s.TeamRecord}|{s.IndividualRecord}";
    private static string GetGameSignature(Cellular.Cloud_API.Models.Game g) => $"{g.MobileID}|{N(g.GameNumber)}|{N(g.Lanes)}|{g.Score}|{g.Win}|{g.StartingLane}|{g.SessionID}|{g.TeamResult}|{g.IndividualResult}";
    private static string GetFrameSignature(Cellular.Cloud_API.Models.Frames f) => $"{f.MobileID}|{f.GameId}|{f.ShotOne}|{f.ShotTwo}|{f.FrameNumber}|{f.Lane}|{f.Result}";
    private static string GetShotSignature(Cellular.Cloud_API.Models.Shot s) => $"{s.MobileID}|{s.Type}|{s.SmartDotID}|{s.SessionID}|{s.BallID}|{s.FrameID}|{s.ShotNumber}|{s.LeaveType}|{N(s.Side)}|{N(s.Position)}|{N(s.Comment)}";

    private static List<T>? ParseJsonList<T>(string? json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var list = JsonSerializer.Deserialize<List<T>>(json, CloudJsonOptions);
            return list;
        }
        catch
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Array)
                    return JsonSerializer.Deserialize<List<T>>(json, CloudJsonOptions);
                if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
                    return JsonSerializer.Deserialize<List<T>>(data.GetRawText(), CloudJsonOptions);
            }
            catch { }
            return null;
        }
    }

    private async Task DeleteUserDataInOrderAsync(int userId)
    {
        var sessions = await _sessionRepo.GetSessionsByUserIdAsync(userId);
        var sessionIds = sessions.Select(s => s.SessionId).ToList();
        var games = await _gameRepo.GetGamesByUserIdAsync(userId);
        var gameIds = games.Select(g => g.GameId).ToList();
        var frameIds = new List<int>();
        foreach (var gid in gameIds)
            frameIds.AddRange(await _frameRepo.GetFrameIdsByGameIdAsync(gid));

        foreach (var fid in frameIds)
        {
            var shotIds = await _frameRepo.GetShotIdsByFrameIdAsync(fid);
            foreach (var sid in shotIds)
            {
                var shot = await _shotRepo.GetShotById(sid);
                if (shot != null) await _conn.DeleteAsync(shot);
            }
        }
        foreach (var fid in frameIds)
        {
            var f = await _frameRepo.GetFrameById(fid);
            if (f != null) await _conn.DeleteAsync(f);
        }
        foreach (var g in games)
            await _conn.DeleteAsync(g);
        foreach (var s in sessions)
            await _conn.DeleteAsync(s);
        var events = await _eventRepo.GetEventsByUserIdAsync(userId);
        foreach (var e in events) await _conn.DeleteAsync(e);
        var establishments = await _establishmentRepo.GetEstablishmentsByUserIdAsync(userId);
        foreach (var e in establishments) await _conn.DeleteAsync(e);
        var balls = await _ballRepo.GetBallsByUserIdAsync(userId);
        foreach (var b in balls) await _conn.DeleteAsync(b);

        // Reset SQLite AUTOINCREMENT counters so IDs don't keep increasing after an overwrite sync.
        // We reset each table's sequence to the current MAX(pk) to avoid duplicate PKs
        // if the DB contains data for multiple users.
        try
        {
            await _conn.ExecuteAsync("UPDATE sqlite_sequence SET seq = COALESCE((SELECT MAX(BallId) FROM ball), 0) WHERE name = 'ball';");
            await _conn.ExecuteAsync("UPDATE sqlite_sequence SET seq = COALESCE((SELECT MAX(EstaID) FROM establishment), 0) WHERE name = 'establishment';");
            await _conn.ExecuteAsync("UPDATE sqlite_sequence SET seq = COALESCE((SELECT MAX(EventId) FROM event), 0) WHERE name = 'event';");
            await _conn.ExecuteAsync("UPDATE sqlite_sequence SET seq = COALESCE((SELECT MAX(SessionId) FROM session), 0) WHERE name = 'session';");
            await _conn.ExecuteAsync("UPDATE sqlite_sequence SET seq = COALESCE((SELECT MAX(GameId) FROM game), 0) WHERE name = 'game';");
            await _conn.ExecuteAsync("UPDATE sqlite_sequence SET seq = COALESCE((SELECT MAX(FrameId) FROM bowlingFrame), 0) WHERE name = 'bowlingFrame';");
            await _conn.ExecuteAsync("UPDATE sqlite_sequence SET seq = COALESCE((SELECT MAX(ShotId) FROM shot), 0) WHERE name = 'shot';");
        }
        catch
        {
            // If sqlite_sequence doesn't exist yet (or the schema differs), ignore and continue.
        }
    }

    private async Task<(int Balls, int Establishments, int Events, int Sessions, int Games, int Frames, int Shots)> GetLocalCountsAsync(int userId)
    {
        var balls = await _ballRepo.GetBallsByUserIdAsync(userId);
        var establishments = await _establishmentRepo.GetEstablishmentsByUserIdAsync(userId);
        var events = await _eventRepo.GetEventsByUserIdAsync(userId);
        var sessions = await _sessionRepo.GetSessionsByUserIdAsync(userId);
        var games = await _gameRepo.GetGamesByUserIdAsync(userId);
        var gameIds = games.Select(g => g.GameId).ToList();
        var frameCount = 0;
        var shotCount = 0;
        foreach (var gid in gameIds)
        {
            var fids = await _frameRepo.GetFrameIdsByGameIdAsync(gid);
            frameCount += fids.Count;
            foreach (var fid in fids)
                shotCount += (await _frameRepo.GetShotIdsByFrameIdAsync(fid)).Count;
        }
        return (balls.Count, establishments.Count, events.Count, sessions.Count, games.Count, frameCount, shotCount);
    }

    private async Task<(string? Error, int Balls, int Establishments, int Events, int Sessions, int Games, int Frames, int Shots)> FetchCloudCountsAsync(string? apiUsername, string? apiPassword, int? syncUserId)
    {
        var empty = new List<object>();
        int cb = 0, ce = 0, cev = 0, cs = 0, cg = 0, cf = 0, csh = 0;
        var getFilter = BuildBallEventCloudGetQuery(ResolveSyncMobileId(syncUserId));
        string? r;

        r = await ExecuteGetWithOptionalQueryFallbackAsync(EntityType.Ball, empty, apiUsername, apiPassword, getFilter).ConfigureAwait(false);
        if (r != null && r.StartsWith("Request did not succeed", StringComparison.Ordinal)) return (r, 0, 0, 0, 0, 0, 0, 0);
        cb = CountJsonArray(r);

        r = await _api.ExecuteRequest(EntityType.Establishment, OperationType.Get, empty, -1, null, apiUsername, apiPassword);
        if (r != null && r.StartsWith("Request did not succeed", StringComparison.Ordinal)) return (r, 0, 0, 0, 0, 0, 0, 0);
        ce = CountJsonArray(r);

        r = await ExecuteGetWithOptionalQueryFallbackAsync(EntityType.Event, empty, apiUsername, apiPassword, getFilter).ConfigureAwait(false);
        if (r != null && r.StartsWith("Request did not succeed", StringComparison.Ordinal)) return (r, 0, 0, 0, 0, 0, 0, 0);
        cev = CountJsonArray(r);

        r = await _api.ExecuteRequest(EntityType.Session, OperationType.Get, empty, -1, null, apiUsername, apiPassword);
        if (r != null && r.StartsWith("Request did not succeed", StringComparison.Ordinal)) return (r, 0, 0, 0, 0, 0, 0, 0);
        cs = CountJsonArray(r);

        r = await _api.ExecuteRequest(EntityType.Game, OperationType.Get, empty, -1, null, apiUsername, apiPassword);
        if (r != null && r.StartsWith("Request did not succeed", StringComparison.Ordinal)) return (r, 0, 0, 0, 0, 0, 0, 0);
        cg = CountJsonArray(r);
        cf = await CountCloudFramesAsync(empty, r, apiUsername, apiPassword).ConfigureAwait(false);

        r = await _api.ExecuteRequest(EntityType.Shot, OperationType.Get, empty, -1, null, apiUsername, apiPassword);
        if (r != null && r.StartsWith("Request did not succeed", StringComparison.Ordinal)) return (r, 0, 0, 0, 0, 0, 0, 0);
        csh = CountJsonArray(r);

        return (null, cb, ce, cev, cs, cg, cf, csh);
    }

    private async Task<string?> ExecuteGetWithOptionalQueryFallbackAsync(
        EntityType entityType,
        List<object> empty,
        string? apiUsername,
        string? apiPassword,
        IReadOnlyDictionary<string, string>? getQuery)
    {
        if (getQuery != null && getQuery.Count > 0)
        {
            var withQuery = await _api.ExecuteRequest(entityType, OperationType.Get, empty, -1, null, apiUsername, apiPassword, getQuery).ConfigureAwait(false);
            if (withQuery != null && !withQuery.StartsWith("Request did not succeed", StringComparison.Ordinal))
                return withQuery;
        }

        return await _api.ExecuteRequest(entityType, OperationType.Get, empty, -1, null, apiUsername, apiPassword, null).ConfigureAwait(false);
    }

    /// <summary>
    /// Matches TestServer.py: <c>GetBallsByUsername</c> / <c>GetEventsByUsername</c> use query <c>?mobileID=</c> only;
    /// the JWT from Authorize supplies the login user; server resolves combinedDB user with username + mobileID.
    /// </summary>
    private static Dictionary<string, string> BuildBallEventCloudGetQuery(int syncUserId)
    {
        var mobileId = syncUserId > 0 ? syncUserId : DefaultCloudMobileId;
        return new Dictionary<string, string> { ["mobileID"] = mobileId.ToString() };
    }

    /// <summary>Explicit sync user id from caller, else Preferences, else guest default.</summary>
    private static int ResolveSyncMobileId(int? explicitUserId)
    {
        if (explicitUserId is > 0)
            return explicitUserId.Value;
        var pref = Preferences.Get("UserId", -1);
        return pref > 0 ? pref : DefaultCloudMobileId;
    }

    private async Task<(string FramesJson, string? Error)> FetchMergedFramesJsonAsync(
        List<object> empty,
        string? gamesJson,
        string? apiUsername,
        string? apiPassword)
    {
        var candidates = GetGameFrameLookupIds(gamesJson);
        if (candidates.Count == 0)
            return ("[]", null);

        var merged = new Dictionary<string, string>(StringComparer.Ordinal); // key: mobileID|id fallback -> raw json
        string? lastHardError = null;

        foreach (var gid in candidates)
        {
            var fr = await _api.ExecuteRequest(EntityType.Frame, OperationType.Get, empty, gid, null, apiUsername, apiPassword).ConfigureAwait(false);
            if (fr != null && fr.StartsWith("Request did not succeed", StringComparison.Ordinal))
            {
                lastHardError ??= fr;
                continue;
            }

            foreach (var raw in GetJsonArrayElementRawStrings(fr))
            {
                var key = TryGetCloudRowDedupeKey(raw) ?? raw;
                merged[key] = raw;
            }
        }

        if (merged.Count == 0)
            return ("[]", lastHardError);

        return ("[" + string.Join(",", merged.Values) + "]", null);
    }

    private async Task<int> CountCloudFramesAsync(List<object> empty, string? gamesJson, string? apiUsername, string? apiPassword)
    {
        var candidates = GetGameFrameLookupIds(gamesJson);
        if (candidates.Count == 0) return 0;

        var counted = new HashSet<string>(StringComparer.Ordinal);
        foreach (var gid in candidates)
        {
            var fr = await _api.ExecuteRequest(EntityType.Frame, OperationType.Get, empty, gid, null, apiUsername, apiPassword).ConfigureAwait(false);
            if (fr == null || fr.StartsWith("Request did not succeed", StringComparison.Ordinal))
                continue;

            foreach (var raw in GetJsonArrayElementRawStrings(fr))
            {
                var key = TryGetCloudRowDedupeKey(raw) ?? raw;
                counted.Add(key);
            }
        }

        return counted.Count;
    }

    private static string? TryGetCloudRowDedupeKey(string raw)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            int? mobile = null;
            if (root.TryGetProperty("mobileID", out var m) && m.ValueKind == JsonValueKind.Number && m.TryGetInt32(out var mid))
                mobile = mid;

            int? id = null;
            if (root.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.Number && idEl.TryGetInt32(out var iid))
                id = iid;
            else if (root.TryGetProperty("ID", out var idEl2) && idEl2.ValueKind == JsonValueKind.Number && idEl2.TryGetInt32(out var iid2))
                id = iid2;

            if (mobile.HasValue) return $"m:{mobile.Value}";
            if (id.HasValue) return $"i:{id.Value}";
        }
        catch
        {
            // ignore
        }
        return null;
    }

    /// <summary>
    /// Cloud frame queries may key off either the cloud surrogate game id OR the app's mobile game id.
    /// </summary>
    private static List<int> GetGameFrameLookupIds(string? gamesJson)
    {
        var set = new HashSet<int>();
        if (string.IsNullOrWhiteSpace(gamesJson)) return new List<int>();

        try
        {
            using var doc = JsonDocument.Parse(gamesJson);
            var root = doc.RootElement;
            JsonElement arr;
            if (root.ValueKind == JsonValueKind.Array) arr = root;
            else if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array) arr = data;
            else return new List<int>();

            foreach (var el in arr.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object) continue;

                void addProp(string name)
                {
                    if (!el.TryGetProperty(name, out var p)) return;
                    if (p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var v) && v > 0)
                        set.Add(v);
                }

                addProp("id");
                addProp("ID");
                addProp("mobileID");
            }
        }
        catch
        {
            return new List<int>();
        }

        return set.OrderBy(x => x).ToList();
    }

    private static int CountJsonArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return 0;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Array) return root.GetArrayLength();
            if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array) return data.GetArrayLength();
            return 0;
        }
        catch { return 0; }
    }

    /// <summary>Parse JSON array and return each element's raw JSON string (for merging frame arrays).</summary>
    private static List<string> GetJsonArrayElementRawStrings(string? json)
    {
        var list = new List<string>();
        if (string.IsNullOrWhiteSpace(json)) return list;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in root.EnumerateArray()) list.Add(el.GetRawText());
                return list;
            }
            if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
                foreach (var el in data.EnumerateArray()) list.Add(el.GetRawText());
            return list;
        }
        catch { return list; }
    }

    /// <summary>Parse JSON array of objects and return id values (supports "id" or "ID").</summary>
    private static List<int>? GetIdsFromJsonArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            JsonElement arr;
            if (root.ValueKind == JsonValueKind.Array) arr = root;
            else if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array) arr = data;
            else return null;
            var ids = new List<int>();
            foreach (var el in arr.EnumerateArray())
            {
                if (el.TryGetProperty("id", out var p) || el.TryGetProperty("ID", out p))
                {
                    if (p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var id))
                        ids.Add(id);
                }
            }
            return ids;
        }
        catch { return null; }
    }
}
