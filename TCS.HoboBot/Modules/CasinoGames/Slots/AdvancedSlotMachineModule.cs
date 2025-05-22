using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using System.Globalization;
using System.Text;
using TCS.HoboBot.Data;

namespace TCS.HoboBot.Modules.CasinoGames {
    public enum AdvancedSlotIcon {
        // Common symbols (lower value)
        Nine, Ten, Jack, Queen, King, Ace,
        // Themed symbols (higher value)
        GemPurple, GemBlue, GemGreen, GemRed,
        // Special Symbols
        Wild, Scatter
    }

    public sealed class AdvancedSlotMachineModule : InteractionModuleBase<SocketInteractionContext> {
        private static readonly Random Rng = new();
        private const float MinBetAdv = 0.1f;
        private const float MaxBetAdv = 25f; // Higher max bet for advanced slots

        private const string CmdPrefix5x4 = "advslots5x4";
        private const string CmdPrefix5x5 = "advslots5x5";

        private static readonly IReadOnlyList<AdvancedSlotIcon> AllIcons =
            Enum.GetValues( typeof(AdvancedSlotIcon) ).Cast<AdvancedSlotIcon>().ToList().AsReadOnly();

        // Weighted symbols for reel generation (excluding scatter for normal reel population)
        private static readonly List<AdvancedSlotIcon> ReelPopulationSymbols = new List<AdvancedSlotIcon> {
            AdvancedSlotIcon.Nine, AdvancedSlotIcon.Nine, AdvancedSlotIcon.Nine,
            AdvancedSlotIcon.Ten, AdvancedSlotIcon.Ten, AdvancedSlotIcon.Ten,
            AdvancedSlotIcon.Jack, AdvancedSlotIcon.Jack,
            AdvancedSlotIcon.Queen, AdvancedSlotIcon.Queen,
            AdvancedSlotIcon.King, AdvancedSlotIcon.King,
            AdvancedSlotIcon.Ace,
            AdvancedSlotIcon.GemPurple, AdvancedSlotIcon.GemPurple,
            AdvancedSlotIcon.GemBlue, AdvancedSlotIcon.GemBlue,
            AdvancedSlotIcon.GemGreen,
            AdvancedSlotIcon.GemRed,
            AdvancedSlotIcon.Wild, AdvancedSlotIcon.Wild // Wilds appear with some frequency
        };


        private static readonly IReadOnlyDictionary<AdvancedSlotIcon, string> IconToEmojiMap = new Dictionary<AdvancedSlotIcon, string> {
            { AdvancedSlotIcon.Nine, "9️⃣" }, { AdvancedSlotIcon.Ten, "🔟" }, { AdvancedSlotIcon.Jack, "🇯" },
            { AdvancedSlotIcon.Queen, "🇶" }, { AdvancedSlotIcon.King, "🇰" }, { AdvancedSlotIcon.Ace, "🇦" },
            { AdvancedSlotIcon.GemPurple, "💜" }, { AdvancedSlotIcon.GemBlue, "💎" },
            { AdvancedSlotIcon.GemGreen, "✳️" }, { AdvancedSlotIcon.GemRed, "❤️‍🔥" },
            { AdvancedSlotIcon.Wild, "🌟" }, { AdvancedSlotIcon.Scatter, "💲" }
        };

        // Payouts: Symbol -> (Count -> Multiplier of bet per line)
        private static readonly Dictionary<AdvancedSlotIcon, Dictionary<int, decimal>> SymbolLinePayouts = new Dictionary<AdvancedSlotIcon, Dictionary<int, decimal>> {
            { AdvancedSlotIcon.Nine, new() { { 3, 0.3m }, { 4, 0.6m }, { 5, 1.2m } } },
            { AdvancedSlotIcon.Ten, new() { { 3, 0.3m }, { 4, 0.6m }, { 5, 1.2m } } },
            { AdvancedSlotIcon.Jack, new() { { 3, 0.4m }, { 4, 0.8m }, { 5, 1.5m } } },
            { AdvancedSlotIcon.Queen, new() { { 3, 0.4m }, { 4, 0.8m }, { 5, 1.5m } } },
            { AdvancedSlotIcon.King, new() { { 3, 0.5m }, { 4, 1.0m }, { 5, 2.0m } } },
            { AdvancedSlotIcon.Ace, new() { { 3, 0.5m }, { 4, 1.0m }, { 5, 2.5m } } },
            { AdvancedSlotIcon.GemPurple, new() { { 3, 0.8m }, { 4, 1.5m }, { 5, 3.0m } } },
            { AdvancedSlotIcon.GemBlue, new() { { 3, 1.0m }, { 4, 2.0m }, { 5, 4.0m } } },
            { AdvancedSlotIcon.GemGreen, new() { { 3, 1.2m }, { 4, 2.5m }, { 5, 5.0m } } },
            { AdvancedSlotIcon.GemRed, new() { { 3, 1.5m }, { 4, 3.0m }, { 5, 7.5m } } },
            { AdvancedSlotIcon.Wild, new() { { 3, 2.0m }, { 4, 5.0m }, { 5, 10.0m } } } // Wilds can also form their own lines
        };

        // Scatter Payouts: Count -> Multiplier of total bet
        private static readonly Dictionary<int, decimal> ScatterPayouts = new Dictionary<int, decimal> {
            { 3, 2m }, { 4, 10m }, { 5, 50m } // Min 3 scatters for a win
        };

        private static List<List<int>> GetPaylines(int rows, int cols) {
            var lines = new List<List<int>>();
            if ( cols != 5 ) {
                return lines; // Designed for 5 columns
            }

            // Standard Horizontal Lines
            for (int r = 0; r < rows; r++) lines.Add( Enumerable.Repeat( r, cols ).ToList() );

            // Additional common patterns (example lines)
            if ( rows >= 3 ) {
                lines.Add( new List<int> { 0, 1, 2, 1, 0 } ); // V-shape (if rows >=3)
                lines.Add( new List<int> { rows - 1, rows - 2, rows - 3, rows - 2, rows - 1 } ); // Inverse V-shape (if rows >=3)
                lines.Add( new List<int> { 0, 0, 1, 2, 2 } ); // Z-like (if rows >=3)
                lines.Add( new List<int> { rows - 1, rows - 1, rows - 2, rows - 3, rows - 3 } ); // Inverse Z-like (if rows >=3)
            }

            if ( rows >= 4 ) {
                lines.Add( new List<int> { 0, 1, 2, 3, 3 } );
                lines.Add( new List<int> { rows - 1, rows - 2, rows - 3, rows - 4, rows - 4 } );
                lines.Add( new List<int> { 1, 0, 1, 2, 3 } );
                lines.Add( new List<int> { rows - 2, rows - 1, rows - 2, rows - 3, rows - 4 } );
            }

            // Cap number of lines to make it understandable for this example
            return lines.Take( rows == 4 ? 15 : 20 ).ToList();
        }

        private string GetEmoji(AdvancedSlotIcon icon) => IconToEmojiMap.TryGetValue( icon, out var emoji ) ? emoji : "❓";

        private AdvancedSlotIcon GetRandomReelSymbol(bool allowScatter = false) {
            if ( allowScatter && Rng.Next( 100 ) < 10 ) // ~10% chance for scatter if allowed (e.g. specific reels)
            {
                return AdvancedSlotIcon.Scatter;
            }

            return ReelPopulationSymbols[Rng.Next( ReelPopulationSymbols.Count )];
        }

        private AdvancedSlotIcon[][] SpinGrid(int rows, int cols) {
            var grid = new AdvancedSlotIcon[ rows ][];
            for (int r = 0; r < rows; r++) {
                grid[r] = new AdvancedSlotIcon[ cols ];
                for (int c = 0; c < cols; c++) {
                    // Scatters might appear less frequently or on specific reels in real slots
                    // For simplicity, allow scatter on any reel with some probability.
                    bool canHaveScatter = (c == 1 || c == 2 || c == 3); // Example: Scatters on reels 2, 3, 4
                    grid[r][c] = GetRandomReelSymbol( allowScatter: canHaveScatter );
                }
            }

            return grid;
        }

        private (decimal totalBetMultiplier, string winDescription) CalculateGridPayout(AdvancedSlotIcon[][] grid, int rows, int cols) {
            decimal combinedLineMultiplier = 0m;
            var winDescriptions = new List<string>();
            var paylines = GetPaylines( rows, cols );

            // Line Wins (Left to Right)
            for (int i = 0; i < paylines.Count; i++) {
                var linePath = paylines[i]; // List of row indices for each column
                AdvancedSlotIcon firstReelSymbol = grid[linePath[0]][0];
                AdvancedSlotIcon symbolToMatch = (firstReelSymbol == AdvancedSlotIcon.Wild && cols > 1) ? grid[linePath[1]][1] : firstReelSymbol;

                if ( symbolToMatch == AdvancedSlotIcon.Wild && firstReelSymbol == AdvancedSlotIcon.Wild ) // If line starts Wild, Wild, find first non-wild or count wilds
                {
                    for (int k = 0; k < cols; k++) {
                        if ( grid[linePath[k]][k] != AdvancedSlotIcon.Wild ) {
                            symbolToMatch = grid[linePath[k]][k];
                            break;
                        }
                    }
                }


                int streak = 0;
                for (int c = 0; c < cols; c++) {
                    AdvancedSlotIcon currentSymbolOnLine = grid[linePath[c]][c];
                    if ( currentSymbolOnLine == symbolToMatch || currentSymbolOnLine == AdvancedSlotIcon.Wild || symbolToMatch == AdvancedSlotIcon.Wild ) {
                        streak++;
                        if ( symbolToMatch == AdvancedSlotIcon.Wild && currentSymbolOnLine != AdvancedSlotIcon.Wild ) // Lock onto the first non-wild symbol if initial was wild
                        {
                            symbolToMatch = currentSymbolOnLine;
                        }
                    }
                    else {
                        break;
                    }
                }

                if ( streak >= 3 && symbolToMatch != AdvancedSlotIcon.Scatter ) // Min 3 for line win, Scatters don't win on lines
                {
                    if ( SymbolLinePayouts.TryGetValue( symbolToMatch, out var payouts ) && payouts.TryGetValue( streak, out decimal lineMultiplier ) ) {
                        combinedLineMultiplier += lineMultiplier;
                        winDescriptions.Add( $"Line {i + 1}: {streak}x{GetEmoji( symbolToMatch )} ({lineMultiplier}x)" );
                    }
                }
            }

            // Scatter Wins
            int scatterCount = grid.SelectMany( row => row ).Count( s => s == AdvancedSlotIcon.Scatter );
            if ( ScatterPayouts.TryGetValue( scatterCount, out decimal scatterMultiplier ) ) {
                combinedLineMultiplier += scatterMultiplier; // Scatter multipliers add to total bet multiplier
                winDescriptions.Add( $"{scatterCount}x{GetEmoji( AdvancedSlotIcon.Scatter )} Scatters pay {scatterMultiplier}x total bet!" );
            }

            string finalDescription = winDescriptions.Any() ? string.Join( "\n", winDescriptions ) : "No wins this round.";
            return (combinedLineMultiplier, finalDescription);
        }

        private Embed BuildAdvancedEmbed(SocketUser user, AdvancedSlotIcon[][] grid, float bet, decimal payoutMultiplier, string winDescription, decimal totalWinnings, int rows, int cols) {
            var gridDisplay = new StringBuilder();
            for (int r = 0; r < rows; r++) {
                for (int c = 0; c < cols; c++) {
                    gridDisplay.Append( GetEmoji( grid[r][c] ) );
                    if ( c < cols - 1 ) {
                        gridDisplay.Append( cols > 4 ? " " : " | " ); // Adjust spacing
                    }
                }

                gridDisplay.Append( "\n" );
            }

            string outcomeMessage;
            decimal profit = totalWinnings - (decimal)bet;

            if ( payoutMultiplier == 0m ) {
                outcomeMessage = $"Unlucky! You lost **{bet:C2}**.";
            }
            else if ( profit == 0 && payoutMultiplier > 0 ) {
                outcomeMessage = $"Push! Your **{bet:C2}** bet is returned.";
            }
            else {
                outcomeMessage = $"Congratulations! You won **{profit:C2}** (Total: {totalWinnings:C2}).";
            }

            // add your wallet balance to the outcome message
            outcomeMessage += $"\nYour new balance: **${PlayersWallet.GetBalance( user.Id ):C2}**";

            var embedBuilder = new EmbedBuilder()
                .WithTitle( $"Advanced Slots ({rows}x{cols}) – {bet:C2} Bet" )
                .WithDescription( $"{user.Mention} spins the reels…\n\n{gridDisplay.ToString().Trim()}\n\n{winDescription}" )
                .WithFooter( outcomeMessage );

            if ( profit > 0 ) {
                embedBuilder.WithColor( Color.Green );
            }
            else if ( profit == 0 && payoutMultiplier > 0 ) {
                embedBuilder.WithColor( Color.LightGrey );
            }
            else {
                embedBuilder.WithColor( Color.Red );
            }

            return embedBuilder.Build();
        }

        private bool ValidateAdvancedBet(ref float bet, out string? error) {
            error = null;
            if ( bet < MinBetAdv ) {
                error = $"Bet must be at least ${MinBetAdv:C2}.";
                return false;
            }

            if ( bet > MaxBetAdv ) {
                bet = MaxBetAdv; // Cap bet
            }

            if ( PlayersWallet.GetBalance( Context.User.Id ) < bet ) {
                error = $"{Context.User.Mention} doesn’t have enough cash! Your balance: ${PlayersWallet.GetBalance( Context.User.Id ):C2}. Bet: ${bet:C2}.";
                return false;
            }

            return true;
        }

        private async Task PlayAdvancedSlots(float bet, int rows, int cols, string commandPrefix, bool isSpinAgain = false, SocketInteraction? interaction = null) {
            float currentBet = bet;
            if ( !isSpinAgain ) {
                if ( !ValidateAdvancedBet( ref currentBet, out string? error ) ) {
                    await RespondAsync( error, ephemeral: true );
                    return;
                }
            }
            else if ( interaction != null ) // Spin again funds check
            {
                if ( PlayersWallet.GetBalance( Context.User.Id ) < currentBet ) {
                    await interaction.ModifyOriginalResponseAsync( m => {
                            m.Content = $"{Context.User.Mention} doesn't have enough cash for ${currentBet:C2}!";
                            m.Components = new ComponentBuilder().Build();
                        }
                    );
                    return;
                }
            }

            PlayersWallet.SubtractFromBalance( Context.User.Id, currentBet );
            AdvancedSlotIcon[][] spinResult = SpinGrid( rows, cols );
            var (payoutMult, winDesc) = CalculateGridPayout( spinResult, rows, cols );
            decimal totalReturned = (decimal)currentBet * payoutMult;

            if ( payoutMult > 0 ) {
                PlayersWallet.AddToBalance( Context.User.Id, (float)totalReturned );
            }

            Embed embed = BuildAdvancedEmbed( Context.User, spinResult, currentBet, payoutMult, winDesc, totalReturned, rows, cols );
            var buttons = new ComponentBuilder()
                .WithButton( "Spin Again", $"{commandPrefix}_again_{currentBet.ToString( CultureInfo.InvariantCulture )}_{rows}x{cols}", ButtonStyle.Primary )
                .WithButton( "End", $"{commandPrefix}_end", ButtonStyle.Danger );

            if ( isSpinAgain && interaction != null ) {
                await interaction.ModifyOriginalResponseAsync( m => {
                        m.Embed = embed;
                        m.Components = buttons.Build();
                    }
                );
            }
            else {
                await RespondAsync( embed: embed, components: buttons.Build(), ephemeral: true );
            }

            if ( payoutMult >= 5m ) // Announce significant wins
            {
                decimal profit = totalReturned - (decimal)currentBet;
                if ( profit > 0 ) {
                    await Context.Channel.SendMessageAsync( $"{Context.User.Mention} wins **{profit:C2}** on Advanced Slots ({rows}x{cols})!" );
                }
            }
        }

        // --- 5x4 Slot ---
        [SlashCommand( "slots5x4", "Play a 5-reel, 4-row advanced slot machine." )]
        public async Task Slots5x4Async([Summary( description: "Your bet amount" )] float bet)
            => await PlayAdvancedSlots( bet, 4, 5, CmdPrefix5x4 );

        [ComponentInteraction( "advslots5x4_again_*_*" )] // bet_RxC
        public async Task OnSpinAgain5x4(string rawBet, string gridSizeRaw) {
            await DeferAsync( ephemeral: true );
            HandleAdvancedSpinAgain( rawBet, gridSizeRaw, 4, 5, CmdPrefix5x4 );
        }
        [ComponentInteraction( "advslots5x4_end" )]
        public async Task OnEnd5x4() => await HandleAdvancedEndGame( "Advanced Slots (5x4)" );

        // --- 5x5 Slot ---
        [SlashCommand( "slots5x5", "Play a 5-reel, 5-row advanced slot machine." )]
        public async Task Slots5x5Async([Summary( description: "Your bet amount" )] float bet)
            => await PlayAdvancedSlots( bet, 5, 5, CmdPrefix5x5 );

        [ComponentInteraction( "advslots5x5_again_*_*" )] // bet_RxC
        public async Task OnSpinAgain5x5(string rawBet, string gridSizeRaw) {
            await DeferAsync( ephemeral: true );
            HandleAdvancedSpinAgain( rawBet, gridSizeRaw, 5, 5, CmdPrefix5x5 );
        }
        [ComponentInteraction( "advslots5x5_end" )]
        public async Task OnEnd5x5() => await HandleAdvancedEndGame( "Advanced Slots (5x5)" );


        private async void HandleAdvancedSpinAgain(string rawBet, string gridSizeRaw, int expectedRows, int expectedCols, string cmdPrefix) {
            if ( !float.TryParse( rawBet, NumberStyles.Float, CultureInfo.InvariantCulture, out float bet ) ) {
                await Context.Interaction.ModifyOriginalResponseAsync( m => m.Content = "Invalid bet data." );
                return;
            }

            // gridSizeRaw is part of the ID like "4x5", verify it if needed, though PlayAdvancedSlots takes rows/cols explicitly
            await PlayAdvancedSlots( bet, expectedRows, expectedCols, cmdPrefix, true, Context.Interaction );
        }

        private async Task HandleAdvancedEndGame(string gameTitle) {
            await DeferAsync( ephemeral: true ); // Defer if not already (though main handlers do)
            await Context.Interaction.ModifyOriginalResponseAsync( m => {
                    m.Embed = new EmbedBuilder().WithTitle( $"{gameTitle} – Game Over" ).WithDescription( $"{Context.User.Mention} ended the game." ).Build();
                    m.Components = new ComponentBuilder().Build();
                }
            );
        }
    }
}