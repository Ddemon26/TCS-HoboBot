using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using TCS.HoboBot.Modules.CasinoGames.Slots;
using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

namespace Tests;

public class TestInfo {
    public string TestName { get; set; } = string.Empty;
    public string TestDate { get; set; } = DateTime.UtcNow.ToString("o"); // ISO 8601 format
    public long TotalSpins { get; set; }
    public decimal TotalBet { get; set; }
    public decimal TotalWinnings { get; set; }
    public decimal TotalRtp { get; set; }
    public decimal HitFrequency { get; set; }
    public double SimulationDurationSeconds { get; set; }
    public required List<ComboInfo> ComboCounts { get; set; }
}

public class ComboInfo {
    public string Combo { get; set; } = string.Empty;
    public long Count { get; set; }
    public double Percentage { get; set; }
}

[TestClass]
public class SlotMachine3X3RtpTest {
    // --- Simulation Configuration ---
    const int NUMBER_OF_SPINS_TO_SIMULATE = 25_000_000;
    const float SIMULATION_BET_AMOUNT = 1.0f; // The module uses float for bets

    static readonly object Lock = new();

    [TestMethod]
    public void CalculateTotalRtp() {
        // --- Arrange ---
        var totalWinnings = 0m;
        long wins = 0;
        ConcurrentDictionary<string, long> comboCounts = new();
        var stopwatch = Stopwatch.StartNew();

        // --- Act ---
        Parallel.For(
            0L, NUMBER_OF_SPINS_TO_SIMULATE,
            () => new SlotMachine3X3Module(), // Each thread gets its own instance of the harness
            (i, loopState, module) => {
                // 1. Simulate a spin
                ThreeXThreeSlotIcon[][] grid = module.SpinReelsInternal();

                // 2. Calculate the payout multiplier for that spin
                (decimal payoutMultiplier, string winDescription) = module.CalculatePayoutInternal( grid, SIMULATION_BET_AMOUNT );

                // 3. If there was a win, calculate the winnings and aggregate the results
                if ( payoutMultiplier > 0 ) {
                    decimal spinWinnings = (decimal)SIMULATION_BET_AMOUNT * payoutMultiplier;
                    Interlocked.Increment( ref wins );

                    // Parse winDescription to extract the icon combos
                    // Expected format:
                    // "Wins on X line(s):\nLine 1: <icons> (<multiplier>x)\n..."
                    string[] lines = winDescription.Split( '\n' );
                    // Skip the header line and process each winning line combo
                    for (var j = 1; j < lines.Length; j++) {
                        string line = lines[j].Trim();
                        if ( string.IsNullOrEmpty( line ) )
                            continue;
                        int colonIndex = line.IndexOf( ':' );
                        if ( colonIndex >= 0 ) {
                            int start = colonIndex + 1;
                            int parenIndex = line.IndexOf( '(', start );
                            string combo = parenIndex > 0
                                ? line.Substring( start, parenIndex - start ).Trim()
                                : line.Substring( start ).Trim();
                            comboCounts.AddOrUpdate( combo, 1, (_, count) => count + 1 );
                        }
                    }

                    // Use a lock for decimal type as Interlocked does not support it
                    lock (Lock) {
                        totalWinnings += spinWinnings;
                    }
                }

                return module; // Return the module instance for the next iteration on this thread
            },
            (_) => { } // Finalizer delegate, not needed here
        );

        stopwatch.Stop();

        // --- Assert & Analyze ---
        decimal totalBet = (decimal)SIMULATION_BET_AMOUNT * NUMBER_OF_SPINS_TO_SIMULATE;
        decimal totalRtp = totalWinnings / totalBet * 100;
        decimal hitFrequency = (decimal)wins / NUMBER_OF_SPINS_TO_SIMULATE * 100;

        Console.WriteLine( "\n--- 3x3 Slots RTP Simulation Results ---" );
        Console.WriteLine( $"Simulation Duration: {stopwatch.Elapsed.TotalSeconds:F2} seconds" );
        Console.WriteLine( $"Total Spins: {NUMBER_OF_SPINS_TO_SIMULATE:N0}" );
        Console.WriteLine( $"Utilized {Environment.ProcessorCount} CPU Cores" );
        Console.WriteLine( "------------------------------" );
        Console.WriteLine( $"Total Bet: {totalBet:C}" );
        Console.WriteLine( $"Total Won: {totalWinnings:C}" );
        Console.WriteLine( $"Total Profit/Loss: {totalWinnings - totalBet:C}" );
        Console.WriteLine( $"Winning Spins: {wins:N0}" );
        Console.WriteLine( $"Hit Frequency: {hitFrequency:F4}%" );
        Console.WriteLine( "------------------------------" );
        Console.WriteLine( $"Simulated Total RTP: {totalRtp:F4}%" );
        Console.WriteLine( "\n--- Winning Icon Combos Frequency ---" );
        foreach (KeyValuePair<string, long> combo in comboCounts.OrderByDescending( kvp => kvp.Value )) {
            double percent = wins > 0 ? (double)combo.Value / wins * 100 : 0;
            Console.WriteLine( $"Combo: {combo.Key,-15} Count: {combo.Value:N0} ({percent:F4}%)" );
        }

        // --- Create and Populate the TestInfo Object ---
        var testInfo = new TestInfo {
            TestName = "3x3 Slots RTP Simulation",
            TestDate = $"{DateTime.Now:G}",
            TotalSpins = NUMBER_OF_SPINS_TO_SIMULATE,
            TotalBet = totalBet,
            TotalWinnings = totalWinnings,
            TotalRtp = totalRtp,
            HitFrequency = hitFrequency,
            SimulationDurationSeconds = stopwatch.Elapsed.TotalSeconds,
            ComboCounts = comboCounts.OrderByDescending( kvp => kvp.Value )
                .Select( kvp => new ComboInfo {
                        Combo = kvp.Key,
                        Count = kvp.Value,
                        Percentage = wins > 0 ? ((double)kvp.Value / wins) * 100 : 0
                    }
                ).ToList()
        };

        // --- Serialize to JSON ---
        var fileName = "Slots3x3Log.json";
        const string directoryPath = "Data";
        string filePath = Path.Combine( directoryPath, fileName );

        // Ensure the directory exists
        Directory.CreateDirectory( directoryPath );

        // Serialize and write the file
        string json = JsonConvert.SerializeObject( testInfo, Formatting.Indented );
        File.WriteAllText( filePath, json );

        Console.WriteLine( $"\nTest results saved to: {filePath}" );

        // Assert that the final RTP is within a reasonable statistical margin of the target.
        Assert.IsTrue( totalRtp is > 95 and < 105, $"RTP of {totalRtp:F12}% is outside the expected range (95%-105%)." );
    }
}