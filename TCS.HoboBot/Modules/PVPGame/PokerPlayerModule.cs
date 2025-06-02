using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using TCS.HoboBot.Data;

namespace TCS.HoboBot.Modules.Util;

public class PokerPlayerModule : InteractionModuleBase<SocketInteractionContext> {
    static readonly Random Rng = new();

    // Icon mapping for quick, colorful output
    static readonly Dictionary<string, string> FlowerIcons = new() {
        { "Red", "🔴" },
        { "Orange", "🟠" },
        { "Yellow", "🟡" },
        { "Blue", "🔵" },
        { "Purple", "🟣" },
        { "Rainbow", "🌈" },
        { "Pastel", "🌸" },
        { "White", "⚪" },
        { "Black", "⚫" },
    };

    // Approximate RuneScape flower odds (weights sum to 1017)
    readonly List<(string Color, int Weight)> m_flowerWeights = [
        ("Red", 138),
        ("Orange", 138),
        ("Yellow", 160),
        ("Blue", 150),
        ("Purple", 150),
        ("Rainbow", 150),
        ("Pastel", 100),
        ("White", 11), // ≈ 0.11 %
        ("Black", 20),
    ];

    [SlashCommand( "flowerpoker", "Plants five flowers for each player and decides the winner using classic Flower‑Poker hand rankings." )]
    public async Task FlowerPokerAsync(SocketUser user, float bet) {
        // Validate bet
        if ( bet <= 0 ) {
            await RespondAsync( "You must bet some cash!" );
            return;
        }

        // Check balances
        if ( PlayersWallet.GetBalance( Context.Guild.Id, Context.User.Id ) < bet ) {
            await RespondAsync( $"{Context.User.Mention} doesn't have enough cash!" );
            return;
        }

        if ( PlayersWallet.GetBalance(Context.Guild.Id,  user.Id ) < bet ) {
            await RespondAsync( $"{user.GlobalName} doesn't have enough cash!", ephemeral: true );
            return;
        }

        // Generate hands
        List<string> playerHand = PlantHand();
        List<string> opponentHand = PlantHand();

        // Evaluate
        int playerRank = EvaluateHand( playerHand );
        int opponentRank = EvaluateHand( opponentHand );

        string result;
        if ( playerRank < opponentRank ) {
            PlayersWallet.AddToBalance( Context.Guild.Id, Context.User.Id, bet );
            PlayersWallet.SubtractFromBalance(Context.Guild.Id,  user.Id, bet );
            result = $"{Context.User.Mention} wins! (+${bet:0.00})\n" +
                     $"New balances: {Context.User.Mention}: ${PlayersWallet.GetBalance(Context.Guild.Id,  Context.User.Id ):0.00}, " +
                     $"{user.Mention}: ${PlayersWallet.GetBalance(Context.Guild.Id,  user.Id ):0.00}";
        }
        else if ( playerRank > opponentRank ) {
            PlayersWallet.AddToBalance(Context.Guild.Id,  user.Id, bet );
            PlayersWallet.SubtractFromBalance( Context.Guild.Id, Context.User.Id, bet );
            result = $"{user.Mention} wins! (+${bet:0.00})\n" +
                     $"New balances: {Context.User.Mention}: ${PlayersWallet.GetBalance( Context.Guild.Id, Context.User.Id ):0.00}, " +
                     $"{user.Mention}: ${PlayersWallet.GetBalance(Context.Guild.Id,  user.Id ):0.00}";
        }
        else {
            result = "It's a tie! No money changes hands.";
        }
        
        var outputEmbed = BuildUthEmbed(
            "Flower Poker",
            $"{Context.User.Mention } vs {user.Mention}",
            $"Bet: ${bet:0.00}",
            Color.Green
        );
        
        outputEmbed.AddField( Context.User.GlobalName, $"`{FormatHand( playerHand )}`" + $" ({RankName( playerRank )})", inline: true );
        outputEmbed.AddField( user.GlobalName, $"`{FormatHand( opponentHand )}`" + $" ({RankName( opponentRank )})", inline: true );
        outputEmbed.AddField( "Result", result, inline: false );

        // await RespondAsync(
        //     "**FLOWER POKER**\n" +
        //     $"{Context.User.Mention} flowers: {FormatHand( playerHand )} ({RankName( playerRank )})\n" +
        //     $"{user.Mention} flowers: {FormatHand( opponentHand )} ({RankName( opponentRank )})\n" +
        //     result
        // );
        
        await RespondAsync( embed: outputEmbed.Build() );
    }

    // ----- Helper methods -------------------------------------------------
    List<string> PlantHand(int flowers = 5) {
        List<string> hand = new(flowers);
        for (var i = 0; i < flowers; i++)
            hand.Add( RandomFlower() );
        return hand;
    }

    string RandomFlower() {
        int total = m_flowerWeights.Sum( f => f.Weight );
        int roll = Rng.Next( 1, total + 1 );
        var acc = 0;
        foreach ((string colour, int weight) in m_flowerWeights) {
            acc += weight;
            if ( roll <= acc ) {
                return colour;
            }
        }

        return "Red"; // Fallback (should never hit)
    }

    static int EvaluateHand(List<string> hand) {
        // Auto‑win if the hand contains black or white
        if ( hand.Contains( "Black" ) || hand.Contains( "White" ) ) {
            return 0;
        }

        // Group counts
        int[] groups = hand.GroupBy( c => c ).Select( g => g.Count() ).OrderByDescending( c => c ).ToArray();

        return groups switch {
            _ when groups[0] == 5 => 1, // 5‑oak
            _ when groups[0] == 4 => 2, // 4‑oak
            _ when groups[0] == 3 && groups[1] == 2 => 3, // Full House
            _ when groups[0] == 3 => 4, // 3‑oak
            _ when groups[0] == 2 && groups[1] == 2 => 5, // Two Pairs
            _ when groups[0] == 2 => 6, // One Pair
            _ => 7, // Bust
        };
    }

    string RankName(int rank) => rank switch {
        0 => "Black/White auto‑win",
        1 => "5‑oak",
        2 => "4‑oak",
        3 => "Full House",
        4 => "3‑oak",
        5 => "Two Pair",
        6 => "One Pair",
        7 => "Bust",
        _ => "Unknown",
    };

    static string FormatHand(IEnumerable<string> hand) 
        => string.Join( " ", hand.Select( c => FlowerIcons.GetValueOrDefault( c, c ) ) );
    
    EmbedBuilder BuildUthEmbed(string title, string description, string footer, Color color) {
        var embed = new EmbedBuilder()
            .WithAuthor( Context.User.GlobalName, Context.User.GetAvatarUrl() )
            .WithTitle( title )
            .WithDescription( description )
            .WithCurrentTimestamp()
            .WithColor( color )
            .WithFooter( footer, Context.User.GetAvatarUrl() );

        return embed;
    }
}