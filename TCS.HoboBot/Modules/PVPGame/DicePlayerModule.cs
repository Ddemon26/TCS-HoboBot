using Discord.Interactions;
using Discord.WebSocket;
using TCS.HoboBot.Data;
namespace TCS.HoboBot.Modules.Util;

public class DicePlayerModule : InteractionModuleBase<SocketInteractionContext> {
    static readonly Random Rng = new();
    const int MAX = 100;

    [SlashCommand(
        "diceplayer", "Rolls a random number from 1 to 100 " +
                      "and compares it to another user's roll."
    )]
    public async Task MoneyAsync(SocketUser user, float bet) {
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

        int userRoll = Rng.Next( 1, MAX + 1 );
        int opponentRoll = Rng.Next( 1, MAX + 1 );

        if ( userRoll > opponentRoll ) {
            PlayersWallet.AddToBalance( Context.User.Id, bet );
            PlayersWallet.SubtractFromBalance( user.Id, bet );
        }
        else if ( userRoll < opponentRoll ) {
            PlayersWallet.AddToBalance( user.Id, bet );
            PlayersWallet.SubtractFromBalance( Context.User.Id, bet );
        }

        var header = $"🎲 {Context.User.Mention} vs {user.Mention} 🎲";
        var rolls = $"{Context.User.Mention} rolled a **{userRoll}**!\n{user.Mention} rolled a **{opponentRoll}**!";
        string outcome = userRoll > opponentRoll
            ? $"{Context.User.Mention} wins! (+${bet:0.00})"
            : userRoll < opponentRoll
                ? $"{user.Mention} wins! (+${bet:0.00})"
                : "It's a tie!";
        var balances = $"New balances: {Context.User.Mention}: ${PlayersWallet.GetBalance( Context.User.Id ):0.00}, {user.Mention}: ${PlayersWallet.GetBalance( user.Id ):0.00}";

        await RespondAsync(
            "**DICE GAME**\n" +
            $"{header}\n\n" +
            $"{rolls}\n\n" +
            $"{outcome}\n\n" +
            $"{balances}"
        );
    }
}