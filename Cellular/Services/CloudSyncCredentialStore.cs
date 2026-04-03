using Cellular.Cloud_API;
using Microsoft.Maui.Storage;

namespace Cellular.Services;

/// <summary>
/// Stores the phone user's plain password for RevMetrix JWT (Authorize). Required because only a bcrypt hash is kept in SQLite.
/// Cleared on sign-out. Service <c>string</c>/<c>string</c> auth is only used for PostUserApp bootstrap in <see cref="CloudSyncService"/>, not for tokens here.
/// </summary>
public static class CloudSyncCredentialStore
{
    private const string UserKey = "RevMetrixPhoneUsername";
    private const string PassKey = "RevMetrixPhonePassword";

    public static async Task StoreAsync(string username, string password)
    {
        await SecureStorage.Default.SetAsync(UserKey, username).ConfigureAwait(false);
        await SecureStorage.Default.SetAsync(PassKey, password).ConfigureAwait(false);
    }

    public static async Task<(bool Ok, string? Username, string? Password)> TryGetAsync()
    {
        var u = await SecureStorage.Default.GetAsync(UserKey).ConfigureAwait(false);
        var p = await SecureStorage.Default.GetAsync(PassKey).ConfigureAwait(false);
        if (string.IsNullOrEmpty(u) || string.IsNullOrEmpty(p))
            return (false, null, null);
        return (true, u, p);
    }

    public static Task ClearAsync()
    {
        RevMetrixTokenCache.ClearAll();
        try
        {
            SecureStorage.Default.Remove(UserKey);
            SecureStorage.Default.Remove(PassKey);
        }
        catch
        {
            /* key may be absent */
        }

        return Task.CompletedTask;
    }
}
