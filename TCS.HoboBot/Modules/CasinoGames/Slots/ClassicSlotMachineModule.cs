using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using TCS.HoboBot.Data;

namespace TCS.HoboBot.Modules.CasinoGames {
    public enum ClassicSlotIcon { Cherry, Lemon, Orange, Plum, Bell, Hotdog, Bar, Seven }
    
    // 🎰

    public sealed class ClassicSlotMachineModule : BaseSlotMachineModule<ClassicSlotIcon> {
        protected override string GameName => "Classic Slots";
        protected override string GameCommandPrefix => "slots";

        static readonly IReadOnlyList<string> Emojis = [
            "🍒", "🍋", "🍊", "🍑", "🔔", "🌭", "🍷", "7️⃣",
        ];
        protected override IReadOnlyList<string> SymbolToEmojiMap => Emojis;

        static readonly IReadOnlyList<ClassicSlotIcon> Icons =
            Enum.GetValues( typeof(ClassicSlotIcon) ).Cast<ClassicSlotIcon>().ToList().AsReadOnly();
        protected override IReadOnlyList<ClassicSlotIcon> Symbols => Icons;

        protected override int NumberOfReels => 3;
        // NumberOfRows defaults to 1 from the base class, which is correct here.

        const float RTP = 1.1f; // RTP set to 95%, easily adjustable

// Base payouts designed for theoretical 100% RTP (unscaled)
        static readonly Dictionary<string, decimal> BasePayoutMultipliers = new() {
            { "ThreeSevens", 120m },
            { "ThreeBars", 60m },
            { "ThreeHotdogs", 40m },
            { "ThreeBells", 25m },
            { "ThreeFruits", 15m },
            { "TwoSevens", 7m },
            { "TwoOfAKind", 3m },
        };


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
            ClassicSlotIcon[] resultRow = new ClassicSlotIcon[ NumberOfReels ];
            for (var i = 0; i < NumberOfReels; i++) {
                resultRow[i] = GetRandomSymbol();
            }

            return new ClassicSlotIcon[][] { resultRow }; // 2D array with one row
        }

        protected override (decimal payoutMultiplier, string winDescription) CalculatePayoutInternal(ClassicSlotIcon[][] currentSpin, float bet) {
            ClassicSlotIcon[] r = currentSpin[0];
            var multiplier = 0m;
            var description = "No win this time.";

            bool allEqual = r[0] == r[1] && r[1] == r[2];
            bool allFruits = allEqual && r[0] is ClassicSlotIcon.Cherry or ClassicSlotIcon.Lemon or ClassicSlotIcon.Orange or ClassicSlotIcon.Plum;

            if ( allEqual ) {
                if ( r[0] == ClassicSlotIcon.Seven ) {
                    multiplier = BasePayoutMultipliers["ThreeSevens"] * (decimal)RTP;
                    description = $"JACKPOT! Three Sevens {GetEmojiForSymbol( ClassicSlotIcon.Seven )}{GetEmojiForSymbol( ClassicSlotIcon.Seven )}{GetEmojiForSymbol( ClassicSlotIcon.Seven )}!";
                }
                else if ( r[0] == ClassicSlotIcon.Bar ) {
                    multiplier = BasePayoutMultipliers["ThreeBars"] * (decimal)RTP;
                    description = $"Three Bars {GetEmojiForSymbol( ClassicSlotIcon.Bar )}{GetEmojiForSymbol( ClassicSlotIcon.Bar )}{GetEmojiForSymbol( ClassicSlotIcon.Bar )}!";
                }
                else if ( r[0] == ClassicSlotIcon.Hotdog ) {
                    multiplier = BasePayoutMultipliers["ThreeHotdogs"] * (decimal)RTP;
                    description = $"Three Hotdogs {GetEmojiForSymbol( ClassicSlotIcon.Hotdog )}{GetEmojiForSymbol( ClassicSlotIcon.Hotdog )}{GetEmojiForSymbol( ClassicSlotIcon.Hotdog )}!";
                }
                else if ( r[0] == ClassicSlotIcon.Bell ) {
                    multiplier = BasePayoutMultipliers["ThreeBells"] * (decimal)RTP;
                    description = $"Three Bells {GetEmojiForSymbol( ClassicSlotIcon.Bell )}{GetEmojiForSymbol( ClassicSlotIcon.Bell )}{GetEmojiForSymbol( ClassicSlotIcon.Bell )}!";
                }
                else if ( allFruits ) {
                    multiplier = BasePayoutMultipliers["ThreeFruits"] * (decimal)RTP;
                    description = $"Three {r[0]}s {GetEmojiForSymbol( r[0] )}{GetEmojiForSymbol( r[0] )}{GetEmojiForSymbol( r[0] )}!";
                }
            }
            else if ( r.Count( icon => icon == ClassicSlotIcon.Seven ) == 2 ) {
                multiplier = BasePayoutMultipliers["TwoSevens"] * (decimal)RTP;
                description = $"Two Sevens {GetEmojiForSymbol( ClassicSlotIcon.Seven )}!";
            }
            else if ( r.GroupBy( icon => icon ).Any( g => g.Count() == 2 ) ) {
                var twoKindSymbol = r.GroupBy( icon => icon ).First( g => g.Count() == 2 ).Key;
                multiplier = BasePayoutMultipliers["TwoOfAKind"] * (decimal)RTP;
                description = $"Two {twoKindSymbol}s {GetEmojiForSymbol( twoKindSymbol )}!";
            }

            return (multiplier, description);
        }


        protected override Embed BuildGameEmbedInternal(SocketUser user, ClassicSlotIcon[][] currentSpin, float bet, decimal payoutMultiplier, string winDescription, decimal totalWinnings) {
            ClassicSlotIcon[] r = currentSpin[0];
            string reelDisplay = string.Join( " ", r.Select( GetEmojiForSymbol ) );

            string outcomeMessage;
            decimal profit = totalWinnings - (decimal)bet;

            outcomeMessage = payoutMultiplier switch {
                0m => $"Unlucky! You lost **{bet:C2}**.",
                1m => $"Push! Your **{bet:C2}** bet is returned.",
                _ => $"Congratulations! You won **{profit:C2}** (Total: {totalWinnings:C2}).\n",
            };

            // add your wallet balance to the outcome message
            outcomeMessage += $"\nYour new balance: **${PlayersWallet.GetBalance( user.Id ):C2}**";

            var embedBuilder = new EmbedBuilder()
                .WithTitle( $"{GameName} – {bet:C2} Bet" )
                .WithDescription( $"{user.Mention} pulls the handle…\n**{reelDisplay}**\n\n{winDescription}" )
                .WithFooter( outcomeMessage );

            switch (payoutMultiplier) {
                case > 1m:
                    embedBuilder.WithColor( Color.Green ); // Win
                    break;
                case 1m:
                    embedBuilder.WithColor( Color.LightGrey ); // Push
                    break;
                default:
                    embedBuilder.WithColor( Color.Red ); // Loss
                    break;
            }

            return embedBuilder.Build();
        }
    }
}