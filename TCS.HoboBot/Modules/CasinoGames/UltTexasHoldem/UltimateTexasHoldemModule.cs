using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using HoldemPoker.Cards;
using HoldemPoker.Evaluator;
using TCS.HoboBot.Data;

namespace TCS.HoboBot.Modules.CasinoGames {
    public class UthGameState {
        public ulong PlayerId { get; }
        public List<Card> Deck { get; }
        public List<Card> PlayerHand { get; }
        public List<Card> DealerHand { get; }
        public List<Card> CommunityCards { get; }
        public int AnteBet { get; set; }
        public int BlindBet { get; set; }
        public int PlayBet { get; set; }
        public int TripsBet { get; set; }
        public bool Folded { get; set; }

        public UthGameState(ulong playerId, int anteBet, int tripsBet) {
            PlayerId = playerId;
            AnteBet = anteBet;
            BlindBet = anteBet;
            TripsBet = tripsBet;
            Folded = false;

            Deck = DeckHelper.CreateDeck();
            Deck.Shuffle();

            PlayerHand = [Deck.Draw(), Deck.Draw()];
            DealerHand = [Deck.Draw(), Deck.Draw()];
            CommunityCards = [];
        }

        public string GetHandString(IEnumerable<Card> hand) => string.Join( " ", hand.Select( c => $"`{c}`" ) );
    }

    public static class DeckHelper {
        static readonly Random Rng = new();

        public static List<Card> CreateDeck() =>
            Enum.GetValues( typeof(CardType) )
                .Cast<CardType>()
                .SelectMany( type => Enum.GetValues( typeof(CardColor) )
                                 .Cast<CardColor>()
                                 .Select( color => new Card( type, color ) )
                )
                .ToList();

        public static void Shuffle(this IList<Card> list) {
            int n = list.Count;
            while (n > 1) {
                n--;
                int k = Rng.Next( n + 1 );
                (list[k], list[n]) = (list[n], list[k]);
            }
        }

        public static Card Draw(this IList<Card> list) {
            var card = list[0];
            list.RemoveAt( 0 );
            return card;
        }
    }


    [Group( "casino", "Casino games commands" )]
    public sealed class UltimateTexasHoldemModule : InteractionModuleBase<SocketInteractionContext> {
        const float MIN_BET_UTH = 5f;
        const float MAX_BET_UTH = 1000f;
        
        const string CMD_PREFIX_UTH = "uth_game";
        const string BET_3X = "bet3x";
        const string BET_4X = "bet4x";
        const string BET_2X = "bet2x";
        const string BET_1X = "bet1x";
        const string CHECK_PREFLOP = "check_preflop";
        const string CHECK_FLOP = "check_flop";
        const string FOLD = "fold";
        const string PLAY_AGAIN = "play_again";
        const string END_GAME = "end_game";
        
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

        static string GetCurrentHandDescription(UthGameState game) {
            Card[] allCards = game.PlayerHand.Concat( game.CommunityCards ).ToArray();

            if ( allCards.Length < 5 ) {
                if ( game.PlayerHand[0].Type == game.PlayerHand[1].Type ) {
                    return $"Pair of {game.PlayerHand[0].Type}s";
                }

                var highCard = game.PlayerHand.OrderByDescending( c => c.Type ).First();
                return $"High Card, {highCard.Type}";
            }

            int ranking = HoldemHandEvaluator.GetHandRanking( allCards );
            return HoldemHandEvaluator.GetHandDescription( ranking );
        }
        
        static (decimal winnings, string description, string playerHandDesc) CalculateShowdownResult(UthGameState game) {
            var resultBuilder = new StringBuilder();
            var totalWinnings = 0m;

            // --- Evaluate the player’s 7-card hand once ---------------------------------
            Card[] playerEvalCards = game.PlayerHand.Concat( game.CommunityCards ).ToArray();
            int playerRanking = HoldemHandEvaluator.GetHandRanking( playerEvalCards );
            var playerHandCategory = HoldemHandEvaluator.GetHandCategory( playerRanking );
            string playerHandDesc = HoldemHandEvaluator.GetHandDescription( playerRanking );
            // ----------------------------------------------------------------------------

            // ---------------------------------------------------------------------------
            // FOLD PATH – player forfeits Ante & Blind, but Trips is still live
            // ---------------------------------------------------------------------------
            if ( game.Folded ) {
                resultBuilder.AppendLine( "You folded. You lose your Ante and Blind bets." );
                totalWinnings = -(game.AnteBet + game.BlindBet);

                if ( game.TripsBet > 0 ) {
                    TripsPayouts.TryGetValue( playerHandCategory, out decimal tripsMultiplier );

                    if ( tripsMultiplier > 0 ) {
                        decimal win = game.TripsBet * tripsMultiplier;
                        totalWinnings += win;
                        resultBuilder.AppendLine( $"✅ **Trips Bet:** Wins **${win:N2}** ({tripsMultiplier}x)!" );
                    }
                    else {
                        totalWinnings -= game.TripsBet;
                        resultBuilder.AppendLine( $"❌ **Trips Bet:** You lose ${game.TripsBet:N2}." );
                    }
                }

                return (totalWinnings, resultBuilder.ToString(), playerHandDesc);
            }
            // ---------------------------------------------------------------------------

            // At this point the player stayed in, so we need to evaluate the dealer
            Card[] dealerEvalCards = game.DealerHand.Concat( game.CommunityCards ).ToArray();
            int dealerRanking = HoldemHandEvaluator.GetHandRanking( dealerEvalCards );
            var dealerHandCategory = HoldemHandEvaluator.GetHandCategory( dealerRanking );

            // ----------------------- Trips bet (player stayed in) ----------------------
            /*if ( game.TripsBet > 0 ) {
                TripsPayouts.TryGetValue( playerHandCategory, out decimal tripsMultiplier );

                if ( tripsMultiplier > 0 ) {
                    decimal win = game.TripsBet * tripsMultiplier;
                    totalWinnings += win;
                    resultBuilder.AppendLine( $"✅ **Trips Bet:** Wins **${win:N2}** ({tripsMultiplier}x)!\n" );
                }
                else {
                    totalWinnings -= game.TripsBet;
                    resultBuilder.AppendLine( $"❌ **Trips Bet:** You lose ${game.TripsBet:N2}.\n" );
                }
            }*/
            // --------------------------------------------------------------------------

            bool dealerQualifies = dealerHandCategory <= PokerHandCategory.OnePair;

            if ( playerRanking < dealerRanking ) // player wins
            {
                resultBuilder.AppendLine( "🎉 **You win the hand!**\n" );
                totalWinnings += game.PlayBet;

                if ( dealerQualifies ) {
                    totalWinnings += game.AnteBet;
                    resultBuilder.AppendLine( $"✅ Play (${game.PlayBet}) and Ante (${game.AnteBet}) bets win." );
                }
                else {
                    resultBuilder.AppendLine( $"✅ Play bet (${game.PlayBet}) wins. Ante pushes (Dealer did not qualify)." );
                }

                BlindPayouts.TryGetValue( playerHandCategory, out decimal blindMultiplier );
                if ( blindMultiplier > 0 ) {
                    decimal win = game.BlindBet * blindMultiplier;
                    totalWinnings += win;
                    resultBuilder.AppendLine( $"✅ **Blind Bet:** Wins **${win:N2}** ({blindMultiplier}x)!" );
                }
                else {
                    resultBuilder.AppendLine( "🅿️ **Blind Bet:** Pushes (hand not a Straight or better)." );
                }
            }
            else if ( playerRanking > dealerRanking ) // dealer wins
            {
                resultBuilder.AppendLine( "😭 **Dealer wins the hand.**\n" );
                decimal loss = game.AnteBet + game.BlindBet + game.PlayBet;
                totalWinnings -= loss;
                resultBuilder.AppendLine( $"❌ All bets lose ($-{loss:N2})." );
            }
            else // tie
            {
                resultBuilder.AppendLine( "⚖️ **It's a push!**" );
                resultBuilder.AppendLine( "🅿️ All bets push. Your wager is returned." );
            }
            
            // ----------------------- Trips bet (player stayed in) ----------------------
            if ( game.TripsBet > 0 ) {
                TripsPayouts.TryGetValue( playerHandCategory, out decimal tripsMultiplier );

                if ( tripsMultiplier > 0 ) {
                    decimal win = game.TripsBet * tripsMultiplier;
                    totalWinnings += win;
                    resultBuilder.AppendLine( $"✅ **Trips Bet:** Wins **${win:N2}** ({tripsMultiplier}x)!\n" );
                }
                else {
                    totalWinnings -= game.TripsBet;
                    resultBuilder.AppendLine( $"❌ **Trips Bet:** You lose ${game.TripsBet:N2}.\n" );
                }
            }
            // --------------------------------------------------------------------------

            return (totalWinnings, resultBuilder.ToString(), playerHandDesc);
        }


        EmbedBuilder BuildUthEmbed(UthGameState game, string title, string description, string footer, Color color) {
            var embed = new EmbedBuilder()
                .WithAuthor( Context.User.GlobalName, Context.User.GetAvatarUrl() )
                .WithTitle( title )
                .WithDescription( description )
                .WithColor( color )
                .WithFooter( footer );

            if ( game.PlayerHand.Count != 0 ) {
                embed.AddField( "Your Cards", game.GetHandString( game.PlayerHand ), true );
            }

            if ( game.DealerHand.Count != 0 && game.Folded ) {
                embed.AddField( "Dealer Cards", game.GetHandString( game.DealerHand ), true );
            }

            if ( game.CommunityCards.Count != 0 ) {
                embed.AddField( "Community Cards", game.GetHandString( game.CommunityCards ) );
            }

            return embed;
        }

        bool ValidateBet(float bet, float tripsBet, out string? error) {
            error = null;
            if ( bet < MIN_BET_UTH ) {
                error = $"Ante bet must be at least ${MIN_BET_UTH:C2}.";
                return false;
            }

            if ( bet > MAX_BET_UTH ) {
                error = $"Ante bet cannot exceed ${MAX_BET_UTH:C2}.";
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

            string currentHandDesc = GetCurrentHandDescription( game );
            string description = $"Your cards are dealt. Make your move." +
                                 $"\nYour current best hand: **{currentHandDesc}**";

            var embed = BuildUthEmbed(
                game,
                title: 
                $"Ante: ${bet:N2}\n" +
                $"Blind: ${bet:N2}\n" +
                $"Trips: ${tripsBet:N2}\n" +
                $"Total: ${bet*2+tripsBet:N2}\n\n" +
                $"Ultimate Texas Hold'em - Pre-Flop ",
                description: description,
                footer: "You can bet 3x or 4x your Ante, or check to see the flop for free.",
                color: Color.Blue
            ).Build();

            var components = new ComponentBuilder()
                .WithButton( "Bet 4x", $"{CMD_PREFIX_UTH}:{BET_4X}:{gameId}", ButtonStyle.Success )
                .WithButton( "Bet 3x", $"{CMD_PREFIX_UTH}:{BET_3X}:{gameId}", ButtonStyle.Success )
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

        [ComponentInteraction( $"{CMD_PREFIX_UTH}:{PLAY_AGAIN}:*:*", ignoreGroupNames: true )]
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

        [ComponentInteraction( $"{CMD_PREFIX_UTH}:{END_GAME}", ignoreGroupNames: true )]
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
                case BET_4X:
                {
                    int betAmount = 4 * game.AnteBet;
                    if ( PlayersWallet.GetBalance( Context.Guild.Id, Context.User.Id ) < betAmount ) {
                        await FollowupAsync( "Insufficient funds for 4x bet.", ephemeral: true );
                        return;
                    }

                    PlayersWallet.SubtractFromBalance( Context.Guild.Id, Context.User.Id, betAmount );
                    game.PlayBet = betAmount;
                    await ProcessShowdown( game, gameId );
                    break;
                }
                case BET_3X:
                {
                    int betAmount = 3 * game.AnteBet;
                    if ( PlayersWallet.GetBalance( Context.Guild.Id, Context.User.Id ) < betAmount ) {
                        await FollowupAsync( "Insufficient funds for 3x bet.", ephemeral: true );
                        return;
                    }

                    PlayersWallet.SubtractFromBalance( Context.Guild.Id, Context.User.Id, betAmount );
                    game.PlayBet = betAmount;
                    await ProcessShowdown( game, gameId );
                    break;
                }
                case BET_2X:
                {
                    int betAmount = 2 * game.AnteBet;
                    if ( PlayersWallet.GetBalance( Context.Guild.Id, Context.User.Id ) < betAmount ) {
                        await FollowupAsync( "Insufficient funds for 2x bet.", ephemeral: true );
                        return;
                    }

                    PlayersWallet.SubtractFromBalance( Context.Guild.Id, Context.User.Id, betAmount );
                    game.PlayBet = betAmount;
                    await ProcessShowdown( game, gameId );
                    break;
                }
                case BET_1X:
                {
                    int betAmount = 1 * game.AnteBet;
                    if ( PlayersWallet.GetBalance( Context.Guild.Id, Context.User.Id ) < betAmount ) {
                        await FollowupAsync( "Insufficient funds for 1x bet.", ephemeral: true );
                        return;
                    }

                    PlayersWallet.SubtractFromBalance( Context.Guild.Id, Context.User.Id, betAmount );
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

            string currentHandDesc = GetCurrentHandDescription( game );
            var description = $"Three community cards are revealed.\n\nYour current best hand: **{currentHandDesc}**";

            var embedBuilder = BuildUthEmbed(
                game,
                "Ultimate Texas Hold'em - The Flop",
                description,
                "You can bet 2x your Ante or check to see the final cards.",
                Color.Orange
            );
            var components = new ComponentBuilder()
                .WithButton( "Bet 2x", $"{CMD_PREFIX_UTH}:{BET_2X}:{gameId}", ButtonStyle.Success )
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

            string currentHandDesc = GetCurrentHandDescription( game );
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
                .WithButton( "Bet 1x", $"{CMD_PREFIX_UTH}:{BET_1X}:{gameId}", ButtonStyle.Success )
                .WithButton( "Fold", $"{CMD_PREFIX_UTH}:{FOLD}:{gameId}", ButtonStyle.Danger )
                .Build();

            await ModifyOriginalResponseAsync( p => {
                    p.Embed = embedBuilder.Build();
                    p.Components = components;
                }
            );
        }

        async Task ProcessShowdown(UthGameState game, ulong gameId) {
            if ( game.PlayBet == 0 && !game.Folded ) {
                game.Folded = true;
            }

            while (game.CommunityCards.Count < 5)
                game.CommunityCards.Add( game.Deck.Draw() );

            (decimal winnings, string winDescription, string playerHandDesc) = CalculateShowdownResult( game );

            string dealerHandDesc = HoldemHandEvaluator.GetHandDescription(
                HoldemHandEvaluator.GetHandRanking( game.DealerHand.Concat( game.CommunityCards ).ToArray() )
            );
            var finalDescription = new StringBuilder();
            finalDescription.AppendLine( $"Dealer has: **{dealerHandDesc}**" );
            finalDescription.AppendLine( $"Your Hand: **{playerHandDesc}**" );
            finalDescription.AppendLine();
            finalDescription.AppendLine( winDescription );

            decimal profit = winnings;
            string footer = profit > 0 ? $"Congratulations! You won ${profit:N2}." :
                profit < 0 ? $"Unlucky. You lost ${-profit:N2}." :
                "Push! You broke even on the hand.";
            var finalColor = profit > 0 ? Color.Green : profit < 0 ? Color.Red : Color.LightGrey;

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
                    .WithValue( game.GetHandString( game.DealerHand ) )
                    .WithIsInline( true )
            );
            PlayersWallet.AddToBalance( Context.Guild.Id, Context.User.Id, (float)winnings );
            if ( profit > game.AnteBet + game.BlindBet + game.TripsBet * 5m ) {
                await Context.Channel.SendMessageAsync(
                    $"🎉**WINNER**🎉\n" +
                    $" {Context.User.Mention} won **${winnings:N2}**! Playing - Texas Hold'em\n" +
                    $"{winDescription}\n" +
                    $"Winning hand: **{playerHandDesc}**"
                );
            }

            string currentFooter = embedBuilder.Footer?.Text ?? "";
            currentFooter += $"\nYour total balance is ${PlayersWallet.GetBalance( Context.Guild.Id, Context.User.Id ):N2}";
            embedBuilder.Footer = new EmbedFooterBuilder()
                .WithText( currentFooter )
                .WithIconUrl( Context.User.GetAvatarUrl() );

            var components = new ComponentBuilder()
                .WithButton( "Play Again", $"{CMD_PREFIX_UTH}:{PLAY_AGAIN}:{game.AnteBet}:{game.TripsBet}" )
                .WithButton( "End Game", $"{CMD_PREFIX_UTH}:{END_GAME}", ButtonStyle.Danger )
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