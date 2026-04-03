namespace Cellular.Cloud_API;

/// <summary>
/// Layout matches TestServer.py / production: one origin (no trailing slash), all routes under <c>/api</c>.
/// Authenticated calls use <c>Authorization: Bearer</c> with JWT from <c>POST /api/posts/Authorize</c> response field <c>tokenA</c>.
/// Mobile sync POSTs should also send <c>?mobileID=</c> (app user PK) per TestServer.py / production WebApi.
/// <para>
/// HttpClient uses default TLS certificate validation. Do not ship handlers that disable verification;
/// use a local base URL only for dev with trusted certs or platform debug settings.
/// </para>
/// </summary>
public static class RevMetrixApi
{
    /// <summary>Production host; no trailing slash.</summary>
    public const string Origin = "https://api.revmetrix.io";

    public const string ApiPathPrefix = "/api";

    /// <summary><c>https://api.revmetrix.io/api</c> — no trailing slash.</summary>
    public static string ApiRoot => Origin.TrimEnd('/') + ApiPathPrefix;

    public static string Posts(string action) => $"{ApiRoot}/posts/{action}";
    public static string Gets(string action) => $"{ApiRoot}/gets/{action}";
    public static string Deletes(string action) => $"{ApiRoot}/deletes/{action}";
}
