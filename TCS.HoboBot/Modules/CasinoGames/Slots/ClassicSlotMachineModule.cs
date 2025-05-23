using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using TCS.HoboBot.Data;

namespace TCS.HoboBot.Modules.CasinoGames {
    public enum ClassicSlotIcon { Cherry, Lemon, Orange, Plum, Bell, Hotdog, Bar, Seven }

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

        // Post-spin RTP multiplier
        const float RTP = 1.2f;

        // Scale pre-RTP to exactly 1.0: 1 / 1.744140625 ≈ 0.573348264277716
        const decimal SCALING_FACTOR = 0.573348264277716m;

        // Base payouts scaled so that pre-RTP = 100%; after RTP=1.2, long-term payback = 120%
        static readonly Dictionary<string, decimal> BasePayoutMultipliers = new() {
            { "ThreeSevens", 120m * SCALING_FACTOR }, // ≈ 68.8018
            { "ThreeBars", 60m * SCALING_FACTOR }, // ≈ 34.4009
            { "ThreeHotdogs", 40m * SCALING_FACTOR }, // ≈ 22.9339
            { "ThreeBells", 25m * SCALING_FACTOR }, // ≈ 14.3337
            { "ThreeFruits", 15m * SCALING_FACTOR }, // ≈ 8.6002
            { "TwoSevens", 7m * SCALING_FACTOR }, // ≈ 4.0134
            { "TwoOfAKind", 3m * SCALING_FACTOR }, // ≈ 1.7200
        };

        [SlashCommand( "slots_classic", "Pull a three-reel slot machine." )]
        public async Task SlotsAsync([Summary( description: "Your bet amount" )] float bet) =>
            await PlaySlotsAsync( bet );

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
            ClassicSlotIcon[] row = new ClassicSlotIcon[ NumberOfReels ];
            for (var i = 0; i < NumberOfReels; i++) {
                row[i] = GetRandomSymbol();
            }

            return [row];
        }

        protected override (decimal payoutMultiplier, string winDescription) CalculatePayoutInternal(
            ClassicSlotIcon[][] currentSpin, float bet
        ) {
            ClassicSlotIcon[] r = currentSpin[0];
            var multiplier = 0m;
            var description = "No win this time.";

            bool allEqual = r[0] == r[1] && r[1] == r[2];
            bool allFruits = allEqual &&
                             (r[0] is ClassicSlotIcon.Cherry or ClassicSlotIcon.Lemon or
                                 ClassicSlotIcon.Orange or ClassicSlotIcon.Plum);

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
                var sym = r.GroupBy( icon => icon ).First( g => g.Count() == 2 ).Key;
                multiplier = BasePayoutMultipliers["TwoOfAKind"] * (decimal)RTP;
                description = $"Two {sym}s {GetEmojiForSymbol( sym )}!";
            }

            return (multiplier, description);
        }

        protected override Embed BuildGameEmbedInternal(
            SocketUser user,
            ClassicSlotIcon[][] currentSpin,
            float bet,
            decimal payoutMultiplier,
            string winDescription,
            decimal totalWinnings
        ) {
            ClassicSlotIcon[] r = currentSpin[0];
            string reelDisplay = string.Join( " ", r.Select( GetEmojiForSymbol ) );
            decimal profit = totalWinnings - (decimal)bet;

            string outcome = payoutMultiplier switch {
                0m => $"Unlucky! You lost **{bet:C2}**.",
                _ => profit == 0
                    ? $"Push! Your **{bet:C2}** bet is returned."
                    : $"Congratulations! You won **{profit:C2}** (Total: {totalWinnings:C2}).\n",
            };
            outcome += $"\nYour new balance: **${PlayersWallet.GetBalance( Context.Guild.Id, user.Id ):C2}**";

            var builder = new EmbedBuilder()
                .WithTitle( $"{GameName} – {bet:C2} Bet" )
                .WithDescription( $"{user.Mention} pulls the handle…\n**{reelDisplay}**\n\n{winDescription}" )
                .WithFooter( outcome )
                .WithColor(
                    payoutMultiplier > 1m
                        ? Color.Green
                        : payoutMultiplier == 1m
                            ? Color.LightGrey
                            : Color.Red
                );

            return builder.Build();
        }
    }
}