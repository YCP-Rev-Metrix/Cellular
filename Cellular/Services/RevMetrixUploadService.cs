using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Cellular.Services;

/// <summary>
/// Uploads files (video, sensor log) to RevMetrix API (Digital Ocean Space).
/// Uses same authorize + upload flow as the Python script (api.revmetrix.io).
/// </summary>
public class RevMetrixUploadService
{
    private const string BaseUrl = "https://api.revmetrix.io";
    private readonly HttpClient _client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

    /// <summary>
    /// Get a Bearer token via POST /api/posts/Authorize with username/password.
    /// </summary>
    public async Task<string?> GetTokenAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        var body = new { username, password };
        var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        var response = await _client.PostAsync($"{BaseUrl}/api/posts/Authorize", content, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
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
        using var form = new MultipartFormDataContent();
        using var fileContent = new StreamContent(fileStream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(fileContent, "file", fileName);

        var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/api/videos/upload?folder={Uri.EscapeDataString(folder)}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        request.Content = form;

        var response = await _client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("key", out var keyProp))
            return keyProp.GetString() ?? throw new InvalidOperationException("Upload response missing 'key'");
        throw new InvalidOperationException("Upload response missing 'key'");
    }
}
