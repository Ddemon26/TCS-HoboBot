using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Diagnostics;
using System.Reflection;
using TCS.HoboBot.Modules.CasinoGames; // Make sure to import the module's namespace
using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;
using TestContext = Microsoft.VisualStudio.TestTools.UnitTesting.TestContext;

namespace TCS.HoboBot.Tests {
    [TestClass]
    public class SlotMachine3X3ModuleTests {
        // --- Simulation Configuration ---
        const int NumberOfSpinsToSimulate = 2_000_000; // Ample for a 3x3 machine
        const float SimulationBetAmount = 1.0f;

        static MethodInfo _spinReelsMethod;
        static MethodInfo _calculatePayoutMethod;

        /// <summary>
        /// This runs once before all tests. It uses reflection to get access
        /// to the protected methods from the module so we can test them.
        /// </summary>
        [ClassInitialize]
        public static void ClassInitialize(TestContext context) {
            var moduleType = typeof(SlotMachine3X3Module);
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;

            // Get the protected methods we need to invoke
            _spinReelsMethod = moduleType.GetMethod( "SpinReelsInternal", flags );
            _calculatePayoutMethod = moduleType.GetMethod( "CalculatePayoutInternal", flags );

            if ( _spinReelsMethod == null || _calculatePayoutMethod == null ) {
                throw new InvalidOperationException( "Could not find required protected methods via reflection. They may have been renamed." );
            }
        }

        [TestMethod]
        public void CalculateRtp_ShouldMatchTheoreticalValue() {
            // --- 1. Calculate the Theoretical RTP ---
            var module = new SlotMachine3X3Module();
            decimal theoreticalRtp = CalculateTheoreticalRtp( module );

            // --- 2. Run the Simulation ---
            decimal totalWin = 0m;
            var stopwatch = Stopwatch.StartNew();

            for (int i = 0; i < NumberOfSpinsToSimulate; i++) {
                // Invoke the protected methods using the MethodInfo we cached
                var grid = (ClassicSlotIcon[][])_spinReelsMethod.Invoke( module, null );
                var result = ((decimal, string))_calculatePayoutMethod.Invoke( module, new object[] { grid, SimulationBetAmount } );

                totalWin += result.Item1; // Item1 is the payoutMultiplier
            }

            stopwatch.Stop();

            decimal totalBet = (decimal)SimulationBetAmount * NumberOfSpinsToSimulate;
            decimal simulatedRtp = (totalWin / totalBet);

            // --- 3. Assert and Analyze ---
            Console.WriteLine( "--- 3x3 Slots RTP Verification ---" );
            Console.WriteLine( $"Total Spins Simulated: {NumberOfSpinsToSimulate:N0}" );
            Console.WriteLine( $"Simulation Duration: {stopwatch.Elapsed.TotalSeconds:F2} seconds" );
            Console.WriteLine( "----------------------------------" );
            Console.WriteLine( $"Theoretical RTP: {theoreticalRtp:P4}" );
            Console.WriteLine( $"Simulated RTP:     {simulatedRtp:P4}" );
            Console.WriteLine( "----------------------------------" );

            // Assert that the simulated RTP is within 0.1% of the theoretical value.
            Assert.AreEqual( (double)theoreticalRtp, (double)simulatedRtp, 0.001, "Simulated RTP deviates too much from the theoretical calculation." );
        }

        /// <summary>
        /// Calculates the exact mathematical RTP of the slot machine based on its properties.
        /// </summary>
        decimal CalculateTheoreticalRtp(SlotMachine3X3Module module) {
            // Use reflection to get private/protected fields
            var symbols = (IReadOnlyList<ClassicSlotIcon>)module.GetType().GetProperty( "Symbols", BindingFlags.NonPublic | BindingFlags.Instance ).GetValue( module );
            var paylinesField = typeof(SlotMachine3X3Module).GetField( "Paylines", BindingFlags.NonPublic | BindingFlags.Static );
            var paylines = (List<List<(int r, int c)>>)paylinesField.GetValue( null );
            var getAdjustedPayoutMethod = module.GetType().GetMethod( "GetAdjustedLinePayoutMultiplier", BindingFlags.NonPublic | BindingFlags.Instance );

            int symbolCount = symbols.Count;
            if ( symbolCount == 0 ) return 0;

            // The probability of getting any specific 3-of-a-kind on one line
            decimal probOfThreeOfAKind = (decimal)Math.Pow( 1.0 / symbolCount, 3 );

            // Calculate the sum of all possible winning line multipliers
            decimal sumOfAllWinMultipliers = 0;
            foreach (var symbol in symbols) {
                // Invoke the private GetAdjustedLinePayoutMultiplier method for each symbol
                sumOfAllWinMultipliers += (decimal)getAdjustedPayoutMethod.Invoke( module, new object[] { symbol } );
            }

            // Expected Return = (Prob of any 3-of-a-kind) * (Average Payout) * (Number of Lines)
            // But a simpler way is: Sum over all lines [ Sum over all symbols [ P(3 of this symbol) * Payout(this symbol) ] ]
            decimal totalExpectedReturn = 0;
            foreach (var symbol in symbols) {
                decimal adjustedPayout = (decimal)getAdjustedPayoutMethod.Invoke( module, new object[] { symbol } );
                totalExpectedReturn += probOfThreeOfAKind * adjustedPayout;
            }

            // Multiply by the number of paylines
            return totalExpectedReturn * paylines.Count;
        }
    }
}