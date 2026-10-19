using Clientprefs.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Capabilities;
using Microsoft.Extensions.Logging;

namespace NaiWa;

/// <summary>Per-player volume. Kept behind an interface so the plugin loads without ClientprefsApi.dll.</summary>
public interface IVolumeSource : IDisposable
{
    /// <summary>The player's volume, or null when they have not set one.</summary>
    float? GetVolume(CCSPlayerController player);
}

/// <summary>
/// Reads the shared "Volume" cookie from Clientprefs. This is the only type that references
/// ClientprefsApi, so a missing assembly only fails when this class is first used.
/// </summary>
public sealed class ClientprefsVolume : IVolumeSource
{
    private readonly IClientprefsApi _api;
    private readonly ILogger _logger;
    private int _cookie = -1;

    private ClientprefsVolume(IClientprefsApi api, ILogger logger, bool hotReload)
    {
        _api = api;
        _logger = logger;
        _api.OnDatabaseLoaded += RegisterCookie;
        // The database is already up after a hot reload, so OnDatabaseLoaded will not fire again.
        if (hotReload)
            RegisterCookie();
    }

    /// <summary>Returns null when the Clientprefs plugin is not loaded.</summary>
    public static ClientprefsVolume? TryCreate(ILogger logger, bool hotReload)
    {
        IClientprefsApi? api;
        try
        {
            api = new PluginCapability<IClientprefsApi>("Clientprefs").Get();
        }
        catch (KeyNotFoundException)
        {
            api = null;
        }
        return api == null ? null : new ClientprefsVolume(api, logger, hotReload);
    }

    public float? GetVolume(CCSPlayerController player)
    {
        if (_cookie == -1)
            return null;
        try
        {
            var value = _api.GetPlayerCookie(player, _cookie);
            if (!string.IsNullOrEmpty(value) && float.TryParse(value, out var parsed))
                return parsed;
        }
        catch (Exception)
        {
            // Clientprefs throws when the player is invalid or cookies are not cached yet
        }
        return null;
    }

    public void Dispose() => _api.OnDatabaseLoaded -= RegisterCookie;

    private async void RegisterCookie()
    {
        try
        {
            // Same cookie as the other sound plugins, so one volume setting covers all of them.
            _cookie = await _api.RegPlayerCookieAsync("Volume", "Player volume preference", CookieAccess.CookieAccess_Public);
        }
        catch (Exception e)
        {
            _logger.LogError("Failed to register Volume cookie: {Message}", e.Message);
            return;
        }
        if (_cookie == -1)
            _logger.LogError("Failed to register Volume cookie");
    }
}
