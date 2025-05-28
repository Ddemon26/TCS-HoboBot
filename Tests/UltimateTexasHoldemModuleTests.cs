using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using HoldemPoker.Cards;
using HoldemPoker.Evaluator;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TCS.HoboBot.Modules.CasinoGames;
using TestContext = Microsoft.VisualStudio.TestTools.UnitTesting.TestContext;

namespace Tests {
    /// <summary>
    /// Test class to simulate Ultimate Texas Hold'em gameplay and calculate RTP using MSTest.
    /// </summary>
    [TestClass]
    public class UltimateTexasHoldemTest {
        const int SIMULATION_COUNT = 10_000_000;
        const int ANTE_BET = 10;
        const int TRIPS_BET = 5;
        const ulong PLAYER_ID = 12345;

        // Global accumulators (only written under lock in the final step)
        decimal m_totalWagered;
        decimal m_totalNet;
        decimal m_totalTripsWagered;
        decimal m_totalTripsNet;

        readonly ConcurrentDictionary<PokerHandCategory, long> m_playerHandDistribution = new();
        long m_dealerQualifyCount;
        long m_playerWinsCount;
        long m_dealerWinsCount;
        long m_pushCount;
        long m_foldCount;

        readonly object m_lock = new();

        public required TestContext TestContext { get; set; }

        [TestMethod]
        public void RunRtpSimulation_Parallel() {
            TestContext.WriteLine( "Starting Ultimate Texas Hold'em Simulation (parallel)..." );
            TestContext.WriteLine( $"Running {SIMULATION_COUNT:N0} hands on {Environment.ProcessorCount} cores." );

            var sw = Stopwatch.StartNew();

            // Parallel.For with thread-local state:
            Parallel.For(
                fromInclusive: 0,
                toExclusive: SIMULATION_COUNT,
                localInit: () => new ThreadLocalStats(),
                body: (_, _, local) => {
                    RunOneHand( local );
                    return local;
                },
                localFinally: local => {
                    // merge each thread’s stats into globals under a lock
                    lock (m_lock) {
                        m_totalWagered += local.TotalWagered;
                        m_totalNet += local.TotalNet;
                        m_totalTripsWagered += local.TotalTripsWagered;
                        m_totalTripsNet += local.TotalTripsNet;
                        m_dealerQualifyCount += local.DealerQualifyCount;
                        m_playerWinsCount += local.PlayerWinsCount;
                        m_dealerWinsCount += local.DealerWinsCount;
                        m_pushCount += local.PushCount;
                        m_foldCount += local.FoldCount;

                        foreach (var kv in local.HandDistribution)
                            m_playerHandDistribution.AddOrUpdate(
                                kv.Key, kv.Value, (_, old) => old + kv.Value
                            );
                    }
                }
            );

            sw.Stop();
            PrintResults( sw.Elapsed );
        }

        void RunOneHand(ThreadLocalStats stats) {
            // Exactly the same logic you already have in PlaySingleHand(),
            // but writing into the ThreadLocalStats instance rather than the globals.
            var game = new UthGameState( PLAYER_ID, ANTE_BET, TRIPS_BET );

            // Track wagers
            stats.TotalWagered += game.AnteBet + game.BlindBet;
            if ( game.TripsBet > 0 ) stats.TotalTripsWagered += game.TripsBet;

            // Decide bets (4X, 2X, 1X, or fold)
            if ( ShouldBet4X( game.PlayerHand ) ) {
                game.PlayBet = 4 * game.AnteBet;
                stats.TotalWagered += game.PlayBet;
            }
            else {
                // reveal flop
                for (int j = 0; j < 3; j++) game.CommunityCards.Add( game.Deck.Draw() );
                if ( ShouldBet2X( game.PlayerHand, game.CommunityCards ) ) {
                    game.PlayBet = 2 * game.AnteBet;
                    stats.TotalWagered += game.PlayBet;
                }
                else {
                    // reveal turn & river
                    game.CommunityCards.Add( game.Deck.Draw() );
                    game.CommunityCards.Add( game.Deck.Draw() );
                    if ( ShouldBet1X( game.PlayerHand, game.CommunityCards ) ) {
                        game.PlayBet = 1 * game.AnteBet;
                        stats.TotalWagered += game.PlayBet;
                    }
                    else {
                        game.Folded = true;
                        stats.FoldCount++;
                    }
                }
            }

            // finish board
            while (game.CommunityCards.Count < 5)
                game.CommunityCards.Add( game.Deck.Draw() );

            var (netHandOutcome, _, _) = TestableCalculateShowdownResult( game );

            stats.TotalNet += netHandOutcome;

            // hand distribution
            Card[] playerCards = game.PlayerHand.Concat( game.CommunityCards ).ToArray();
            int rank = HoldemHandEvaluator.GetHandRanking( playerCards );
            var cat = HoldemHandEvaluator.GetHandCategory( rank );
            stats.HandDistribution.AddOrUpdate( cat, 1, (_, old) => old + 1 );

            // showdown metrics
            if ( !game.Folded ) {
                // dealer qualification
                Card[] dealerCards = game.DealerHand.Concat( game.CommunityCards ).ToArray();
                int dealerRank = HoldemHandEvaluator.GetHandRanking( dealerCards );
                var dealerCat = HoldemHandEvaluator.GetHandCategory( dealerRank );
                if ( dealerCat >= PokerHandCategory.OnePair ) stats.DealerQualifyCount++;

                if ( rank < dealerRank ) {
                    stats.PlayerWinsCount++;
                }
                else if ( rank > dealerRank ) {
                    stats.DealerWinsCount++;
                }
                else {
                    stats.PushCount++;
                }
            }

            // trips bet outcome
            if ( game.TripsBet > 0 ) {
                stats.TotalTripsNet += CalculateTripsPayout( cat, game.TripsBet );
            }
        }

        decimal CalculateTripsPayout(PokerHandCategory cat, int bet) {
            var payouts = new Dictionary<PokerHandCategory, decimal> {
                { PokerHandCategory.RoyalFlush, 50m }, { PokerHandCategory.StraightFlush, 40m },
                { PokerHandCategory.FourOfAKind, 30m }, { PokerHandCategory.FullHouse, 8m },
                { PokerHandCategory.Flush, 7m }, { PokerHandCategory.Straight, 4m },
                { PokerHandCategory.ThreeOfAKind, 3m }
            };
            if ( payouts.TryGetValue( cat, out var mul ) ) return bet * mul;
            return -bet;
        }

        #region Player Strategy
        bool ShouldBet4X(List<Card> hand) {
            var c1 = hand[0];
            var c2 = hand[1];
            if ( c1.Type == c2.Type ) return true;
            if ( c1.Type == CardType.Ace || c2.Type == CardType.Ace ) return true;
            if ( c1.Color == c2.Color && (c1.Type >= CardType.Queen || c2.Type >= CardType.Queen) ) return true;
            return false;
        }

        bool ShouldBet2X(List<Card> playerHand, List<Card> community) {
            Card[] allCards = playerHand.Concat( community ).ToArray();
            var handCategory = HoldemHandEvaluator.GetHandCategory( HoldemHandEvaluator.GetHandRanking( allCards ) );
            return handCategory >= PokerHandCategory.TwoPairs;
        }

        bool ShouldBet1X(List<Card> playerHand, List<Card> community) {
            Card[] allCards = playerHand.Concat( community ).ToArray();
            var handCategory = HoldemHandEvaluator.GetHandCategory( HoldemHandEvaluator.GetHandRanking( allCards ) );
            return handCategory >= PokerHandCategory.OnePair;
        }
        #endregion

        (decimal winnings, string description, string playerHandDesc) TestableCalculateShowdownResult(UthGameState game) {
            var totalWinnings = 0m;
            Card[] playerEvalCards = game.PlayerHand.Concat( game.CommunityCards ).ToArray();
            int playerRanking = HoldemHandEvaluator.GetHandRanking( playerEvalCards );
            var playerHandCategory = HoldemHandEvaluator.GetHandCategory( playerRanking );
            string playerHandDesc = HoldemHandEvaluator.GetHandDescription( playerRanking );

            Dictionary<PokerHandCategory, decimal> tripsPayouts = new() {
                { PokerHandCategory.RoyalFlush, 50m }, { PokerHandCategory.StraightFlush, 40m },
                { PokerHandCategory.FourOfAKind, 30m }, { PokerHandCategory.FullHouse, 8m },
                { PokerHandCategory.Flush, 7m }, { PokerHandCategory.Straight, 4m },
                { PokerHandCategory.ThreeOfAKind, 3m },
            };
            Dictionary<PokerHandCategory, decimal> blindPayouts = new() {
                { PokerHandCategory.RoyalFlush, 500m }, { PokerHandCategory.StraightFlush, 50m },
                { PokerHandCategory.FourOfAKind, 10m }, { PokerHandCategory.FullHouse, 3m },
                { PokerHandCategory.Flush, 1.5m }, { PokerHandCategory.Straight, 1m },
            };

            if ( game.Folded ) {
                totalWinnings = -(game.AnteBet + game.BlindBet);
                if ( game.TripsBet > 0 ) {
                    tripsPayouts.TryGetValue( playerHandCategory, out decimal tripsMultiplier );
                    if ( tripsMultiplier > 0 ) totalWinnings += game.TripsBet * tripsMultiplier;
                    else totalWinnings -= game.TripsBet;
                }

                return (totalWinnings, "Folded", playerHandDesc);
            }

            Card[] dealerEvalCards = game.DealerHand.Concat( game.CommunityCards ).ToArray();
            int dealerRanking = HoldemHandEvaluator.GetHandRanking( dealerEvalCards );
            var dealerHandCategory = HoldemHandEvaluator.GetHandCategory( dealerRanking );
            bool dealerQualifies = dealerHandCategory >= PokerHandCategory.OnePair;

            if ( dealerQualifies ) m_dealerQualifyCount++;

            if ( playerRanking < dealerRanking ) {
                m_playerWinsCount++;
                totalWinnings += game.PlayBet;
                if ( dealerQualifies ) totalWinnings += game.AnteBet;

                blindPayouts.TryGetValue( playerHandCategory, out decimal blindMultiplier );
                if ( blindMultiplier > 0 ) totalWinnings += game.BlindBet * blindMultiplier;
            }
            else if ( playerRanking > dealerRanking ) {
                m_dealerWinsCount++;
                totalWinnings -= (game.AnteBet + game.BlindBet + game.PlayBet);
            }
            else {
                m_pushCount++;
            }

            if ( game.TripsBet > 0 ) {
                tripsPayouts.TryGetValue( playerHandCategory, out decimal tripsMultiplier );
                if ( tripsMultiplier > 0 ) totalWinnings += (game.TripsBet * tripsMultiplier);
                else totalWinnings -= game.TripsBet;
            }

            return (totalWinnings, "Showdown", playerHandDesc);
        }

        /// <summary>
        /// Holder for all per‐thread counters and sums.
        /// </summary>
        class ThreadLocalStats {
            public decimal TotalWagered;
            public decimal TotalNet;
            public decimal TotalTripsWagered;
            public decimal TotalTripsNet;
            public long DealerQualifyCount;
            public long PlayerWinsCount;
            public long DealerWinsCount;
            public long PushCount;
            public long FoldCount;
            public readonly ConcurrentDictionary<PokerHandCategory, long> HandDistribution = new();
        }

        void PrintResults(TimeSpan duration) {
            var sb = new StringBuilder();
            sb.AppendLine( "\n--- Simulation Complete ---" );
            sb.AppendLine( $"Execution Time: {duration.TotalSeconds:F2} seconds" );
            sb.AppendLine( $"Total Hands Played: {SIMULATION_COUNT:N0}" );
            sb.AppendLine();

            // --- *** RTP CALCULATION FIX *** ---
            sb.AppendLine( "--- Overall Game RTP ---" );
            // 1. Calculate the total gross amount returned to the player.
            decimal totalReturned = m_totalWagered + m_totalNet;
            // 2. Calculate RTP using the correct "total returned" value.
            decimal overallRtp = (totalReturned / m_totalWagered) * 100;

            sb.AppendLine( $"Total Wagered: {m_totalWagered:C2}" );
            sb.AppendLine( $"Total Returned: {totalReturned:C2}" );
            sb.AppendLine( $"Net Profit/Loss: {m_totalNet:C2}" );
            sb.AppendLine( $"Overall Game RTP: {overallRtp:F4}%" );
            sb.AppendLine();

            sb.AppendLine( "--- Trips Bet RTP ---" );
            if ( m_totalTripsWagered > 0 ) {
                // This logic was already correct, as it used the net result appropriately.
                decimal tripsReturned = m_totalTripsWagered + m_totalTripsNet;
                decimal tripsRtp = (tripsReturned / m_totalTripsWagered) * 100;
                sb.AppendLine( $"Total Trips Wagered: {m_totalTripsWagered:C2}" );
                sb.AppendLine( $"Net Trips P/L: {m_totalTripsNet:C2}" );
                sb.AppendLine( $"Trips Bet RTP: {tripsRtp:F4}%" );
            }
            else {
                sb.AppendLine( "Trips bet was not placed." );
            }

            sb.AppendLine();

            sb.AppendLine( "--- Game Statistics ---" );
            sb.AppendLine( $"Player Win Rate: {(double)m_playerWinsCount / SIMULATION_COUNT:P2}" );
            sb.AppendLine( $"Dealer Win Rate: {(double)m_dealerWinsCount / SIMULATION_COUNT:P2}" );
            sb.AppendLine( $"Push Rate: {(double)m_pushCount / SIMULATION_COUNT:P2}" );
            sb.AppendLine( $"Fold Rate: {(double)m_foldCount / SIMULATION_COUNT:P2}" );
            sb.AppendLine( $"Dealer Qualification Rate: {(double)m_dealerQualifyCount / SIMULATION_COUNT:P2}" );
            sb.AppendLine();

            sb.AppendLine( "--- Player Hand Distribution ---" );
            IOrderedEnumerable<KeyValuePair<PokerHandCategory, long>> orderedHands = m_playerHandDistribution.OrderByDescending( kvp => kvp.Key );
            foreach (KeyValuePair<PokerHandCategory, long> entry in orderedHands) {
                sb.AppendLine( $"{entry.Key,-15}: {entry.Value,12:N0} ({(double)entry.Value / SIMULATION_COUNT:P4})" );
            }

            TestContext.WriteLine( sb.ToString() );
        }
    }
}