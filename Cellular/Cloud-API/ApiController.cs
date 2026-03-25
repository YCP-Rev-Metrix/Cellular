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
    Shot
}

public enum OperationType
{
    Get,
    Post,
    Delete
}

public class ApiController
{
    /// <summary>
    /// Executes the API request and returns the response body as a string.
    /// Optionally invokes onAuthResponse when the authorization response is received.
    /// When username/password are provided, they are used for auth; otherwise placeholder credentials are used.
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

        if (operationType == OperationType.Post && (data == null || data.Count == 0))
        {
            Debug.WriteLine("Data was null or empty for POST");
            return null;
        }

        string tokenUrl = RevMetrixApi.Posts("Authorize");

        var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        try
        {
            // Try provided credentials first, then fall back to known defaults.
            var credentialAttempts = new List<(string Username, string Password)>();
            if (!string.IsNullOrWhiteSpace(username) && !string.IsNullOrWhiteSpace(password))
                credentialAttempts.Add((username!, password!));
            credentialAttempts.Add(("Guest", "Guest"));
            credentialAttempts.Add(("string", "string"));

            string? tokenValue = null;
            string? lastAuthBody = null;
            int lastAuthStatus = 0;
            foreach (var cred in credentialAttempts.Distinct())
            {
                var authData = new { username = cred.Username, password = cred.Password };
                var authBody = JsonSerializer.Serialize(authData, jsonOptions);
                string basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{cred.Username}:{cred.Password}"));
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basic);

                using var authContent = new StringContent(authBody, Encoding.UTF8, "application/json");
                HttpResponseMessage authResponse = await client
                    .PostAsync(tokenUrl, authContent)
                    .ConfigureAwait(false);
                var authResponseBody = await authResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
                lastAuthBody = authResponseBody;
                lastAuthStatus = (int)authResponse.StatusCode;
                Debug.WriteLine(authResponse);
                Debug.WriteLine(authResponseBody);

                try
                {
                    using var doc = JsonDocument.Parse(authResponseBody);
                    if (doc.RootElement.ValueKind == JsonValueKind.Object)
                    {
                        var root = doc.RootElement;
                        if (root.TryGetProperty("tokenA", out var p)) tokenValue = p.GetString();
                        else if (root.TryGetProperty("accessToken", out p)) tokenValue = p.GetString();
                        else if (root.TryGetProperty("access_token", out p)) tokenValue = p.GetString();
                        else if (root.TryGetProperty("data", out p) && p.ValueKind == JsonValueKind.Object && p.TryGetProperty("token", out var q))
                            tokenValue = q.GetString();
                    }
                }
                catch (JsonException) { }

                if (!string.IsNullOrWhiteSpace(tokenValue))
                {
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenValue);
                    break;
                }
            }

            onAuthResponse?.Invoke(lastAuthBody ?? string.Empty);
            if (string.IsNullOrWhiteSpace(tokenValue))
                return $"Request did not succeed: {lastAuthStatus} Unauthorized for auth {tokenUrl}";

            if (operationType == OperationType.Get)
            {
                string getUrl = executor.GetUrl(id);
                if (getQuery != null && getQuery.Count > 0)
                {
                    var qs = string.Join("&", getQuery
                        .Where(kv => !string.IsNullOrWhiteSpace(kv.Key))
                        .Select(kv => $"{WebUtility.UrlEncode(kv.Key)}={WebUtility.UrlEncode(kv.Value ?? string.Empty)}"));
                    if (!string.IsNullOrWhiteSpace(qs))
                        getUrl += (getUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?") + qs;
                }
                HttpResponseMessage response = await client.GetAsync(getUrl).ConfigureAwait(false);
                var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                Debug.WriteLine(responseBody);
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
                // TestServer.py / production: DELETE with JSON body { "username": "<login>", "mobileID": <app user id> }.
                // Use default property names (anonymous types use lowercase username + mobileID) so keys stay mobileID not mobileId.
                if (data != null && data.Count > 0)
                {
                    string? lastDeleteOkBody = null;
                    var deleteJsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = null };

                    for (int i = 0; i < data.Count; i++)
                    {
                        var one = data[i];
                        var requestBody = JsonSerializer.Serialize(one, one.GetType(), deleteJsonOptions);
                        using var request = new HttpRequestMessage(HttpMethod.Delete, deleteUrl)
                        {
                            Content = new StringContent(requestBody, Encoding.UTF8, "application/json")
                        };

                        HttpResponseMessage response = await client.SendAsync(request).ConfigureAwait(false);
                        lastDeleteOkBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        Debug.WriteLine(lastDeleteOkBody);

                        if (!response.IsSuccessStatusCode)
                        {
                            Debug.WriteLine("HTTP Request failed: " + (int)response.StatusCode + " " + response.ReasonPhrase + " for " + deleteUrl);
                            return "Request did not succeed: " + (int)response.StatusCode + " " + response.ReasonPhrase + " for " + operationType + " " + deleteUrl;
                        }
                    }

                    return lastDeleteOkBody;
                }
                else
                {
                    HttpResponseMessage response = await client.DeleteAsync(deleteUrl).ConfigureAwait(false);
                    var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    Debug.WriteLine(responseBody);
                    if (!response.IsSuccessStatusCode)
                    {
                        Debug.WriteLine("HTTP Request failed: " + (int)response.StatusCode + " " + response.ReasonPhrase + " for " + deleteUrl);
                        return "Request did not succeed: " + (int)response.StatusCode + " " + response.ReasonPhrase + " for " + operationType + " " + deleteUrl;
                    }
                    return responseBody;
                }
            }

            // POST: server expects one JSON object per request (per TestServer.py)
            string? lastOkBody = null;
            for (int i = 0; i < data!.Count; i++)
            {
                var one = data[i];
                string requestUrl = executor.GetUrl(id);
                string requestBody = JsonSerializer.Serialize(one, one.GetType(), jsonOptions);
                using var content = new StringContent(requestBody, Encoding.UTF8, "application/json");
                HttpResponseMessage response = await client.PostAsync(requestUrl, content).ConfigureAwait(false);
                lastOkBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                Debug.WriteLine(lastOkBody);
                if (!response.IsSuccessStatusCode)
                {
                    Debug.WriteLine("HTTP Request failed: " + (int)response.StatusCode + " " + response.ReasonPhrase + " for " + requestUrl);
                    return "Request did not succeed: " + (int)response.StatusCode + " " + response.ReasonPhrase + " for " + operationType + " " + requestUrl;
                }
            }
            return lastOkBody;
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
}
