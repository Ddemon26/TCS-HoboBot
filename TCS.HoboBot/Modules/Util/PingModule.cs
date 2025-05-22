using System.Collections.Concurrent;
using Discord.Interactions;
using Discord.WebSocket;
using TCS.HoboBot.ActionEvents;
using TCS.HoboBot.Data;
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

public class TopHoboModule : InteractionModuleBase<SocketInteractionContext> {
    [SlashCommand( "top", "Check the top hobos!" )]
    public async Task TopAsync() {
        IEnumerable<Task<string>> topHoboTasks = PlayersWallet.Cash
            .OrderByDescending( kv => kv.Value )
            .Take( 10 )
            .Select( async kv => {
                    var user = await Context.Client.Rest.GetUserAsync( kv.Key );
                    string globalName = user?.GlobalName ?? kv.Key.ToString(); // this sometimes returns null
                    return $"{globalName}: ${kv.Value:0.00}";
                }
            );

        string[] topHobos = await Task.WhenAll( topHoboTasks );
        string result = string.Join( "\n", topHobos );
        await RespondAsync( $"Top hobos:\n{result}" );
    }
}

public class BalanceModule : InteractionModuleBase<SocketInteractionContext> {
    [SlashCommand( "balance", "Check your balance." )]
    public async Task BalanceAsync(SocketUser? user = null) {
        var targetUser = user ?? Context.User;
        float balance = PlayersWallet.GetBalance( targetUser.Id );
        await RespondAsync(
            $"{targetUser.GlobalName} has **${balance:0.00}** in their hobo wallet."
        );
    }
}

public class ProstituteModule : InteractionModuleBase<SocketInteractionContext> {
    public static ConcurrentDictionary<ulong, DateTimeOffset> NextProstitution = new();
    public static readonly TimeSpan ProstitutionCooldown = TimeSpan.FromMinutes( 30 );
    [SlashCommand( "prostitute", "Prostitute yourself for money!" )]
    public async Task ProstituteAsync() {
        ulong userId = Context.User.Id;
        var now = DateTimeOffset.UtcNow;

        // Cool-down check
        if ( NextProstitution.TryGetValue( userId, out var next ) && now < next ) {
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
        float newBalance = PlayersWallet.Cash.AddOrUpdate(
            userId,
            Math.Max( 0f, delta ), // first beg ever
            (_, old) => MathF.Max( 0f, old + delta )
        ); // never below 0

        // Record next allowed to beg time
        NextProstitution[userId] = now + ProstitutionCooldown;

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
        if ( PlayersWallet.NextBeg.TryGetValue( userId, out var next ) && now < next ) {
            var remaining = next - now;
            await RespondAsync(
                $"⏳ Easy there, hobo! Try again in **{remaining:mm\\:ss}**.",
                ephemeral: true
            );
            return;
        }

        // ---------------- Roll event ----------------
        (float delta, string story) = BegEvents.Roll();

        // ---------------- Apply balance (no negatives) ----------------
        float newBalance = PlayersWallet.Cash.AddOrUpdate(
            userId,
            Math.Max( 0f, delta ), // first beg ever
            (_, old) => MathF.Max( 0f, old + delta )
        ); // never below 0

        // Record next allowed to beg time
        PlayersWallet.NextBeg[userId] = now + PlayersWallet.BegCooldown;

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

public class WorkModule : InteractionModuleBase<SocketInteractionContext> {
    [SlashCommand( "work", "Work hard for your money!" )]
    public async Task WorkAsync() {
        ulong userId = Context.User.Id;
        var now = DateTimeOffset.UtcNow;

        // Cool-down check
        if ( PlayersWallet.NextJob.TryGetValue( userId, out var next ) && now < next ) {
            var remaining = next - now;
            await RespondAsync(
                $"⏳ Easy there, hobo! Try again in **{remaining:mm\\:ss}**.",
                ephemeral: true
            );
            return;
        }

        // ---------------- Roll event ----------------
        (float delta, string story) = WorkEvents.Roll();

        // ---------------- Apply balance (no negatives) ----------------
        float newBalance = PlayersWallet.Cash.AddOrUpdate(
            userId,
            Math.Max( 0f, delta ), // first beg ever
            (_, old) => MathF.Max( 0f, old + delta )
        ); // never below 0

        // Record next allowed to beg time
        PlayersWallet.NextJob[userId] = now + PlayersWallet.JobCooldown;

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

/*// offer these cheeseburgers, works just like working
public class OfferModule : InteractionModuleBase<SocketInteractionContext> {
    [SlashCommand( "offer", "Offer these cheeseburgers!" )]
    public async Task OfferAsync() {
        ulong userId = Context.User.Id;
        var now = DateTimeOffset.UtcNow;

        // Cool-down check
        if ( PlayersWallet.NextJob.TryGetValue( userId, out var next ) && now < next ) {
            var remaining = next - now;
            await RespondAsync(
                $"⏳ Easy there, hobo! Try again in **{remaining:mm\\:ss}**.",
                ephemeral: true
            );
            return;
        }

        // ---------------- Roll event ----------------
        (float delta, string story) = WorkEvents.Roll();

        // ---------------- Apply balance (no negatives) ----------------
        float newBalance = PlayersWallet.Cash.AddOrUpdate(
            userId,
            Math.Max( 0f, delta ), // first beg ever
            (_, old) => MathF.Max( 0f, old + delta )
        ); // never below 0

        // Record next allowed to beg time
        PlayersWallet.NextJob[userId] = now + PlayersWallet.JobCooldown;

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
}*/