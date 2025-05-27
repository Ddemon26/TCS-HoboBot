using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;

namespace TCS.HoboBot.Modules.CasinoGames.Slots {
    public sealed class AdvancedSlotsRulesModule : InteractionModuleBase<SocketInteractionContext> {
        [SlashCommand( "slots-info", "Displays the payout information for the Advanced Slots game." )]
        public async Task DisplaySlotsInfo() {
            var embed = new EmbedBuilder()
                .WithTitle( "🎰 Advanced Slots - Payouts & Info 🎰" )
                .WithColor( Color.Gold )
                .WithDescription(
                    "Payouts are shown as multipliers of your total bet. Wins are awarded for matching symbols on a payline from left to right."
                );

            // --- Build Symbol Payouts Field ---
            var linePayoutsText = new StringBuilder();
            // We read the payout data directly from the main AdvancedSlotMachineModule
            foreach (var symbolPayout in AdvancedSlotMachineModule.FinalLinePayouts) {
                var symbol = symbolPayout.Key;
                var payouts = symbolPayout.Value;

                // Get the emoji for the current symbol
                var emoji = AdvancedSlotMachineModule.IconToEmojiMap.GetValueOrDefault( symbol, "❓" );

                // Format the line like: "🍒 (Nine): 2x: 0.4, 3x: 0.5, 4x: 1.0, 5x: 1.4"
                var payoutStrings = payouts.Select( p => $"{p.Key}x: **{p.Value}**" );
                linePayoutsText.AppendLine( $"{emoji} ({symbol}): {string.Join( ", ", payoutStrings )}" );
            }

            embed.AddField( "Symbol Payouts (per line)", linePayoutsText.ToString() );

            // --- Build Special Symbols Field ---
            var specialSymbolsText = new StringBuilder();
            var wildEmoji = AdvancedSlotMachineModule.IconToEmojiMap[AdvancedSlotIcon.Wild];
            var scatterEmoji = AdvancedSlotMachineModule.IconToEmojiMap[AdvancedSlotIcon.Scatter];
            var minigameEmoji = AdvancedSlotMachineModule.IconToEmojiMap[AdvancedSlotIcon.MiniGame];

            specialSymbolsText.AppendLine( $"{wildEmoji} **Wild:** Substitutes for any symbol except {scatterEmoji} and {minigameEmoji}." );

            // Build the scatter payout description
            var scatterPayouts = AdvancedSlotMachineModule.FixedScatterPayouts
                .Select( p => $"{p.Key} = **{p.Value}x**" );
            specialSymbolsText.AppendLine( $"{scatterEmoji} **Scatter:** Pays when 2 or more appear anywhere. ({string.Join( ", ", scatterPayouts )})" );

            specialSymbolsText.AppendLine( $"{minigameEmoji} **Mini-Game:** Get 4 or more to trigger the bonus round!" );
            embed.AddField( "Special Symbols", specialSymbolsText.ToString() );

            // --- Build Mini-Game Payouts Field ---
            var miniGamePayoutsText = new StringBuilder();
            var payoutChunks = AdvancedSlotMachineModule.MiniGamePayouts
                .Select( p => $"{p.Key}{minigameEmoji} = **{p.Value}x**" )
                .Chunk( 3 ); // Group payouts into chunks of 3 for better formatting

            foreach (var chunk in payoutChunks) {
                miniGamePayoutsText.AppendLine( string.Join( " | ", chunk ) );
            }

            embed.AddField( "⭐ Mini-Game Star Payouts", miniGamePayoutsText.ToString() );

            await RespondAsync( embed: embed.Build() );
        }
    }
}