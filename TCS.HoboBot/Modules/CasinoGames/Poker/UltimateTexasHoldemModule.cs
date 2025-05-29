using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using HoldemPoker.Cards;
using HoldemPoker.Evaluator;
using TCS.HoboBot.Data;
using TCS.HoboBot.Modules.CasinoGames.Utils;
namespace TCS.HoboBot.Modules.CasinoGames.Poker {
    public class UthGameState {
        public ulong PlayerId { get; }
        //public ulong GameId { get; set; }

        // Bets and outcomes
        public decimal AnteBet { get; set; }
        public decimal AntePayout { get; set; }

        public decimal PlayBet { get; set; }
        public decimal PlayPayout { get; set; }

        public decimal BlindBet { get; set; }
        public decimal BlindPayout { get; set; }

        public decimal TripsBet { get; set; }
        public decimal TripsPayout { get; set; }

        public decimal EntireBet => AnteBet + BlindBet + PlayBet;
        public decimal TotalWinnings => AntePayout + PlayPayout + BlindPayout + TripsPayout;
        public decimal NetProfit => TotalWinnings - EntireBet + TripsBet;

        public bool AnteWon { get; set; }
        public bool GameWon { get; set; }
        public bool BlindWon { get; set; }
        public bool TripsWon { get; set; }
        public bool Tie { get; set; }
        public bool Folded { get; set; }

        public Deck Deck { get; }
        public List<Card> CommunityCards { get; }

        // Player and dealer hands
        public List<Card> PlayerHand { get; }
        public int PlayerHandRanking => HoldemHandEvaluator
            .GetHandRanking( PlayerHand.Concat( CommunityCards ).ToArray() );
        public PokerHandCategory PlayerHandCategory => HoldemHandEvaluator
            .GetHandCategory( PlayerHand.Concat( CommunityCards ).ToArray() );
        public string PlayerHandDesc => HoldemHandEvaluator
            .GetHandDescription( HoldemHandEvaluator.GetHandRanking( PlayerHand.Concat( CommunityCards ).ToArray() ) );
        public List<Card> DealerHand { get; }
        public int DealerHandRanking => HoldemHandEvaluator
            .GetHandRanking( DealerHand.Concat( CommunityCards ).ToArray() );
        public PokerHandCategory DealerHandCategory => HoldemHandEvaluator
            .GetHandCategory( DealerHand.Concat( CommunityCards ).ToArray() );
        public string DealerHandDesc => HoldemHandEvaluator
            .GetHandDescription( HoldemHandEvaluator.GetHandRanking( DealerHand.Concat( CommunityCards ).ToArray() ) );
        public bool DealerQualified => DealerHandCategory <= PokerHandCategory.OnePair;
        public bool IsPlayerHigherRank => PlayerHandRanking < DealerHandRanking;

        public UthGameState(ulong playerId, int anteBet, int tripsBet) {
            PlayerId = playerId;

            AnteBet = anteBet;
            AnteWon = false;
            AntePayout = 0;

            BlindBet = anteBet;
            BlindWon = false;
            BlindPayout = 0;

            TripsBet = tripsBet;
            TripsWon = false;
            TripsPayout = 0;

            PlayBet = 0;
            GameWon = false;
            PlayPayout = 0;

            Tie = false;
            Folded = false;

            Deck = new Deck();

            PlayerHand = [Deck.Draw(), Deck.Draw()];
            DealerHand = [Deck.Draw(), Deck.Draw()];
            CommunityCards = [];
        }

        public static string GetHandString(IEnumerable<Card> hand) => string.Join( " ", hand.Select( c => $"`{c}`" ) );

        public string GetCurrentHandDescription() {
            Card[] allCards = PlayerHand.Concat( CommunityCards ).ToArray();

            if ( allCards.Length < 5 ) {
                if ( PlayerHand[0].Type == PlayerHand[1].Type ) {
                    return $"Pair of {PlayerHand[0].Type}s";
                }

                var highCard = PlayerHand.OrderByDescending( c => c.Type ).First();
                return $"High Card, {highCard.Type}";
            }

            int ranking = HoldemHandEvaluator.GetHandRanking( allCards );
            return HoldemHandEvaluator.GetHandDescription( ranking );
        }
    }

    [Group( "casino", "Casino games commands" )]
    public sealed class UltimateTexasHoldemModule : InteractionModuleBase<SocketInteractionContext> {
        const float MIN_MAIN_BET = 5f;
        const float MAX_MAIN_BET = 1000f;
        const float MAX_TRIPS_BET = 100f;

        const string CMD_PREFIX_UTH = "uth_game";
        const string CMD_PL_AY_AGAIN_UTH = "uth_play_again";
        const string CMD_END_GAME = "uth_end_game";
        const string BET_3_X = "bet3x";
        const string BET_4_X = "bet4x";
        const string BET_2_X = "bet2x";
        const string BET_1_X = "bet1x";
        const string CHECK_PREFLOP = "check_preflop";
        const string CHECK_FLOP = "check_flop";
        const string FOLD = "fold";

        static readonly ConcurrentDictionary<ulong, UthGameState> ActiveGames = new();

        static readonly Dictionary<PokerHandCategory, decimal> TripsPayouts = new() {
            { PokerHandCategory.RoyalFlush, 50m }, { PokerHandCategory.StraightFlush, 40m },
            { PokerHandCategory.FourOfAKind, 30m }, { PokerHandCategory.FullHouse, 8m },
            { PokerHandCategory.Flush, 7m }, { PokerHandCategory.Straight, 4m },
            { PokerHandCategory.ThreeOfAKind, 3m },
        };

        static readonly Dictionary<PokerHandCategory, decimal> BlindPayouts = new() {
            { PokerHandCategory.RoyalFlush, 500m }, { PokerHandCategory.StraightFlush, 50m },
            { PokerHandCategory.FourOfAKind, 10m }, { PokerHandCategory.FullHouse, 3m },
            { PokerHandCategory.Flush, 1.5m }, { PokerHandCategory.Straight, 1m },
        };

        static void CalculateShowdownResult(UthGameState game, out string result) {
            var resultBuilder = new StringBuilder();
            //var totalWinnings = 0m;

            // --- Evaluate the player’s 7-card hand ---------------------------------
            Card[] playerEvalCards = game.PlayerHand.Concat( game.CommunityCards ).ToArray();
            int playerRanking = HoldemHandEvaluator.GetHandRanking( playerEvalCards );
            var playerHandCategory = HoldemHandEvaluator.GetHandCategory( playerRanking );
            // string playerHandDesc = HoldemHandEvaluator.GetHandDescription( playerRanking );

            // FOLD PATH – player forfeits Ante & Blind, but Trips is still live --------
            if ( game.Folded ) {
                resultBuilder.AppendLine( "You folded. You lose your Ante and Blind bets." );
                //totalWinnings = -(game.AnteBet + game.BlindBet);

                if ( game.TripsBet > 0 ) {
                    TripsPayouts.TryGetValue( playerHandCategory, out decimal tripsMultiplier );

                    if ( tripsMultiplier > 0 ) {
                        decimal win = game.TripsBet * tripsMultiplier;
                        //totalWinnings += win;
                        game.TripsWon = true; // Trips bet wins
                        game.TripsPayout = win; // Store the payout for later use
                        resultBuilder.AppendLine( $"✅ **Trips Bet:** Wins **${win:N2}** ({tripsMultiplier}x)!" );
                    }
                    else {
                        //totalWinnings -= game.TripsBet;
                        game.TripsWon = false; // Trips bet does not win
                        game.TripsPayout = 0; // Trips bet does not pay out
                        resultBuilder.AppendLine( $"❌ **Trips Bet:** You lose ${game.TripsBet:N2}." );
                    }
                }

                result = resultBuilder.ToString();
                //return /*totalWinnings,*/ resultBuilder.ToString() /*, playerHandDesc*/;
                return;
            }
            // ---------------------------------------------------------------------------

            // At this point the player stayed in, so we need to evaluate the dealer
            Card[] dealerEvalCards = game.DealerHand.Concat( game.CommunityCards ).ToArray();
            int dealerRanking = HoldemHandEvaluator.GetHandRanking( dealerEvalCards );
            //var dealerHandCategory = HoldemHandEvaluator.GetHandCategory( dealerRanking );

            /*"📝 How to Play",
            "1. **Ante & Blind Bets:** Place equal bets on the Ante and Blind spots. You can also make an optional Trips bet.\n" +
            "2. **Hole Cards:** You and the dealer receive two cards face down.\n" +
            "3. **Pre-Flop Decision:** You can either:\n" +
            "   - **Check:** Make no additional bet.\n" +
            "   - **Bet 3x or 4x:** Bet 3 or 4 times your Ante on the Play spot. This is your only chance to bet this much.\n" +
            "4. **The Flop:** Three community cards are dealt face up.\n" +
            "   - If you checked pre-flop, you can now **Bet 2x** your Ante on the Play spot, or **Check** again.\n" +
            "   - If you already bet, you do nothing.\n" +
            "5. **The Turn & River:** The final two community cards are dealt face up.\n" +
            "   - If you checked twice, you must now either **Bet 1x** your Ante on the Play spot or **Fold** (losing your Ante and Blind bets).\n" +
            "   - If you already bet, you do nothing.\n" +
            "6. **Showdown:** If you haven't folded, you and the dealer reveal your hands. The best five-card hand wins."

            "💰 Dealer Qualification & Payouts",
            "The dealer needs at least a **Pair** to 'qualify'.\n" +
            "- **If Dealer Doesn't Qualify:** Your Ante bet is returned (push). All other bets (Play, Blind) are still in action against your hand.\n" +
            "- **If Dealer Qualifies:**\n" +
            "  - **Player Wins:** Ante and Play bets pay 1 to 1. Blind bet pays according to the payout table (see below) if your winning hand is a Straight or better; otherwise, it pushes.\n" +
            "  - **Dealer Wins:** You lose Ante, Blind, and Play bets.\n" +
            "  - **Tie:** Ante, Blind, and Play bets push."*/

            //bool dealerQualifies = dealerHandCategory <= PokerHandCategory.OnePair;

            if ( game.IsPlayerHigherRank ) // player wins
            {
                resultBuilder.AppendLine( "🎉 **You win the hand!**\n" );
                //totalWinnings += game.PlayBet * 2; // Play bet pays 1:1, so we double it
                game.GameWon = true; // Mark the game as won
                game.PlayPayout = game.PlayBet * 2; // Store the payout for later use

                if ( game.DealerQualified ) {
                    //totalWinnings += game.AnteBet * 2;
                    game.AnteWon = true; // Ante bet wins
                    game.AntePayout = game.AnteBet * 2; // Ante bet pays 1:1
                    resultBuilder.AppendLine( $"✅ Play (${game.PlayBet:N2}) and Ante (${game.AnteBet:N2}) bets win." );
                }
                else {
                    //totalWinnings += game.AnteBet; // Ante bet is returned on a push
                    game.AnteWon = false; // Ante bet does not win
                    game.AntePayout = 0; // Ante bet does not pay out
                    resultBuilder.AppendLine( $"✅ Play bet (${game.PlayBet:N2}) wins. Ante pushes (Dealer did not qualify)." );
                }

                BlindPayouts.TryGetValue( playerHandCategory, out decimal blindMultiplier );
                if ( blindMultiplier > 0 ) {
                    decimal win = game.BlindBet * blindMultiplier;
                    //totalWinnings += win;
                    game.BlindWon = true; // Blind bet wins
                    game.BlindPayout = win; // Store the payout for later use
                    resultBuilder.AppendLine( $"✅ **Blind Bet:** Wins **${win:N2}** ({blindMultiplier}x)!" );
                }
                else {
                    //totalWinnings += game.BlindBet; // Add the original Blind bet back on a push
                    game.BlindWon = false; // Blind bet does not win
                    game.BlindPayout = game.BlindBet; // Blind bet pushes
                    resultBuilder.AppendLine( "🅿️ **Blind Bet:** Pushes (hand not a Straight or better)." );
                }
            }
            else if ( playerRanking > dealerRanking ) // dealer wins
            {
                game.GameWon = false;
                decimal loss = game.AnteBet + game.BlindBet + game.PlayBet;
                resultBuilder.AppendLine( "😭 **Dealer wins the hand.**\n" );
                //totalWinnings -= loss;
                resultBuilder.AppendLine( $"❌ All bets lose ($-{loss:N2})." );
            }
            else // tie
            {
                game.Tie = true;
                resultBuilder.AppendLine( "⚖️ **It's a push!**" );
                resultBuilder.AppendLine( "🅿️ All bets push. Your wager is returned." );
            }

            // ----------------------- Trips bet (player stayed in) ----------------------
            if ( game.TripsBet > 0 ) {
                TripsPayouts.TryGetValue( playerHandCategory, out decimal tripsMultiplier );

                if ( tripsMultiplier > 0 ) {
                    decimal win = game.TripsBet * tripsMultiplier;
                    //totalWinnings += win;
                    game.TripsWon = true; // Trips bet wins
                    game.TripsPayout = win; // Store the payout for later use
                    resultBuilder.AppendLine( $"✅ **Trips Bet:** Wins **${win:N2}** ({tripsMultiplier}x)!\n" );
                }
                else {
                    //totalWinnings -= game.TripsBet;
                    game.TripsWon = false; // Trips bet does not win
                    game.TripsPayout = 0; // Trips bet does not pay out
                    resultBuilder.AppendLine( $"❌ **Trips Bet:** You lose ${game.TripsBet:N2}.\n" );
                }
            }
            // --------------------------------------------------------------------------

            // Build the final result string
            result = resultBuilder.ToString();
            return /*totalWinnings,*/ /*resultBuilder.ToString() */ /*, playerHandDesc*/;
        }


        EmbedBuilder BuildUthEmbed(UthGameState game, string title, string description, string footer, Color color) {
            var embed = new EmbedBuilder()
                .WithAuthor( Context.User.GlobalName, Context.User.GetAvatarUrl() )
                .WithTitle( title )
                .WithDescription( description )
                //.WithThumbnailUrl( "https://www.google.com/imgres?q=casino%20icon&imgurl=https%3A%2F%2Fstatic-00.iconduck.com%2Fassets.00%2Fcasino-icon-2048x2048-qpd16ckr.png&imgrefurl=https%3A%2F%2Ficonduck.com%2Ficons%2F161061%2Fcasino&docid=T7Eivj4VPcdv3M&tbnid=vKr95fq_aL7STM&vet=12ahUKEwiq5aPJvMWNAxXCMDQIHTSmDUMQM3oECBgQAA..i&w=2048&h=2048&hcb=2&ved=2ahUKEwiq5aPJvMWNAxXCMDQIHTSmDUMQM3oECBgQAA" ) // Replace with actual thumbnail URL
                // .WithImageUrl( "https://cdn.discordapp.com/attachments/1176938074622664764/1278438997009502353/file-3FE6ck7ZFGzpXK9J7RUvldbl.png?ex=68376699&is=68361519&hm=9819c70dcc3cb8baffe31e674f0a5d7fe701b2a1c7ebb1f16feda78b9b816f22&" ) // Replace with actual image URL
                // .WithUrl("https://example.com")
                .WithCurrentTimestamp()
                .WithColor( color )
                .WithFooter( footer, Context.User.GetAvatarUrl() );

            if ( game.PlayerHand.Count != 0 ) {
                embed.AddField( "Your Cards", UthGameState.GetHandString( game.PlayerHand ), true );
            }

            if ( game.DealerHand.Count != 0 && game.Folded ) {
                embed.AddField( "Dealer Cards", UthGameState.GetHandString( game.DealerHand ), true );
            }

            if ( game.CommunityCards.Count != 0 ) {
                embed.AddField( "Community Cards", UthGameState.GetHandString( game.CommunityCards ) );
            }

            return embed;
        }

        bool ValidateBet(float bet, float tripsBet, out string? error) {
            error = null;
            if ( bet < MIN_MAIN_BET ) {
                error = $"Ante bet must be at least ${MIN_MAIN_BET:C2}.";
                return false;
            }

            if ( bet > MAX_MAIN_BET ) {
                error = $"Ante bet cannot exceed ${MAX_MAIN_BET:C2}.";
                return false;
            }

            if ( tripsBet > MAX_TRIPS_BET ) {
                error = $"Trips bet cannot exceed ${MAX_TRIPS_BET:C2}.";
                return false;
            }

            float totalBet = bet + tripsBet;
            if ( PlayersWallet.GetBalance( Context.Guild.Id, Context.User.Id ) < totalBet ) {
                error = $"{Context.User.Mention} does’t have enough cash! Balance: " +
                        $"${PlayersWallet.GetBalance( Context.Guild.Id, Context.User.Id ):C2}. Bet: ${totalBet:C2}.";
                return false;
            }

            return true;
        }

        async Task PlayUthAsync(float bet, float tripsBet, bool isPlayAgain = false, SocketInteraction? interaction = null) {
            KeyValuePair<ulong, UthGameState> existingGame = ActiveGames.FirstOrDefault( kvp => kvp.Value.PlayerId == Context.User.Id );
            if ( existingGame.Key != 0 ) {
                ActiveGames.TryRemove( existingGame.Key, out _ );
            }

            PlayersWallet.SubtractFromBalance( Context.Guild.Id, Context.User.Id, bet + tripsBet );

            ulong gameId = (interaction ?? Context.Interaction).Id;
            var game = new UthGameState( Context.User.Id, (int)bet, (int)tripsBet );
            ActiveGames[gameId] = game;

            string currentHandDesc = game.GetCurrentHandDescription();
            string description = $"Your cards are dealt. Make your move." +
                                 $"\nYour current best hand: **{currentHandDesc}**";

            var embed = BuildUthEmbed(
                game,
                title:
                $"Ante: ${bet:N2}\n" +
                $"Blind: ${bet:N2}\n" +
                $"Trips: ${tripsBet:N2}\n" +
                $"Total: ${bet * 2 + tripsBet:N2}\n\n" +
                $"Ultimate Texas Hold'em - Pre-Flop ",
                description: description,
                footer: "You can bet 3x or 4x your Ante, or check to see the flop for free.",
                color: Color.Blue
            ).Build();

            var components = new ComponentBuilder()
                .WithButton( "Bet 4x", $"{CMD_PREFIX_UTH}:{BET_4_X}:{gameId}", ButtonStyle.Success )
                .WithButton( "Bet 3x", $"{CMD_PREFIX_UTH}:{BET_3_X}:{gameId}", ButtonStyle.Success )
                .WithButton( "Check", $"{CMD_PREFIX_UTH}:{CHECK_PREFLOP}:{gameId}", ButtonStyle.Secondary )
                .WithButton( "Fold", $"{CMD_PREFIX_UTH}:{FOLD}:{gameId}", ButtonStyle.Danger, row: 1 )
                .Build();

            if ( isPlayAgain && interaction is SocketMessageComponent component ) {
                await component.ModifyOriginalResponseAsync( p => {
                        p.Embed = embed;
                        p.Components = components;
                    }
                );
            }
            else {
                await FollowupAsync( embed: embed, components: components, ephemeral: true );
            }
        }

        [SlashCommand( "texasholdem", "Play a game of Ultimate Texas Hold'em." )]
        public async Task StartUthGameCommand(
            [Summary( description: "Your Ante bet amount." )] float bet,
            [Summary( description: "Your optional Trips bet." )] float tripsBet = 0
        ) {
            if ( !ValidateBet( bet, tripsBet, out string? failReason ) ) {
                await RespondAsync( failReason, ephemeral: true );
                return;
            }

            await DeferAsync( ephemeral: true );
            await PlayUthAsync( bet, tripsBet );
        }

        [ComponentInteraction( $"{CMD_PL_AY_AGAIN_UTH}:*:*", ignoreGroupNames: true )]
        public async Task OnPlayAgainAsync(string anteBetStr, string tripsBetStr) {
            await DeferAsync();

            if ( !float.TryParse( anteBetStr, NumberStyles.Any, CultureInfo.InvariantCulture, out float bet ) ||
                 !float.TryParse( tripsBetStr, NumberStyles.Any, CultureInfo.InvariantCulture, out float tripsBet ) ) {
                await ModifyOriginalResponseAsync( m => {
                        m.Content = "Invalid bet data in 'Play Again' button.";
                        m.Components = new ComponentBuilder().Build();
                    }
                );
                return;
            }

            if ( !ValidateBet( bet, tripsBet, out string? failReason ) ) {
                await ModifyOriginalResponseAsync( p => {
                        p.Content = failReason;
                        p.Embed = null;
                        p.Components = new ComponentBuilder().Build();
                    }
                );
                return;
            }

            await PlayUthAsync( bet, tripsBet, isPlayAgain: true, interaction: Context.Interaction );
        }

        [ComponentInteraction( $"{CMD_END_GAME}", ignoreGroupNames: true )]
        public async Task OnEndGameAsync() {
            await DeferAsync();
            await ModifyOriginalResponseAsync( m => {
                    m.Embed = new EmbedBuilder()
                        .WithTitle( "Ultimate Texas Hold'em – Game Over" )
                        .WithDescription( $"{Context.User.Mention} ended the game." )
                        .WithColor( Color.DarkGrey )
                        .Build();
                    m.Components = new ComponentBuilder().Build();
                }
            );
        }


        [ComponentInteraction( $"{CMD_PREFIX_UTH}:*:*", ignoreGroupNames: true )]
        public async Task OnGameAction(string action, string gameIdStr) {
            if ( !ulong.TryParse( gameIdStr, out ulong gameId ) || !ActiveGames.TryGetValue( gameId, out var game ) || game.PlayerId != Context.User.Id ) {
                await RespondAsync( "This isn't your game or it has expired.", ephemeral: true );
                return;
            }

            await DeferAsync();

            switch (action) {
                case BET_4_X:
                {
                    decimal betAmount = 4 * game.AnteBet;
                    if ( PlayersWallet.GetBalance( Context.Guild.Id, Context.User.Id ) < (float)betAmount ) {
                        await FollowupAsync( "Insufficient funds for 4x bet.", ephemeral: true );
                        return;
                    }

                    PlayersWallet.SubtractFromBalance( Context.Guild.Id, Context.User.Id, (float)betAmount );
                    game.PlayBet = betAmount;
                    await ProcessShowdown( game, gameId );
                    break;
                }
                case BET_3_X:
                {
                    decimal betAmount = 3 * game.AnteBet;
                    if ( PlayersWallet.GetBalance( Context.Guild.Id, Context.User.Id ) < (float)betAmount ) {
                        await FollowupAsync( "Insufficient funds for 3x bet.", ephemeral: true );
                        return;
                    }

                    PlayersWallet.SubtractFromBalance( Context.Guild.Id, Context.User.Id, (float)betAmount );
                    game.PlayBet = betAmount;
                    await ProcessShowdown( game, gameId );
                    break;
                }
                case BET_2_X:
                {
                    decimal betAmount = 2 * game.AnteBet;
                    if ( PlayersWallet.GetBalance( Context.Guild.Id, Context.User.Id ) < (float)betAmount ) {
                        await FollowupAsync( "Insufficient funds for 2x bet.", ephemeral: true );
                        return;
                    }

                    PlayersWallet.SubtractFromBalance( Context.Guild.Id, Context.User.Id, (float)betAmount );
                    game.PlayBet = betAmount;
                    await ProcessShowdown( game, gameId );
                    break;
                }
                case BET_1_X:
                {
                    decimal betAmount = 1 * game.AnteBet;
                    if ( PlayersWallet.GetBalance( Context.Guild.Id, Context.User.Id ) < (float)betAmount ) {
                        await FollowupAsync( "Insufficient funds for 1x bet.", ephemeral: true );
                        return;
                    }

                    PlayersWallet.SubtractFromBalance( Context.Guild.Id, Context.User.Id, (float)betAmount );
                    game.PlayBet = betAmount;
                    await ProcessShowdown( game, gameId );
                    break;
                }
                case CHECK_PREFLOP:
                    await ProcessFlop( game, gameId );
                    break;
                case CHECK_FLOP:
                    await ProcessTurnAndRiver( game, gameId );
                    break;
                case FOLD:
                    game.Folded = true;
                    await ProcessShowdown( game, gameId );
                    break;
            }
        }

        async Task ProcessFlop(UthGameState game, ulong gameId) {
            for (var i = 0; i < 3; i++) game.CommunityCards.Add( game.Deck.Draw() );

            string currentHandDesc = game.GetCurrentHandDescription();
            var description = $"Three community cards are revealed.\n\nYour current best hand: **{currentHandDesc}**";

            var embedBuilder = BuildUthEmbed(
                game,
                "Ultimate Texas Hold'em - The Flop",
                description,
                "You can bet 2x your Ante or check to see the final cards.",
                Color.Orange
            );
            var components = new ComponentBuilder()
                .WithButton( "Bet 2x", $"{CMD_PREFIX_UTH}:{BET_2_X}:{gameId}", ButtonStyle.Success )
                .WithButton( "Check", $"{CMD_PREFIX_UTH}:{CHECK_FLOP}:{gameId}", ButtonStyle.Secondary )
                .WithButton( "Fold", $"{CMD_PREFIX_UTH}:{FOLD}:{gameId}", ButtonStyle.Danger, row: 1 )
                .Build();

            await ModifyOriginalResponseAsync( p => {
                    p.Embed = embedBuilder.Build();
                    p.Components = components;
                }
            );
        }

        async Task ProcessTurnAndRiver(UthGameState game, ulong gameId) {
            game.CommunityCards.Add( game.Deck.Draw() );
            game.CommunityCards.Add( game.Deck.Draw() );

            string currentHandDesc = game.GetCurrentHandDescription();
            string description = $"All community cards are on the table.\n" +
                                 $"Your current best hand: **{currentHandDesc}**";

            var embedBuilder = BuildUthEmbed(
                game,
                "Ultimate Texas Hold'em - The River",
                description,
                "You must bet 1x your Ante to play, or fold your hand.",
                Color.DarkRed
            );

            var components = new ComponentBuilder()
                .WithButton( "Bet 1x", $"{CMD_PREFIX_UTH}:{BET_1_X}:{gameId}", ButtonStyle.Success )
                .WithButton( "Fold", $"{CMD_PREFIX_UTH}:{FOLD}:{gameId}", ButtonStyle.Danger )
                .Build();

            await ModifyOriginalResponseAsync( p => {
                    p.Embed = embedBuilder.Build();
                    p.Components = components;
                }
            );
        }

        async Task ProcessShowdown(UthGameState game, ulong gameId) {
            if ( game is { PlayBet: 0, Folded: false } ) {
                game.Folded = true;
            }

            while (game.CommunityCards.Count < 5) {
                game.CommunityCards.Add( game.Deck.Draw() );
            }

            CalculateShowdownResult( game, out string winDescription );

            var finalDescription = new StringBuilder();
            finalDescription.AppendLine( $"Dealer has: **{game.DealerHandDesc}**" );
            finalDescription.AppendLine( $"Your Hand: **{game.PlayerHandDesc}**" );
            finalDescription.AppendLine();
            finalDescription.AppendLine( winDescription );

            string footer = game.GameWon
                ? $"Congratulations! You won ${game.TotalWinnings:N2}."
                : game.Tie
                    ? "Push! You broke even on the hand."
                    : $"Unlucky. You lost ${-game.EntireBet:N2}.";

            var finalColor = game.GameWon
                ? Color.Green
                : game.Tie
                    ? Color.LightGrey
                    : Color.Red;

            var embedBuilder = BuildUthEmbed(
                game,
                "Ultimate Texas Hold'em - Showdown",
                finalDescription.ToString(),
                footer,
                finalColor
            );

            // Remove any existing 'Dealer Cards' field so it doesn't show twice.
            embedBuilder.Fields.RemoveAll( f => f.Name == "Dealer Cards" );
            embedBuilder.Fields.Insert(
                1, new EmbedFieldBuilder()
                    .WithName( "Dealer Cards" )
                    .WithValue( UthGameState.GetHandString( game.DealerHand ) )
                    .WithIsInline( true )
            );

            if ( game.Tie ) {
                PlayersWallet.AddToBalance( Context.Guild.Id, Context.User.Id, (float)game.EntireBet );
            }
            else if ( game.GameWon ) {
                PlayersWallet.AddToBalance( Context.Guild.Id, Context.User.Id, (float)game.TotalWinnings );
            }

            if ( game.TripsWon ) {
                PlayersWallet.AddToBalance( Context.Guild.Id, Context.User.Id, (float)game.TripsPayout );
            }

            var channelMessage = string.Empty;
            TripsPayouts.TryGetValue( game.PlayerHandCategory, out decimal tripsMultiplier );

            if ( game.NetProfit > game.EntireBet /*+ game.TripsPayout*/ * 4m ) {
                channelMessage = $"🎉**WINNER**🎉\n" +
                                 $" {Context.User.Mention} won **${game.NetProfit:N2}**! Playing - Texas Hold'em\n" +
                                 $"Winning hand: **{game.PlayerHandDesc}**";
                // await Context.Channel.SendMessageAsync(
                //     $"🎉**WINNER**🎉\n" +
                //     $" {Context.User.Mention} won **${game.NetProffit:N2}**! Playing - Texas Hold'em\n" +
                //     $"Winning hand: **{game.PlayerHandDesc}**"
                // );
            }

            if ( game.TripsPayout > game.TripsBet * 5m ) {
                channelMessage += $"\n{Context.User.Mention} played a Trips bet of **${game.TripsBet:N2}** " +
                                  $"and won **${game.TripsPayout:N2} ({tripsMultiplier})**!";
            }

            if ( !string.IsNullOrEmpty( channelMessage ) ) {
                await Context.Channel.SendMessageAsync( channelMessage );
            }

            string currentFooter = embedBuilder.Footer?.Text ?? "";
            currentFooter += $"\nYour total balance is ${PlayersWallet.GetBalance( Context.Guild.Id, Context.User.Id ):N2}";
            embedBuilder.Footer = new EmbedFooterBuilder()
                .WithText( currentFooter )
                .WithIconUrl( Context.User.GetAvatarUrl() );

            var components = new ComponentBuilder()
                .WithButton( "Play Again", $"{CMD_PL_AY_AGAIN_UTH}:{game.AnteBet}:{game.TripsBet}" )
                .WithButton( "End Game", $"{CMD_END_GAME}", ButtonStyle.Danger )
                .Build();

            ActiveGames.TryRemove( gameId, out _ );
            await ModifyOriginalResponseAsync( p => {
                    p.Embed = embedBuilder.Build();
                    p.Components = components;
                }
            );
        }
    }
}