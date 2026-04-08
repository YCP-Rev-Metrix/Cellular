namespace Cellular.Cloud_API;

public static class CiclopesSettings
{
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
        var value = Environment.GetEnvironmentVariable(key);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
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
