// csharp
using Cellular.Cloud_API.Endpoints;
using Cellular.Cloud_API.Models;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace Cellular.Cloud_API;

public enum EntityType
{
    Ball,
    Establishment,
    Event,
    Frame,
    Game,
    Session,
    Shot,
    CiclopesAggRun,
    CiclopesLaneBallsRun,
    CiclopesFourDBodyRun,
    CiclopesLaneBallsQuery,
    CiclopesFourDBodyQuery
}

public enum OperationType
{
    Get,
    Post,
    Delete
}

/// <summary>Caches JWT <c>tokenA</c> per login (24h); invalidated on 401 or expiry.</summary>
internal static class RevMetrixTokenCache
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, Entry> ByCredential = new(StringComparer.Ordinal);

    private readonly struct Entry(string token, DateTime obtainedUtc)
    {
        public string Token { get; } = token;
        public DateTime ObtainedUtc { get; } = obtainedUtc;
    }

    private static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(24);

    internal static string CredKey(string username, string password) => username + "\xfffd" + password;

    internal static bool TryGet(string key, out string? token)
    {
        lock (Gate)
        {
            if (ByCredential.TryGetValue(key, out var e) && DateTime.UtcNow < e.ObtainedUtc + TokenLifetime)
            {
                token = e.Token;
                return true;
            }

            ByCredential.Remove(key);
            token = null;
            return false;
        }
    }

    internal static void Set(string key, string token)
    {
        lock (Gate) { ByCredential[key] = new Entry(token, DateTime.UtcNow); }
    }

    internal static void Invalidate(string key)
    {
        lock (Gate) { ByCredential.Remove(key); }
    }

    internal static void ClearAll()
    {
        lock (Gate) { ByCredential.Clear(); }
    }
}

public class ApiController
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>Orphan cleanup; uses the same phone login as <see cref="ExecuteRequest"/>.</summary>
    public async Task<string?> DeleteOrphanedAppDataAsync(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            return "Request did not succeed: Missing username or password for Authorize.";

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        string tokenUrl = RevMetrixApi.Posts("Authorize");
        var credentialAttempts = new List<(string Username, string Password)> { (username, password) };

        try
        {
            string deleteUrl = RevMetrixApi.Deletes("DeleteOrphanedAppData");
            for (int authRetry = 0; authRetry < 2; authRetry++)
            {
                var (tokenValue, credKey, _, _) = await AcquireBearerAsync(client, tokenUrl, jsonOptions, credentialAttempts)
                    .ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(tokenValue))
                    return $"Request did not succeed: Unauthorized for auth {tokenUrl}";

                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenValue);
                using HttpResponseMessage response = await client.DeleteAsync(deleteUrl).ConfigureAwait(false);
                var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                Debug.WriteLine(responseBody);
                if (response.StatusCode == HttpStatusCode.Unauthorized && credKey != null && authRetry == 0)
                {
                    RevMetrixTokenCache.Invalidate(credKey);
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    Debug.WriteLine("HTTP Request failed: " + (int)response.StatusCode + " " + response.ReasonPhrase + " for " + deleteUrl);
                    return "Request did not succeed: " + (int)response.StatusCode + " " + response.ReasonPhrase + " for Delete " + deleteUrl;
                }

                return null;
            }

            return "Request did not succeed: 401 Unauthorized for Delete " + deleteUrl;
        }
        catch (TaskCanceledException ex) when (!ex.CancellationToken.IsCancellationRequested)
        {
            Debug.WriteLine("Request timed out: " + ex);
            throw;
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Request failed: " + ex);
            throw;
        }
    }

    public async Task<CiclopesRunResponse?> ExecuteCiclopesRunRequest(CiclopesRunRequest requestData)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
        ApiExecutor executor = new ApiExecutor(EntityType.CiclopesAggRun, OperationType.Post);
        string requestUrl = executor.GetUrl();

        var requestBody = JsonSerializer.Serialize(requestData);
        var content = new StringContent(requestBody, Encoding.UTF8, "application/json");

        HttpResponseMessage response = await client.PostAsync(requestUrl, content).ConfigureAwait(false);

        try
        {
            response.EnsureSuccessStatusCode();
            var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            Debug.WriteLine(responseBody);

            return JsonSerializer.Deserialize<CiclopesRunResponse>(responseBody, JsonOptions);
        }
        catch (HttpRequestException httpEx)
        {
            Debug.WriteLine("HTTP Request failed: " + httpEx);
            throw;
        }
        catch (JsonException jsonEx)
        {
            Debug.WriteLine("JSON parse failed: " + jsonEx);
            throw;
        }
    }
    /// <summary>
    /// Executes the API request and returns the response body as a string.
    /// Optionally invokes onAuthResponse when the authorization response is received.
    /// Phone JWT: <paramref name="username"/> and <paramref name="password"/> are required (signed-in user). No service-account fallback.
    /// <paramref name="getQuery"/> is appended to GET and POST URLs (e.g. <c>mobileID</c> per TestServer.py).
    /// JWT is cached 24h per credential (see <see cref="RevMetrixTokenCache"/>); 401 clears cache and re-authorizes once.
    /// </summary>
    public async Task<string?> ExecuteRequest(
        EntityType entityType,
        OperationType operationType,
        List<Object>? data = null,
        int id = -1,
        Action<string>? onAuthResponse = null,
        string? username = null,
        string? password = null,
        IReadOnlyDictionary<string, string>? getQuery = null
    )
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        ApiExecutor executor = new ApiExecutor(entityType, operationType);

        static string AppendUrlQuery(string url, IReadOnlyDictionary<string, string>? query)
        {
            if (query == null || query.Count == 0) return url;
            var qs = string.Join("&", query
                .Where(kv => !string.IsNullOrWhiteSpace(kv.Key))
                .Select(kv => $"{WebUtility.UrlEncode(kv.Key)}={WebUtility.UrlEncode(kv.Value ?? string.Empty)}"));
            if (string.IsNullOrWhiteSpace(qs)) return url;
            return url + (url.Contains('?', StringComparison.Ordinal) ? "&" : "?") + qs;
        }

        if (operationType == OperationType.Post && (data == null || data.Count == 0))
        {
            Debug.WriteLine("Data was null or empty for POST");
            return null;
        }

        string tokenUrl = RevMetrixApi.Posts("Authorize");

        var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            Debug.WriteLine("Authorize requires phone username and password.");
            return "Request did not succeed: Missing username or password for Authorize.";
        }

        IEnumerable<(string Username, string Password)> distinctAttempts = new[] { (username!, password!) };

        try
        {
            string? tokenValue = null;
            string? credKey = null;
            string? lastAuthBody = null;
            int lastAuthStatus = 0;

            for (int authRound = 0; authRound < 2; authRound++)
            {
                (tokenValue, credKey, lastAuthBody, lastAuthStatus) =
                    await AcquireBearerAsync(client, tokenUrl, jsonOptions, distinctAttempts).ConfigureAwait(false);

                if (string.IsNullOrWhiteSpace(tokenValue))
                {
                    onAuthResponse?.Invoke(lastAuthBody ?? string.Empty);
                    return $"Request did not succeed: {lastAuthStatus} Unauthorized for auth {tokenUrl}";
                }

                onAuthResponse?.Invoke(lastAuthBody ?? string.Empty);
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenValue);

                if (operationType == OperationType.Get)
                {
                    string getUrl = AppendUrlQuery(executor.GetUrl(id), getQuery);
                    HttpResponseMessage response = await client.GetAsync(getUrl).ConfigureAwait(false);
                    var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    Debug.WriteLine(responseBody);
                    if (response.StatusCode == HttpStatusCode.Unauthorized && credKey != null && authRound == 0)
                    {
                        RevMetrixTokenCache.Invalidate(credKey);
                        continue;
                    }

                    if (!response.IsSuccessStatusCode)
                    {
                        Debug.WriteLine("HTTP Request failed: " + (int)response.StatusCode + " " + response.ReasonPhrase + " for " + getUrl);
                        return "Request did not succeed: " + (int)response.StatusCode + " " + response.ReasonPhrase + " for " + operationType + " " + getUrl;
                    }

                    return responseBody;
                }

                if (operationType == OperationType.Delete)
                {
                    string deleteUrl = executor.GetUrl(id);
                    if (data != null && data.Count > 0)
                    {
                        string? lastDeleteOkBody = null;
                        var deleteJsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = null };

                        for (int i = 0; i < data.Count; i++)
                        {
                            for (int delete401Retry = 0; delete401Retry < 2; delete401Retry++)
                            {
                                if (delete401Retry > 0 && credKey != null)
                                {
                                    RevMetrixTokenCache.Invalidate(credKey);
                                    (tokenValue, credKey, _, _) = await AcquireBearerAsync(client, tokenUrl, jsonOptions, distinctAttempts)
                                        .ConfigureAwait(false);
                                    if (string.IsNullOrWhiteSpace(tokenValue))
                                        return $"Request did not succeed: Unauthorized for auth after 401 {tokenUrl}";
                                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenValue);
                                }

                                var one = data[i];
                                var requestBody = JsonSerializer.Serialize(one, one.GetType(), deleteJsonOptions);
                                using var request = new HttpRequestMessage(HttpMethod.Delete, deleteUrl)
                                {
                                    Content = new StringContent(requestBody, Encoding.UTF8, "application/json")
                                };

                                HttpResponseMessage response = await client.SendAsync(request).ConfigureAwait(false);
                                lastDeleteOkBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                                Debug.WriteLine(lastDeleteOkBody);

                                if (response.StatusCode == HttpStatusCode.Unauthorized && credKey != null && delete401Retry == 0)
                                    continue;

                                if (!response.IsSuccessStatusCode)
                                {
                                    Debug.WriteLine("HTTP Request failed: " + (int)response.StatusCode + " " + response.ReasonPhrase + " for " + deleteUrl);
                                    return "Request did not succeed: " + (int)response.StatusCode + " " + response.ReasonPhrase + " for " + operationType + " " + deleteUrl;
                                }

                                break;
                            }
                        }

                        return lastDeleteOkBody;
                    }

                    HttpResponseMessage delResp = await client.DeleteAsync(deleteUrl).ConfigureAwait(false);
                    var delBody = await delResp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    Debug.WriteLine(delBody);
                    if (delResp.StatusCode == HttpStatusCode.Unauthorized && credKey != null && authRound == 0)
                    {
                        RevMetrixTokenCache.Invalidate(credKey);
                        continue;
                    }

                    if (!delResp.IsSuccessStatusCode)
                    {
                        Debug.WriteLine("HTTP Request failed: " + (int)delResp.StatusCode + " " + delResp.ReasonPhrase + " for " + deleteUrl);
                        return "Request did not succeed: " + (int)delResp.StatusCode + " " + delResp.ReasonPhrase + " for " + operationType + " " + deleteUrl;
                    }

                    return delBody;
                }

                // POST: one JSON object per request (TestServer.py); optional ?mobileID= matches phone combined user.
                string? lastOkBody = null;
                for (int i = 0; i < data!.Count; i++)
                {
                    for (int post401Retry = 0; post401Retry < 2; post401Retry++)
                    {
                        if (post401Retry > 0 && credKey != null)
                        {
                            RevMetrixTokenCache.Invalidate(credKey);
                            (tokenValue, credKey, _, _) = await AcquireBearerAsync(client, tokenUrl, jsonOptions, distinctAttempts)
                                .ConfigureAwait(false);
                            if (string.IsNullOrWhiteSpace(tokenValue))
                                return $"Request did not succeed: Unauthorized for auth after 401 {tokenUrl}";
                            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenValue);
                        }

                        var one = data[i];
                        string requestUrl = AppendUrlQuery(executor.GetUrl(id), getQuery);
                        string requestBody = JsonSerializer.Serialize(one, one.GetType(), jsonOptions);
                        using var content = new StringContent(requestBody, Encoding.UTF8, "application/json");
                        HttpResponseMessage response = await client.PostAsync(requestUrl, content).ConfigureAwait(false);
                        lastOkBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        Debug.WriteLine(lastOkBody);

                        if (response.StatusCode == HttpStatusCode.Unauthorized && credKey != null && post401Retry == 0)
                            continue;

                        if (!response.IsSuccessStatusCode)
                        {
                            Debug.WriteLine("HTTP Request failed: " + (int)response.StatusCode + " " + response.ReasonPhrase + " for " + requestUrl);
                            return "Request did not succeed: " + (int)response.StatusCode + " " + response.ReasonPhrase + " for " + operationType + " " + requestUrl;
                        }

                        break;
                    }
                }

                return lastOkBody;
            }

            return $"Request did not succeed: 401 Unauthorized for {operationType} (re-auth retry exhausted)";
        }
        catch (TaskCanceledException ex) when (!ex.CancellationToken.IsCancellationRequested)
        {
            Debug.WriteLine("Request timed out: " + ex);
            throw;
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Request failed: " + ex);
            throw;
        }
    }

    public async Task<LaneBallsRunResponse?> ExecuteLaneBallsRunRequest(CiclopesRunRequest requestData)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
        ApiExecutor executor = new ApiExecutor(EntityType.CiclopesLaneBallsRun, OperationType.Post);
        string requestUrl = executor.GetUrl();

        var requestBody = JsonSerializer.Serialize(requestData);
        var content = new StringContent(requestBody, Encoding.UTF8, "application/json");

        HttpResponseMessage response = await client.PostAsync(requestUrl, content).ConfigureAwait(false);

        try
        {
            response.EnsureSuccessStatusCode();
            var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            Debug.WriteLine(responseBody);

            return JsonSerializer.Deserialize<LaneBallsRunResponse>(responseBody, JsonOptions);
        }
        catch (HttpRequestException httpEx)
        {
            Debug.WriteLine("HTTP Request failed: " + httpEx);
            throw;
        }
        catch (JsonException jsonEx)
        {
            Debug.WriteLine("JSON parse failed: " + jsonEx);
            throw;
        }
    }

    public async Task<FourDBodyRunResponse?> ExecuteFourDBodyRunRequest(CiclopesRunRequest requestData)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
        ApiExecutor executor = new ApiExecutor(EntityType.CiclopesFourDBodyRun, OperationType.Post);
        string requestUrl = executor.GetUrl();

        var requestBody = JsonSerializer.Serialize(requestData);
        var content = new StringContent(requestBody, Encoding.UTF8, "application/json");

        HttpResponseMessage response = await client.PostAsync(requestUrl, content).ConfigureAwait(false);

        try
        {
            response.EnsureSuccessStatusCode();
            var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            Debug.WriteLine(responseBody);

            return JsonSerializer.Deserialize<FourDBodyRunResponse>(responseBody, JsonOptions);
        }
        catch (HttpRequestException httpEx)
        {
            Debug.WriteLine("HTTP Request failed: " + httpEx);
            throw;
        }
        catch (JsonException jsonEx)
        {
            Debug.WriteLine("JSON parse failed: " + jsonEx);
            throw;
        }
    }

    public async Task<LaneBallsQueryResponse?> ExecuteLaneBallsQueryRequest(CiclopesQueryRequest requestData)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
        var executor = new ApiExecutor(EntityType.CiclopesLaneBallsQuery, OperationType.Post);
        var requestUrl = executor.GetUrl();

        var requestBody = JsonSerializer.Serialize(requestData);
        var content = new StringContent(requestBody, Encoding.UTF8, "application/json");

        var response = await client.PostAsync(requestUrl, content).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        Debug.WriteLine(responseBody);
        return JsonSerializer.Deserialize<LaneBallsQueryResponse>(responseBody, JsonOptions);
    }

    public async Task<FourDBodyQueryResponse?> ExecuteFourDBodyQueryRequest(CiclopesQueryRequest requestData)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
        var executor = new ApiExecutor(EntityType.CiclopesFourDBodyQuery, OperationType.Post);
        var requestUrl = executor.GetUrl();

        var requestBody = JsonSerializer.Serialize(requestData);
        var content = new StringContent(requestBody, Encoding.UTF8, "application/json");

        var response = await client.PostAsync(requestUrl, content).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        Debug.WriteLine(responseBody);
        return JsonSerializer.Deserialize<FourDBodyQueryResponse>(responseBody, JsonOptions);
    }

    private static bool TryExtractTokenFromAuthBody(string authResponseBody, out string? tokenValue)
    {
        tokenValue = null;
        try
        {
            using var doc = JsonDocument.Parse(authResponseBody);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return false;
            var root = doc.RootElement;
            if (root.TryGetProperty("tokenA", out var p)) tokenValue = p.GetString();
            else if (root.TryGetProperty("accessToken", out p)) tokenValue = p.GetString();
            else if (root.TryGetProperty("access_token", out p)) tokenValue = p.GetString();
            else if (root.TryGetProperty("data", out p) && p.ValueKind == JsonValueKind.Object && p.TryGetProperty("token", out var q))
                tokenValue = q.GetString();
            return !string.IsNullOrWhiteSpace(tokenValue);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>Returns cached JWT if still within 24h; otherwise POST Authorize and caches result.</summary>
    private static async Task<(string? token, string? credKey, string? lastAuthBody, int lastAuthStatus)> AcquireBearerAsync(
        HttpClient client,
        string tokenUrl,
        JsonSerializerOptions jsonOptions,
        IEnumerable<(string Username, string Password)> credentialAttempts)
    {
        string? lastAuthBody = null;
        int lastAuthStatus = 0;

        foreach (var cred in credentialAttempts)
        {
            var key = RevMetrixTokenCache.CredKey(cred.Username, cred.Password);
            if (RevMetrixTokenCache.TryGet(key, out var cached))
                return (cached, key, null, 200);

            var authData = new { username = cred.Username, password = cred.Password };
            var authBody = JsonSerializer.Serialize(authData, jsonOptions);
            string basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{cred.Username}:{cred.Password}"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basic);

            using var authContent = new StringContent(authBody, Encoding.UTF8, "application/json");
            HttpResponseMessage authResponse = await client.PostAsync(tokenUrl, authContent).ConfigureAwait(false);
            lastAuthBody = await authResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
            lastAuthStatus = (int)authResponse.StatusCode;
            Debug.WriteLine(authResponse);
            Debug.WriteLine(lastAuthBody);

            if (TryExtractTokenFromAuthBody(lastAuthBody, out var tokenValue))
            {
                RevMetrixTokenCache.Set(key, tokenValue!);
                return (tokenValue, key, lastAuthBody, lastAuthStatus);
            }
        }

        return (null, null, lastAuthBody, lastAuthStatus);
    }
}
