using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using HoldemPoker.Cards;
using HoldemPoker.Evaluator;
using TCS.HoboBot.Modules.CasinoGames; // Your module's namespace
using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;
using TestContext = Microsoft.VisualStudio.TestTools.UnitTesting.TestContext;

namespace TCS.HoboBot.Tests {
    [TestClass]
    public class UltimateTexasHoldemModuleTests {
        // --- Simulation Configuration ---
        private const int NumberOfHandsToSimulate = 2_000_000; // High number for accuracy
        private const int AnteBetAmount = 10; // A fixed bet amount for the simulation
        private const int TripsBetAmount = 5; // A fixed amount for the Trips side bet

        private static MethodInfo _calculateShowdownMethod;

        /// <summary>
        /// Caches the private CalculateShowdownResult method using reflection before tests run.
        /// </summary>
        [ClassInitialize]
        public static void ClassInitialize(TestContext context) {
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
            _calculateShowdownMethod = typeof(UltimateTexasHoldemModule).GetMethod( "CalculateShowdownResult", flags );

            if ( _calculateShowdownMethod == null ) {
                throw new InvalidOperationException( "Could not find the 'CalculateShowdownResult' method via reflection." );
            }
        }

        /// <summary>
        /// A bot player with a fixed, simple strategy for making decisions.
        /// </summary>
        private static class PlayerStrategy {
            public enum Action { Bet4x, Bet3x, Bet2x, Bet1x, Check, Fold }

            // Pre-flop: Decide whether to bet 4x, 3x, or check.
            public static Action DecidePreFlopAction(IReadOnlyList<Card> playerHand) {
                var c1 = playerHand[0];
                var c2 = playerHand[1];

                bool isSuited = c1.Color == c2.Color;
                bool isPair = c1.Type == c2.Type;
                var highCard = (CardType)Math.Max( (int)c1.Type, (int)c2.Type );
                var lowCard = (CardType)Math.Min( (int)c1.Type, (int)c2.Type );

                // Bet 4x on premium hands
                if ( isPair || highCard == CardType.Ace ||
                     (highCard == CardType.King && (isSuited || lowCard >= CardType.Five)) ||
                     (highCard == CardType.Queen && (isSuited || lowCard >= CardType.Eight)) ||
                     (highCard == CardType.Jack && (isSuited || lowCard >= CardType.Ten)) ) {
                    return Action.Bet4x;
                }

                return Action.Check;
            }

            // Flop: After seeing 3 community cards, decide whether to bet 2x or check.
            public static Action DecideFlopAction(IReadOnlyList<Card> playerHand, IReadOnlyList<Card> communityCards) {
                var allCards = playerHand.Concat( communityCards ).ToArray();
                var handCategory = HoldemHandEvaluator.GetHandCategory( HoldemHandEvaluator.GetHandRanking( allCards ) );

                // Bet 2x with any decent made hand (Two Pair or better)
                if ( handCategory <= PokerHandCategory.TwoPairs ) {
                    return Action.Bet2x;
                }

                return Action.Check;
            }

            // River: After all 5 community cards, decide whether to bet 1x or fold.
            public static Action DecideRiverAction(IReadOnlyList<Card> playerHand, IReadOnlyList<Card> communityCards, IReadOnlyList<Card> dealerHand) {
                var playerEvalCards = playerHand.Concat( communityCards ).ToArray();
                var dealerEvalCards = dealerHand.Concat( communityCards ).ToArray();

                int playerRanking = HoldemHandEvaluator.GetHandRanking( playerEvalCards );
                int dealerRanking = HoldemHandEvaluator.GetHandRanking( dealerEvalCards );

                // Bet 1x if our hand beats the dealer's hand, otherwise fold.
                if ( playerRanking < dealerRanking ) // Lower ranking value is better
                {
                    return Action.Bet1x;
                }

                return Action.Fold;
            }
        }

        [TestMethod]
        public void SimulateGames_And_CalculateRtp() {
            var stopwatch = Stopwatch.StartNew();

            long totalMainWagered = 0;
            long totalTripsWagered = 0;
            decimal totalMainNetWin = 0m;
            decimal totalTripsNetWin = 0m;

            var threadLock = new object();

            // Run the simulation in parallel to speed it up significantly
            Parallel.For(
                0, NumberOfHandsToSimulate, () => (0L, 0L, 0m, 0m), (i, loop, state) => {
                    var (localMainWagered, localTripsWagered, localMainNet, localTripsNet) = state;

                    // --- Simulate one full hand of UTH ---
                    var game = new UthGameState( 0, AnteBetAmount, TripsBetAmount );
                    localMainWagered += game.AnteBet + game.BlindBet;
                    localTripsWagered += game.TripsBet;

                    // 1. Pre-Flop decision
                    var preFlopAction = PlayerStrategy.DecidePreFlopAction( game.PlayerHand );
                    if ( preFlopAction == PlayerStrategy.Action.Bet4x ) game.PlayBet = 4 * game.AnteBet;
                    else if ( preFlopAction == PlayerStrategy.Action.Bet3x ) game.PlayBet = 3 * game.AnteBet;

                    if ( preFlopAction == PlayerStrategy.Action.Check ) {
                        // 2. Flop decision
                        for (int j = 0; j < 3; j++) game.CommunityCards.Add( game.Deck.Draw() );
                        var flopAction = PlayerStrategy.DecideFlopAction( game.PlayerHand, game.CommunityCards );
                        if ( flopAction == PlayerStrategy.Action.Bet2x ) game.PlayBet = 2 * game.AnteBet;

                        if ( flopAction == PlayerStrategy.Action.Check ) {
                            // 3. River decision
                            game.CommunityCards.Add( game.Deck.Draw() );
                            game.CommunityCards.Add( game.Deck.Draw() );
                            var riverAction = PlayerStrategy.DecideRiverAction( game.PlayerHand, game.CommunityCards, game.DealerHand );
                            if ( riverAction == PlayerStrategy.Action.Bet1x ) game.PlayBet = 1 * game.AnteBet;
                            else game.Folded = true;
                        }
                    }

                    // Finalize game state for showdown
                    localMainWagered += game.PlayBet;
                    while (game.CommunityCards.Count < 5) game.CommunityCards.Add( game.Deck.Draw() );

                    // --- Calculate Results using the game's actual private method ---
                    var result = ((decimal winnings, string description, string playerHandDesc))_calculateShowdownMethod.Invoke( null, new object[] { game } );

                    // Separate the winnings/losses for the main game and the Trips bet
                    var tripsResult = result.winnings;
                    var mainResult = result.winnings;

                    // This logic mirrors the payout calculation to isolate the trips bet result
                    if ( game.TripsBet > 0 ) {
                        var playerHandCategory = HoldemHandEvaluator.GetHandCategory( HoldemHandEvaluator.GetHandRanking( game.PlayerHand.Concat( game.CommunityCards ).ToArray() ) );
                        TripsPayouts.TryGetValue( playerHandCategory, out var tripsMultiplier );
                        if ( tripsMultiplier > 0 ) {
                            tripsResult = (game.TripsBet * tripsMultiplier) - game.TripsBet;
                            mainResult -= (game.TripsBet * tripsMultiplier);
                        }
                        else {
                            tripsResult = -game.TripsBet;
                            mainResult -= -game.TripsBet;
                        }
                    }

                    localMainNet += mainResult;
                    localTripsNet += tripsResult;

                    return (localMainWagered, localTripsWagered, localMainNet, localTripsNet);
                },
                state => {
                    // Atomically add the results from each thread to the final totals
                    lock (threadLock) {
                        totalMainWagered += state.Item1;
                        totalTripsWagered += state.Item2;
                        totalMainNetWin += state.Item3;
                        totalTripsNetWin += state.Item4;
                    }
                }
            );

            stopwatch.Stop();

            // --- Analyze and Print Results ---
            decimal mainGameRtp = ((decimal)totalMainWagered + totalMainNetWin) / totalMainWagered;
            decimal tripsBetRtp = ((decimal)totalTripsWagered + totalTripsNetWin) / totalTripsWagered;

            Console.WriteLine( "--- Ultimate Texas Hold'em RTP Simulation Results ---" );
            Console.WriteLine( $"Total Hands Simulated: {NumberOfHandsToSimulate:N0}" );
            Console.WriteLine( $"Simulation Duration: {stopwatch.Elapsed.TotalSeconds:F2} seconds" );
            Console.WriteLine( "-----------------------------------------------------" );
            Console.WriteLine( "Strategy Used: Simple threshold-based betting at each stage." );
            Console.WriteLine( "-----------------------------------------------------" );
            Console.WriteLine( $"Main Game (Ante/Blind/Play) RTP: {mainGameRtp:P4}" );
            Console.WriteLine( $"Trips Bet RTP:                   {tripsBetRtp:P4}" );
            Console.WriteLine( "-----------------------------------------------------" );

            // For UTH, an RTP of ~97-99% for the main game is expected with good strategy.
            // The Trips bet RTP is typically much lower.
            Assert.IsTrue( mainGameRtp > 0.95m, "Main game RTP is unexpectedly low." );
            Assert.IsTrue( tripsBetRtp < 0.98m, "Trips bet RTP is unexpectedly high." );
        }

        // Copied from game module for test isolation
        static readonly IReadOnlyDictionary<PokerHandCategory, decimal> TripsPayouts = new Dictionary<PokerHandCategory, decimal> {
            { PokerHandCategory.RoyalFlush, 50m }, { PokerHandCategory.StraightFlush, 40m },
            { PokerHandCategory.FourOfAKind, 30m }, { PokerHandCategory.FullHouse, 8m },
            { PokerHandCategory.Flush, 7m }, { PokerHandCategory.Straight, 4m },
            { PokerHandCategory.ThreeOfAKind, 3m }
        };
    }
}