using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using System.Text;
using TCS.HoboBot.Data;

namespace TCS.HoboBot.Modules.CasinoGames {
    // Can reuse ClassicSlotIcon or define a new enum if symbols/payouts differ significantly.
    // For this example, we reuse ClassicSlotIcon.
    public sealed class SlotMachine3x3Module : BaseSlotMachineModule<ClassicSlotIcon> {
        protected override string GameName => "3x3 Slots";
        protected override string GameCommandPrefix => "slots3x3";

        // Reusing symbols and emojis from ClassicSlotMachineModule for simplicity
        private static readonly IReadOnlyList<string> _emojis = new string[] {
            "🍒", "🍋", "🍊", "🍑", "🔔", "🌭", "🍷", "7️⃣",
            // Potentially add more/different symbols for a 3x3 grid if desired
            // "💠", "⭐", "🔶"
        };
        protected override IReadOnlyList<string> SymbolToEmojiMap => _emojis;

        private static readonly IReadOnlyList<ClassicSlotIcon> _icons =
            Enum.GetValues( typeof(ClassicSlotIcon) ).Cast<ClassicSlotIcon>().ToList().AsReadOnly();
        protected override IReadOnlyList<ClassicSlotIcon> Symbols => _icons;

        protected override int NumberOfReels => 3;
        protected override int NumberOfRows => 3;

        // Define Paylines for 3x3 grid: (row, col) 0-indexed
        private static readonly List<List<(int r, int c)>> Paylines = new List<List<(int r, int c)>> {
            new List<(int r, int c)> { (0, 0), (0, 1), (0, 2) }, // Top row
            new List<(int r, int c)> { (1, 0), (1, 1), (1, 2) }, // Middle row
            new List<(int r, int c)> { (2, 0), (2, 1), (2, 2) }, // Bottom row
            new List<(int r, int c)> { (0, 0), (1, 1), (2, 2) }, // Diagonal TL to BR
            new List<(int r, int c)> { (0, 2), (1, 1), (2, 0) } // Diagonal TR to BL
        };

        // Payouts for 3-of-a-kind on a line for 3x3
        private static readonly Dictionary<ClassicSlotIcon, decimal> LinePayoutMultipliers = new Dictionary<ClassicSlotIcon, decimal> {
            { ClassicSlotIcon.Seven, 25m }, // Reduced from classic to account for multiple lines
            { ClassicSlotIcon.Bar, 15m },
            { ClassicSlotIcon.Hotdog, 10m },
            { ClassicSlotIcon.Bell, 8m },
            { ClassicSlotIcon.Cherry, 5m },
            { ClassicSlotIcon.Lemon, 5m },
            { ClassicSlotIcon.Orange, 5m },
            { ClassicSlotIcon.Plum, 5m }
        };


        [SlashCommand( "slots3x3", "Play a 3x3 grid slot machine." )]
        public async Task Slots3x3Async([Summary( description: "Your bet amount" )] float bet) {
            await PlaySlotsAsync( bet );
        }

        [ComponentInteraction( "slots3x3_again_*" )]
        public async Task OnSpinAgainButton3x3(string rawBet) {
            await DeferAsync( ephemeral: true );
            await HandleSpinAgainAsync( rawBet );
        }

        [ComponentInteraction( "slots3x3_end" )]
        public async Task OnEndButton3x3() {
            await DeferAsync( ephemeral: true );
            await HandleEndGameAsync();
        }

        protected override ClassicSlotIcon[][] SpinReelsInternal() {
            var grid = new ClassicSlotIcon[ NumberOfRows ][];
            for (int r = 0; r < NumberOfRows; r++) {
                grid[r] = new ClassicSlotIcon[ NumberOfReels ];
                for (int c = 0; c < NumberOfReels; c++) {
                    grid[r][c] = GetRandomSymbol();
                }
            }

            return grid;
        }

        protected override (decimal payoutMultiplier, string winDescription) CalculatePayoutInternal(ClassicSlotIcon[][] grid, float bet) {
            decimal totalBetMultiplier = 0m; // Multiplier for the original bet
            var winDescriptionsList = new List<string>();

            for (int i = 0; i < Paylines.Count; i++) {
                var linePath = Paylines[i];
                ClassicSlotIcon s1 = grid[linePath[0].r][linePath[0].c];
                ClassicSlotIcon s2 = grid[linePath[1].r][linePath[1].c];
                ClassicSlotIcon s3 = grid[linePath[2].r][linePath[2].c];

                if ( s1 == s2 && s2 == s3 ) // 3 of a kind on the line
                {
                    if ( LinePayoutMultipliers.TryGetValue( s1, out decimal lineWinMultiplier ) ) {
                        totalBetMultiplier += lineWinMultiplier;
                        winDescriptionsList.Add( $"Line {i + 1}: {GetEmojiForSymbol( s1 )}{GetEmojiForSymbol( s1 )}{GetEmojiForSymbol( s1 )} ({lineWinMultiplier}x)" );
                    }
                }
            }

            if ( totalBetMultiplier > 0 ) {
                return (totalBetMultiplier, $"Wins on {winDescriptionsList.Count} line(s):\n" + string.Join( "\n", winDescriptionsList ));
            }

            return (0m, "No winning lines this spin.");
        }

        protected override Embed BuildGameEmbedInternal(SocketUser user, ClassicSlotIcon[][] grid, float bet, decimal payoutMultiplier, string winDescription, decimal totalWinnings) {
            var gridDisplay = new StringBuilder();
            for (int r = 0; r < NumberOfRows; r++) {
                for (int c = 0; c < NumberOfReels; c++) {
                    gridDisplay.Append( GetEmojiForSymbol( grid[r][c] ) );
                    if ( c < NumberOfReels - 1 ) {
                        gridDisplay.Append( " | " );
                    }
                }

                gridDisplay.Append( "\n" );
            }

            string outcomeMessage;
            decimal profit = totalWinnings - (decimal)bet;

            if ( payoutMultiplier == 0m ) {
                outcomeMessage = $"Unlucky! You lost **{bet:C2}**.";
            }
            // For multi-line slots, payoutMultiplier is the sum of line multipliers.
            // If totalBetMultiplier is 1x, it means profit is 0 (bet returned).
            else if ( profit == 0 && payoutMultiplier > 0 ) {
                outcomeMessage = $"Push! Your **{bet:C2}** bet is returned.";
            }
            else {
                outcomeMessage = $"Congratulations! You won **{profit:C2}** (Total: {totalWinnings:C2}).";
            }

            // add your wallet balance to the outcome message
            outcomeMessage += $"\nYour new balance: **${PlayersWallet.GetBalance( user.Id ):C2}**";


            var embedBuilder = new EmbedBuilder()
                .WithTitle( $"{GameName} – {bet:C2} Bet" )
                .WithDescription( $"{user.Mention} spins the {NumberOfRows}x{NumberOfReels} reels…\n\n**{gridDisplay.ToString().Trim()}**\n\n{winDescription}" )
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
    }
}