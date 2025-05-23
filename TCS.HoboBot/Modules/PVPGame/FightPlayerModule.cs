using System.Collections.Concurrent;
using Discord.Interactions;
using Discord.WebSocket;
using TCS.HoboBot.Data;
namespace TCS.HoboBot.Modules.Util;

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
            PlayersWallet.SubtractFromBalance( Context.Guild.Id, Context.User.Id, bill );
            await RespondAsync(
                $"{Context.User.Mention} won the fight against {user.Mention}! \n " +
                $"{user.Mention} must pay **${bill:0.00}** in medical bills."
            );
        }
        else {
            PlayersWallet.SubtractFromBalance( Context.Guild.Id, Context.User.Id, bill );
            await RespondAsync(
                $"{Context.User.Mention} got beat up by {user.Mention} and had to pay **${bill:0.00}** in medical bills."
            );
        }

        // Record next allowed fight time
        NextFight[userId] = now + FightCooldown;
    }
}