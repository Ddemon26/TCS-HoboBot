using System.Collections.Concurrent;
using Discord.Interactions;
using Discord.WebSocket;
using TCS.HoboBot.ActionEvents;
using TCS.HoboBot.Data;
namespace TCS.HoboBot.Modules.Util;

public class SpinWheelModule : InteractionModuleBase<SocketInteractionContext> {
    public static ConcurrentDictionary<ulong, DateTimeOffset> NextSpin = new();
    public static readonly TimeSpan SpinCooldown = TimeSpan.FromSeconds( 3 );
    static readonly Random Rng = new();
    const ulong SNEAKY_USER = 268654531452207105;

    [SlashCommand( "spin", "Spins the wheel!" )]
    public async Task SpinAsync() {
        ulong userId = Context.User.Id;
        var now = DateTimeOffset.UtcNow;

        if ( NextSpin.TryGetValue( userId, out var next ) && now < next ) {
            var remaining = next - now;
            await RespondAsync(
                $"⏳ You need to wait **{remaining:mm\\:ss}** before spinning again.",
                ephemeral: true
            );
            return;
        }

        NextSpin[userId] = now + SpinCooldown;

        int roll = Rng.Next( 100 ); // 0-99
        if ( roll < 90 ) {
            string message = Context.User.Id == SNEAKY_USER
                ? "Better luck next time, loser!"
                : $"{Context.User.Mention} Is gay!";
            await RespondAsync( message );
        }
        else {
            PlayersWallet.AddToBalance( userId, 10f );
            await RespondAsync(
                $"You won 10 bucks\n" +
                $"{Context.User.Mention} Hobo wallet now holds **${PlayersWallet.GetBalance( userId ):0.00}**"
            );
        }
    }
}

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

public class DicePlayerModule : InteractionModuleBase<SocketInteractionContext> {
    static readonly Random Rng = new();

    [SlashCommand( "diceplayer", "Rolls a random number from 1 to the provided max value and compares it to another user's roll." )]
    public async Task MoneyAsync(SocketUser user, float bet = 0, int max = 100) {
        if ( max < 2 ) {
            await RespondAsync( "The max value must be greater than 2." );
            return;
        }

        // if cash is 0 or fewer, let's return
        if ( bet <= 0 ) {
            await RespondAsync( "You must bet some cash!" );
            return;
        }

        // Check if the user has enough cash
        if ( PlayersWallet.GetBalance( Context.User.Id ) < bet ) {
            await RespondAsync( $"{Context.User.Mention} doesn't have enough cash!" );
            return;
        }

        // check if the opponent has enough cash
        if ( PlayersWallet.GetBalance( user.Id ) < bet ) {
            await RespondAsync( $"{user.Mention} doesn't have enough cash!" );
            return;
        }

        int userRoll = Rng.Next( 1, max + 1 );
        int opponentRoll = Rng.Next( 1, max + 1 );

        if ( userRoll > opponentRoll ) {
            PlayersWallet.AddToBalance( Context.User.Id, bet );
            PlayersWallet.SubtractFromBalance( user.Id, bet );
        }
        else if ( userRoll < opponentRoll ) {
            PlayersWallet.AddToBalance( user.Id, bet );
            PlayersWallet.SubtractFromBalance( Context.User.Id, bet );
        }

        string result = userRoll > opponentRoll
            ? $"{Context.User.Mention} wins! (+${bet:0.00})\nNew balances: {Context.User.Mention}: ${PlayersWallet.GetBalance( Context.User.Id ):0.00}, {user.Mention}: ${PlayersWallet.GetBalance( user.Id ):0.00}"
            : userRoll < opponentRoll
                ? $"{user.Mention} wins! (+${bet:0.00})\nNew balances: {Context.User.Mention}: ${PlayersWallet.GetBalance( Context.User.Id ):0.00}, {user.Mention}: ${PlayersWallet.GetBalance( user.Id ):0.00}"
                : "It's a tie!";

        await RespondAsync(
            $"{Context.User.Mention} rolled a **{userRoll}**" +
            $" against {user.Mention}'s **{opponentRoll}**! \n" +
            result
        );
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
        (float delta, string story) = PrositutionEvents.Roll();

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

public class RobUserModule : InteractionModuleBase<SocketInteractionContext> {
    static readonly Random Rng = new();

    [SlashCommand( "rob", "Attempt to rob a user (max 30% of their total, rare chance)." )]
    public async Task RobAsync(SocketUser target) {
        ulong userId = Context.User.Id;
        var now = DateTimeOffset.UtcNow;
        // Check if the user can call the command
        if ( PlayersWallet.NextRob.TryGetValue( userId, out var next ) && now < next ) {
            var remaining = next - now;
            await RespondAsync( $"⏳ You need to wait **{remaining:mm\\:ss}** before robbing again.", ephemeral: true );
            return;
        }

        // Record the next allowed call time.
        PlayersWallet.NextRob[userId] = now + PlayersWallet.RobCooldown;

        // Cannot rob yourself.
        if ( target.Id == Context.User.Id ) {
            await RespondAsync( "You cannot rob yourself!" );
            return;
        }

        float targetBalance = PlayersWallet.GetBalance( target.Id );
        if ( targetBalance <= 0 ) {
            await RespondAsync( $"{target.Mention} has no cash to steal!" );
            return;
        }

        // Attempt the robbery: 10% chance of success.
        var chance = 0.10;
        if ( Rng.NextDouble() < chance ) {
            // Determine a random amount to steal: between 10% and 30% of the target’s balance.
            float minSteal = targetBalance * 0.10f;
            float maxSteal = targetBalance * 0.30f;
            float amount = minSteal + (float)Rng.NextDouble() * (maxSteal - minSteal);

            PlayersWallet.SubtractFromBalance( target.Id, amount );
            PlayersWallet.AddToBalance( Context.User.Id, amount );

            await RespondAsync(
                $"{Context.User.Mention} successfully robbed {target.Mention} for **${amount:0.00}**!"
            );
        }
        else {
            await RespondAsync(
                $"{Context.User.Mention} attempted to rob {target.Mention} but got caught!"
            );
        }
    }
}

public class FightPlayerModule : InteractionModuleBase<SocketInteractionContext> {
    public static ConcurrentDictionary<ulong, DateTimeOffset> NextFight = new();
    public static readonly TimeSpan FightCooldown = TimeSpan.FromMinutes( 10 );
    static readonly Random Rng = new();

    [SlashCommand( "fight", "Fight another user for money!" )]
    public async Task FightAsync(SocketUser user) {
        ulong userId = Context.User.Id;
        var now = DateTimeOffset.UtcNow;

        // Cool-down check
        if ( NextFight.TryGetValue( userId, out var allowedAt ) && now < allowedAt ) {
            var remaining = allowedAt - now;
            await RespondAsync(
                $"⏳ You need to wait **{remaining:mm\\:ss}** before fighting again.",
                ephemeral: true
            );
            return;
        }

        // Fight logic
        bool win = Rng.NextDouble() < 0.5;
        float bill = Rng.Next( 5, 20 );

        if ( win ) {
            PlayersWallet.SubtractFromBalance( user.Id, bill );
            await RespondAsync(
                $"{Context.User.Mention} won the fight against {user.Mention}! \n " +
                $"{user.Mention} must pay **${bill:0.00}** in medical bills."
            );
        }
        else {
            PlayersWallet.SubtractFromBalance( Context.User.Id, bill );
            await RespondAsync(
                $"{Context.User.Mention} got beat up by {user.Mention} and had to pay **${bill:0.00}** in medical bills."
            );
        }

        // Record next allowed fight time
        NextFight[userId] = now + FightCooldown;
    }
}