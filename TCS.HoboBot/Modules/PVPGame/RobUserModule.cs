using Discord.Interactions;
using Discord.WebSocket;
using TCS.HoboBot.Data;
namespace TCS.HoboBot.Modules.Util;

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

        float targetBalance = PlayersWallet.GetBalance( Context.Guild.Id, target.Id );
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

            PlayersWallet.SubtractFromBalance( Context.Guild.Id, target.Id, amount );
            PlayersWallet.AddToBalance( Context.Guild.Id, Context.User.Id, amount );

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