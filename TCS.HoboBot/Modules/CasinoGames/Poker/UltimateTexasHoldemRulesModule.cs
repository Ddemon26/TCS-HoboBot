using System.Text;
using Discord;
using Discord.Interactions;

// Using the same namespace as your other casino games for consistency
namespace TCS.HoboBot.Modules.CasinoGames {
    public sealed class UltimateTexasHoldemRulesModule : InteractionModuleBase<SocketInteractionContext> {
        // Emojis for hand combinations
        const string ROYAL_FLUSH_EMOJI = "👑"; // Crown for Royal Flush
        const string STRAIGHT_FLUSH_EMOJI = "✨"; // Sparkles for Straight Flush
        const string FOUR_OF_A_KIND_EMOJI = "🍀"; // Four Leaf Clover for Quads
        const string FULL_HOUSE_EMOJI = "🏠"; // House for Full House
        const string FLUSH_EMOJI = "💧"; // Water drop for Flush (same suit)
        const string STRAIGHT_EMOJI = "�"; // Upwards chart for Straight
        const string THREE_OF_A_KIND_EMOJI = "🎯"; // Target for Trips
        const string TWO_PAIR_EMOJI = "✌️"; // Victory hand for Two Pair
        const string ONE_PAIR_EMOJI = "☝️"; // Index pointing up for One Pair
        const string HIGH_CARD_EMOJI = "🃏"; // Joker for High Card (or generic card)

        [SlashCommand( "uth-rules", "Displays the rules and payouts for Ultimate Texas Hold'em." )]
        public async Task DisplayUthRules() {
            var embedBuilder = new EmbedBuilder()
                .WithTitle( "♦️ Ultimate Texas Hold'em - Rules & Payouts ♦️" )
                .WithColor( Color.DarkBlue )
                .WithDescription(
                    "Ultimate Texas Hold'em is a poker-based casino game where you play against the dealer. " +
                    "The goal is to make a better five-card poker hand than the dealer using your two hole cards and five community cards."
                )
                .AddField(
                    "📝 How to Play",
                    "1. **Ante & Blind Bets:** Place equal bets on the Ante and Blind spots. You can also make an optional Trips bet.\n" +
                    "2. **Hole Cards:** You and the dealer receive two cards face down.\n" +
                    "3. **Pre-Flop Decision:** You can either:\n" +
                    "   - **Check:** Make no additional bet.\n" +
                    "   - **Bet 3x or 4x:** Bet 3 or 4 times your Ante on the Play spot. This is your only chance to bet this much.\n" +
                    "4. **The Flop:** Three community cards are dealt face up.\n" +
                    "   - If you checked pre-flop, you can now **Bet 2x** your Ante on the Play spot, or **Check** again.\n" +
                    "   - If you already bet, you do nothing.\n" +
                    "5. **The Turn & River:** The final two community cards are dealt face up.\n" +
                    "   - If you checked twice, you must now either **Bet 1x** your Ante on the Play spot or **Fold** (losing your Ante and Blind bets).\n" +
                    "   - If you already bet, you do nothing.\n" +
                    "6. **Showdown:** If you haven't folded, you and the dealer reveal your hands. The best five-card hand wins."
                )
                .AddField(
                    "💰 Dealer Qualification & Payouts",
                    "The dealer needs at least a **Pair** to 'qualify'.\n" +
                    "- **If Dealer Doesn't Qualify:** Your Ante bet is returned (push). All other bets (Play, Blind) are still in action against your hand.\n" +
                    "- **If Dealer Qualifies:**\n" +
                    "  - **Player Wins:** Ante and Play bets pay 1 to 1. Blind bet pays according to the payout table (see below) if your winning hand is a Straight or better; otherwise, it pushes.\n" +
                    "  - **Dealer Wins:** You lose Ante, Blind, and Play bets.\n" +
                    "  - **Tie:** Ante, Blind, and Play bets push."
                );

            var blindPayouts = new StringBuilder();
            blindPayouts.AppendLine( $"{ROYAL_FLUSH_EMOJI} Royal Flush: 500 to 1" );
            blindPayouts.AppendLine( $"{STRAIGHT_FLUSH_EMOJI} Straight Flush: 50 to 1" );
            blindPayouts.AppendLine( $"{FOUR_OF_A_KIND_EMOJI} Four of a Kind: 10 to 1" );
            blindPayouts.AppendLine( $"{FULL_HOUSE_EMOJI} Full House: 3 to 1" );
            blindPayouts.AppendLine( $"{FLUSH_EMOJI} Flush: 3 to 2 (1.5 to 1)" );
            blindPayouts.AppendLine( $"{STRAIGHT_EMOJI} Straight: 1 to 1" );
            blindPayouts.AppendLine( $"{THREE_OF_A_KIND_EMOJI} Three of a Kind or less: Push (if you win the hand)" );

            embedBuilder.AddField( "💸 Blind Bet Payouts (if you beat the dealer with a qualifying hand)", blindPayouts.ToString() );

            var tripsPayouts = new StringBuilder();
            tripsPayouts.AppendLine( $"{ROYAL_FLUSH_EMOJI} Royal Flush: 50 to 1" );
            tripsPayouts.AppendLine( $"{STRAIGHT_FLUSH_EMOJI} Straight Flush: 40 to 1" );
            tripsPayouts.AppendLine( $"{FOUR_OF_A_KIND_EMOJI} Four of a Kind: 30 to 1" );
            tripsPayouts.AppendLine( $"{FULL_HOUSE_EMOJI} Full House: 8 to 1" );
            tripsPayouts.AppendLine( $"{FLUSH_EMOJI} Flush: 7 to 1" );
            tripsPayouts.AppendLine( $"{STRAIGHT_EMOJI} Straight: 4 to 1" );
            tripsPayouts.AppendLine( $"{THREE_OF_A_KIND_EMOJI} Three of a Kind: 3 to 1" );

            embedBuilder.AddField( "💲 Trips Bonus Bet Payouts (pays regardless of dealer's hand)", tripsPayouts.ToString() );

            embedBuilder.WithFooter( "Payouts may vary by casino. This information is for the bot's game version." );

            await RespondAsync( embed: embedBuilder.Build(),
                ephemeral: true // Only the user who invoked the command can see this
            );
        }
    }
}