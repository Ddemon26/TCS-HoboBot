using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using TCS.HoboBot.Modules.CasinoGames;
using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

namespace Tests;

[TestClass] public class ClassicSlotMachineRtpTest {
    const long NUMBER_OF_SPINS = 10_000_000;
    const float BET_AMOUNT = 1.0f;
    static readonly object Lock = new();

    [TestMethod] public void CalculateTotalRtp() {
        var totalWinnings = 0m;
        long winCount = 0;
        ConcurrentDictionary<string, long> comboCounts = new();
        var stopwatch = Stopwatch.StartNew();

        Parallel.For(
            0L,
            NUMBER_OF_SPINS,
            () => new ClassicSlotMachineModule(),
            (i, state, module) => {
                ClassicSlotIcon[][] grid = module.SpinReelsInternal();
                (decimal multiplier, string description) = module.CalculatePayoutInternal( grid, BET_AMOUNT );

                if ( multiplier > 0m ) {
                    decimal spinWin = (decimal)BET_AMOUNT * multiplier;
                    Interlocked.Increment( ref winCount );
                    comboCounts.AddOrUpdate( description, 1, (_, c) => c + 1 );

                    lock (Lock) {
                        totalWinnings += spinWin;
                    }
                }

                return module;
            },
            _ => { }
        );

        stopwatch.Stop();

        var totalBet = (decimal)(BET_AMOUNT * NUMBER_OF_SPINS);
        decimal rtp = totalWinnings / totalBet * 100m;
        decimal hitFreq = (decimal)winCount / NUMBER_OF_SPINS * 100m;

        Console.WriteLine( "\n--- Classic Slots RTP Simulation Results ---" );
        Console.WriteLine( $"Duration: {stopwatch.Elapsed.TotalSeconds:F2}s" );
        Console.WriteLine( $"Spins: {NUMBER_OF_SPINS:N0}" );
        Console.WriteLine( $"Total Bet: {totalBet:C}" );
        Console.WriteLine( $"Total Won: {totalWinnings:C}" );
        Console.WriteLine( $"Profit/Loss: {totalWinnings - totalBet:C}" );
        Console.WriteLine( $"Winning Spins: {winCount:N0}" );
        Console.WriteLine( $"Hit Frequency: {hitFreq:F4}%" );
        Console.WriteLine( $"Simulated RTP: {rtp:F4}%" );
        Console.WriteLine( "\n--- Win Descriptions Frequency ---" );
        foreach (KeyValuePair<string, long> kvp in comboCounts.OrderByDescending( k => k.Value )) {
            double pct = winCount > 0 ? (double)kvp.Value / winCount * 100 : 0;
            Console.WriteLine( $"\"{kvp.Key}\": {kvp.Value:N0} ({pct:F4}%)" );
        }
        
        // --- Create and Populate the TestInfo Object ---
        var testInfo = new TestInfo {
            TestName = "Classic Slots RTP Simulation",
            TestDate = $"{DateTime.Now:G}",
            TotalSpins = NUMBER_OF_SPINS,
            TotalBet = totalBet,
            TotalWinnings = totalWinnings,
            TotalRtp = rtp,
            HitFrequency = hitFreq,
            SimulationDurationSeconds = stopwatch.Elapsed.TotalSeconds,
            ComboCounts = comboCounts.OrderByDescending( kvp => kvp.Value )
                .Select( kvp => new ComboInfo {
                        Combo = kvp.Key,
                        Count = kvp.Value,
                        Percentage = winCount > 0 ? ((double)kvp.Value / winCount) * 100 : 0
                    }
                ).ToList(),
        };

        // --- Serialize to JSON and Write to File ---
        string fileName = "SlotsClassic.json";
        string directoryPath = "Data";
        string filePath = Path.Combine(directoryPath, fileName);

        // Ensure the Data directory exists
        Directory.CreateDirectory(directoryPath);

        // Serialize object to JSON and write it to the file
        string json = JsonConvert.SerializeObject(testInfo, Formatting.Indented);
        File.WriteAllText(filePath, json);

        Console.WriteLine($"\nTest results have been saved to: {filePath}");

        // Expect RTP close to 100%
        Assert.IsTrue( rtp > 95m && rtp < 105m, $"RTP of {rtp:F4}% outside expected range (95%-105%)." );
    }
}