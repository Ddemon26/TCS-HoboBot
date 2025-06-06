using System.Collections.Concurrent;
using Discord;
using Discord.WebSocket;
using TCS.HoboBot.Services;
namespace TCS.HoboBot;

public static class HoboBotRolesManager {
    // Make the cache field private.
    static readonly ConcurrentDictionary<ulong, Dictionary<HoboBotRoles, ulong>> Cache = new();

    // Public accessor method to return the cache.
    public static ConcurrentDictionary<ulong, Dictionary<HoboBotRoles, ulong>> GetCache()
    {
        return Cache;
    }
    
    public static async Task AddRolesAsync(SocketGuildUser? user, HoboBotRoles key, CancellationToken ct = default) {
        // Remove any other canonical role
        foreach (var otherKey in Enum.GetValues<HoboBotRoles>()) {
            if ( otherKey == key ) {
                continue;
            }

            await RemoveRoleAsync( user, otherKey, ct );
        }

        await AddRoleAsync( user, key, ct );
    }
    static Task AddRoleAsync(SocketGuildUser? user, HoboBotRoles key, CancellationToken ct = default)
        => user != null && Resolve( user.Guild, key ) is { } role && !user.Roles.Contains( role )
            ? user.AddRoleAsync( role, options: new RequestOptions { CancelToken = ct } )
            : Task.CompletedTask;
    static Task RemoveRoleAsync(SocketGuildUser? user, HoboBotRoles key, CancellationToken ct = default)
        => user != null && Resolve( user.Guild, key ) is { } role && user.Roles.Contains( role )
            ? user.RemoveRoleAsync( role, options: new RequestOptions { CancelToken = ct } )
            : Task.CompletedTask;
    /// <summary>Resolve a canonical role without extra API calls.</summary>
    static SocketRole? Resolve(SocketGuild guild, HoboBotRoles key) =>
        Cache.TryGetValue( guild.Id, out Dictionary<HoboBotRoles, ulong>? map ) && map.TryGetValue( key, out ulong id )
            ? guild.GetRole( id )
            : null;
}