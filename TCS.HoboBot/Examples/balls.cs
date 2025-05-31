/*using System.Collections.Concurrent;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
namespace TCS.HoboBot.Examples;

public static class BallsDataBase {
    public static ConcurrentDictionary<ulong, ConcurrentDictionary<ulong, float>> BallsCache { get; } = new();
}

public class BallsModule : InteractionModuleBase<SocketInteractionContext> {
    [SlashCommand( "balls", "Replies with 'Balls!'" )]
// file: TCS.HoboBot/Examples/balls.cs

[ComponentInteraction("lol", ignoreGroupNames: true)]
    public async Task BallsAsync(
        [Summary("balls", "The number of balls to display")] int balls = 1
    ) {
        await DeferAsync(ephemeral: true);

        var embed = BuildUthEmbed("rioshe", "flat ass mother fucker", "flaccid boy", Color.DarkGreen).Build();

        var components = new ComponentBuilder()
            .WithButton("BIG DICK?", $"idstring:420:6972", ButtonStyle.Success)
            .Build();

        await FollowupAsync(embed: embed, components: components, ephemeral: true);
    }
    
    [SlashCommand("ballsdeep", "Go deeper")]
    public async Task BallsDeepAsync(
        [Summary("ballsDeep", "Go deep in user")] SocketUser user)
    {
        await RespondAsync( $"{Context.User.Mention} went ballsdeep in {user.Mention}");
    }
    
    [ComponentInteraction( $"idstring:*:*", ignoreGroupNames: true )]
    public async Task BallsBet4xAsync(int number1, int number2) {
        await DeferAsync( );

        var embed = BuildUthEmbed( "sdasdd", "flat asfgasdfsdfar", "fldsfasdfsy", Color.Red );
        
        var components = new ComponentBuilder()
            .WithButton( "smol DICK?", $"lol", ButtonStyle.Success )
            // .WithButton( "Bet 3x", $"2", ButtonStyle.Success )
            // .WithButton( "Check", $"3", ButtonStyle.Secondary )
            // .WithButton( "Fold", $"4", ButtonStyle.Danger, row: 1 )
            .Build();

        await ModifyOriginalResponseAsync( p => {
                p.Embed = embed.Build();
                p.Components = components;
            }
        ); 
    }
    
    EmbedBuilder BuildUthEmbed(string title, string description, string footer, Color color) {
        var embed = new EmbedBuilder()
            .WithAuthor( Context.User.GlobalName, Context.User.GetAvatarUrl() )
            .WithTitle( title )
            .WithDescription( description )
            //.WithThumbnailUrl( "https://www.google.com/imgres?q=casino%20icon&imgurl=https%3A%2F%2Fstatic-00.iconduck.com%2Fassets.00%2Fcasino-icon-2048x2048-qpd16ckr.png&imgrefurl=https%3A%2F%2Ficonduck.com%2Ficons%2F161061%2Fcasino&docid=T7Eivj4VPcdv3M&tbnid=vKr95fq_aL7STM&vet=12ahUKEwiq5aPJvMWNAxXCMDQIHTSmDUMQM3oECBgQAA..i&w=2048&h=2048&hcb=2&ved=2ahUKEwiq5aPJvMWNAxXCMDQIHTSmDUMQM3oECBgQAA" ) // Replace with actual thumbnail URL
            // .WithImageUrl( "https://cdn.discordapp.com/attachments/1176938074622664764/1278438997009502353/file-3FE6ck7ZFGzpXK9J7RUvldbl.png?ex=68376699&is=68361519&hm=9819c70dcc3cb8baffe31e674f0a5d7fe701b2a1c7ebb1f16feda78b9b816f22&" ) // Replace with actual image URL
            // .WithUrl("https://example.com")
            .WithCurrentTimestamp()
            .WithColor( color )
            .WithFooter( footer, Context.User.GetAvatarUrl() );

        return embed;
    }

}*/