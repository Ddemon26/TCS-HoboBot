using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;

// Using the same namespace as your other casino games for consistency
namespace TCS.HoboBot.Modules.CasinoGames
{
    public sealed class BlackjackRulesModule : InteractionModuleBase<SocketInteractionContext>
    {
        [SlashCommand("blackjack-rules", "Displays the rules and payouts for Blackjack.")]
        public async Task DisplayBlackjackRules()
        {
            var embedBuilder = new EmbedBuilder()
                .WithTitle("♠️ Blackjack Rules & Payouts ♥️")
                .WithColor(Color.DarkRed)
                .WithDescription(
                    "Blackjack is a classic card game where you play against the dealer. " +
                    "The objective is to get a hand total closer to 21 than the dealer without going over 21."
                )
                .AddField("🃏 Card Values",
                    "• **Aces (A):** Can be worth 1 or 11, whichever is more favorable for your hand.\n" +
                    "• **Face Cards (K, Q, J):** Are each worth 10.\n" +
                    "• **Number Cards (2-10):** Are worth their face value."
                )
                .AddField("▶️ The Gameplay",
                    "1. **Place Your Bet:** Start by placing your wager.\n" +
                    "2. **The Deal:** You and the dealer are both dealt two cards. Your cards are face up, while the dealer shows one card face up (the 'upcard') and one face down.\n" +
                    "3. **Your Turn:** You decide how to play your hand with the actions listed below.\n" +
                    "4. **Dealer's Turn:** After you stand, the dealer reveals their face-down card and must **hit** until their hand total is 17 or more. The dealer must stand on a 'soft 17' (an Ace and a 6) in this version.\n" +
                    "5. **The Outcome:** Hands are compared to determine the winner."
                );

            var playerActions = new StringBuilder();
            playerActions.AppendLine("**▶️ Hit:** Take another card to increase your hand's value.");
            playerActions.AppendLine("**⏹️ Stand:** Take no more cards. This ends your turn.");
            playerActions.AppendLine("**⏫ Double Down:** You can double your initial bet, but you receive **only one** additional card and must stand immediately after. This is usually only allowed on your first two cards.");
            playerActions.AppendLine("**🔀 Split:** If your first two cards have the same value (e.g., two 8s, or a K and a Q), you can split them into two separate hands. This requires placing a second bet equal to your first. You then play each hand independently.");
            playerActions.AppendLine("**🛡️ Insurance:** If the dealer's upcard is an Ace, you may be offered insurance. This is a side bet, equal to half your original wager, that pays 2 to 1 if the dealer has a Blackjack. If the dealer does not have Blackjack, you lose the insurance bet.");

            embedBuilder.AddField("PLAYER ACTIONS", playerActions.ToString());
            
            var outcomes = new StringBuilder();
            outcomes.AppendLine("**🏆 Blackjack:** If your first two cards total 21. This is an automatic win that pays **3 to 2** (e.g., a $10 bet wins $15).");
            outcomes.AppendLine("**👍 Win:** Your hand is higher than the dealer's without going over 21, or the dealer busts. Pays **1 to 1** (e.g., a $10 bet wins $10).");
            outcomes.AppendLine("**⚖️ Push:** You and the dealer have the same hand total. Your bet is returned.");
            outcomes.AppendLine("**👎 Bust / Lose:** Your hand total exceeds 21, or the dealer's hand is higher than yours. You lose your bet.");

            embedBuilder.AddField("💰 Outcomes & Payouts", outcomes.ToString());
            
            embedBuilder.WithFooter("Rules and payouts are for this bot's version of Blackjack.");

            await RespondAsync(embed: embedBuilder.Build());
        }
    }
}
