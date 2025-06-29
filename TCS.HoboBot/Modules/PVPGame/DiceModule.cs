using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using System;
using System.Threading.Tasks;

namespace TCS.HoboBot.Modules.Util;

public class DiceModule : InteractionModuleBase<SocketInteractionContext> {
    static readonly Random Rng = new();
    const int MAX = 100;

    [SlashCommand( "dicevs", "Rolls a random number from 1 to 100 and compares it to another user's roll." )]
    public async Task DiceVsAsync(SocketUser opponent) {
        if ( opponent.IsBot ) {
            await RespondAsync( "You can't challenge a bot to a dice game!", ephemeral: true );
            return;
        }

        if ( opponent.Id == Context.User.Id ) {
            await RespondAsync( "You can't challenge yourself to a dice game!", ephemeral: true );
            return;
        }

        int userRoll = Rng.Next( 1, MAX + 1 );
        int opponentRoll = Rng.Next( 1, MAX + 1 );

        var header = $"🎲 {Context.User.Mention} vs {opponent.Mention} 🎲";
        var rolls = $"{Context.User.Mention} rolled a **{userRoll}**!\n{opponent.Mention} rolled a **{opponentRoll}**!";
        string outcome = userRoll > opponentRoll
            ? $"{Context.User.Mention} wins!"
            : userRoll < opponentRoll
                ? $"{opponent.Mention} wins!"
                : "It's a tie!";

        var output = $"{header}\n\n{rolls}\n\n{outcome}";

        var embed = new EmbedBuilder()
            .WithAuthor( Context.User.GlobalName, Context.User.GetAvatarUrl() )
            .WithTitle( "Dice Game" )
            .WithDescription( output )
            .WithCurrentTimestamp()
            .WithColor( Color.Blue )
            .Build();

        await RespondAsync( embed: embed );
    }
}