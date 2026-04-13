using System.Reflection;
using System.Text.Json;

namespace Cellular.Cloud_API;

public static class CiclopesSettings
{
    private static readonly Lazy<Dictionary<string, string>> _settings = new(LoadSettings);

    public static string? ApiBase => NormalizeBaseUrl(Get("CICLOPES_API_BASE"));

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
