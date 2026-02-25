namespace Cellular.Cloud_API;

public static class CiclopesSettings
{
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
            if (string.IsNullOrWhiteSpace(Ip) || Port is null)
            {
                return null;
            }

            return $"http://{Ip}:{Port}/";
        }
    }

    private static string? Get(string key)
    {
        var value = Environment.GetEnvironmentVariable(key);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
