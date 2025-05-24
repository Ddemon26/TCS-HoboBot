using Discord.Interactions;
using Discord.WebSocket;
using TCS.HoboBot.Data;

namespace TCS.HoboBot.Modules.Util;

public class DicePlayerModule : InteractionModuleBase<SocketInteractionContext> {
    static readonly Random Rng = new();
    const int MAX = 100;

    [SlashCommand( "diceplayer", "Rolls a random number from 1 to 100 and compares it to another user's roll." )]
    public async Task MoneyAsync(SocketUser opponent, float bet) {
        if ( bet <= 0 ) {
            await RespondAsync( "You must bet some cash!" );
            return;
        }

        ulong guildId = Context.Guild.Id;
        ulong userId = Context.User.Id;
        ulong opponentId = opponent.Id;

        if ( PlayersWallet.GetBalance( guildId, userId ) < bet ) {
            await RespondAsync( $"{Context.User.Mention} doesn't have enough cash!" );
            return;
        }

        if ( PlayersWallet.GetBalance( guildId, opponentId ) < bet ) {
            await RespondAsync( $"{opponent.Mention} doesn't have enough cash!" );
            return;
        }

        int userRoll = Rng.Next( 1, MAX + 1 );
        int opponentRoll = Rng.Next( 1, MAX + 1 );

        if ( userRoll > opponentRoll ) {
            PlayersWallet.AddToBalance( guildId, userId, bet );
            PlayersWallet.SubtractFromBalance( guildId, opponentId, bet );
        }
        else if ( userRoll < opponentRoll ) {
            PlayersWallet.AddToBalance( guildId, opponentId, bet );
            PlayersWallet.SubtractFromBalance( guildId, userId, bet );
        }

        var header = $"🎲 {Context.User.Mention} vs {opponent.Mention} 🎲";
        var rolls = $"{Context.User.Mention} rolled a **{userRoll}**!\n{opponent.Mention} rolled a **{opponentRoll}**!";
        string outcome = userRoll > opponentRoll
            ? $"{Context.User.Mention} wins! (+${bet:0.00})"
            : userRoll < opponentRoll
                ? $"{opponent.Mention} wins! (+${bet:0.00})"
                : "It's a tie!";
        var balances = $"New balances: {Context.User.Mention}: ${PlayersWallet.GetBalance( guildId, userId ):0.00}, {opponent.Mention}: ${PlayersWallet.GetBalance( guildId, opponentId ):0.00}";

        await RespondAsync( $"**DICE GAME**\n{header}\n\n{rolls}\n\n{outcome}\n\n{balances}" );
    }
}