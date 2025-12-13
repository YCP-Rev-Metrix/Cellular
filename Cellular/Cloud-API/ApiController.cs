// csharp
using Cellular.Cloud_API.Endpoints;
using Cellular.Cloud_API.Models;
using System.Diagnostics;
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
    Post
}

public class ApiController
{
    /// <summary>
    /// Executes the API request and returns the authorization response body as a string.
    /// Optionally invokes onAuthResponse when the authorization response is received.
    /// </summary>
    public async Task<string?> ExecuteRequest(
        EntityType entityType,
        OperationType operationType,
        List<Object>? data = null,
        int id = -1,
        Action<string>? onAuthResponse = null
    )
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        ApiExecutor executor = new ApiExecutor(entityType, operationType);

        string requestUrl = executor.GetUrl();

        if (data == null)
        {
            Debug.WriteLine("Data was null");
            return null;
        }

        var requestBody = JsonSerializer.Serialize(data);
        var content = new StringContent(requestBody, Encoding.UTF8, "application/json");

        // First get validation token
        string tokenUrl = "https://api.revmetrix.io/api/posts/Authorize";

        var authData = new {
            username = "string",
            password = "string"
        };

        var authBody = JsonSerializer.Serialize(authData);

        // Use explicit username/password and UTF8 for Basic auth
        string username = "string";
        string password = "string";
        string authenticationString = $"{username}:{password}";
        string base64EncodedAuthenticationString = Convert.ToBase64String(Encoding.UTF8.GetBytes(authenticationString));

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", base64EncodedAuthenticationString);

        try
        {
            HttpResponseMessage authResponse = await client
                .PostAsync(tokenUrl, new StringContent(authBody, Encoding.UTF8, "application/json"))
                .ConfigureAwait(false);

            Debug.WriteLine(authResponse);

            // Read the authorization response body
            var authResponseBody = await authResponse.Content.ReadAsStringAsync().ConfigureAwait(false);

            Debug.WriteLine(authResponseBody);
            onAuthResponse?.Invoke(authResponseBody);

            // Try to extract a token from common JSON fields and set Authorization header
            string? tokenValue = null;
            try
            {
                using var doc = JsonDocument.Parse(authResponseBody);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    JsonElement root = doc.RootElement;
                    if (root.TryGetProperty("tokenA", out var p)) tokenValue = p.GetString();
                    else if (root.TryGetProperty("accessToken", out p)) tokenValue = p.GetString();
                    else if (root.TryGetProperty("access_token", out p)) tokenValue = p.GetString();
                    else if (root.TryGetProperty("data", out p) && p.ValueKind == JsonValueKind.Object && p.TryGetProperty("token", out var q))
                        tokenValue = q.GetString();
                }
            }
            catch (JsonException) { /* ignore parse errors and proceed without token */ }

            if (!string.IsNullOrEmpty(tokenValue))
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenValue);
            }
            // If no tokenValue, the Basic header set earlier remains and will be sent with the next request

            // Send original request with Authorization header (if present)
            HttpResponseMessage response;
            if (operationType == OperationType.Get)
            {
                response = await client.GetAsync(requestUrl).ConfigureAwait(false);
            }
            else
            {
                response = await client.PostAsync(requestUrl, content).ConfigureAwait(false);
            }

            try
            {
                response.EnsureSuccessStatusCode();
                            var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                
                            Debug.WriteLine(responseBody);
                
                            return responseBody;
            } catch(HttpRequestException httpEx)
            {
                Debug.WriteLine("HTTP Request failed: " + httpEx);
                return "Request did not succeed: " + httpEx.Message;
            }
            
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
