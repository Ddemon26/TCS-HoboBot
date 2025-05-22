using System.Collections.Concurrent;
using Discord;
using Discord.WebSocket;
using TCS.HoboBot.Modules.Moderation;
namespace TCS.HoboBot;

public static class HoboRolesHandler {
    public static ConcurrentDictionary<ulong, Dictionary<DealerRole, ulong>> Cache = new();
    public static async Task AddRolesAsync(SocketGuildUser? user, DealerRole key, CancellationToken ct = default) {
        // Remove any other canonical role
        foreach (var otherKey in Enum.GetValues<DealerRole>()) {
            if ( otherKey == key ) {
                continue;
            }

            await RemoveRoleAsync( user, otherKey, ct );
        }

        await AddRoleAsync( user, key, ct );
    }
    static Task AddRoleAsync(SocketGuildUser? user, DealerRole key, CancellationToken ct = default)
        => user != null && Resolve( user.Guild, key ) is { } role && !user.Roles.Contains( role )
            ? user.AddRoleAsync( role, options: new RequestOptions { CancelToken = ct } )
            : Task.CompletedTask;
    static Task RemoveRoleAsync(SocketGuildUser? user, DealerRole key, CancellationToken ct = default)
        => user != null && Resolve( user.Guild, key ) is { } role && user.Roles.Contains( role )
            ? user.RemoveRoleAsync( role, options: new RequestOptions { CancelToken = ct } )
            : Task.CompletedTask;
    /// <summary>Resolve a canonical role without extra API calls.</summary>
    static SocketRole? Resolve(SocketGuild guild, DealerRole key) =>
        Cache.TryGetValue( guild.Id, out Dictionary<DealerRole, ulong>? map ) && map.TryGetValue( key, out ulong id )
            ? guild.GetRole( id )
            : null;
}