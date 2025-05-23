using System.Collections.Concurrent;
using Discord.Interactions;
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
            PlayersWallet.AddToBalance(Context.Guild.Id, userId, 1f );
            await RespondAsync(
                $"You won 1 buck\n" +
                $"{Context.User.Mention} Hobo wallet now holds **${PlayersWallet.GetBalance( Context.Guild.Id, userId ):0.00}**"
            );
        }
    }
}