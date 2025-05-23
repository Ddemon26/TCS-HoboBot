using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using System.Text;
using TCS.HoboBot.Data;

namespace TCS.HoboBot.Modules.CasinoGames {
    public enum ThreeXThreeSlotIcon { Cherry, Lemon, Orange, Plum, Bell, Hotdog, Bar, Seven, Rare }

    public sealed class SlotMachine3X3Module : BaseSlotMachineModule<ThreeXThreeSlotIcon> {
        protected override string GameName => "3x3 Slots";
        protected override string GameCommandPrefix => "slots3x3";

        static readonly Dictionary<ulong, (int spinsRemaining, float betAmount)> ActiveFreeSpins = new();

        static readonly IReadOnlyList<string> Emojis = [
            "🍒", "🍋", "🍊", "🍑", "🔔", "🌭", "🍷", "7️⃣", "💠"
        ];
        protected override IReadOnlyList<string> SymbolToEmojiMap => Emojis;

        static readonly IReadOnlyList<ThreeXThreeSlotIcon> Icons =
            Enum.GetValues( typeof(ThreeXThreeSlotIcon) ).Cast<ThreeXThreeSlotIcon>().ToList().AsReadOnly();
        protected override IReadOnlyList<ThreeXThreeSlotIcon> Symbols => Icons;

        protected override int NumberOfReels => 3;
        protected override int NumberOfRows => 3;

        static readonly List<List<(int r, int c)>> Paylines = [
            new() { (0, 0), (0, 1), (0, 2) },
            new() { (1, 0), (1, 1), (1, 2) },
            new() { (2, 0), (2, 1), (2, 2) },
            new() { (0, 0), (1, 1), (2, 2) },
            new() { (0, 2), (1, 1), (2, 0) },
        ];

        const float RTP = 1.1f;

        /// <summary>
        /// A dictionary that defines the base payout multipliers for each slot icon in the 3x3 slot machine game.
        /// The key is a <see cref="ThreeXThreeSlotIcon"/> representing the slot icon, and the value is a decimal
        /// representing the base payout multiplier for a winning line of that icon.
        /// </summary>
        static readonly Dictionary<ThreeXThreeSlotIcon, decimal> BaseLinePayoutMultipliers = new() {
            { ThreeXThreeSlotIcon.Seven, 30m },
            { ThreeXThreeSlotIcon.Bar, 20m },
            { ThreeXThreeSlotIcon.Hotdog, 15m },
            { ThreeXThreeSlotIcon.Bell, 10m },
            { ThreeXThreeSlotIcon.Cherry, 8m },
            { ThreeXThreeSlotIcon.Lemon, 8m },
            { ThreeXThreeSlotIcon.Orange, 8m },
            { ThreeXThreeSlotIcon.Plum, 8m },
            { ThreeXThreeSlotIcon.Rare, 50m },
        };

        decimal GetAdjustedLinePayoutMultiplier(ThreeXThreeSlotIcon symbol) =>
            BaseLinePayoutMultipliers.TryGetValue( symbol, out decimal baseMultiplier ) ? baseMultiplier * (decimal)RTP : 0m;

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

        protected override ThreeXThreeSlotIcon[][] SpinReelsInternal() {
            ThreeXThreeSlotIcon[][] grid = new ThreeXThreeSlotIcon[ NumberOfRows ][];
            for (var r = 0; r < NumberOfRows; r++) {
                grid[r] = new ThreeXThreeSlotIcon[ NumberOfReels ];
                for (var c = 0; c < NumberOfReels; c++) {
                    grid[r][c] = GetRandomSymbol();
                }
            }

            return grid;
        }

        protected override (decimal payoutMultiplier, string winDescription) CalculatePayoutInternal(ThreeXThreeSlotIcon[][] grid, float bet) {
            var totalBetMultiplier = 0m;
            List<string> winDescriptionsList = new List<string>();
            int freeSpinsAwarded = 0;

            foreach (List<(int r, int c)> linePath in Paylines) {
                var s1 = grid[linePath[0].r][linePath[0].c];
                var s2 = grid[linePath[1].r][linePath[1].c];
                var s3 = grid[linePath[2].r][linePath[2].c];

                if ( s1 == s2 && s2 == s3 ) {
                    decimal lineWinMultiplier = GetAdjustedLinePayoutMultiplier( s1 );
                    if ( lineWinMultiplier > 0m ) {
                        totalBetMultiplier += lineWinMultiplier;
                        winDescriptionsList.Add( $"Line win: {GetEmojiForSymbol( s1 )}{GetEmojiForSymbol( s1 )}{GetEmojiForSymbol( s1 )} ({lineWinMultiplier:0.##}x)" );

                        // only award free spins on three Rares
                        if ( s1 == ThreeXThreeSlotIcon.Rare )
                            freeSpinsAwarded += 5;
                    }
                }
            }

            if ( freeSpinsAwarded > 0 ) {
                ulong userId = Context.User.Id;
                if ( ActiveFreeSpins.ContainsKey( userId ) )
                    ActiveFreeSpins[userId] = (ActiveFreeSpins[userId].spinsRemaining + freeSpinsAwarded, bet);
                else
                    ActiveFreeSpins[userId] = (freeSpinsAwarded, bet);

                _ = Context.Channel.SendMessageAsync( $"🎰 {Context.User.Mention} got **{freeSpinsAwarded}** free spins at {bet:C2}!" );
            }

            return totalBetMultiplier > 0 ? (totalBetMultiplier, string.Join( "\n", winDescriptionsList )) : (0m, "No wins this spin.");
        }

        protected override Embed BuildGameEmbedInternal(SocketUser user, ThreeXThreeSlotIcon[][] grid, float bet, decimal payoutMultiplier, string winDescription, decimal totalWinnings) {
            var gridDisplay = new StringBuilder();
            for (var r = 0; r < NumberOfRows; r++) {
                for (var c = 0; c < NumberOfReels; c++) {
                    gridDisplay.Append( GetEmojiForSymbol( grid[r][c] ) );
                    if ( c < NumberOfReels - 1 ) gridDisplay.Append( " | " );
                }

                gridDisplay.AppendLine();
            }

            decimal profit = totalWinnings - (decimal)bet;
            string outcomeMessage = payoutMultiplier > 0
                ? $"You won {profit:C2}! (Total: {totalWinnings:C2})"
                : $"You lost {bet:C2}.";

            outcomeMessage += $"\nNew balance: {PlayersWallet.GetBalance( user.Id ):C2}";

            return new EmbedBuilder()
                .WithTitle( $"{GameName} – {bet:C2} Bet" )
                .WithDescription( $"{user.Mention} spins:\n\n{gridDisplay}\n{winDescription}" )
                .WithFooter( outcomeMessage )
                .WithColor( profit > 0 ? Color.Green : Color.Red )
                .Build();
        }
    }
}