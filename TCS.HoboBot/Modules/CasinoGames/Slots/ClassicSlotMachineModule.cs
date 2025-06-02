using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using TCS.HoboBot.Data;

namespace TCS.HoboBot.Modules.CasinoGames {
    public enum ClassicSlotIcon {
        Cherry, 
        // Lemon, 
        // Orange,
        // Plum, 
        Bell, 
        //Hotdog, 
        Bar,
        Seven,
        Wild,
    }

    public sealed class ClassicSlotMachineModule : BaseSlotMachineModule<ClassicSlotIcon> {
        protected override string GameName => "Classic Slots";
        protected override string GameCommandPrefix => "slots";
        protected override IReadOnlyDictionary<ClassicSlotIcon, string> IconToEmojiMap => EmojiMap;

        static readonly IReadOnlyDictionary<ClassicSlotIcon, string> EmojiMap = new Dictionary<ClassicSlotIcon, string> {
            { ClassicSlotIcon.Cherry, "🍒" },
            // { ClassicSlotIcon.Lemon, "🍋" },
            // { ClassicSlotIcon.Orange, "🍊" },
            // { ClassicSlotIcon.Plum, "🍑" },
            { ClassicSlotIcon.Bell, "🔔" },
            //{ ClassicSlotIcon.Hotdog, "🌭" },
            { ClassicSlotIcon.Bar, "🍷" },
            { ClassicSlotIcon.Seven, "7️⃣" },
            { ClassicSlotIcon.Wild, "🃏" },
        };
        
        static readonly List<List<(int r, int c)>> Paylines = [
            new() { (0, 0), (0, 1), (0, 2) },
        ];
        

        protected override int NumberOfReels => 3;
        

        static readonly Dictionary<ClassicSlotIcon, double> SymbolWeights = new() {
            { ClassicSlotIcon.Cherry, 20 },   // more common
            // { ClassicSlotIcon.Lemon, 20 }, 
            // { ClassicSlotIcon.Orange, 18 }, 
            // { ClassicSlotIcon.Plum, 15 },  
            { ClassicSlotIcon.Bell, 14 },     // less common
            //{ ClassicSlotIcon.Hotdog, 10 },   // rare
            { ClassicSlotIcon.Bar, 10 },       // rare
            { ClassicSlotIcon.Seven, 5.5 },     // very rare
            { ClassicSlotIcon.Wild, 15 },    // very rare, acts as a joker
        };

        static readonly Dictionary<ClassicSlotIcon, decimal> BasePayoutMultipliers = new() {
            { ClassicSlotIcon.Cherry, 1.1m },
            // { ClassicSlotIcon.Lemon, 5.0m },
            // { ClassicSlotIcon.Orange, 8.0m  },
            // { ClassicSlotIcon.Plum, 10.0m },
            { ClassicSlotIcon.Bell, 1.5m },
            //{ ClassicSlotIcon.Hotdog, 25.0m },
            { ClassicSlotIcon.Bar, 8.0m },
            { ClassicSlotIcon.Seven, 10.0m },
            { ClassicSlotIcon.Wild, 1.25m }, // Wilds pay out more
        };
        
        static readonly double TotalWeight;
        static readonly List<KeyValuePair<ClassicSlotIcon, double>> CumulativeWeights = [];
        
        static decimal GetLinePayoutMultiplier(ClassicSlotIcon symbol) =>
            BasePayoutMultipliers.GetValueOrDefault( symbol, 0m );

        static ClassicSlotMachineModule() {
            double cumulative = 0;
            foreach (KeyValuePair<ClassicSlotIcon, double> kv in SymbolWeights) {
                cumulative += kv.Value;
                CumulativeWeights.Add(new KeyValuePair<ClassicSlotIcon, double>(kv.Key, cumulative));
            }
            TotalWeight = cumulative;
        }

        static ClassicSlotIcon GetWeightedRandomSymbol() {
            double roll = Rng.NextDouble() * TotalWeight;
            foreach (KeyValuePair<ClassicSlotIcon, double> kv in CumulativeWeights) {
                if (roll < kv.Value)
                    return kv.Key;
            }
            return CumulativeWeights.Last().Key;
        }

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

        public override ClassicSlotIcon[][] SpinReelsInternal() {
            ClassicSlotIcon[] row = new ClassicSlotIcon[NumberOfReels];
            for (var i = 0; i < NumberOfReels; i++) {
                row[i] = GetWeightedRandomSymbol();
            }
            return [row];
        }

        public override (decimal payoutMultiplier, string winDescription) CalculatePayoutInternal(
            ClassicSlotIcon[][] currentSpin, float bet
        ) {
            decimal totalBetMultiplier = 0m;
            List<string> winDescriptions = new List<string>();

            for (var i = 0; i < Paylines.Count; i++) {
                List<(int r, int c)> path = Paylines[i];
                ClassicSlotIcon[] symbols = new[] {
                    currentSpin[path[0].r][path[0].c],
                    currentSpin[path[1].r][path[1].c],
                    currentSpin[path[2].r][path[2].c],
                };

                if (TryGetWinningSymbol(symbols, out var payingSymbol)) {
                    decimal lineMultiplier;

                    if (payingSymbol != ClassicSlotIcon.Wild && payingSymbol != ClassicSlotIcon.Cherry) {
                        int wildCount = symbols.Count(s => s == ClassicSlotIcon.Wild);
                        // Pure 7-7-7
                        lineMultiplier = GetLinePayoutMultiplier(payingSymbol) 
                                         * (wildCount == 0 ? 4m : 1m);
                    }
                    else {
                        lineMultiplier = GetLinePayoutMultiplier(payingSymbol);
                    }

                    if (lineMultiplier > 0m) {
                        totalBetMultiplier += lineMultiplier;
                        winDescriptions.Add(
                            $"{string.Concat(symbols.Select(GetEmojiForSymbol))} " +
                            $"({lineMultiplier:0.##}x)"
                        );
                    }
                }
            }

            string description = totalBetMultiplier > 0m
                ? "Hit:" + string.Join(", ", winDescriptions)
                : "No win this time.";

            return (totalBetMultiplier, description);
        }
        
        /// <summary>
        /// Returns true – and the symbol that should be paid – if the three positions constitute a win.
        /// Rules preserved:
        ///   • Three identical symbols  (AAA or WWW)  
        ///   • Two identical + one Wild (AXW, AWX, WAX)  
        ///   • Two Wilds + one symbol   (WWX, WXW, XWW)
        /// </summary>
        static bool TryGetWinningSymbol(
            IReadOnlyList<ClassicSlotIcon> symbols,
            out ClassicSlotIcon winningSymbol
        ) {
            var wild = ClassicSlotIcon.Wild;
            int wildCount = symbols.Count( s => s == wild );

            // All Wilds
            if ( wildCount == 3 ) {
                winningSymbol = wild;
                return true;
            }

            // No Wilds – must all match
            if ( wildCount == 0 && symbols[0] == symbols[1] && symbols[1] == symbols[2] ) {
                winningSymbol = symbols[0];
                return true;
            }

            // One or two Wilds – remaining non-Wilds must be identical
            if ( wildCount is 1 or 2 ) {
                var nonWild = symbols.First( s => s != wild );
                if ( symbols.All( s => s == wild || s == nonWild ) ) {
                    winningSymbol = nonWild;
                    return true;
                }
            }

            winningSymbol = default;
            return false;
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