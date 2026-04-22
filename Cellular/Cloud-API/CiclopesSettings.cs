using System.Reflection;
using System.Text.Json;
using Microsoft.Maui.Storage;

namespace Cellular.Cloud_API;

public static class CiclopesSettings
{
    private const string ApiBaseOverrideKey = "CICLOPES_API_BASE_OVERRIDE";

    private static readonly Lazy<Dictionary<string, string>> _settings = new(LoadSettings);
    private static string? _apiBaseOverride = Preferences.Default.Get<string?>(ApiBaseOverrideKey, null);

    public static string? ApiBase => NormalizeBaseUrl(
        !string.IsNullOrWhiteSpace(_apiBaseOverride) ? _apiBaseOverride : Get("CICLOPES_API_BASE"));

    public static void SetApiBaseOverride(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            _apiBaseOverride = null;
            Preferences.Default.Remove(ApiBaseOverrideKey);
        }
        else
        {
            _apiBaseOverride = value.Trim();
            Preferences.Default.Set(ApiBaseOverrideKey, _apiBaseOverride);
        }
    }

    public static string? Ip => Get("CICLOPES_IP");

    public static int? Port
    {
        get
        {
            var raw = Get("CICLOPES_PORT");
            return int.TryParse(raw, out var port) ? port : null;
        }
    }

    public static string? BaseUrl
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(ApiBase))
            {
                return ApiBase;
            }

            if (string.IsNullOrWhiteSpace(Ip) || Port is null)
            {
                return null;
            }

            return NormalizeBaseUrl($"http://{Ip}:{Port}");
        }
    }

    private static string? Get(string key)
    {
        _settings.Value.TryGetValue(key, out var value);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static Dictionary<string, string> LoadSettings()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("settings.json"));

        if (resourceName is null)
        {
            return new Dictionary<string, string>();
        }

        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();

        return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
               ?? new Dictionary<string, string>();
    }

    private static string? NormalizeBaseUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().TrimEnd('/') + "/";
    }
}
