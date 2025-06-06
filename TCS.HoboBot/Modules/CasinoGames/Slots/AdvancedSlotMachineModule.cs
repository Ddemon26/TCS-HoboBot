using System.Globalization;
using System.Text;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using TCS.HoboBot.Data; // Assumed to exist

namespace TCS.HoboBot.Modules.CasinoGames.Slots {
    public enum AdvancedSlotIcon {
        Nine, Ten, Jack, Queen, King, Ace, GemPurple, GemBlue, GemGreen, GemRed,
        Wild, Scatter, MiniGame, Locked,
    }

    public sealed class AdvancedSlotMachineModule : InteractionModuleBase<SocketInteractionContext> {
        static readonly Random Rng = new();

        // --- Command Constants ---
        const float MIN_BET_ADV = 5f;
        const float MAX_BET_ADV = 10_000f;
        const string CMD_PREFIX5_X5 = "advslots5x5";

        // --- Game Constants ---
        public const int MINIGAME_TRIGGER_COUNT = 4;
        public const float MINIGAME_ICON_BOOST_CHANCE = 35f;
        public const int ROWS = 5; // Assuming this is fixed for "Advanced Slots" tied to CMD_PREFIX5_X5
        public const int COLS = 5; // Assuming this is fixed
        public const int GRID_SIZE = ROWS * COLS;

        public static readonly IReadOnlyDictionary<AdvancedSlotIcon, string> IconToEmojiMap =
            new Dictionary<AdvancedSlotIcon, string> {
                { AdvancedSlotIcon.Nine, "🍒" }, { AdvancedSlotIcon.Ten, "🍋" }, { AdvancedSlotIcon.Jack, "♣️" },
                { AdvancedSlotIcon.Queen, "♦️" }, { AdvancedSlotIcon.King, "♥️" }, { AdvancedSlotIcon.Ace, "7️⃣" },
                { AdvancedSlotIcon.GemPurple, "💜" }, { AdvancedSlotIcon.GemBlue, "💎" }, { AdvancedSlotIcon.GemGreen, "💚" },
                { AdvancedSlotIcon.GemRed, "❤️‍🔥" }, { AdvancedSlotIcon.Wild, "🃏" }, { AdvancedSlotIcon.Scatter, "💲" },
                { AdvancedSlotIcon.MiniGame, "⭐" }, { AdvancedSlotIcon.Locked, "🔒" },
            };

        public static readonly Dictionary<AdvancedSlotIcon, double> SymbolWeights = new() {
            { AdvancedSlotIcon.Nine, 12 }, { AdvancedSlotIcon.Ten, 12 }, { AdvancedSlotIcon.Jack, 10 },
            { AdvancedSlotIcon.Queen, 10 }, { AdvancedSlotIcon.King, 10 }, { AdvancedSlotIcon.Ace, 8 },
            { AdvancedSlotIcon.GemPurple, 4 }, { AdvancedSlotIcon.GemBlue, 4 }, { AdvancedSlotIcon.GemGreen, 4 },
            { AdvancedSlotIcon.GemRed, 3 }, { AdvancedSlotIcon.Wild, 3.2 }, { AdvancedSlotIcon.Scatter, 1.4 },
            { AdvancedSlotIcon.MiniGame, 4.8 },
        };

        public static readonly Dictionary<AdvancedSlotIcon, IReadOnlyDictionary<int, decimal>> FinalLinePayouts =
            new() {
                { AdvancedSlotIcon.Nine, new Dictionary<int, decimal> { { 2, 0.4m }, { 3, 0.5m }, { 4, 1.0m }, { 5, 1.4m } } },
                { AdvancedSlotIcon.Ten, new Dictionary<int, decimal> { { 2, 0.4m }, { 3, 0.5m }, { 4, 1.0m }, { 5, 1.4m } } },
                { AdvancedSlotIcon.Jack, new Dictionary<int, decimal> { { 2, 0.5m }, { 3, 0.6m }, { 4, 1.1m }, { 5, 1.5m } } },
                { AdvancedSlotIcon.Queen, new Dictionary<int, decimal> { { 2, 0.5m }, { 3, 0.6m }, { 4, 1.1m }, { 5, 1.5m } } },
                { AdvancedSlotIcon.King, new Dictionary<int, decimal> { { 2, 0.6m }, { 3, 0.7m }, { 4, 1.6m }, { 5, 2.0m } } },
                { AdvancedSlotIcon.Ace, new Dictionary<int, decimal> { { 2, 0.8m }, { 3, 1.0m }, { 4, 2.0m }, { 5, 2.5m } } },
                { AdvancedSlotIcon.GemPurple, new Dictionary<int, decimal> { { 2, 1.0m }, { 3, 1.5m }, { 4, 2.5m }, { 5, 3.0m } } },
                { AdvancedSlotIcon.GemBlue, new Dictionary<int, decimal> { { 2, 1.0m }, { 3, 1.5m }, { 4, 2.5m }, { 5, 3.0m } } },
                { AdvancedSlotIcon.GemGreen, new Dictionary<int, decimal> { { 2, 1.0m }, { 3, 1.5m }, { 4, 2.5m }, { 5, 3.0m } } },
                { AdvancedSlotIcon.GemRed, new Dictionary<int, decimal> { { 2, 1.5m }, { 3, 2.0m }, { 4, 3.0m }, { 5, 3.5m } } },
                { AdvancedSlotIcon.Wild, new Dictionary<int, decimal> { { 2, 1.5m }, { 3, 2.5m }, { 4, 3.5m }, { 5, 4.0m } } },
            };

        public static readonly Dictionary<int, decimal> FixedScatterPayouts = new() {
            { 2, 2.5m }, { 3, 3m }, { 4, 5m }, { 5, 10m },
        };

        public static readonly Dictionary<int, decimal> MiniGamePayouts = new() {
            { 5, 2m }, { 6, 2.5m }, { 7, 3m }, { 8, 3.5m }, { 9, 4m }, { 10, 5m }, { 11, 6m }, { 12, 7m },
            { 13, 8m }, { 14, 9.5m }, { 15, 11m }, { 16, 13m }, { 17, 15m },
            { 18, 18m }, { 19, 22m }, { 20, 27m }, { 21, 35m }, { 22, 45m },
            { 23, 60m }, { 24, 80m }, { 25, 100m },
        };

        static readonly double TotalWeight;
        public static readonly List<KeyValuePair<AdvancedSlotIcon, double>> CumulativeWeights;

        // Optimizations: Pre-calculated values
        public static readonly IReadOnlyList<List<int>> PaylinesFor5X5Grid;
        static readonly IReadOnlyList<AdvancedSlotIcon> StaticNonSpecialSymbolsForMiniGame;
        static readonly IReadOnlyList<KeyValuePair<int, decimal>> StaticSortedMiniGamePayouts;


        static AdvancedSlotMachineModule() {
            TotalWeight = SymbolWeights.Values.Sum();
            double cumulative = 0;
            CumulativeWeights = new List<KeyValuePair<AdvancedSlotIcon, double>>( SymbolWeights.Count );
            foreach ((var symbol, double weight) in SymbolWeights.OrderBy( kvp => kvp.Key.ToString() )) {
                // Consistent order for FindIndex
                cumulative += weight;
                CumulativeWeights.Add( new KeyValuePair<AdvancedSlotIcon, double>( symbol, cumulative ) );
            }

            // Pre-calculate paylines for 5x5 grid (OLD)
            List<List<int>> paylines = [];
            for (var r = 0; r < ROWS; r++) paylines.Add( Enumerable.Repeat( r, COLS ).ToList() );
            if ( ROWS >= 3 ) {
                // ROWS is 5, COLS is 5
                paylines.Add( [0, 1, 2, 1, 0] );
                paylines.Add( [ROWS - 1, ROWS - 2, ROWS - 3, ROWS - 2, ROWS - 1] );
            }
            
            PaylinesFor5X5Grid = paylines;
            
            /*// Pre-calculate paylines for 5x5 grid (NEW)
            List<List<int>> paylines = [];

            // Current Horizontal Lines (5 lines)
            // Lines are 0-indexed, so Line 1 is index 0 for description purposes later.
            // Example: Line 1 is [0,0,0,0,0], Line 2 is [1,1,1,1,1], etc.
            for (var r = 0; r < ROWS; r++) paylines.Add( Enumerable.Repeat( r, COLS ).ToList() );

            // Current V-Shapes (2 lines)
            // These are Lines 6 and 7
            if ( ROWS >= 3 ) { // ROWS is 5, COLS is 5
                paylines.Add( [0, 1, 2, 1, 0] ); // Line 6
                paylines.Add( [ROWS - 1, ROWS - 2, ROWS - 3, ROWS - 2, ROWS - 1] ); // Line 7 (For 5x5: [4,3,2,3,4])
            }

            // --- ADDING NEW PAYLINES ---
            // Total lines so far: 5 horizontal + 2 V-shapes = 7 lines.
            // We will number new lines starting from 8.

            // Full Diagonals (2 lines)
            paylines.Add( [0, 1, 2, 3, 4] ); // Line 8: Top-left to bottom-right
            paylines.Add( [4, 3, 2, 1, 0] ); // Line 9: Bottom-left to top-right

            // Additional V-shapes / U-shapes (6 lines)
            paylines.Add( [1, 2, 3, 2, 1] ); // Line 10: Middle V-shape
            paylines.Add( [3, 2, 1, 2, 3] ); // Line 11: Middle Inverted V-shape
            paylines.Add( [0, 0, 1, 2, 2] ); // Line 12: Stair down variant
            paylines.Add( [4, 4, 3, 2, 2] ); // Line 13: Stair up variant
            paylines.Add( [2, 1, 0, 1, 2] ); // Line 14: Wider V-shape (Apex at top)
            paylines.Add( [2, 3, 4, 3, 2] ); // Line 15: Wider Inverted V-shape (Apex at bottom)

            // Zig-zags / Waves (8 lines)
            paylines.Add( [0, 1, 0, 1, 0] ); // Line 16
            paylines.Add( [1, 0, 1, 0, 1] ); // Line 17
            paylines.Add( [4, 3, 4, 3, 4] ); // Line 18
            paylines.Add( [3, 4, 3, 4, 3] ); // Line 19
            paylines.Add( [2, 3, 2, 1, 2] ); // Line 20 (M-shape centered on row 2)
            paylines.Add( [2, 1, 2, 3, 2] ); // Line 21 (W-shape centered on row 2)
            paylines.Add( [1, 2, 1, 2, 1] ); // Line 22
            paylines.Add( [3, 2, 3, 2, 3] ); // Line 23

            // Total paylines = 7 (original) + 16 (new) = 23 paylines
            PaylinesFor5X5Grid = paylines;*/

            StaticNonSpecialSymbolsForMiniGame = SymbolWeights.Keys
                .Where( icon => icon != AdvancedSlotIcon.Scatter && icon != AdvancedSlotIcon.Wild && icon != AdvancedSlotIcon.MiniGame )
                .ToList();

            StaticSortedMiniGamePayouts = MiniGamePayouts
                .OrderByDescending( p => p.Key )
                .ToList();
        }

        static decimal GetAdjustedSymbolPayout(AdvancedSlotIcon symbol, int count) {
            if ( FinalLinePayouts.TryGetValue( symbol, out IReadOnlyDictionary<int, decimal>? payouts ) && payouts.TryGetValue( count, out decimal payout ) ) {
                return payout;
            }

            return 0m;
        }

        public static decimal GetAdjustedScatterPayout(int count) {
            return FixedScatterPayouts.GetValueOrDefault( count, 0m );
        }

        static string GetEmoji(AdvancedSlotIcon icon) => IconToEmojiMap.GetValueOrDefault( icon, "❓" );

        static AdvancedSlotIcon GetRandomReelSymbol() {
            double randomValue = Rng.NextDouble() * TotalWeight;
            // FindIndex is efficient for sorted lists if we need to find the first match.
            // The current loop structure is also fine for small N.
            int index = CumulativeWeights.FindIndex( kvp => randomValue < kvp.Value );
            if ( index != -1 ) {
                return CumulativeWeights[index].Key;
            }

            // Fallback, should ideally not be hit if logic is correct and SymbolWeights is not empty
            return CumulativeWeights.LastOrDefault().Key; // Or handle empty list if possible
        }

        static AdvancedSlotIcon[][] SpinGrid(int rows, int cols) {
            AdvancedSlotIcon[][] grid = new AdvancedSlotIcon[ rows ][];
            for (var r = 0; r < rows; r++) {
                grid[r] = new AdvancedSlotIcon[ cols ];
                for (var c = 0; c < cols; c++) {
                    grid[r][c] = GetRandomReelSymbol();
                }
            }

            return grid;
        }

        public static (decimal totalBetMultiplier, string winDescription) CalculateGridPayout(AdvancedSlotIcon[][] grid, int rows, int cols, IReadOnlyList<List<int>> paylines) {
            var combinedLineMultiplier = 0m;
            List<string> winDescriptions = []; // Capacity can be pre-set if average win lines are known

            for (var i = 0; i < paylines.Count; i++) {
                List<int> linePath = paylines[i];
                var firstReelSymbol = grid[linePath[0]][0];
                var symbolToMatch = firstReelSymbol;

                if ( firstReelSymbol == AdvancedSlotIcon.Wild ) {
                    for (var c = 1; c < cols; c++) {
                        var currentSymbolOnLine = grid[linePath[c]][c];
                        if ( currentSymbolOnLine != AdvancedSlotIcon.Wild ) {
                            symbolToMatch = currentSymbolOnLine;
                            break;
                        }
                    }
                    // If all are Wilds, the symbolToMatch remains Wild, which is correct.
                }

                if ( symbolToMatch == AdvancedSlotIcon.Scatter || symbolToMatch == AdvancedSlotIcon.MiniGame ) {
                    continue; // Scatters and MiniGame icons don't form line wins
                }

                var streak = 0;
                for (var c = 0; c < cols; c++) {
                    var currentSymbolOnLine = grid[linePath[c]][c];
                    if ( currentSymbolOnLine == symbolToMatch || currentSymbolOnLine == AdvancedSlotIcon.Wild ) {
                        streak++;
                    }
                    else {
                        break;
                    }
                }

                if ( streak >= 2 ) {
                    // Minimum streak for a payout
                    decimal lineMultiplier = GetAdjustedSymbolPayout( symbolToMatch, streak );
                    if ( lineMultiplier > 0m ) {
                        combinedLineMultiplier += lineMultiplier;
                        winDescriptions.Add( $"Line {i + 1}: {streak}x{GetEmoji( symbolToMatch )} ({lineMultiplier:0.##}x)" );
                    }
                }
            }

            // Optimized scatter count
            var scatterCount = 0;
            for (var r = 0; r < rows; r++) {
                for (var c = 0; c < cols; c++) {
                    if ( grid[r][c] == AdvancedSlotIcon.Scatter ) {
                        scatterCount++;
                    }
                }
            }

            if ( scatterCount > 0 ) {
                decimal scatterMultiplier = GetAdjustedScatterPayout( scatterCount );
                if ( scatterMultiplier > 0m ) {
                    combinedLineMultiplier += scatterMultiplier;
                    winDescriptions.Add( $"{scatterCount}x{GetEmoji( AdvancedSlotIcon.Scatter )} Scatters pay {scatterMultiplier:0.##}x total bet!" );
                }
            }


            string finalDesc = winDescriptions.Count > 0 ? string.Join( '\n', winDescriptions ) : "No wins this round.";
            return (combinedLineMultiplier, finalDesc);
        }

        Embed BuildAdvancedEmbed(
            SocketUser user,
            AdvancedSlotIcon[][] grid,
            float bet,
            decimal payoutMultiplier,
            string description,
            decimal totalWinnings,
            string? titleOverride = null
        ) {
            var gridDisplay = new StringBuilder(); // Consider pre-sizing: rows * (cols * (emojiAvgLen + 3))
            for (var r = 0; r < ROWS; r++) {
                for (var c = 0; c < COLS; c++) {
                    gridDisplay.Append( GetEmoji( grid[r][c] ) );
                    if ( c < COLS - 1 ) {
                        gridDisplay.Append( " | " );
                    }
                }

                gridDisplay.Append( '\n' );
            }

            var userBalance = (decimal)PlayersWallet.GetBalance( Context.Guild.Id, user.Id ); // Get balance once for the embed
            decimal profit = totalWinnings - (decimal)bet;
            string outcome;

            if ( payoutMultiplier == -1 ) {
                // Mini-game active state
                outcome = $"Collect as many {GetEmoji( AdvancedSlotIcon.MiniGame )} as possible!";
            }
            else {
                outcome = payoutMultiplier switch {
                    0m => $"Unlucky! You lost {bet:C2}.",
                    _ when profit == 0 && payoutMultiplier > 0 => $"Push! Your **{bet:C2}** bet is returned.", // Ensure payoutMultiplier > 0 for true push
                    _ => $"Total Multiplier: ({payoutMultiplier:F1}x)\n\n" +
                         $"Congratulations! You won {profit:C2} (Total: {totalWinnings:C2}).",
                };
                outcome += $"\nYour new balance: {userBalance:C2}";
            }

            var embed = new EmbedBuilder()
                .WithTitle( titleOverride ?? $"Advanced Slots ({ROWS}x{COLS}) – {bet:C2} Bet" )
                .WithDescription( $"{user.Mention} spins the reels…\n\n{gridDisplay.ToString().TrimEnd()}\n\n{description}" ) // TrimEnd for trailing newline
                .WithFooter( outcome );

            embed.WithColor( payoutMultiplier == -1 ? Color.Gold : profit > 0 ? Color.Green : profit == 0 && payoutMultiplier > 0 ? Color.LightGrey : Color.Red );
            return embed.Build();
        }

        bool ValidateAdvancedBet(ref float bet, out string? error) {
            error = null;
            switch (bet) {
                case < MIN_BET_ADV:
                    error = $"Bet must be at least ${MIN_BET_ADV:C2}.";
                    return false;
                case > MAX_BET_ADV:
                    bet = MAX_BET_ADV; // Cap bet, don't error
                    break;
            }

            // Get balance at once
            var currentBalance = (decimal)PlayersWallet.GetBalance( Context.Guild.Id, Context.User.Id );
            if ( currentBalance < (decimal)bet ) {
                error = $"{Context.User.Mention} does’t have enough cash! Balance: ${currentBalance:C2}. Bet: ${bet:C2}.";
                return false;
            }

            return true;
        }

        async Task PlayAdvancedSlots(float bet, int rows, int cols, string commandPrefix, bool isSpinAgain = false, SocketInteraction? interaction = null) {
            float currentBet = bet;

            if ( isSpinAgain && interaction != null ) {
                var currentBalance = (decimal)PlayersWallet.GetBalance( Context.Guild.Id, Context.User.Id );
                if ( currentBalance < (decimal)currentBet ) {
                    await interaction.ModifyOriginalResponseAsync( m => {
                            m.Content = $"{Context.User.Mention} doesn't have enough cash for ${currentBet:C2}! Your balance: ${currentBalance:C2}";
                            m.Components = new ComponentBuilder().Build();
                        }
                    );
                    return;
                }
            }
            else {
                if ( !ValidateAdvancedBet( ref currentBet, out string? failReason ) ) {
                    await RespondAsync( failReason, ephemeral: true );
                    return;
                }
            }

            if ( !isSpinAgain ) {
                await DeferAsync( ephemeral: true );
            }

            PlayersWallet.SubtractFromBalance( Context.Guild.Id, Context.User.Id, currentBet );
            AdvancedSlotIcon[][] spin = SpinGrid( rows, cols );

            // Optimized miniGameIconCount
            var miniGameIconCount = 0;
            for (var r = 0; r < rows; r++) {
                for (var c = 0; c < cols; c++) {
                    if ( spin[r][c] == AdvancedSlotIcon.MiniGame ) {
                        miniGameIconCount++;
                    }
                }
            }

            if ( miniGameIconCount >= MINIGAME_TRIGGER_COUNT ) {
                await StartMiniGame( currentBet, rows, cols, isSpinAgain ? interaction as SocketMessageComponent : null );
                return;
            }

            (decimal payoutMult, string winDesc) = CalculateGridPayout( spin, rows, cols, PaylinesFor5X5Grid ); // Use pre-calculated paylines
            decimal returned = (decimal)currentBet * payoutMult;

            if ( payoutMult > 0 ) {
                if ( CasinoManager.GetJackpot( Context.Guild.Id, currentBet, out var type, out float jackpotValue ) ) {
                    // Assuming currentBet is the original bet
                    winDesc += $"\n\n🎉 YOU hit the **{type} Jackpot** of **{jackpotValue:C2}!**";
                    returned += (decimal)jackpotValue;
                    var msg = $"🎉 {Context.User.Mention} has hit the **{type} Jackpot** of **{jackpotValue:C2}** Playing Advanced Slots!";
                    // Consider not awaiting this if it's not critical for the spin flow, or use ExecuteWithRetryAsync
                    _ = Context.Channel.SendMessageAsync( msg );
                }

                PlayersWallet.AddToBalance( Context.Guild.Id, Context.User.Id, (float)returned );
            }
            else {
                CasinoManager.AddToSlotsJackpots( Context.Guild.Id, currentBet );
            }

            var embed = BuildAdvancedEmbed( Context.User, spin, currentBet, payoutMult, winDesc, returned );
            var buttons = new ComponentBuilder()
                .WithButton( "Spin Again", $"{commandPrefix}_again_{currentBet.ToString( CultureInfo.InvariantCulture )}", style: ButtonStyle.Primary )
                .WithButton( "End", $"{commandPrefix}_end", ButtonStyle.Danger );

            if ( isSpinAgain && interaction != null ) {
                await interaction.ModifyOriginalResponseAsync( m => {
                        m.Embed = embed;
                        m.Components = buttons.Build();
                    }
                );
            }
            else {
                await FollowupAsync( embed: embed, components: buttons.Build(), ephemeral: true );
            }


            if ( payoutMult >= 5m ) {
                // Consider basing this on profit instead
                decimal profit = returned - (decimal)currentBet;
                if ( profit > 0 ) {
                    // This message can also be fire-and-forget or have minimal retry
                    _ = Context.Channel.SendMessageAsync( $"🎰{Context.User.Mention} wins **{profit:C2}** on Advanced Slots ({rows}x{cols})!" );
                }
            }
        }

        async Task StartMiniGame(float bet, int rows, int cols, SocketMessageComponent? interaction = null) {
            AdvancedSlotIcon[][] grid = new AdvancedSlotIcon[ rows ][];
            for (var r = 0; r < rows; r++) {
                grid[r] = new AdvancedSlotIcon[ cols ];
                for (var c = 0; c < cols; c++) {
                    grid[r][c] = AdvancedSlotIcon.Locked;
                }
            }

            const string description = $"🌟 **MINI-GAME ACTIVATED!** 🌟\n" +
                                       $"Press 'Spin Reel' to start revealing the columns!";

            var embed = BuildAdvancedEmbed( Context.User, grid, bet, -1, description, 0, "⭐ Mini-Game! ⭐" );
            // Custom ID format: "minigame_spin_{nextReel}_{bet}_{rows}x{cols}_{lockedReelData}"
            var customId = $"minigame_spin_1_{bet.ToString( CultureInfo.InvariantCulture )}_{rows}x{cols}_";

            var buttons = new ComponentBuilder()
                .WithButton( "Spin Reel 1", customId, ButtonStyle.Success )
                .WithButton( "End", $"{CMD_PREFIX5_X5}_end", ButtonStyle.Danger );

            if ( interaction != null ) {
                await interaction.ModifyOriginalResponseAsync( m => {
                        m.Embed = embed;
                        m.Components = buttons.Build();
                    }
                );
            }
            else {
                await FollowupAsync( embed: embed, components: buttons.Build(), ephemeral: true );
            }
        }

        [ComponentInteraction( "minigame_spin_*" )]
        public async Task HandleMiniGameSpin(string rawData) {
            // Parameter name changed from "data" to avoid conflict if it's a property.
            // Do not DeferAsync() here if the plan is to immediately modify the original response.
            // If DeferAsync is used, FollowupAsync should be used for the first message,
            // but the following modifications should be to that followup.
            // For component interactions, DeferAsync() is usually called to acknowledge, then ModifyOriginalResponseAsync.

            await DeferAsync(); // Acknowledge the interaction

            string[] parts = rawData.Split( '_' ); // Original uses 'data,' which might be a class property
            int reelToSpin = int.Parse( parts[0] ); // First part of the custom ID actual data
            float bet = float.Parse( parts[1], CultureInfo.InvariantCulture );
            string[] dimensionsStr = parts[2].Split( 'x' );
            int rows = int.Parse( dimensionsStr[0] );
            int cols = int.Parse( dimensionsStr[1] );
            // lockedReelData is everything after the fourth underscore (parts[3] onwards)
            string lockedReelData = parts.Length > 3 ? string.Join( "_", parts.Skip( 3 ) ) : "";


            AdvancedSlotIcon[][] grid = new AdvancedSlotIcon[ rows ][];
            string[] lockedReelsStrings = lockedReelData.Split( '-', StringSplitOptions.RemoveEmptyEntries );
            for (var r = 0; r < rows; r++) {
                grid[r] = new AdvancedSlotIcon[ cols ];
                for (var c = 0; c < cols; c++) {
                    if ( c < lockedReelsStrings.Length ) {
                        // Ensure parsing is safe
                        AdvancedSlotIcon[] reelIcons = lockedReelsStrings[c]
                            .Split( ',', StringSplitOptions.RemoveEmptyEntries )
                            .Select( s => Enum.TryParse<AdvancedSlotIcon>( s, out var icon ) ? icon : AdvancedSlotIcon.Locked ) // Safer parsing
                            .ToArray();
                        grid[r][c] = (r < reelIcons.Length) ? reelIcons[r] : AdvancedSlotIcon.Locked; // Boundary check
                    }
                    else {
                        grid[r][c] = AdvancedSlotIcon.Locked;
                    }
                }
            }

            AdvancedSlotIcon[] newReel = SpinSingleMiniGameReel( rows );
            for (var r = 0; r < rows; r++) {
                if ( reelToSpin - 1 < cols && reelToSpin - 1 >= 0 ) // Boundary check for reelToSpin
                    grid[r][reelToSpin - 1] = newReel[r];
            }

            List<string> existingReels = lockedReelData.Split( '-', StringSplitOptions.RemoveEmptyEntries ).ToList();
            string currentReelIconIds = string.Join( ",", newReel.Select( i => (int)i ) );

            // Ensure we are replacing or adding at the correct position
            int currentReelIndex = reelToSpin - 1;
            while (existingReels.Count <= currentReelIndex) {
                existingReels.Add( "" ); // Pad with empty strings if needed, though ideally it's sequential
            }

            existingReels[currentReelIndex] = currentReelIconIds;
            string newLockedReelDataString = string.Join( "-", existingReels );


            if ( reelToSpin < cols ) {
                int nextReel = reelToSpin + 1;
                var description = $"Reel {reelToSpin} revealed! Total Stars: {CountStars( grid )}"; // CountStars is fine
                var embed = BuildAdvancedEmbed( Context.User, grid, bet, -1, description, 0, $"⭐ Mini-Game! Reel {nextReel} ⭐" );
                var customId = $"minigame_spin_{nextReel}_{bet.ToString( CultureInfo.InvariantCulture )}_{rows}x{cols}_{newLockedReelDataString}";
                var buttons = new ComponentBuilder()
                    .WithButton( $"Spin Reel {nextReel}", customId, ButtonStyle.Success )
                    .WithButton( "End", $"{CMD_PREFIX5_X5}_end", ButtonStyle.Danger );
                await ModifyOriginalResponseAsync( m => {
                        m.Embed = embed;
                        m.Components = buttons.Build();
                    }
                );
            }
            else {
                int totalStars = CountStars( grid );
                decimal multiplier = CalculateMiniGamePayout( totalStars ); // Uses pre-sorted payouts
                decimal winnings = (decimal)bet * multiplier;
                if ( winnings > 0 ) PlayersWallet.AddToBalance( Context.Guild.Id, Context.User.Id, (float)winnings );

                string description = $"**Mini-Game Over!**\n\n" +
                                     $"You collected a total of **{totalStars}** {GetEmoji( AdvancedSlotIcon.MiniGame )} stars!\n" +
                                     $"This gives you a **{multiplier:0.##}x** multiplier!";
                var embed = BuildAdvancedEmbed( Context.User, grid, bet, multiplier, description, winnings, "⭐ Mini-Game Complete! ⭐" );
                var buttons = new ComponentBuilder()
                    .WithButton( "Spin Again", $"{CMD_PREFIX5_X5}_again_{bet.ToString( CultureInfo.InvariantCulture )}", style: ButtonStyle.Primary )
                    .WithButton( "End", $"{CMD_PREFIX5_X5}_end", ButtonStyle.Danger );
                await ModifyOriginalResponseAsync( m => {
                        m.Embed = embed;
                        m.Components = buttons.Build();
                    }
                );

                if ( multiplier >= 5m ) {
                    decimal profit = winnings - (decimal)bet; // Profit calculation for mini-game
                    if ( profit > 0 ) {
                        _ = Context.Channel.SendMessageAsync( $"🎰 {Context.User.Mention} wins **{profit:C2}** on the Advanced Slots Mini-Game!" );
                    }
                }
            }
        }

        static int CountStars(AdvancedSlotIcon[][] grid) {
            var starCount = 0;
            // Optimized count
            for (var r = 0; r < grid.Length; r++) {
                for (var c = 0; c < grid[r].Length; c++) {
                    if ( grid[r][c] == AdvancedSlotIcon.MiniGame ) {
                        starCount++;
                    }
                }
            }

            return starCount;
        }

        AdvancedSlotIcon[] SpinSingleMiniGameReel(int rows) {
            AdvancedSlotIcon[] reel = new AdvancedSlotIcon[ rows ];
            for (var r = 0; r < rows; r++) {
                if ( Rng.Next( 100 ) < MINIGAME_ICON_BOOST_CHANCE ) {
                    reel[r] = AdvancedSlotIcon.MiniGame;
                }
                else {
                    // Use pre-calculated list of non-special symbols
                    reel[r] = StaticNonSpecialSymbolsForMiniGame[Rng.Next( StaticNonSpecialSymbolsForMiniGame.Count )];
                }
            }

            return reel;
        }

        public decimal CalculateMiniGamePayout(int totalIcons) {
            var multiplier = 0m;
            // Use pre-sorted list for mini-game payouts
            foreach (KeyValuePair<int, decimal> payout in StaticSortedMiniGamePayouts) {
                if ( totalIcons >= payout.Key ) {
                    multiplier = payout.Value;
                    break;
                }
            }

            return multiplier;
        }

        [SlashCommand( "slots5x5", "Play a 5-reel, 5-row advanced slot machine." )]
        public async Task Slots5X5Async([Summary( description: "Your bet amount" )] float bet)
            => await PlayAdvancedSlots( bet, ROWS, COLS, CMD_PREFIX5_X5 );

        [ComponentInteraction( "advslots5x5_again_*" )]
        public async Task OnSpinAgain5x5(string rawBetStringFromCustomId) {
            // Parameter name more descriptive
            // DeferAsync right at the start when handling component interactions
            // This acknowledges the interaction quickly.
            await DeferAsync( ephemeral: true ); // Assuming this interaction should also be ephemeral.
            // If it's modifying a public message, ephemeral might not be needed here.
            await HandleAdvancedSpinAgain( rawBetStringFromCustomId, ROWS, COLS, CMD_PREFIX5_X5 );
        }

        [ComponentInteraction( "advslots5x5_end" )]
        public async Task OnEnd5x5() {
            await DeferAsync( ephemeral: true ); // Acknowledge, then modify
            await HandleAdvancedEndGame( "Advanced Slots (5x5)" );
        }

        async Task HandleAdvancedSpinAgain(string rawBetData, int expectedRows, int expectedCols, string cmdPrefix) {
            // The bet amount is the last part after the final underscore.
            string betString = rawBetData.Substring( rawBetData.LastIndexOf( '_' ) + 1 );
            if ( !float.TryParse( betString, NumberStyles.Any, CultureInfo.InvariantCulture, out float bet ) ) {
                // If DeferAsync was called, use ModifyOriginalResponseAsync or FollowupAsync
                await ModifyOriginalResponseAsync( m => m.Content = "Invalid bet data for spin again." );
                return;
            }

            // Pass Context.Interaction which is a SocketMessageComponent from OnSpinAgain5x5
            await PlayAdvancedSlots( bet, expectedRows, expectedCols, cmdPrefix, true, Context.Interaction as SocketMessageComponent );
        }

        async Task HandleAdvancedEndGame(string gameTitle) {
            // No DeferAsync here if we used it in the calling method (e.g. OnEnd5x5)
            // If DeferAsync was used in OnEnd5x5:
            await ModifyOriginalResponseAsync( m => {
                    m.Embed = new EmbedBuilder()
                        .WithTitle( $"{gameTitle} – Game Over" )
                        .WithDescription( $"{Context.User.Mention} ended the game." )
                        .WithColor( Color.DarkGrey ) // Optional: different colour for ended game
                        .Build();
                    m.Components = new ComponentBuilder().Build(); // Remove buttons
                }
            );
        }
    }
}