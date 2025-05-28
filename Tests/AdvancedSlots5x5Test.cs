using System.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TCS.HoboBot.Modules.CasinoGames.Slots;
using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

namespace Tests;

[TestClass] public class AdvancedSlots5X5Test {
    // --- Simulation Configuration ---
    const int NUMBER_OF_SPINS_TO_SIMULATE = 10_000_000; // Total spins to simulate
    const int PRE_SIMULATION_ROUNDS = 1_000_000; // Rounds to calculate averages
    const decimal SIMULATION_BET_AMOUNT = 1.0m;
    
    static readonly object Lock = new();

    #region Pre-Calculated Statistical Data
    // --- These values are calculated ONCE to feed the hyper-fast main simulation ---
    static readonly double SMiniGameTriggerProbability;
    static readonly decimal SAverageNormalSpinPayout;
    static readonly decimal SAverageScatterOnlyPayout; // Average scatter wins from a normal spin
    static readonly decimal SAverageMiniGamePayout;


    static AdvancedSlots5X5Test() {
        Console.WriteLine( "--- Starting one-time statistical pre-calculation... ---" );
        var stopwatch = Stopwatch.StartNew();


        // --- Step 2: Calculate Theoretic Probabilities ---
        Dictionary<AdvancedSlotIcon, double> symbolWeights = AdvancedSlotMachineModule.SymbolWeights;
        double totalWeight = symbolWeights.Values.Sum();
        double probMiniGameIcon = symbolWeights[AdvancedSlotIcon.MiniGame] / totalWeight;

        SMiniGameTriggerProbability = 1.0 - BinomialCdf( AdvancedSlotMachineModule.MINIGAME_TRIGGER_COUNT - 1, AdvancedSlotMachineModule.GRID_SIZE, probMiniGameIcon );

        // --- Step 3: Calculate Average Payouts via Monte Carlo Pre-Simulation ---
        (SAverageNormalSpinPayout, SAverageScatterOnlyPayout) = CalculateAverageNormalAndScatterPayouts();
        SAverageMiniGamePayout = CalculateAverageMiniGamePayout();

        stopwatch.Stop();
        Console.WriteLine( $"--- Pre-calculation complete in {stopwatch.Elapsed.TotalSeconds:F2} seconds. ---" );
        Console.WriteLine( $"Mini-Game Trigger Probability: {SMiniGameTriggerProbability:P4}" );
        Console.WriteLine( $"Average Normal Spin Payout: {SAverageNormalSpinPayout:F4}x" );
        Console.WriteLine( $"Average Mini-Game Payout: {SAverageMiniGamePayout:F4}x" );
        Console.WriteLine( $"Average Scatter Only Payout: {SAverageScatterOnlyPayout:F4}x" );
    }
    #endregion

    [TestMethod]
    public void CalculateTotalRtp() {
        // --- Arrange ---
        var totalNormalWinOverall = 0m; // Accumulates SAverageNormalSpinPayout (lines + scatters)
        var totalScatterWinFromNormal = 0m; // Accumulates SAverageScatterOnlyPayout
        var totalMiniGameWin = 0m;
        long miniGameTriggers = 0;

        var stopwatch = Stopwatch.StartNew();

        // --- Act ---
        Parallel.For(
            0L, NUMBER_OF_SPINS_TO_SIMULATE,
            () => new ThreadState { Rng = new Random() }, // Initialize ThreadState with a new Random instance
            (_, _, threadState) => {
                if ( threadState.Rng.NextDouble() < SMiniGameTriggerProbability ) {
                    threadState.MiniGameTriggers++;
                    threadState.TotalMiniGameWin += SAverageMiniGamePayout;
                }
                else {
                    threadState.TotalNormalWinOverall += SAverageNormalSpinPayout;
                    threadState.TotalScatterWinFromNormal += SAverageScatterOnlyPayout;
                }
                return threadState;
            },
            (finalThreadState) => {
                lock (Lock) {
                    totalNormalWinOverall += finalThreadState.TotalNormalWinOverall;
                    totalScatterWinFromNormal += finalThreadState.TotalScatterWinFromNormal;
                    totalMiniGameWin += finalThreadState.TotalMiniGameWin;
                }
                Interlocked.Add( ref miniGameTriggers, finalThreadState.MiniGameTriggers );
            }
        );

        stopwatch.Stop();

        // --- Assert & Analyze ---
        const decimal totalBet = SIMULATION_BET_AMOUNT * NUMBER_OF_SPINS_TO_SIMULATE;
        
        // totalNormalWinOverall includes both line wins and scatter wins from normal spins.
        // To get line wins only, we subtract totalScatterWinFromNormal.
        decimal totalLineWinsFromNormal = totalNormalWinOverall - totalScatterWinFromNormal;
        decimal totalWinnings = totalLineWinsFromNormal + totalScatterWinFromNormal + totalMiniGameWin; // Or simply totalNormalWinOverall + totalMiniGameWin

        decimal totalRtp = (totalWinnings / totalBet) * 100;
        decimal lineWinsRtp = (totalLineWinsFromNormal / totalBet) * 100;
        decimal scatterWinsRtp = (totalScatterWinFromNormal / totalBet) * 100;
        decimal miniGameRtp = (totalMiniGameWin / totalBet) * 100;

        Console.WriteLine( "\n--- Ludicrous Speed RTP Simulation Results ---" );
        Console.WriteLine( $"Main Simulation Duration: {stopwatch.Elapsed.TotalMilliseconds:F2} ms" );
        Console.WriteLine( $"Total Spins: {NUMBER_OF_SPINS_TO_SIMULATE:N0}" );
        Console.WriteLine( $"Utilized {Environment.ProcessorCount} CPU Cores" );
        Console.WriteLine( "------------------------------" );
        Console.WriteLine( $"Total Bet: {totalBet:C}" );
        Console.WriteLine( $"Total Won: {totalWinnings:C}" );
        Console.WriteLine( $"Mini-Games Triggered: {miniGameTriggers:N0} (1 in every ~{NUMBER_OF_SPINS_TO_SIMULATE / (double)Math.Max( 1, miniGameTriggers ):F2} spins)" );
        Console.WriteLine( "------------------------------" );
        Console.WriteLine( $"Line Wins Contribution: {totalLineWinsFromNormal:C} | RTP: {lineWinsRtp:F4}%" );
        Console.WriteLine( $"Scatter Wins Contribution: {totalScatterWinFromNormal:C} | RTP: {scatterWinsRtp:F4}%" );
        Console.WriteLine( $"Mini-Game Contribution: {totalMiniGameWin:C} | RTP: {miniGameRtp:F4}%" );
        Console.WriteLine( "------------------------------" );
        Console.WriteLine( $"Combined Total RTP: {totalRtp:F4}%" );

        Assert.IsTrue( totalRtp is > 95 and < 105, $"RTP of {totalRtp:F4}% is outside the expected range (95%-105%)." );
    }

    #region Pre-Calculation and Simulation Helpers
    // Simplified ThreadState for the main simulation
    class ThreadState {
        public decimal TotalNormalWinOverall; // Accumulates SAverageNormalSpinPayout (lines + scatters)
        public decimal TotalScatterWinFromNormal; // Accumulates SAverageScatterOnlyPayout
        public decimal TotalMiniGameWin;
        public long MiniGameTriggers;
        public required Random Rng;
    }

    // Runs a sub-simulation to find the average payout for a normal spin.
    static decimal CalculateAverageNormalPayout() {
        var totalPayout = 0m;
        var rng = new Random();
        for (var i = 0; i < PRE_SIMULATION_ROUNDS; i++) {
            AdvancedSlotIcon[][] grid = HyperOptimizedRtpTests.SpinGrid( rng );
            (decimal payout, _) = AdvancedSlotMachineModule.CalculateGridPayout( grid, AdvancedSlotMachineModule.ROWS, AdvancedSlotMachineModule.COLS );
            totalPayout += payout;
        }

        return totalPayout / PRE_SIMULATION_ROUNDS;
    }
    
    // Runs a sub-simulation to find the average payout for a normal spin (lines + scatters)
    // and the average payout for scatters only within that normal spin.
    static (decimal averageTotalNormalPayout, decimal averageScatterOnlyPayout) CalculateAverageNormalAndScatterPayouts() {
        var totalOverallPayoutSum = 0m;
        var totalScatterOnlyPayoutSum = 0m;
        var rng = new Random();

        for (var i = 0; i < PRE_SIMULATION_ROUNDS; i++) {
            AdvancedSlotIcon[][] grid = HyperOptimizedRtpTests.SpinGrid( rng );
            (decimal payout, _) = AdvancedSlotMachineModule.CalculateGridPayout( grid, AdvancedSlotMachineModule.ROWS, AdvancedSlotMachineModule.COLS );
            totalOverallPayoutSum += payout;

            int scatterCount = grid.SelectMany( r => r ).Count( s => s == AdvancedSlotIcon.Scatter );
            decimal scatterPayout = AdvancedSlotMachineModule.GetAdjustedScatterPayout( scatterCount );
            totalScatterOnlyPayoutSum += scatterPayout;
        }

        return (totalOverallPayoutSum / PRE_SIMULATION_ROUNDS, totalScatterOnlyPayoutSum / PRE_SIMULATION_ROUNDS);
    }

    // Runs a sub-simulation to find the average payout for a mini-game.
    static decimal CalculateAverageMiniGamePayout() {
        var totalPayout = 0m;
        var rng = new Random();
        for (var i = 0; i < PRE_SIMULATION_ROUNDS; i++) {
            totalPayout += HyperOptimizedRtpTests.SimulateMiniGame( rng );
        }

        return totalPayout / PRE_SIMULATION_ROUNDS;
    }
    #endregion

    #region Math Helpers
    // Calculates nCr (n choose r) for combinations.
    static double Combinations(int n, int k) {
        if ( k < 0 || k > n ) return 0;
        if ( k == 0 || k == n ) return 1;
        if ( k > n / 2 ) k = n - k;
        double res = 1;
        for (var i = 1; i <= k; i++) {
            res = res * (n - i + 1) / i;
        }

        return res;
    }

    // Binomial Probability: P(X=k) = C(n,k) * p^k * (1-p)^(n-k)
    static double BinomialProbability(int k, int n, double p) {
        return Combinations( n, k ) * Math.Pow( p, k ) * Math.Pow( 1 - p, n - k );
    }

    // Cumulative Binomial Probability: P(X<=k)
    static double BinomialCdf(int k, int n, double p) {
        double cdf = 0;
        for (var i = 0; i <= k; i++) {
            cdf += BinomialProbability( i, n, p );
        }

        return cdf;
    }
    #endregion
}

// A stripped-down helper class to power pre-simulation calculations.
public static class HyperOptimizedRtpTests {
    static readonly double STotalWeight;
    static readonly List<KeyValuePair<AdvancedSlotIcon, double>> SCumulativeWeights;
    static readonly AdvancedSlotIcon[] SMiniGameNonSpecialSymbols;
    static readonly Func<int, decimal> SCalculateMiniGamePayout;
   // const float MINI_WEIGHT = 35f; // 45% chance to get a MiniGame icon

    static HyperOptimizedRtpTests() {
        STotalWeight = AdvancedSlotMachineModule.SymbolWeights.Values.Sum();
        SCumulativeWeights = AdvancedSlotMachineModule.CumulativeWeights;

        var tempModuleInstance = new AdvancedSlotMachineModule();
        SCalculateMiniGamePayout = tempModuleInstance.CalculateMiniGamePayout;

        SMiniGameNonSpecialSymbols = AdvancedSlotMachineModule.SymbolWeights.Keys
            .Where(icon => icon != AdvancedSlotIcon.Scatter && icon != AdvancedSlotIcon.Wild && icon != AdvancedSlotIcon.MiniGame)
            .ToArray();
    }

    public static AdvancedSlotIcon[][] SpinGrid(Random rng) {
        AdvancedSlotIcon[][] grid = new AdvancedSlotIcon[AdvancedSlotMachineModule.ROWS][];
        for (var r = 0; r < AdvancedSlotMachineModule.ROWS; r++) {
            grid[r] = new AdvancedSlotIcon[AdvancedSlotMachineModule.COLS];
            for (var c = 0; c < AdvancedSlotMachineModule.COLS; c++) {
                grid[r][c] = GetRandomReelSymbol(rng);
            }
        }
        return grid;
    }

    public static decimal SimulateMiniGame(Random rng) {
        var totalStars = 0;
        for (var c = 0; c < AdvancedSlotMachineModule.COLS; c++) { // Assuming COLS for number of reels in mini-game
            AdvancedSlotIcon[] reel = SpinSingleMiniGameReel(rng, AdvancedSlotMachineModule.ROWS);
            totalStars += reel.Count(i => i == AdvancedSlotIcon.MiniGame);
        }
        return SCalculateMiniGamePayout(totalStars);
    }

    static AdvancedSlotIcon GetRandomReelSymbol(Random rng) {
        double randomValue = rng.NextDouble() * STotalWeight;
        foreach ((var symbol, double cumulativeWeight) in SCumulativeWeights) {
            if ( randomValue < cumulativeWeight ) return symbol;
        }

        return SCumulativeWeights.Last().Key;
    }

    static AdvancedSlotIcon[] SpinSingleMiniGameReel(Random rng, int rows) {
        AdvancedSlotIcon[] reel = new AdvancedSlotIcon[rows];
        for (var r = 0; r < rows; r++) {
            reel[r] = rng
                .Next(100) < AdvancedSlotMachineModule.MINIGAME_ICON_BOOST_CHANCE
                ? AdvancedSlotIcon.MiniGame : SMiniGameNonSpecialSymbols[rng.Next(SMiniGameNonSpecialSymbols.Length)];
        }
        return reel;
    }
}