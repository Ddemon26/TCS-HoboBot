using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using System.Text;
using TCS.HoboBot.Data;

namespace TCS.HoboBot.Modules.CasinoGames {
    public sealed class SlotMachine3X3Module : BaseSlotMachineModule<ClassicSlotIcon> {
        protected override string GameName => "3x3 Slots";
        protected override string GameCommandPrefix => "slots3x3";

        static readonly IReadOnlyList<string> Emojis = [
            "🍒", "🍋", "🍊", "🍑", "🔔", "🌭", "🍷", "7️⃣",
        ];
        protected override IReadOnlyList<string> SymbolToEmojiMap => Emojis;

        static readonly IReadOnlyList<ClassicSlotIcon> Icons =
            Enum.GetValues( typeof(ClassicSlotIcon) ).Cast<ClassicSlotIcon>().ToList().AsReadOnly();
        protected override IReadOnlyList<ClassicSlotIcon> Symbols => Icons;

        protected override int NumberOfReels => 3;
        protected override int NumberOfRows => 3;

        static readonly List<List<(int r, int c)>> Paylines = [
            new() { (0, 0), (0, 1), (0, 2) }, // Top
            new() { (1, 0), (1, 1), (1, 2) }, // Mid
            new() { (2, 0), (2, 1), (2, 2) }, // Bot
            new() { (0, 0), (1, 1), (2, 2) }, // Diag ↘
            new() { (0, 2), (1, 1), (2, 0) }, // Diag ↙
        ];

        const float RTP = 1.2f;
        const decimal SCALING_FACTOR = 0.957009m; // = 1 / 1.044922

        static readonly Dictionary<ClassicSlotIcon, decimal> BaseLinePayoutMultipliers = new() {
            { ClassicSlotIcon.Seven, 30m * SCALING_FACTOR }, // ≈ 28.7103
            { ClassicSlotIcon.Bar, 20m * SCALING_FACTOR }, // ≈ 19.1402
            { ClassicSlotIcon.Hotdog, 15m * SCALING_FACTOR }, // ≈ 14.3551
            { ClassicSlotIcon.Bell, 10m * SCALING_FACTOR }, // ≈ 9.5701
            { ClassicSlotIcon.Cherry, 8m * SCALING_FACTOR }, // ≈ 7.6561
            { ClassicSlotIcon.Lemon, 8m * SCALING_FACTOR }, // ≈ 7.6561
            { ClassicSlotIcon.Orange, 8m * SCALING_FACTOR }, // ≈ 7.6561
            { ClassicSlotIcon.Plum, 8m * SCALING_FACTOR }, // ≈ 7.6561
        };

        decimal GetAdjustedLinePayoutMultiplier(ClassicSlotIcon symbol) =>
            BaseLinePayoutMultipliers.TryGetValue( symbol, out decimal baseMultiplier )
                ? baseMultiplier * (decimal)RTP
                : 0m;

        [SlashCommand( "slots3x3", "Play a 3x3 grid slot machine." )]
        public async Task Slots3X3Async([Summary( description: "Your bet amount" )] float bet) =>
            await PlaySlotsAsync( bet );

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
            ClassicSlotIcon[][] grid = new ClassicSlotIcon[ NumberOfRows ][];
            for (var r = 0; r < NumberOfRows; r++) {
                grid[r] = new ClassicSlotIcon[ NumberOfReels ];
                for (var c = 0; c < NumberOfReels; c++) {
                    grid[r][c] = GetRandomSymbol();
                }
            }

            return grid;
        }

        protected override (decimal payoutMultiplier, string winDescription) CalculatePayoutInternal(
            ClassicSlotIcon[][] grid, float bet
        ) {
            var totalBetMultiplier = 0m;
            List<string> winDescriptions = [];

            for (var i = 0; i < Paylines.Count; i++) {
                List<(int r, int c)> path = Paylines[i];
                var s1 = grid[path[0].r][path[0].c];
                var s2 = grid[path[1].r][path[1].c];
                var s3 = grid[path[2].r][path[2].c];

                if ( s1 == s2 && s2 == s3 ) {
                    decimal lineMultiplier = GetAdjustedLinePayoutMultiplier( s1 );
                    if ( lineMultiplier > 0 ) {
                        totalBetMultiplier += lineMultiplier;
                        winDescriptions.Add(
                            $"Line {i + 1}: {GetEmojiForSymbol( s1 )}{GetEmojiForSymbol( s1 )}{GetEmojiForSymbol( s1 )} ({lineMultiplier:0.##}x)"
                        );
                    }
                }
            }

            return totalBetMultiplier > 0
                ? (totalBetMultiplier, $"Wins on {winDescriptions.Count} line(s):\n" + string.Join( "\n", winDescriptions ))
                : (0m, "No winning lines this spin.");
        }

        protected override Embed BuildGameEmbedInternal(
            SocketUser user,
            ClassicSlotIcon[][] grid,
            float bet,
            decimal payoutMultiplier,
            string winDescription,
            decimal totalWinnings
        ) {
            var sb = new StringBuilder();
            for (var r = 0; r < NumberOfRows; r++) {
                for (var c = 0; c < NumberOfReels; c++) {
                    sb.Append( GetEmojiForSymbol( grid[r][c] ) );
                    if ( c < NumberOfReels - 1 ) sb.Append( " | " );
                }

                sb.AppendLine();
            }

            decimal profit = totalWinnings - (decimal)bet;
            string outcome;
            if ( payoutMultiplier == 0m )
                outcome = $"Unlucky! You lost **{bet:C2}**.";
            else if ( profit == 0 )
                outcome = $"Push! Your **{bet:C2}** bet is returned.";
            else
                outcome = $"Congratulations! You won **{profit:C2}** (Total: {totalWinnings:C2}).";

            outcome += $"\nYour new balance: **${PlayersWallet.GetBalance(Context.Guild.Id, user.Id ):C2}**";

            var embed = new EmbedBuilder()
                .WithTitle( $"{GameName} – {bet:C2} Bet" )
                .WithDescription( $"{user.Mention} spins the 3×3…\n\n{sb}\n{winDescription}" )
                .WithFooter( outcome )
                .WithColor( profit > 0 ? Color.Green : profit == 0 ? Color.LightGrey : Color.Red );

            return embed.Build();
        }
    }
}