﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
        public const int ROWS = 5;
        public const int COLS = 5;
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

        static readonly double TotalWeight;
        public static readonly List<KeyValuePair<AdvancedSlotIcon, double>> CumulativeWeights;

        static AdvancedSlotMachineModule() {
            TotalWeight = SymbolWeights.Values.Sum();
            double cumulative = 0;
            CumulativeWeights = [];
            foreach ((var symbol, double weight) in SymbolWeights) {
                cumulative += weight;
                CumulativeWeights.Add( new KeyValuePair<AdvancedSlotIcon, double>( symbol, cumulative ) );
            }
        }

        public static readonly Dictionary<int, decimal> MiniGamePayouts = new() {
            { 5, 2m }, { 6, 2.5m }, { 7, 3m }, { 8, 3.5m }, { 9, 4m }, { 10, 5m }, { 11, 6m }, { 12, 7m },
            { 13, 8m }, { 14, 9.5m }, { 15, 11m }, { 16, 13m }, { 17, 15m },
            { 18, 18m }, { 19, 22m }, { 20, 27m }, { 21, 35m }, { 22, 45m },
            { 23, 60m }, { 24, 80m }, { 25, 100m },
        };

        static decimal GetAdjustedSymbolPayout(AdvancedSlotIcon symbol, int count) {
            if ( FinalLinePayouts.TryGetValue( symbol, out IReadOnlyDictionary<int, decimal>? payouts ) && payouts.TryGetValue( count, out decimal payout ) ) {
                return payout;
            }

            return 0m;
        }

        public static decimal GetAdjustedScatterPayout(int count) {
            return FixedScatterPayouts.GetValueOrDefault( count, 0m );

        }

        static List<List<int>> GetPaylines(int rows, int cols) {
            List<List<int>> lines = [];
            if ( cols != 5 ) {
                return lines;
            }

            for (var r = 0; r < rows; r++) lines.Add( Enumerable.Repeat( r, cols ).ToList() );
            if ( rows >= 3 ) {
                lines.Add( [0, 1, 2, 1, 0] );
                lines.Add( [rows - 1, rows - 2, rows - 3, rows - 2, rows - 1] );
            }

            return lines;
        }

        static string GetEmoji(AdvancedSlotIcon icon) => IconToEmojiMap.GetValueOrDefault( icon, "❓" );

        static AdvancedSlotIcon GetRandomReelSymbol() {
            double randomValue = Rng.NextDouble() * TotalWeight;
            foreach ((var symbol, double cumulativeWeight) in CumulativeWeights) {
                if ( randomValue < cumulativeWeight ) {
                    return symbol;
                }
            }

            return CumulativeWeights.Last().Key;
        }

        public static AdvancedSlotIcon[][] SpinGrid(int rows, int cols) {
            AdvancedSlotIcon[][] grid = new AdvancedSlotIcon[ rows ][];
            for (var r = 0; r < rows; r++) {
                grid[r] = new AdvancedSlotIcon[ cols ];
                for (var c = 0; c < cols; c++) {
                    grid[r][c] = GetRandomReelSymbol();
                }
            }

            return grid;
        }

        public static (decimal totalBetMultiplier, string winDescription) CalculateGridPayout(AdvancedSlotIcon[][] grid, int rows, int cols) {
            var combinedLineMultiplier = 0m;
            List<string> winDescriptions = [];
            List<List<int>> paylines = GetPaylines( rows, cols );

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
                }

                if ( symbolToMatch == AdvancedSlotIcon.Scatter || symbolToMatch == AdvancedSlotIcon.MiniGame ) {
                    continue;
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
                    decimal lineMultiplier = GetAdjustedSymbolPayout( symbolToMatch, streak );
                    if ( lineMultiplier > 0m ) {
                        combinedLineMultiplier += lineMultiplier;
                        winDescriptions.Add( $"Line {i + 1}: {streak}x{GetEmoji( symbolToMatch )} ({lineMultiplier:0.##}x)" );
                    }
                }
            }

            int scatterCount = grid.SelectMany( r => r ).Count( s => s == AdvancedSlotIcon.Scatter );
            decimal scatterMultiplier = GetAdjustedScatterPayout( scatterCount );
            if ( scatterMultiplier > 0m ) {
                combinedLineMultiplier += scatterMultiplier;
                winDescriptions.Add( $"{scatterCount}x{GetEmoji( AdvancedSlotIcon.Scatter )} Scatters pay {scatterMultiplier:0.##}x total bet!" );
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
            int rows,
            int cols,
            string? titleOverride = null
        ) {
            var gridDisplay = new StringBuilder();
            for (var r = 0; r < rows; r++) {
                for (var c = 0; c < cols; c++) {
                    gridDisplay.Append( GetEmoji( grid[r][c] ) );
                    if ( c < cols - 1 ) {
                        gridDisplay.Append( " | " );
                    }
                }

                gridDisplay.Append( '\n' );
            }

            decimal profit = totalWinnings - (decimal)bet;
            string outcome;
            if ( payoutMultiplier == -1 ) {
                outcome = $"Collect as many {GetEmoji( AdvancedSlotIcon.MiniGame )} as possible!";
            }
            else {
                outcome = payoutMultiplier switch {
                    0m => $"Unlucky! You lost {bet:C2}.",
                    _ when profit == 0 => $"Push! Your **{bet:C2}** bet is returned.",
                    _ => $"Total Multiplier: ({payoutMultiplier:F1}x)\n\n" +
                         $"Congratulations! You won {profit:C2} (Total: {totalWinnings:C2}).",
                };
                outcome += $"\nYour new balance: {PlayersWallet.GetBalance( Context.Guild.Id, user.Id ):C2}";
            }

            var embed = new EmbedBuilder()
                .WithTitle( titleOverride ?? $"Advanced Slots ({rows}x{cols}) – {bet:C2} Bet" )
                .WithDescription( $"{user.Mention} spins the reels…\n\n{gridDisplay.ToString().Trim()}\n\n{description}" )
                .WithFooter( outcome );

            embed.WithColor( payoutMultiplier == -1 ? Color.Gold : profit > 0 ? Color.Green : profit == 0 && payoutMultiplier > 0 ? Color.LightGrey : Color.Red );
            return embed.Build();
        }

        bool ValidateAdvancedBet(ref float bet, out string? error) {
            error = null;
            if ( bet < MIN_BET_ADV ) {
                error = $"Bet must be at least ${MIN_BET_ADV:C2}.";
                return false;
            }

            if ( bet > MAX_BET_ADV ) {
                bet = MAX_BET_ADV;
            }

            if ( PlayersWallet.GetBalance( Context.Guild.Id, Context.User.Id ) < bet ) {
                error = $"{Context.User.Mention} doesn’t have enough cash! Balance: ${PlayersWallet.GetBalance( Context.Guild.Id, Context.User.Id ):C2}. Bet: ${bet:C2}.";
                return false;
            }

            return true;
        }

        async Task PlayAdvancedSlots(float bet, int rows, int cols, string commandPrefix, bool isSpinAgain = false, SocketInteraction? interaction = null) {
            float currentBet = bet;

            if ( isSpinAgain && interaction != null ) {
                if ( PlayersWallet.GetBalance( Context.Guild.Id, Context.User.Id ) < currentBet ) {
                    await interaction.ModifyOriginalResponseAsync( m => {
                            m.Content = $"{Context.User.Mention} doesn't have enough cash for ${currentBet:C2}!";
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

            int miniGameIconCount = spin.SelectMany( r => r ).Count( s => s == AdvancedSlotIcon.MiniGame );
            if ( miniGameIconCount >= MINIGAME_TRIGGER_COUNT ) {
                await StartMiniGame( currentBet, rows, cols, isSpinAgain ? interaction as SocketMessageComponent : null );
                return;
            }

            (decimal payoutMult, string winDesc) = CalculateGridPayout( spin, rows, cols );
            decimal returned = (decimal)currentBet * payoutMult;

            if ( payoutMult > 0 ) {
                if ( CasinoManager.GetJackpot( Context.Guild.Id, bet, out var type, out float jackpotValue ) ) {
                    winDesc += $"\n\n🎉 YOU hit the **{type} Jackpot** of **{jackpotValue:C2}!**";
                    returned += (decimal)jackpotValue;
                    var msg = $"🎉 {Context.User.Mention} has hit the **{type} Jackpot** of **{jackpotValue:C2}** Playing Advanced Slots!";
                    await Context.Channel.SendMessageAsync( msg );
                }

                PlayersWallet.AddToBalance( Context.Guild.Id, Context.User.Id, (float)returned );
            }
            else {
                CasinoManager.AddToSlotsJackpots( Context.Guild.Id, bet );
            }

            var embed = BuildAdvancedEmbed( Context.User, spin, currentBet, payoutMult, winDesc, returned, rows, cols );
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
                decimal profit = returned - (decimal)currentBet;
                if ( profit > 0 ) {
                    await Context.Channel.SendMessageAsync( $"🎰{Context.User.Mention} wins **{profit:C2}** on Advanced Slots ({rows}x{cols})!" );
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
            
            var embed = BuildAdvancedEmbed( Context.User, grid, bet, -1, description, 0, rows, cols, "⭐ Mini-Game! ⭐" );
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
        public async Task HandleMiniGameSpin(string data) {
            await DeferAsync();

            string[] parts = data.Split( '_' );
            int reelToSpin = int.Parse( parts[0] );
            float bet = float.Parse( parts[1], CultureInfo.InvariantCulture );
            int[] dimensions = parts[2].Split( 'x' ).Select( int.Parse ).ToArray();
            int rows = dimensions[0];
            int cols = dimensions[1];
            string lockedReelData = parts.Length > 3 ? parts[3] : "";

            AdvancedSlotIcon[][] grid = new AdvancedSlotIcon[ rows ][];
            string[] lockedReels = lockedReelData.Split( '-', StringSplitOptions.RemoveEmptyEntries );
            for (var r = 0; r < rows; r++) {
                grid[r] = new AdvancedSlotIcon[ cols ];
                for (var c = 0; c < cols; c++) {
                    if ( c < lockedReels.Length ) {
                        AdvancedSlotIcon[] reelIcons = lockedReels[c].Split( ',' ).Select( s => (AdvancedSlotIcon)int.Parse( s ) ).ToArray();
                        grid[r][c] = reelIcons[r];
                    }
                    else {
                        grid[r][c] = AdvancedSlotIcon.Locked;
                    }
                }
            }

            AdvancedSlotIcon[] newReel = SpinSingleMiniGameReel( rows );
            for (var r = 0; r < rows; r++) {
                grid[r][reelToSpin - 1] = newReel[r];
            }

            // ... (logic for newLockedReelData remains the same)
            string newLockedReelData = lockedReelData;
            string currentReelIconIds = string.Join( ",", newReel.Select( i => (int)i ) );
            if ( string.IsNullOrEmpty( newLockedReelData ) ) {
                newLockedReelData = currentReelIconIds;
            }
            else {
                newLockedReelData += $"-{currentReelIconIds}";
            }


            Task interactionModificationTask;

            if ( reelToSpin < cols ) {
                int nextReel = reelToSpin + 1;
                var description = $"Reel {reelToSpin} revealed! Total Stars: {CountStars( grid )}";
                var embed = BuildAdvancedEmbed( Context.User, grid, bet, -1, description, 0, rows, cols, $"⭐ Mini-Game! Reel {nextReel} ⭐" );
                var customId = $"minigame_spin_{nextReel}_{bet.ToString( CultureInfo.InvariantCulture )}_{rows}x{cols}_{newLockedReelData}";
                var buttons = new ComponentBuilder()
                    .WithButton( $"Spin Reel {nextReel}", customId, ButtonStyle.Success )
                    .WithButton( "End", $"{CMD_PREFIX5_X5}_end", ButtonStyle.Danger );

                interactionModificationTask = ModifyOriginalResponseAsync( m => {
                        m.Embed = embed;
                        m.Components = buttons.Build();
                    }
                );

            }
            else {
                int totalStars = CountStars( grid );
                decimal multiplier = CalculateMiniGamePayout( totalStars );
                decimal winnings = (decimal)bet * multiplier;
                PlayersWallet.AddToBalance( Context.Guild.Id, Context.User.Id, (float)winnings );

                string description = $"**Mini-Game Over!**\n\n" +
                                     $"You collected a total of **{totalStars}** {GetEmoji( AdvancedSlotIcon.MiniGame )} stars!\n" +
                                     $"This gives you a **{multiplier}x** multiplier!";
                
                var embed = BuildAdvancedEmbed( Context.User, grid, bet, multiplier, description, winnings, rows, cols, "⭐ Mini-Game Complete! ⭐" );
                var buttons = new ComponentBuilder()
                    .WithButton( "Spin Again", $"{CMD_PREFIX5_X5}_again_{bet.ToString( CultureInfo.InvariantCulture )}", style: ButtonStyle.Primary )
                    .WithButton( "End", $"{CMD_PREFIX5_X5}_end", ButtonStyle.Danger );

                interactionModificationTask = ModifyOriginalResponseAsync( m => {
                        m.Embed = embed;
                        m.Components = buttons.Build();
                    }
                );

                // This following message can also be wrapped in a retry but is less critical
                if ( multiplier >= 5m ) {
                    decimal profit = winnings - (decimal)bet;
                    if ( profit > 0 ) {
                        var msg = $"🎰 {Context.User.Mention} wins **{profit:C2}** on the Advanced Slots Mini-Game!";
                        await Context.Channel.SendMessageAsync( msg );
                    }
                }
            }

            // Call the helper method to execute the task with retries.
            await ExecuteWithRetryAsync( interactionModificationTask );
        }

        /// <summary>
        /// A helper method to execute a Discord API call with a retry policy for transient errors.
        /// </summary>
        async Task ExecuteWithRetryAsync(Task apiCall, int maxRetries = 3) {
            var delay = 1000; // Initial delay of 1 second
            for (var i = 0; i < maxRetries; i++) {
                try {
                    await apiCall;
                    return; // Success
                }
                catch (Discord.Net.HttpException ex) when (ex.HttpCode == System.Net.HttpStatusCode.ServiceUnavailable || ex.HttpCode == System.Net.HttpStatusCode.GatewayTimeout) {
                    // Log the retry attempt (optional but good for debugging)
                    Console.WriteLine( $"Discord API returned {ex.HttpCode}. Retrying in {delay}ms... (Attempt {i + 1}/{maxRetries})" );
                    await Task.Delay( delay );
                    delay *= 2; // Double the delay for the next potential retry
                }
            }

            // If all retries fail, the exception will be thrown on the final attempt and the interaction will fail.
            // This is the expected behavior after exhausting all retries.
            // You could also add a final failure message to the user here using FollowupAsync.
            await FollowupAsync( "Sorry, I'm having trouble connecting to Discord's servers. Please try again in a moment.", ephemeral: true );
        }

        static int CountStars(AdvancedSlotIcon[][] grid) => grid
            .SelectMany( r => r )
            .Count( i => i == AdvancedSlotIcon.MiniGame );

        public AdvancedSlotIcon[] SpinSingleMiniGameReel(int rows) {
            AdvancedSlotIcon[] reel = new AdvancedSlotIcon[rows];
            List<AdvancedSlotIcon> nonSpecialSymbols = [];
            foreach (var icon in SymbolWeights.Keys) {
                if (icon != AdvancedSlotIcon.Scatter && icon != AdvancedSlotIcon.Wild && icon != AdvancedSlotIcon.MiniGame) {
                    nonSpecialSymbols.Add(icon);
                }
            }

            for (var r = 0; r < rows; r++) {
                if (Rng.Next(100) < MINIGAME_ICON_BOOST_CHANCE) {
                    reel[r] = AdvancedSlotIcon.MiniGame;
                }
                else {
                    reel[r] = nonSpecialSymbols[Rng.Next(nonSpecialSymbols.Count)];
                }
            }

            return reel;
        }

        public decimal CalculateMiniGamePayout(int totalIcons) {
            var multiplier = 0m;
            foreach (KeyValuePair<int, decimal> payout in MiniGamePayouts.OrderByDescending( p => p.Key )) {
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
        public async Task OnSpinAgain5x5(string rawBet) {
            await DeferAsync();
            await HandleAdvancedSpinAgain( rawBet, ROWS, COLS, CMD_PREFIX5_X5 );
        }

        [ComponentInteraction( "advslots5x5_end" )]
        public async Task OnEnd5x5() => await HandleAdvancedEndGame( "Advanced Slots (5x5)" );

        async Task HandleAdvancedSpinAgain(string rawBet, int expectedRows, int expectedCols, string cmdPrefix) {
            string betString = rawBet.Split( '_' ).Last();
            if ( !float.TryParse( betString, NumberStyles.Any, CultureInfo.InvariantCulture, out float bet ) ) {
                await ModifyOriginalResponseAsync( m => m.Content = "Invalid bet data." );
                return;
            }

            await PlayAdvancedSlots( bet, expectedRows, expectedCols, cmdPrefix, true, Context.Interaction );
        }

        async Task HandleAdvancedEndGame(string gameTitle) {
            await DeferAsync();
            await ModifyOriginalResponseAsync( m => {
                    m.Embed = new EmbedBuilder()
                        .WithTitle( $"{gameTitle} – Game Over" )
                        .WithDescription( $"{Context.User.Mention} ended the game." )
                        .Build();
                    m.Components = new ComponentBuilder().Build();
                }
            );
        }
    }
}