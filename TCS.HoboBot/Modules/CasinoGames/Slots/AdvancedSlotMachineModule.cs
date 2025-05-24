using System.Globalization;
using System.Text;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using TCS.HoboBot.Data;
namespace TCS.HoboBot.Modules.CasinoGames.Slots {
    public enum AdvancedSlotIcon {
        // Common symbols (lower value)
        Nine, Ten, Jack, Queen, King, Ace,
        // Themed symbols (higher value)
        GemPurple, GemBlue, GemGreen, GemRed,
        // Special Symbols
        Wild, Scatter,
    }

    public sealed class AdvancedSlotMachineModule : InteractionModuleBase<SocketInteractionContext> {
        static readonly Random Rng = new();
        const float MIN_BET_ADV = 5f;
        const float MAX_BET_ADV = 1_000f; // Higher max bet for advanced slots

        const string CMD_PREFIX5_X4 = "advslots5x4";
        const string CMD_PREFIX5_X5 = "advslots5x5";

        static readonly IReadOnlyList<AdvancedSlotIcon> AllIcons =
            Enum.GetValues( typeof(AdvancedSlotIcon) ).Cast<AdvancedSlotIcon>().ToList().AsReadOnly();

        // Weighted symbols for reel generation (excluding scatter for normal reel population)
        static readonly List<AdvancedSlotIcon> ReelPopulationSymbols = [
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
            AdvancedSlotIcon.Wild, AdvancedSlotIcon.Wild,
        ];


        static readonly IReadOnlyDictionary<AdvancedSlotIcon, string> IconToEmojiMap = new Dictionary<AdvancedSlotIcon, string> {
            { AdvancedSlotIcon.Nine, "9️⃣" }, { AdvancedSlotIcon.Ten, "🔟" }, { AdvancedSlotIcon.Jack, "🇯" },
            { AdvancedSlotIcon.Queen, "🇶" }, { AdvancedSlotIcon.King, "🇰" }, { AdvancedSlotIcon.Ace, "🇦" },
            { AdvancedSlotIcon.GemPurple, "💜" }, { AdvancedSlotIcon.GemBlue, "💎" },
            { AdvancedSlotIcon.GemGreen, "✳️" }, { AdvancedSlotIcon.GemRed, "❤️‍🔥" },
            { AdvancedSlotIcon.Wild, "🌟" }, { AdvancedSlotIcon.Scatter, "💲" },
        };

        const float RTP = 0.96f; // Adjustable RTP percentage
        // Scale pre-RTP to exactly 100%
        const decimal SCALING_FACTOR = 0.988973m;

        // Base payouts (scaled for 100% pre-RTP)
        static readonly Dictionary<AdvancedSlotIcon, Dictionary<int, decimal>> BaseSymbolLinePayouts = new() {
            { AdvancedSlotIcon.Nine, new Dictionary<int, decimal> { { 3, 0.2m * SCALING_FACTOR }, { 4, 0.4m * SCALING_FACTOR }, { 5, 0.8m * SCALING_FACTOR } } },
            { AdvancedSlotIcon.Ten, new Dictionary<int, decimal> { { 3, 0.2m * SCALING_FACTOR }, { 4, 0.4m * SCALING_FACTOR }, { 5, 0.8m * SCALING_FACTOR } } },
            { AdvancedSlotIcon.Jack, new Dictionary<int, decimal> { { 3, 0.3m * SCALING_FACTOR }, { 4, 0.6m * SCALING_FACTOR }, { 5, 1.2m * SCALING_FACTOR } } },
            { AdvancedSlotIcon.Queen, new Dictionary<int, decimal> { { 3, 0.3m * SCALING_FACTOR }, { 4, 0.6m * SCALING_FACTOR }, { 5, 1.2m * SCALING_FACTOR } } },
            { AdvancedSlotIcon.King, new Dictionary<int, decimal> { { 3, 0.4m * SCALING_FACTOR }, { 4, 0.8m * SCALING_FACTOR }, { 5, 1.6m * SCALING_FACTOR } } },
            { AdvancedSlotIcon.Ace, new Dictionary<int, decimal> { { 3, 0.5m * SCALING_FACTOR }, { 4, 1.0m * SCALING_FACTOR }, { 5, 2.0m * SCALING_FACTOR } } },
            { AdvancedSlotIcon.GemPurple, new Dictionary<int, decimal> { { 3, 0.8m * SCALING_FACTOR }, { 4, 1.6m * SCALING_FACTOR }, { 5, 3.2m * SCALING_FACTOR } } },
            { AdvancedSlotIcon.GemBlue, new Dictionary<int, decimal> { { 3, 1.0m * SCALING_FACTOR }, { 4, 2.0m * SCALING_FACTOR }, { 5, 4.0m * SCALING_FACTOR } } },
            { AdvancedSlotIcon.GemGreen, new Dictionary<int, decimal> { { 3, 1.2m * SCALING_FACTOR }, { 4, 2.4m * SCALING_FACTOR }, { 5, 4.8m * SCALING_FACTOR } } },
            { AdvancedSlotIcon.GemRed, new Dictionary<int, decimal> { { 3, 1.5m * SCALING_FACTOR }, { 4, 3.0m * SCALING_FACTOR }, { 5, 6.0m * SCALING_FACTOR } } },
            { AdvancedSlotIcon.Wild, new Dictionary<int, decimal> { { 3, 2.0m * SCALING_FACTOR }, { 4, 4.0m * SCALING_FACTOR }, { 5, 8.0m * SCALING_FACTOR } } },
        };

        // Scatter payouts (scaled for 100% pre-RTP)
        static readonly Dictionary<int, decimal> BaseScatterPayouts = new() {
            { 3, 2m * SCALING_FACTOR }, { 4, 5m * SCALING_FACTOR }, { 5, 15m * SCALING_FACTOR },
        };

        // Runtime scaling method using the RTP factor
        static decimal GetAdjustedSymbolPayout(AdvancedSlotIcon symbol, int count) {
            if ( BaseSymbolLinePayouts.TryGetValue( symbol, out Dictionary<int, decimal>? payouts ) && payouts.TryGetValue( count, out decimal basePayout ) ) {
                return basePayout * (decimal)RTP;
            }

            return 0m;
        }

        static decimal GetAdjustedScatterPayout(int count) {
            if ( BaseScatterPayouts.TryGetValue( count, out decimal baseScatterPayout ) ) {
                return baseScatterPayout * (decimal)RTP;
            }

            return 0m;
        }



        static List<List<int>> GetPaylines(int rows, int cols) {
            List<List<int>> lines = [];
            if ( cols != 5 ) {
                return lines; // Designed for 5 columns
            }

            // Standard Horizontal Lines
            for (var r = 0; r < rows; r++) {
                lines.Add( Enumerable.Repeat( r, cols ).ToList() );
            }

            // Additional common patterns (example lines)
            if ( rows >= 3 ) {
                lines.Add( [0, 1, 2, 1, 0] ); // V-shape (if rows >=3)
                lines.Add( [rows - 1, rows - 2, rows - 3, rows - 2, rows - 1] ); // Inverse V-shape (if rows >=3)
                lines.Add( [0, 0, 1, 2, 2] ); // Z-like (if rows >=3)
                lines.Add( [rows - 1, rows - 1, rows - 2, rows - 3, rows - 3] ); // Inverse Z-like (if rows >=3)
            }

            if ( rows >= 4 ) {
                lines.Add( [0, 1, 2, 3, 3] );
                lines.Add( [rows - 1, rows - 2, rows - 3, rows - 4, rows - 4] );
                lines.Add( [1, 0, 1, 2, 3] );
                lines.Add( [rows - 2, rows - 1, rows - 2, rows - 3, rows - 4] );
            }

            // Cap the number of lines to make it understandable for this example
            return lines.Take( rows == 4 ? 15 : 20 ).ToList();
        }

        static string GetEmoji(AdvancedSlotIcon icon) => IconToEmojiMap.GetValueOrDefault( icon, "❓" );

        static AdvancedSlotIcon GetRandomReelSymbol(bool allowScatter = false) {
            if ( allowScatter && Rng.Next( 100 ) < 10 ) // ~10% chance for scatter if allowed (e.g., specific reels)
            {
                return AdvancedSlotIcon.Scatter;
            }

            return ReelPopulationSymbols[Rng.Next( ReelPopulationSymbols.Count )];
        }

        AdvancedSlotIcon[][] SpinGrid(int rows, int cols) {
            AdvancedSlotIcon[][] grid = new AdvancedSlotIcon[ rows ][];
            for (var r = 0; r < rows; r++) {
                grid[r] = new AdvancedSlotIcon[ cols ];
                for (var c = 0; c < cols; c++) {
                    // Scatters might appear less frequently or on specific reels in real slots
                    // For simplicity, allow scatter on any reel with some probability.
                    bool canHaveScatter = (c == 1 || c == 2 || c == 3); // Example: Scatters on reels 2, 3, 4
                    grid[r][c] = GetRandomReelSymbol( allowScatter: canHaveScatter );
                }
            }

            return grid;
        }

        static (decimal totalBetMultiplier, string winDescription) CalculateGridPayout(AdvancedSlotIcon[][] grid, int rows, int cols) {
            var combinedLineMultiplier = 0m;
            List<string> winDescriptions = [];
            List<List<int>> paylines = GetPaylines( rows, cols );

            // Line Wins (Left to Right)
            for (var i = 0; i < paylines.Count; i++) {
                List<int> linePath = paylines[i]; // List of row indices for each column
                var firstReelSymbol = grid[linePath[0]][0];
                var symbolToMatch = (firstReelSymbol == AdvancedSlotIcon.Wild && cols > 1) ? grid[linePath[1]][1] : firstReelSymbol;

                if ( symbolToMatch == AdvancedSlotIcon.Wild && firstReelSymbol == AdvancedSlotIcon.Wild ) // If the line starts Wild, Wild, find the first non-wild or count wilds
                {
                    for (var k = 0; k < cols; k++) {
                        if ( grid[linePath[k]][k] != AdvancedSlotIcon.Wild ) {
                            symbolToMatch = grid[linePath[k]][k];
                            break;
                        }
                    }
                }


                var streak = 0;
                for (var c = 0; c < cols; c++) {
                    var currentSymbolOnLine = grid[linePath[c]][c];
                    if ( currentSymbolOnLine == symbolToMatch || currentSymbolOnLine == AdvancedSlotIcon.Wild || symbolToMatch == AdvancedSlotIcon.Wild ) {
                        streak++;
                        if ( symbolToMatch == AdvancedSlotIcon.Wild && currentSymbolOnLine != AdvancedSlotIcon.Wild ) // Lock onto the first non-wild symbol if the initial was wild
                        {
                            symbolToMatch = currentSymbolOnLine;
                        }
                    }
                    else {
                        break;
                    }
                }

                if ( streak >= 3 && symbolToMatch != AdvancedSlotIcon.Scatter ) {
                    decimal lineMultiplier = GetAdjustedSymbolPayout( symbolToMatch, streak );
                    if ( lineMultiplier > 0m ) {
                        combinedLineMultiplier += lineMultiplier;
                        winDescriptions.Add( $"Line {i + 1}: {streak}x{GetEmoji( symbolToMatch )} ({lineMultiplier:0.##}x)" );
                    }
                }
            }

            // Scatter Wins
            int scatterCount = grid.SelectMany( row => row ).Count( s => s == AdvancedSlotIcon.Scatter );
            decimal scatterMultiplier = GetAdjustedScatterPayout( scatterCount );
            if ( scatterMultiplier > 0m ) {
                combinedLineMultiplier += scatterMultiplier;
                winDescriptions.Add( $"{scatterCount}x{GetEmoji( AdvancedSlotIcon.Scatter )} Scatters pay {scatterMultiplier:0.##}x total bet!" );
            }

            string finalDescription = winDescriptions.Any() ? string.Join( "\n", winDescriptions ) : "No wins this round.";
            return (combinedLineMultiplier, finalDescription);
        }

        Embed BuildAdvancedEmbed(SocketUser user, AdvancedSlotIcon[][] grid, float bet, decimal payoutMultiplier, string winDescription, decimal totalWinnings, int rows, int cols) {
            var gridDisplay = new StringBuilder();
            for (var r = 0; r < rows; r++) {
                for (var c = 0; c < cols; c++) {
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
                outcomeMessage = $"Unlucky! You lost {bet:C2}.";
            }
            else if ( profit == 0 && payoutMultiplier > 0 ) {
                outcomeMessage = $"Push! Your **{bet:C2}** bet is returned.";
            }
            else {
                outcomeMessage = $"Congratulations! You won {profit:C2} (Total: {totalWinnings:C2}).";
            }

            // add your wallet balance to the outcome message
            outcomeMessage += $"\nYour new balance: {PlayersWallet.GetBalance(Context.Guild.Id, user.Id ):C2}";

            var embedBuilder = new EmbedBuilder()
                .WithTitle( $"Advanced Slots ({rows}x{cols}) – {bet:C2} Bet" )
                .WithDescription( $"{user.Mention} spins the reels…\n\n{gridDisplay.ToString().Trim()}\n\n{winDescription}" )
                .WithFooter( outcomeMessage );

            switch (profit) {
                case > 0:
                    embedBuilder.WithColor( Color.Green );
                    break;
                case 0 when payoutMultiplier > 0:
                    embedBuilder.WithColor( Color.LightGrey );
                    break;
                default:
                    embedBuilder.WithColor( Color.Red );
                    break;
            }

            return embedBuilder.Build();
        }

        bool ValidateAdvancedBet(ref float bet, out string? error) {
            error = null;
            switch (bet) {
                case < MIN_BET_ADV:
                    error = $"Bet must be at least ${MIN_BET_ADV:C2}.";
                    return false;
                case > MAX_BET_ADV:
                    bet = MAX_BET_ADV; // Cap bet
                    break;
            }

            if ( PlayersWallet.GetBalance( Context.Guild.Id, Context.User.Id ) < bet ) {
                error = $"{Context.User.Mention} does’t have enough cash! Your balance: ${PlayersWallet.GetBalance( Context.Guild.Id, Context.User.Id ):C2}. Bet: ${bet:C2}.";
                return false;
            }

            return true;
        }

        async Task PlayAdvancedSlots(float bet, int rows, int cols, string commandPrefix, bool isSpinAgain = false, SocketInteraction? interaction = null) {
            float currentBet = bet;
            if ( !isSpinAgain ) {
                if ( !ValidateAdvancedBet( ref currentBet, out string? error ) ) {
                    await RespondAsync( error, ephemeral: true );
                    return;
                }
            }
            else if ( interaction != null ) // Spin again funds check
            {
                if ( PlayersWallet.GetBalance( Context.Guild.Id, Context.User.Id  ) < currentBet ) {
                    await interaction.ModifyOriginalResponseAsync( m => {
                            m.Content = $"{Context.User.Mention} doesn't have enough cash for ${currentBet:C2}!";
                            m.Components = new ComponentBuilder().Build();
                        }
                    );
                    return;
                }
            }

            PlayersWallet.SubtractFromBalance( Context.Guild.Id, Context.User.Id , currentBet );
            AdvancedSlotIcon[][] spinResult = SpinGrid( rows, cols );
            (decimal payoutMult, string winDesc) = CalculateGridPayout( spinResult, rows, cols );
            decimal totalReturned = (decimal)currentBet * payoutMult;

            if ( payoutMult > 0 ) {
                PlayersWallet.AddToBalance( Context.Guild.Id, Context.User.Id , (float)totalReturned );
            }else {
                CasinoManager.AddToSlotsJackpots( Context.Guild.Id, bet );
            }

            var embed = BuildAdvancedEmbed( Context.User, spinResult, currentBet, payoutMult, winDesc, totalReturned, rows, cols );
            var buttons = new ComponentBuilder()
                .WithButton( "Spin Again", $"{commandPrefix}_again_{currentBet.ToString( CultureInfo.InvariantCulture )}_{rows}x{cols}" )
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
            
            // Announce jackpot wins
            if ( CasinoManager.GetJackpot( Context.Guild.Id, JackpotType.MegaJackpot, out float jackpot ) ) {
                var msg = $"🎉 {Context.User.Mention} has hit the **Mega Jackpot** of **{jackpot:C2}** on {commandPrefix}!";
                await Context.Channel.SendMessageAsync( msg ); // Send as a new message to the channel
                return;
            }
            if ( CasinoManager.GetJackpot( Context.Guild.Id, JackpotType.ProgressiveJackpot, out jackpot ) ) {
                var msg = $"🎉 {Context.User.Mention} has hit the **Progressive Jackpot** of **{jackpot:C2}** on {commandPrefix}!";
                await Context.Channel.SendMessageAsync( msg ); // Send as a new message to the channel
                return;
            }
            if ( CasinoManager.GetJackpot( Context.Guild.Id, JackpotType.MiniJackpot, out jackpot ) ) {
                var msg = $"🎉 {Context.User.Mention} has hit the **Mini Jackpot** of **{jackpot:C2}** on {commandPrefix}!";
                await Context.Channel.SendMessageAsync( msg ); // Send as a new message to the channel
            }
        }

        // --- 5x4 Slot ---
        [SlashCommand( "slots5x4", "Play a 5-reel, 4-row advanced slot machine." )]
        public async Task Slots5X4Async([Summary( description: "Your bet amount" )] float bet)
            => await PlayAdvancedSlots( bet, 4, 5, CMD_PREFIX5_X4 );

        [ComponentInteraction( "advslots5x4_again_*_*" )] // bet_RxC
        public async Task OnSpinAgain5x4(string rawBet, string gridSizeRaw) {
            await DeferAsync( ephemeral: true );
            HandleAdvancedSpinAgain( rawBet, gridSizeRaw, 4, 5, CMD_PREFIX5_X4 );
        }
        [ComponentInteraction( "advslots5x4_end" )]
        public async Task OnEnd5x4() => await HandleAdvancedEndGame( "Advanced Slots (5x4)" );

        // --- 5x5 Slot ---
        [SlashCommand( "slots5x5", "Play a 5-reel, 5-row advanced slot machine." )]
        public async Task Slots5X5Async([Summary( description: "Your bet amount" )] float bet)
            => await PlayAdvancedSlots( bet, 5, 5, CMD_PREFIX5_X5 );

        [ComponentInteraction( "advslots5x5_again_*_*" )] // bet_RxC
        public async Task OnSpinAgain5x5(string rawBet, string gridSizeRaw) {
            await DeferAsync( ephemeral: true );
            HandleAdvancedSpinAgain( rawBet, gridSizeRaw, 5, 5, CMD_PREFIX5_X5 );
        }
        [ComponentInteraction( "advslots5x5_end" )]
        public async Task OnEnd5x5() => await HandleAdvancedEndGame( "Advanced Slots (5x5)" );


        async void HandleAdvancedSpinAgain(string rawBet, string gridSizeRaw, int expectedRows, int expectedCols, string cmdPrefix) {
            try {
                if ( !float.TryParse( rawBet, NumberStyles.Float, CultureInfo.InvariantCulture, out float bet ) ) {
                    await Context.Interaction.ModifyOriginalResponseAsync( m => m.Content = "Invalid bet data." );
                    return;
                }

                // gridSizeRaw is part of the ID like "4x5", verify it if needed, though PlayAdvancedSlots takes rows/cols explicitly
                await PlayAdvancedSlots( bet, expectedRows, expectedCols, cmdPrefix, true, Context.Interaction );
            }
            catch (Exception e) {
                await Context.Interaction.ModifyOriginalResponseAsync( m => m.Content = $"Error: {e.Message}" );
                Console.WriteLine( $"Error in HandleAdvancedSpinAgain: {e.Message}" );
            }
            finally {
                // Cleanup if needed
                // to await Context.Interaction.ModifyOriginalResponseAsync(m => m.Components = new ComponentBuilder().Build());
            }
        }

        async Task HandleAdvancedEndGame(string gameTitle) {
            await DeferAsync( ephemeral: true ); // Defer if not already (though main handlers do)
            await Context.Interaction.ModifyOriginalResponseAsync( m => {
                    m.Embed = new EmbedBuilder().WithTitle( $"{gameTitle} – Game Over" ).WithDescription( $"{Context.User.Mention} ended the game." ).Build();
                    m.Components = new ComponentBuilder().Build();
                }
            );
        }
    }
}