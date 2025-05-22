using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using TCS.HoboBot.Data;

namespace TCS.HoboBot.Modules.CasinoGames {
    public enum ClassicSlotIcon { Cherry, Lemon, Orange, Plum, Bell, Hotdog, Bar, Seven }

    public sealed class ClassicSlotMachineModule : BaseSlotMachineModule<ClassicSlotIcon> {
        protected override string GameName => "Classic Slots";
        protected override string GameCommandPrefix => "slots";

        private static readonly IReadOnlyList<string> _emojis = new string[] {
            "🍒", "🍋", "🍊", "🍑", "🔔", "🌭", "🍷", "7️⃣"
        };
        protected override IReadOnlyList<string> SymbolToEmojiMap => _emojis;

        private static readonly IReadOnlyList<ClassicSlotIcon> _icons =
            Enum.GetValues( typeof(ClassicSlotIcon) ).Cast<ClassicSlotIcon>().ToList().AsReadOnly();
        protected override IReadOnlyList<ClassicSlotIcon> Symbols => _icons;

        protected override int NumberOfReels => 3;
        // NumberOfRows defaults to 1 from base class, which is correct here.

        [SlashCommand( "slots_classic", "Pull a three-reel slot machine." )]
        public async Task SlotsAsync([Summary( description: "Your bet amount" )] float bet) {
            await PlaySlotsAsync( bet );
        }

        [ComponentInteraction( "slots_again_*" )]
        public async Task OnSpinAgainButton(string rawBet) {
            await DeferAsync( ephemeral: true );
            await HandleSpinAgainAsync( rawBet );
        }

        [ComponentInteraction( "slots_end" )]
        public async Task OnEndButton() {
            await DeferAsync( ephemeral: true );
            await HandleEndGameAsync();
        }

        protected override ClassicSlotIcon[][] SpinReelsInternal() {
            var resultRow = new ClassicSlotIcon[ NumberOfReels ];
            for (int i = 0; i < NumberOfReels; i++) {
                resultRow[i] = GetRandomSymbol();
            }

            return new ClassicSlotIcon[][] { resultRow }; // 2D array with one row
        }

        protected override (decimal payoutMultiplier, string winDescription) CalculatePayoutInternal(ClassicSlotIcon[][] currentSpin, float bet) {
            ClassicSlotIcon[] r = currentSpin[0]; // The single row of reels
            decimal multiplier = 0m;
            string description = "No win this time.";

            bool allEqual = r[0] == r[1] && r[1] == r[2];
            bool allFruits = allEqual && r[0] is ClassicSlotIcon.Cherry or ClassicSlotIcon.Lemon or ClassicSlotIcon.Orange or ClassicSlotIcon.Plum;

            if ( allEqual ) {
                if ( r[0] == ClassicSlotIcon.Seven ) {
                    multiplier = 100m;
                    description = $"JACKPOT! Three Sevens {GetEmojiForSymbol( ClassicSlotIcon.Seven )}{GetEmojiForSymbol( ClassicSlotIcon.Seven )}{GetEmojiForSymbol( ClassicSlotIcon.Seven )}!";
                }
                else if ( r[0] == ClassicSlotIcon.Bar ) {
                    multiplier = 50m;
                    description = $"Three Bars {GetEmojiForSymbol( ClassicSlotIcon.Bar )}{GetEmojiForSymbol( ClassicSlotIcon.Bar )}{GetEmojiForSymbol( ClassicSlotIcon.Bar )}!";
                }
                else if ( r[0] == ClassicSlotIcon.Hotdog ) {
                    multiplier = 30m;
                    description = $"Three Hotdogs {GetEmojiForSymbol( ClassicSlotIcon.Hotdog )}{GetEmojiForSymbol( ClassicSlotIcon.Hotdog )}{GetEmojiForSymbol( ClassicSlotIcon.Hotdog )}!";
                }
                else if ( r[0] == ClassicSlotIcon.Bell ) {
                    multiplier = 20m;
                    description = $"Three Bells {GetEmojiForSymbol( ClassicSlotIcon.Bell )}{GetEmojiForSymbol( ClassicSlotIcon.Bell )}{GetEmojiForSymbol( ClassicSlotIcon.Bell )}!";
                }
                else if ( allFruits ) {
                    multiplier = 10m;
                    description = $"Three {r[0]}s {GetEmojiForSymbol( r[0] )}{GetEmojiForSymbol( r[0] )}{GetEmojiForSymbol( r[0] )}!";
                }
            }
            else if ( r.Count( icon => icon == ClassicSlotIcon.Seven ) == 2 ) {
                multiplier = 5m;
                description = $"Two Sevens {GetEmojiForSymbol( ClassicSlotIcon.Seven )}!";
            }
            else if ( r.GroupBy( icon => icon ).Any( g => g.Count() == 2 ) ) // Any other two of a kind
            {
                var twoKindSymbol = r.GroupBy( icon => icon ).First( g => g.Count() == 2 ).Key;
                multiplier = 2m;
                description = $"Two {twoKindSymbol}s {GetEmojiForSymbol( twoKindSymbol )}!";
            }

            return (multiplier, description);
        }

        protected override Embed BuildGameEmbedInternal(SocketUser user, ClassicSlotIcon[][] currentSpin, float bet, decimal payoutMultiplier, string winDescription, decimal totalWinnings) {
            ClassicSlotIcon[] r = currentSpin[0];
            string reelDisplay = string.Join( " ", r.Select( icon => GetEmojiForSymbol( icon ) ) );

            string outcomeMessage;
            decimal profit = totalWinnings - (decimal)bet;

            if ( payoutMultiplier == 0m ) {
                outcomeMessage = $"Unlucky! You lost **{bet:C2}**.";
            }
            else if ( payoutMultiplier == 1m ) {
                outcomeMessage = $"Push! Your **{bet:C2}** bet is returned."; // Profit is 0
            }
            else {
                outcomeMessage = $"Congratulations! You won **{profit:C2}** (Total: {totalWinnings:C2}).\n";
            }

            // add your wallet balance to the outcome message
            outcomeMessage += $"\nYour new balance: **${PlayersWallet.GetBalance( user.Id ):C2}**";

            var embedBuilder = new EmbedBuilder()
                .WithTitle( $"{GameName} – {bet:C2} Bet" )
                .WithDescription( $"{user.Mention} pulls the handle…\n**{reelDisplay}**\n\n{winDescription}" )
                .WithFooter( outcomeMessage );

            if ( payoutMultiplier > 1m ) {
                embedBuilder.WithColor( Color.Green ); // Win
            }
            else if ( payoutMultiplier == 1m ) {
                embedBuilder.WithColor( Color.LightGrey ); // Push
            }
            else {
                embedBuilder.WithColor( Color.Red ); // Loss
            }

            return embedBuilder.Build();
        }
    }
}