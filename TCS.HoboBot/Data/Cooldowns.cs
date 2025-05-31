using System.Collections.Concurrent;
namespace TCS.HoboBot.Data;

public enum CooldownKind {
    Beg, 
    Job, 
    Rob,
    Prostitution, 
    Versus,
}    
public static class Cooldowns {
    // Outer key = guild, inner key = user, value = next-allowed time
    static readonly ConcurrentDictionary<
        CooldownKind,
        ConcurrentDictionary<ulong, ConcurrentDictionary<ulong, DateTimeOffset>>
    > Global = new();

    static readonly IReadOnlyDictionary<CooldownKind, TimeSpan> Durations =
        new Dictionary<CooldownKind, TimeSpan> {
            [CooldownKind.Beg] = TimeSpan.FromSeconds( 5 ),
            [CooldownKind.Job] = TimeSpan.FromMinutes( 10 ),
            [CooldownKind.Rob] = TimeSpan.FromMinutes( 10 ),
            [CooldownKind.Prostitution] = TimeSpan.FromMinutes( 30 ),
            [CooldownKind.Versus] = TimeSpan.FromMinutes( 5 ),
        };

    public static DateTimeOffset Get(ulong guildId, ulong userId, CooldownKind kind) {
        if ( Global.TryGetValue( kind, out ConcurrentDictionary<ulong, ConcurrentDictionary<ulong, DateTimeOffset>>? perGuild ) &&
             perGuild.TryGetValue( guildId, out ConcurrentDictionary<ulong, DateTimeOffset>? perUser ) &&
             perUser.TryGetValue( userId, out var t ) ) {
            return t;
        }

        return DateTimeOffset.MinValue;
    }

    public static void Set(ulong guildId, ulong userId, CooldownKind kind, DateTimeOffset next) {
        ConcurrentDictionary<ulong, DateTimeOffset> perUser = Global
            .GetOrAdd( kind, _ => new ConcurrentDictionary<ulong, ConcurrentDictionary<ulong, DateTimeOffset>>() )
            .GetOrAdd( guildId, _ => new ConcurrentDictionary<ulong, DateTimeOffset>() );

        perUser[userId] = next;
    }
    
    public static TimeSpan Cooldown(CooldownKind kind) => Durations[kind];
}