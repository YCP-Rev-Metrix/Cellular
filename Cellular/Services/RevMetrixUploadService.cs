using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Cellular.Cloud_API;

namespace Cellular.Services;

/// <summary>
/// Uploads files (video, sensor log) to RevMetrix API (Digital Ocean Space).
/// Uses same authorize + upload flow as the Python script (api.revmetrix.io).
/// </summary>
public class RevMetrixUploadService
{
    // 5 minutes — large video files on a mobile connection can take longer than 60 s
    private readonly HttpClient _client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };

    /// <summary>
    /// Get a Bearer token via POST /api/posts/Authorize with username/password.
    /// </summary>
    public async Task<string?> GetTokenAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        var body = new { username, password };
        var json = JsonSerializer.Serialize(body, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _client.PostAsync(RevMetrixApi.Posts("Authorize"), content, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(responseJson);
        if (doc.RootElement.TryGetProperty("tokenA", out var tokenProp))
            return tokenProp.GetString();
        return null;
    }

    /// <summary>
    /// Upload a file from path. Use for video (app-private path). For log, use the byte[] overload to avoid Android external-storage access.
    /// </summary>
    public async Task<string> UploadFileAsync(string bearerToken, string filePath, string folder, string contentType, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("File not found", filePath);

        using var stream = File.OpenRead(filePath);
        var fileName = Path.GetFileName(filePath);
        return await UploadFileAsync(bearerToken, stream, fileName, folder, contentType, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Upload file from bytes (e.g. log read from app cache). Avoids opening user-chosen paths that may be denied on Android.
    /// </summary>
    public async Task<string> UploadFileAsync(string bearerToken, byte[] fileBytes, string fileName, string folder, string contentType, CancellationToken cancellationToken = default)
    {
        using var stream = new MemoryStream(fileBytes);
        return await UploadFileAsync(bearerToken, stream, fileName, folder, contentType, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> UploadFileAsync(string bearerToken, Stream fileStream, string fileName, string folder, string contentType, CancellationToken cancellationToken = default)
    {
        // Validate the stream has content before sending — avoids a confusing server error
        if (fileStream.CanSeek && fileStream.Length == 0)
            throw new InvalidOperationException($"EMPTY_FILE:{fileName}");

        using var form = new MultipartFormDataContent();
        using var fileContent = new StreamContent(fileStream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(fileContent, "file", fileName);

        var request = new HttpRequestMessage(HttpMethod.Post, $"{RevMetrixApi.Origin}/api/videos/upload?folder={Uri.EscapeDataString(folder)}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        request.Content = form;

        var response = await _client.SendAsync(request, cancellationToken).ConfigureAwait(false);

        // Always read the body before throwing so the API's error detail is visible in the alert
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var detail = string.IsNullOrWhiteSpace(responseBody)
                ? response.ReasonPhrase
                : responseBody;
            throw new HttpRequestException(
                $"{(int)response.StatusCode} {response.ReasonPhrase} — {detail}",
                inner: null,
                statusCode: response.StatusCode);
        }

        using var doc = JsonDocument.Parse(responseBody);
        if (doc.RootElement.TryGetProperty("key", out var keyProp))
            return keyProp.GetString() ?? throw new InvalidOperationException("Upload response missing 'key'");
        throw new InvalidOperationException($"Upload response missing 'key'. Response: {responseBody}");
    }
}
