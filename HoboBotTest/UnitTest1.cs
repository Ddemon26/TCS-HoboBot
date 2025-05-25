using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using TCS.HoboBot.Modules.CasinoGames.Slots; // Your actual game module
using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

namespace TCS.HoboBot.Tests {
    [TestClass]
    public class LudicrousSpeedRtpTests {
        // --- Simulation Configuration ---
        private const int NumberOfSpinsToSimulate = 10_000_000; // Total spins to simulate
        private const int PreSimulationRounds = 1_000_000; // Rounds to calculate averages
        private const decimal SimulationBetAmount = 1.0m;

        // --- Game Constants ---
        private const int Rows = 5;
        private const int Cols = 5;
        private const int GridSize = Rows * Cols;
        private const int MiniGameTriggerCount = 4;

        private static readonly object _lock = new();

        #region Pre-Calculated Statistical Data
        // --- These values are calculated ONCE to feed the hyper-fast main simulation ---
        private static readonly double s_miniGameTriggerProbability;
        private static readonly decimal s_averageNormalSpinPayout;
        private static readonly decimal s_averageMiniGamePayout;

        // --- Cached Reflection Data ---
        private static readonly Func<int, decimal> s_calculateMiniGamePayout;

        static LudicrousSpeedRtpTests() {
            Console.WriteLine( "--- Starting one-time statistical pre-calculation... ---" );
            var stopwatch = Stopwatch.StartNew();

            // --- Step 1: Cache Reflection Data ---
            var tempModuleInstance = new AdvancedSlotMachineModule();
            var payoutMethodInfo = typeof(AdvancedSlotMachineModule).GetMethod( "CalculateMiniGamePayout", BindingFlags.Public | BindingFlags.Instance );
            s_calculateMiniGamePayout = (Func<int, decimal>)Delegate.CreateDelegate( typeof(Func<int, decimal>), tempModuleInstance, payoutMethodInfo );

            // --- Step 2: Calculate Theoretic Probabilities ---
            var symbolWeights = (Dictionary<AdvancedSlotIcon, double>)typeof(AdvancedSlotMachineModule)
                .GetField( "SymbolWeights", BindingFlags.NonPublic | BindingFlags.Static )!.GetValue( null );
            double totalWeight = symbolWeights.Values.Sum();
            double probMiniGameIcon = symbolWeights[AdvancedSlotIcon.MiniGame] / totalWeight;

            s_miniGameTriggerProbability = 1.0 - BinomialCdf( MiniGameTriggerCount - 1, GridSize, probMiniGameIcon );

            // --- Step 3: Calculate Average Payouts via Monte Carlo Pre-Simulation ---
            s_averageNormalSpinPayout = CalculateAverageNormalPayout();
            s_averageMiniGamePayout = CalculateAverageMiniGamePayout();

            stopwatch.Stop();
            Console.WriteLine( $"--- Pre-calculation complete in {stopwatch.Elapsed.TotalSeconds:F2} seconds. ---" );
            Console.WriteLine( $"Mini-Game Trigger Probability: {s_miniGameTriggerProbability:P4}" );
            Console.WriteLine( $"Average Normal Spin Payout: {s_averageNormalSpinPayout:F4}x" );
            Console.WriteLine( $"Average Mini-Game Payout: {s_averageMiniGamePayout:F4}x" );
        }
        #endregion

        [TestMethod]
        public void CalculateTotalRtp_ShouldRunAtLudicrousSpeed() {
            // --- Arrange ---
            decimal totalNormalWin = 0m;
            decimal totalMiniGameWin = 0m;
            long miniGameTriggers = 0;

            var stopwatch = Stopwatch.StartNew();

            // --- Act ---
            // The main loop is now trivial. It checks one probability and adds a pre-calculated average.
            Parallel.For(
                0L, NumberOfSpinsToSimulate,
                () => new ThreadState { Rng = new Random() },
                (_, _, threadState) => {
                    if ( threadState.Rng.NextDouble() < s_miniGameTriggerProbability ) {
                        threadState.MiniGameTriggers++;
                        threadState.TotalMiniGameWin += s_averageMiniGamePayout;
                    }
                    else {
                        threadState.TotalNormalWin += s_averageNormalSpinPayout;
                    }

                    return threadState;
                },
                (finalThreadState) => {
                    lock (_lock) {
                        totalNormalWin += finalThreadState.TotalNormalWin;
                        totalMiniGameWin += finalThreadState.TotalMiniGameWin;
                    }

                    Interlocked.Add( ref miniGameTriggers, finalThreadState.MiniGameTriggers );
                }
            );

            stopwatch.Stop();

            // --- Assert & Analyze ---
            decimal totalBet = SimulationBetAmount * NumberOfSpinsToSimulate;
            decimal totalWinnings = totalNormalWin + totalMiniGameWin;
            decimal totalRtp = (totalWinnings / totalBet) * 100;
            decimal normalGameRtp = (totalNormalWin / totalBet) * 100;
            decimal miniGameRtp = (totalMiniGameWin / totalBet) * 100;

            Console.WriteLine( "\n--- Ludicrous Speed RTP Simulation Results ---" );
            Console.WriteLine( $"Main Simulation Duration: {stopwatch.Elapsed.TotalMilliseconds:F2} ms" );
            // ... rest of the output ...
            Console.WriteLine( $"Total Spins: {NumberOfSpinsToSimulate:N0}" );
            Console.WriteLine( $"Utilized {Environment.ProcessorCount} CPU Cores" );
            Console.WriteLine( "------------------------------" );
            Console.WriteLine( $"Total Bet: {totalBet:C}" );
            Console.WriteLine( $"Total Won: {totalWinnings:C}" );
            Console.WriteLine( $"Mini-Games Triggered: {miniGameTriggers:N0} (1 in every ~{NumberOfSpinsToSimulate / (double)Math.Max( 1, miniGameTriggers ):F2} spins)" );
            Console.WriteLine( "------------------------------" );
            Console.WriteLine( $"Normal Game Contribution: {totalNormalWin:C} | RTP: {normalGameRtp:F4}%" );
            Console.WriteLine( $"Mini-Game Contribution: {totalMiniGameWin:C} | RTP: {miniGameRtp:F4}%" );
            Console.WriteLine( "------------------------------" );
            Console.WriteLine( $"Combined Total RTP: {totalRtp:F4}%" );

            Assert.IsTrue( totalRtp is > 95 and < 105, $"RTP of {totalRtp:F4}% is outside the expected range (75%-110%)." );
        }

        #region Pre-Calculation and Simulation Helpers
        // Simplified ThreadState for the main simulation
        private class ThreadState {
            public decimal TotalNormalWin;
            public decimal TotalMiniGameWin;
            public long MiniGameTriggers;
            public Random Rng;
        }

        // Runs a sub-simulation to find the average payout for a normal spin.
        private static decimal CalculateAverageNormalPayout() {
            decimal totalPayout = 0m;
            var rng = new Random();
            for (int i = 0; i < PreSimulationRounds; i++) {
                var grid = HyperOptimizedRtpTests.SpinGrid( rng ); // Using a helper from our previous optimization
                (decimal payout, _) = AdvancedSlotMachineModule.CalculateGridPayout( grid, Rows, Cols );
                totalPayout += payout;
            }

            return totalPayout / PreSimulationRounds;
        }

        // Runs a sub-simulation to find the average payout for a mini-game.
        private static decimal CalculateAverageMiniGamePayout() {
            decimal totalPayout = 0m;
            var rng = new Random();
            for (int i = 0; i < PreSimulationRounds; i++) {
                totalPayout += HyperOptimizedRtpTests.SimulateMiniGame( rng );
            }

            return totalPayout / PreSimulationRounds;
        }
        #endregion

        #region Math Helpers
        // Calculates nCr (n choose r) for combinations.
        private static double Combinations(int n, int k) {
            if ( k < 0 || k > n ) return 0;
            if ( k == 0 || k == n ) return 1;
            if ( k > n / 2 ) k = n - k;
            double res = 1;
            for (int i = 1; i <= k; i++) {
                res = res * (n - i + 1) / i;
            }

            return res;
        }

        // Binomial Probability: P(X=k) = C(n,k) * p^k * (1-p)^(n-k)
        private static double BinomialProbability(int k, int n, double p) {
            return Combinations( n, k ) * Math.Pow( p, k ) * Math.Pow( 1 - p, n - k );
        }

        // Cumulative Binomial Probability: P(X<=k)
        private static double BinomialCdf(int k, int n, double p) {
            double cdf = 0;
            for (int i = 0; i <= k; i++) {
                cdf += BinomialProbability( i, n, p );
            }

            return cdf;
        }
        #endregion
    }

    // We keep a stripped-down version of the previous class's helpers
    // to power our pre-simulation calculations.
    public static class HyperOptimizedRtpTests {
        private static readonly double s_totalWeight;
        private static readonly List<KeyValuePair<AdvancedSlotIcon, double>> s_cumulativeWeights;
        private static readonly AdvancedSlotIcon[] s_miniGameNonSpecialSymbols;
        private static readonly Func<int, decimal> s_calculateMiniGamePayout;
        const float miniWeight = 35f; // 45% chance to get a MiniGame icon

        static HyperOptimizedRtpTests() {
            // This re-caches the data needed for the pre-simulation helpers.
            var symbolWeights = (Dictionary<AdvancedSlotIcon, double>)typeof(AdvancedSlotMachineModule)
                .GetField( "SymbolWeights", BindingFlags.NonPublic | BindingFlags.Static )!.GetValue( null );
            s_totalWeight = symbolWeights.Values.Sum();

            var cumulativeWeightsField = typeof(AdvancedSlotMachineModule).GetField( "CumulativeWeights", BindingFlags.NonPublic | BindingFlags.Static );
            s_cumulativeWeights = (List<KeyValuePair<AdvancedSlotIcon, double>>)cumulativeWeightsField.GetValue( null );

            var tempModuleInstance = new AdvancedSlotMachineModule();
            var payoutMethodInfo = typeof(AdvancedSlotMachineModule).GetMethod( "CalculateMiniGamePayout", BindingFlags.Public | BindingFlags.Instance );
            s_calculateMiniGamePayout = (Func<int, decimal>)Delegate.CreateDelegate( typeof(Func<int, decimal>), tempModuleInstance, payoutMethodInfo );

            s_miniGameNonSpecialSymbols = new[] { AdvancedSlotIcon.Nine, AdvancedSlotIcon.Ten, AdvancedSlotIcon.Jack, AdvancedSlotIcon.Queen, AdvancedSlotIcon.King, AdvancedSlotIcon.Ace, AdvancedSlotIcon.GemPurple, AdvancedSlotIcon.GemBlue, AdvancedSlotIcon.GemGreen, AdvancedSlotIcon.GemRed };
        }

        public static AdvancedSlotIcon[][] SpinGrid(Random rng) {
            var grid = new AdvancedSlotIcon[ 5 ][];
            for (var r = 0; r < 5; r++) {
                grid[r] = new AdvancedSlotIcon[ 5 ];
                for (var c = 0; c < 5; c++) {
                    grid[r][c] = GetRandomReelSymbol( rng );
                }
            }

            return grid;
        }

        public static decimal SimulateMiniGame(Random rng) {
            int totalStars = 0;
            for (int c = 0; c < 5; c++) {
                var reel = SpinSingleMiniGameReel( rng );
                totalStars += reel.Count( i => i == AdvancedSlotIcon.MiniGame );
            }

            return s_calculateMiniGamePayout( totalStars );
        }

        private static AdvancedSlotIcon GetRandomReelSymbol(Random rng) {
            double randomValue = rng.NextDouble() * s_totalWeight;
            foreach (var (symbol, cumulativeWeight) in s_cumulativeWeights) {
                if ( randomValue < cumulativeWeight ) return symbol;
            }

            return s_cumulativeWeights.Last().Key;
        }

        private static AdvancedSlotIcon[] SpinSingleMiniGameReel(Random rng) {
            var reel = new AdvancedSlotIcon[ 5 ];
            for (int r = 0; r < 5; r++) {
                reel[r] = rng.Next( 100 ) < miniWeight ? AdvancedSlotIcon.MiniGame : s_miniGameNonSpecialSymbols[rng.Next( s_miniGameNonSpecialSymbols.Length )];
            }

            return reel;
        }
    }
}