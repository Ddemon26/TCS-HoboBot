using System.Text;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using TCS.HoboBot.Data;
namespace TCS.HoboBot.Modules.CasinoGames.Slots;

public enum ThreeXThreeSlotIcon { Cherry, Lemon, Orange, Plum, Bell, Hotdog, Bar, Seven, Wild }

public class SlotMachine3X3Module : BaseSlotMachineModule<ThreeXThreeSlotIcon> {
    protected override string GameName => "3x3 Slots";
    protected override string GameCommandPrefix => "slots3x3";
    protected override IReadOnlyDictionary<ThreeXThreeSlotIcon, string> IconToEmojiMap => Emojis;

    static readonly IReadOnlyDictionary<ThreeXThreeSlotIcon, string> Emojis = new Dictionary<ThreeXThreeSlotIcon, string> {
            { ThreeXThreeSlotIcon.Cherry, "🍒" },
            { ThreeXThreeSlotIcon.Lemon, "🍋" },
            { ThreeXThreeSlotIcon.Orange, "🍊" },
            { ThreeXThreeSlotIcon.Plum, "🍑" },
            { ThreeXThreeSlotIcon.Bell, "🔔" },
            { ThreeXThreeSlotIcon.Hotdog, "🌭" },
            { ThreeXThreeSlotIcon.Bar, "🍷" },
            { ThreeXThreeSlotIcon.Seven, "7️⃣" },
            { ThreeXThreeSlotIcon.Wild, "🃏" },
        };

    static readonly double TotalWeight;
    static readonly List<KeyValuePair<ThreeXThreeSlotIcon, double>> CumulativeWeights;

    protected override int NumberOfReels => 3;
    protected override int NumberOfRows => 3;

    static readonly List<List<(int r, int c)>> Paylines = [
        // horizontals
        new() { (0, 0), (0, 1), (0, 2) },
        new() { (1, 0), (1, 1), (1, 2) },
        new() { (2, 0), (2, 1), (2, 2) },

        // diagonals
        new() { (0, 0), (1, 1), (2, 2) },
        new() { (0, 2), (1, 1), (2, 0) },

        // verticals
        new() { (0, 0), (1, 0), (2, 0) },
        new() { (0, 1), (1, 1), (2, 1) },
        new() { (0, 2), (1, 2), (2, 2) },

        // V-shapes
        new() { (0, 0), (1, 1), (0, 2) }, // V top
        new() { (2, 0), (1, 1), (2, 2) }, // ∧ bottom

        // zig-zags
        new() { (0, 0), (1, 1), (2, 0) }, // left zig-zag
        new() { (0, 2), (1, 1), (2, 2) }, // right zig-zag
    ];

    static readonly Dictionary<ThreeXThreeSlotIcon, double> SymbolWeights = new() {
        { ThreeXThreeSlotIcon.Cherry, 20 }, // increased from 13
        { ThreeXThreeSlotIcon.Lemon, 20 }, // increased from 13
        { ThreeXThreeSlotIcon.Orange, 18 }, // increased from 12
        { ThreeXThreeSlotIcon.Plum, 16 }, // increased from 10
        { ThreeXThreeSlotIcon.Bell, 14 }, // increased from 8
        { ThreeXThreeSlotIcon.Hotdog, 10 }, // increased from 5
        { ThreeXThreeSlotIcon.Bar, 8 }, // increased from 3
        { ThreeXThreeSlotIcon.Seven, 6 }, // kept the same to remain rare
        { ThreeXThreeSlotIcon.Wild, 4 }, // Wilds are less common than other symbols
    };

    static readonly Dictionary<ThreeXThreeSlotIcon, decimal> BaseLinePayoutMultipliers = new() {
        { ThreeXThreeSlotIcon.Cherry, 1.2m },
        { ThreeXThreeSlotIcon.Lemon, 1.2m },
        { ThreeXThreeSlotIcon.Orange, 1.25m },
        { ThreeXThreeSlotIcon.Plum, 1.5m },
        { ThreeXThreeSlotIcon.Bell, 3m },
        { ThreeXThreeSlotIcon.Hotdog, 8m },
        { ThreeXThreeSlotIcon.Bar, 10m },
        { ThreeXThreeSlotIcon.Seven, 20m },
    };

    static SlotMachine3X3Module() {
        TotalWeight = SymbolWeights.Values.Sum();
        double cumulative = 0;
        CumulativeWeights = [];
        foreach (KeyValuePair<ThreeXThreeSlotIcon, double> kv in SymbolWeights) {
            cumulative += kv.Value;
            CumulativeWeights.Add( new KeyValuePair<ThreeXThreeSlotIcon, double>( kv.Key, cumulative ) );
        }
    }

    static decimal GetLinePayoutMultiplier(ThreeXThreeSlotIcon symbol) =>
        BaseLinePayoutMultipliers.GetValueOrDefault( symbol, 0m );

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

    public override ThreeXThreeSlotIcon[][] SpinReelsInternal() {
        ThreeXThreeSlotIcon[][] grid = new ThreeXThreeSlotIcon[ NumberOfRows ][];
        for (var r = 0; r < NumberOfRows; r++) {
            grid[r] = new ThreeXThreeSlotIcon[ NumberOfReels ];
            for (var c = 0; c < NumberOfReels; c++) {
                grid[r][c] = GetWeightedRandomSymbol();
            }
        }

        return grid;
    }

    static ThreeXThreeSlotIcon GetWeightedRandomSymbol() {
        double roll = Rng.NextDouble() * TotalWeight;
        foreach (KeyValuePair<ThreeXThreeSlotIcon, double> kv in CumulativeWeights) {
            if ( roll < kv.Value )
                return kv.Key;
        }

        return CumulativeWeights.Last().Key;
    }

    public override (decimal payoutMultiplier, string winDescription) CalculatePayoutInternal(
        ThreeXThreeSlotIcon[][] grid, float bet
    ) {
        decimal totalBetMultiplier = 0m;
        List<string> winDescriptions = new List<string>();

        for (var i = 0; i < Paylines.Count; i++) {
            List<(int r, int c)> path = Paylines[i];
            ThreeXThreeSlotIcon[] symbols = new[] {
                grid[path[0].r][path[0].c],
                grid[path[1].r][path[1].c],
                grid[path[2].r][path[2].c]
            };

            if ( TryGetWinningSymbol( symbols, out var payingSymbol ) ) {
                decimal lineMultiplier = GetLinePayoutMultiplier( payingSymbol );
                if ( lineMultiplier > 0 ) {
                    totalBetMultiplier += lineMultiplier;
                    winDescriptions.Add(
                        $"Line {i + 1}: {string.Concat( symbols.Select( GetEmojiForSymbol ) )} " +
                        $"({lineMultiplier:0.##}x)"
                    );
                }
            }
        }

        return totalBetMultiplier > 0
            ? (totalBetMultiplier,
                $"Wins on {winDescriptions.Count} line(s):\n" + string.Join( '\n', winDescriptions ))
            : (0m, "No winning lines this spin.");
    }

    /// <summary>
    /// Returns true – and the symbol that should be paid – if the three positions constitute a win.
    /// Rules preserved:
    ///   • Three identical symbols  (AAA or WWW)  
    ///   • Two identical + one Wild (AXW, AWX, WAX)  
    ///   • Two Wilds + one symbol   (WWX, WXW, XWW)
    /// </summary>
    static bool TryGetWinningSymbol(
        IReadOnlyList<ThreeXThreeSlotIcon> symbols,
        out ThreeXThreeSlotIcon winningSymbol
    ) {
        var wild = ThreeXThreeSlotIcon.Wild;
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
        ThreeXThreeSlotIcon[][] grid,
        float bet,
        decimal payoutMultiplier,
        string winDescription,
        decimal totalWinnings
    ) {
        var sb = new StringBuilder();
        for (var r = 0; r < NumberOfRows; r++) {
            for (var c = 0; c < NumberOfReels; c++) {
                sb.Append( GetEmojiForSymbol( grid[r][c] ) );
                if ( c < NumberOfReels - 1 ) {
                    sb.Append( " | " );
                }
            }

            sb.AppendLine();
        }

        decimal profit = totalWinnings - (decimal)bet;
        string outcome;
        if ( payoutMultiplier == 0m ) {
            outcome = $"Unlucky! You lost **{bet:C2}**.";
        }
        else if ( profit == 0 ) {
            outcome = $"Push! Your **{bet:C2}** bet is returned.";
        }
        else {
            outcome = $"Congratulations! You won **{profit:C2}** (Total: {totalWinnings:C2}).";
        }

        outcome += $"\nYour new balance: **${PlayersWallet.GetBalance( Context.Guild.Id, user.Id ):C2}**";

        var embed = new EmbedBuilder()
            .WithTitle( $"{GameName} – {bet:C2} Bet" )
            .WithDescription( $"{user.Mention} spins the 3×3…\n\n{sb}\n{winDescription}" )
            .WithFooter( outcome )
            .WithColor( profit > 0 ? Color.Green : profit == 0 ? Color.LightGrey : Color.Red );

        return embed.Build();
    }
}