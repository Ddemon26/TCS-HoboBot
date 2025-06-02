using System.Collections.Concurrent;
using Discord.Interactions;
using Discord.WebSocket;
using TCS.HoboBot.ActionEvents;
using TCS.HoboBot.Data;
using TCS.HoboBot.Guilds;
namespace TCS.HoboBot.Modules.Util;

public class PingModule : InteractionModuleBase<SocketInteractionContext> {
    [SlashCommand( "ping", "Replies with pong and gateway latency." )]
    public async Task PingAsync() {
        int latency = Context.Client.Latency;
        await RespondAsync( $"Pong! 🏓  `{latency} ms`" );
    }
}

public class RollModule : InteractionModuleBase<SocketInteractionContext> {
    static readonly Random Rng = new();

    [SlashCommand( "roll", "Rolls a random number from 1 to the provided max value." )]
    public async Task RollAsync(int max = 100) {
        if ( max < 2 ) {
            await RespondAsync( "The max value must be greater than 2." );
            return;
        }

        int roll = Rng.Next( 1, max + 1 );
        await RespondAsync( $"{Context.User.Mention} rolled a **{roll}**!" );
    }
}

internal record TopHoboCache {
    public List<ulong> LastTopIds { get; set; } = [];
    public Dictionary<ulong, string> CachedNames { get; } = new();
}

public class TopHoboModule : InteractionModuleBase<SocketInteractionContext> {
    //static readonly TopHoboCache Cache = new();
    static readonly ConcurrentDictionary<ulong, TopHoboCache> GuildCaches = new();

    [SlashCommand( "top", "Check the top hobos!" )]
    public async Task TopAsync() {
        await DeferAsync();

        // get or create a per-guild cache
        var guildCache = GuildCaches.GetOrAdd( Context.Guild.Id, _ => new TopHoboCache() );

        ConcurrentDictionary<ulong, PlayerWallet> guildWallets = PlayersWallet.PlayerWallets
            .GetOrAdd( Context.Guild.Id, _ => new ConcurrentDictionary<ulong, PlayerWallet>() );

        List<KeyValuePair<ulong, PlayerWallet>> topKvs = guildWallets
            .OrderByDescending( kv => kv.Value.Cash )
            .Take( 10 )
            .ToList();

        List<ulong> currentIds = topKvs.Select( kv => kv.Key ).ToList();

        if ( !guildCache.LastTopIds.SequenceEqual( currentIds ) ) {
            // fetch new users
            foreach (ulong id in currentIds.Except( guildCache.CachedNames.Keys )) {
                var user = await GuildManager.GetGuildMember( Context.Guild.Id, id );
                guildCache.CachedNames[id] = user?.DisplayName ?? id.ToString();
            }

            // remove stale
            foreach (ulong old in guildCache.CachedNames.Keys.Except( currentIds ).ToList())
                guildCache.CachedNames.Remove( old );

            guildCache.LastTopIds = currentIds;
        }

        IEnumerable<string> lines = topKvs
            .Select( kv => $"{guildCache.CachedNames[kv.Key]}: ${kv.Value.Cash:0.00}" );

        await FollowupAsync( $"Top hobos:\n{string.Join( "\n", lines )}" );
    }
}

public class BalanceModule : InteractionModuleBase<SocketInteractionContext> {
    [SlashCommand( "balance", "Check your balance." )]
    public async Task BalanceAsync(SocketUser? user = null) {
        var targetUser = user ?? Context.User;
        float balance = PlayersWallet.GetBalance( Context.Guild.Id, targetUser.Id );
        await RespondAsync(
            $"{targetUser.GlobalName} has **${balance:0.00}** in their hobo wallet."
        );
    }
}

public class ProstituteModule : InteractionModuleBase<SocketInteractionContext> {
    [SlashCommand( "prostitute", "Prostitute yourself for money!" )]
    public async Task ProstituteAsync() {
        ulong userId = Context.User.Id;
        var now = DateTimeOffset.UtcNow;

        // Cool-down check
        var next = Cooldowns.Get( Context.Guild.Id, userId, CooldownKind.Prostitution );
        if ( now < next ) {
            var remaining = next - now;
            await RespondAsync(
                $"⏳ Easy there, hobo! Try again in **{remaining:mm\\:ss}**.",
                ephemeral: true
            );
            return;
        }

        // ---------------- Roll event ----------------
        (float delta, string story) = ProstitutionEvents.Roll();

        // ---------------- Apply balance (no negatives) ----------------
// apply balance (no negatives)
        if ( delta >= 0f ) {
            PlayersWallet.AddToBalance( Context.Guild.Id, userId, delta );
        }
        else {
            PlayersWallet.SubtractFromBalance( Context.Guild.Id, userId, -delta );
        }

        float newBalance = PlayersWallet.GetBalance( Context.Guild.Id, userId );

        // Record next allowed to beg time
        Cooldowns.Set( Context.Guild.Id, userId, CooldownKind.Prostitution, now + Cooldowns.Cooldown( CooldownKind.Prostitution ) );

        // ---------------- Reply ----------------
        string deltaText = delta switch {
            > 0 => $"(+${delta:0.00})",
            < 0 => $"(-${Math.Abs( delta ):0.00})",
            _ => string.Empty,
        };

        await RespondAsync(
            $"{Context.User.Mention} {story}\n" +
            $"Your hobo wallet now holds **${newBalance:0.00}** {deltaText}"
        );
    }
}

public class BegModule : InteractionModuleBase<SocketInteractionContext> {
    [SlashCommand( "beg", "Hobo-style begging on the streets!" )]
    public async Task BegAsync() {
        ulong userId = Context.User.Id;
        var now = DateTimeOffset.UtcNow;

        // Cool-down check
        var next = Cooldowns.Get( Context.Guild.Id, userId, CooldownKind.Beg );
        if ( now < next ) {
            var remaining = next - now;
            await RespondAsync(
                $"⏳ Easy there, hobo! Try again in **{remaining:mm\\:ss}**.",
                ephemeral: true
            );
            return;
        }

        // ---------------- Roll event ----------------
        (float delta, string story) = BegEvents.Roll();

// In `TCS.HoboBot/Modules/Util/PingModule.cs`, inside BegModule:
        if ( delta >= 0f ) {
            PlayersWallet.AddToBalance( Context.Guild.Id, userId, delta );
        }
        else {
            PlayersWallet.SubtractFromBalance( Context.Guild.Id, userId, -delta );
        }

        float newBalance = PlayersWallet.GetBalance( Context.Guild.Id, userId );

        // Record next allowed to beg time
        Cooldowns.Set( Context.Guild.Id, userId, CooldownKind.Beg, now + Cooldowns.Cooldown( CooldownKind.Beg ) );

        // ---------------- Reply ----------------
        string deltaText = delta switch {
            > 0 => $"(+${delta:0.00})",
            < 0 => $"(-${Math.Abs( delta ):0.00})",
            _ => string.Empty,
        };

        await RespondAsync(
            $"{Context.User.Mention} {story}\n" +
            $"Your hobo wallet now holds **${newBalance:0.00}** {deltaText}"
        );
    }
}