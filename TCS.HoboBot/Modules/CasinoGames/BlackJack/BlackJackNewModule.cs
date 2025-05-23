using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using TCS.HoboBot.Data;

namespace TCS.HoboBot.Modules.CasinoGames;

public class BlackJackNewModule : InteractionModuleBase<SocketInteractionContext> {
    enum Suit { Clubs, Diamonds, Hearts, Spades }

    // every rank now has an * unique * underlying integer (no duplicates)
    enum Rank {
        Two = 2, Three, Four, Five, Six, Seven, Eight, Nine,
        Ten = 10, Jack, Queen, King, Ace,
    }

    readonly struct Card {
        public Rank Rank { get; }
        public Suit Suit { get; }

        /* points for blackjack math */
        public int Points => Rank switch {
            Rank.Jack or Rank.Queen or Rank.King => 10,
            Rank.Ace => 11,
            _ => (int)Rank,
        };

        /* glyph lookup ─ ♣ ♦ ♥ ♠ */
        // static readonly Dictionary<Suit, string> SuitGlyph = new() {
        //     [Suit.Clubs] = "♣",
        //     [Suit.Diamonds] = "♦",
        //     [Suit.Hearts] = "♥",
        //     [Suit.Spades] = "♠",
        // };

        /* emoji lookup ─ ♠♢♡♣ */
        static readonly Dictionary<Suit, string> SuitGlyph = new() {
            [Suit.Clubs] = "♣",
            [Suit.Diamonds] = "♢",
            [Suit.Hearts] = "♡",
            [Suit.Spades] = "♠",
        };

        /* emoji lookup ─ ♣️ ♦️ ♥️ ♠️ */
        // static readonly Dictionary<Suit, string> SuitGlyph = new() {
        //     [Suit.Clubs] = "♣️",
        //     [Suit.Diamonds] = "♦️",
        //     [Suit.Hearts] = "♥️",
        //     [Suit.Spades] = "♠️",
        // };

        /* text for the rank */
        static string RankText(Rank r) => r switch {
            Rank.Ace => "A",
            Rank.King => "K",
            Rank.Queen => "Q",
            Rank.Jack => "J",
            Rank.Ten => "10",
            _ => ((int)r).ToString(),
        };

        public Card(Rank rank, Suit suit) {
            Rank = rank;
            Suit = suit;
        }

        public override string ToString() => $"**{RankText( Rank )}**{SuitGlyph[Suit]}";
    }

    sealed class Shoe {
        readonly Random m_rng;
        readonly int m_decks;
        readonly Stack<Card> m_cards;
        public Shoe(int decks, Random rng) {
            m_decks = decks;
            m_rng = rng;
            m_cards = new Stack<Card>();
            ShuffleNewShoe();
        }
        public Card Deal() {
            if ( m_cards.Count < 60 ) {
                ShuffleNewShoe();
            }

            return m_cards.Pop();
        }
        void ShuffleNewShoe() {
            List<Card> tmp = new(m_decks * 52);
            for (var d = 0; d < m_decks; d++)
                foreach (Suit s in Enum.GetValues( typeof(Suit) )) {
                    foreach (Rank r in Enum.GetValues( typeof(Rank) )) {
                        tmp.Add( new Card( r, s ) );
                    }
                }

            for (int n = tmp.Count - 1; n > 0; n--) {
                int k = m_rng.Next( n + 1 );
                (tmp[n], tmp[k]) = (tmp[k], tmp[n]);
            }

            m_cards.Clear();
            tmp.ForEach( m_cards.Push );
        }
    }

    sealed class Hand {
        readonly List<Card> m_cards = [];
        public IReadOnlyList<Card> Cards => m_cards;
        public void Add(Card c) => m_cards.Add( c );

        IEnumerable<int> PossibleTotals {
            get {
                int hard = m_cards.Sum( c => c.Points );
                int aces = m_cards.Count( c => c.Rank == Rank.Ace );
                for (var soft = 0; soft <= aces; soft++) yield return hard - soft * 10;
            }
        }
        public int Total => PossibleTotals.Where( t => t <= 21 )
            .DefaultIfEmpty( PossibleTotals.Min() ).Max();
        public bool IsBlackjack => m_cards.Count == 2 && Total == 21;
        public bool IsBust => Total > 21;

        public override string ToString() =>
            string.Join( ' ', m_cards.Select( c => c.ToString() ) ) + $"  ({Total})";
    }

    sealed class BlackjackGame {
        readonly Shoe m_shoe;
        public List<Hand?> PlayerHands { get; } = new List<Hand?>(); // Player can have multiple hands
        public List<float> BetsPerHand { get; } = new List<float>(); // Bet for each hand
        public Hand Dealer { get; } = new Hand();

        public bool RoundFinished { get; private set; } // True when all player hands and dealer hand are complete
        public int CurrentPlayerHandIndex { get; private set; } = 0; // Tracks which hand the player is currently playing

        private bool m_canSplitCurrentHand = false;
        private bool m_splitOccurredThisRound = false; // Global flag for the "split once" rule

        public BlackjackGame(Shoe shoe) {
            m_shoe = shoe;
        }

        public Hand? GetCurrentPlayerHand() {
            if ( PlayerHands.Count > CurrentPlayerHandIndex ) {
                return PlayerHands[CurrentPlayerHandIndex];
            }

            return null; // Should not happen in normal flow
        }

        public float GetCurrentBet() {
            if ( BetsPerHand.Count > CurrentPlayerHandIndex ) {
                return BetsPerHand[CurrentPlayerHandIndex];
            }

            return 0; // Should not happen
        }

        public void InitialDeal(float initialBet) {
            PlayerHands.Clear(); // Ensure fresh start
            BetsPerHand.Clear();

            Hand? firstHand = new Hand();
            PlayerHands.Add( firstHand );
            BetsPerHand.Add( initialBet );
            CurrentPlayerHandIndex = 0;

            // Deal initial cards
            firstHand.Add( m_shoe.Deal() );
            Dealer.Add( m_shoe.Deal() );
            firstHand.Add( m_shoe.Deal() );
            Dealer.Add( m_shoe.Deal() ); // Dealer's second card is dealt but might be hidden initially

            UpdateActionFlags();

            // Check for immediate Blackjacks
            if ( firstHand.IsBlackjack || Dealer.IsBlackjack ) {
                // If player has blackjack, they can't do other actions.
                // If dealer has blackjack, game ends.
                // If both, it's a push.
                // Stand will automatically play dealer and finish.
                Stand();
            }
        }

        private void UpdateActionFlags() {
            Hand? currentHand = GetCurrentPlayerHand();
            if ( currentHand == null || RoundFinished ) {
                m_canSplitCurrentHand = false;
                return;
            }

            // Can only split/double on the first two cards of a hand
            bool isFirstTwoCards = currentHand.Cards.Count == 2;

            // Splitting:
            // 1. First two cards.
            // 2. Cards have same rank.
            // 3. No split has occurred yet in this entire round.
            m_canSplitCurrentHand = isFirstTwoCards &&
                                    !m_splitOccurredThisRound &&
                                    currentHand.Cards[0].Rank == currentHand.Cards[1].Rank;
        }

        public bool CanDoubleDown() {
            Hand? currentHand = GetCurrentPlayerHand();
            if ( currentHand == null || RoundFinished ) {
                return false;
            }

            // Can only double down on the first two cards.
            // (Some casinos restrict totals, e.g., 9, 10, 11. We'll allow on any 2 cards for now)
            return currentHand.Cards.Count == 2;
        }

        public bool CanSplit() => m_canSplitCurrentHand;

        public void Hit() {
            if ( RoundFinished || GetCurrentPlayerHand() == null ) {
                return;
            }

            Hand? currentHand = GetCurrentPlayerHand();
            currentHand?.Add( m_shoe.Deal() );

            m_canSplitCurrentHand = false; // Cannot split after hitting

            if ( currentHand is { IsBust: true } || currentHand is { Total: 21 } ) // Also auto-stand on 21
            {
                AdvanceToNextHandOrPlayDealer();
            }
            else {
                UpdateActionFlags(); // In case something changed, though unlikely for hit
            }
        }

        public void Stand() {
            if ( RoundFinished || GetCurrentPlayerHand() == null ) {
                return;
            }

            AdvanceToNextHandOrPlayDealer();
        }

        public bool DoubleDown(Action<float> chargePlayerCallback) // Callback to deduct additional bet
        {
            Hand? currentHand = GetCurrentPlayerHand();
            if ( !CanDoubleDown() || currentHand == null ) {
                return false;
            }

            float currentBet = GetCurrentBet();
            chargePlayerCallback( currentBet ); // Deduct the additional bet from player's wallet

            BetsPerHand[CurrentPlayerHandIndex] = currentBet * 2;
            currentHand.Add( m_shoe.Deal() );

            m_canSplitCurrentHand = false; // Cannot split after doubling
            AdvanceToNextHandOrPlayDealer();
            return true;
        }

        public bool Split(Action<float> chargePlayerCallback) // Callback for the new hand's bet
        {
            Hand? currentHandToSplit = GetCurrentPlayerHand();
            if ( !CanSplit() || currentHandToSplit == null ) {
                return false;
            }

            float originalBet = GetCurrentBet();
            chargePlayerCallback( originalBet ); // Deduct bet for the new hand

            m_splitOccurredThisRound = true; // Mark that a split has happened

            // Create the new hand
            Hand? newHand = new Hand();
            Card cardToMove = currentHandToSplit.Cards[1]; // Take the second card

            // Modify current hand: remove second card, deal a new one
            // Need a way to remove a card from Hand or reconstruct. Let's reconstruct.
            Card firstCardOfSplitHand = currentHandToSplit.Cards[0];
            PlayerHands[CurrentPlayerHandIndex] = new Hand(); // New hand object for the first split hand
            PlayerHands[CurrentPlayerHandIndex]?.Add( firstCardOfSplitHand );
            PlayerHands[CurrentPlayerHandIndex]?.Add( m_shoe.Deal() );

            // Setup the second split hand
            newHand.Add( cardToMove );
            newHand.Add( m_shoe.Deal() );

            // Insert the new hand and its bet *after* the current one
            PlayerHands.Insert( CurrentPlayerHandIndex + 1, newHand );
            BetsPerHand.Insert( CurrentPlayerHandIndex + 1, originalBet );

            // A 21 after splitting Aces is usually just 21, not Blackjack.
            // Our IsBlackjack property correctly handles this (requires 2 cards).

            UpdateActionFlags(); // Update flags for the current hand (which is the first of the two split hands)

            // If the first split hand is 21, player might want to auto-stand or it's played out.
            // For simplicity, we'll let the normal flow handle it. If it's 21, next hit/stand will advance.
            // Or, if you want to auto-stand a 21 on split:
            if ( PlayerHands[CurrentPlayerHandIndex] is { Total: 21 } ) {
                // Don't call Stand() directly as it might skip the second split hand's turn.
                // The next action (Hit/Stand) from player on this 21 hand will trigger AdvanceToNextHandOrPlayDealer.
                // Or, if we want to force it without player action:
                // AdvanceToNextHandOrPlayDealer(); // This is tricky if called mid-action.
                // Best to let player see the 21 and then choose to stand.
            }

            return true;
        }

        private void AdvanceToNextHandOrPlayDealer() {
            CurrentPlayerHandIndex++;
            if ( CurrentPlayerHandIndex >= PlayerHands.Count ) {
                // All player hands have been played
                PlayDealerHand();
            }
            else {
                // There's another hand for the player to play (due to split)
                UpdateActionFlags(); // Update for the new current hand
                // If this new hand is a blackjack (e.g. split Aces, got a 10), it auto-stands.
                if ( GetCurrentPlayerHand() != null && GetCurrentPlayerHand() is { IsBlackjack: true } ) {
                    // No action needed from player, move to next or dealer
                    AdvanceToNextHandOrPlayDealer();
                }
            }
        }

        private void PlayDealerHand() {
            // Dealer only plays if at least one player hand is not bust
            // (or if a player hand was a natural blackjack that hasn't been beaten by dealer blackjack yet)
            bool playerHasActiveHand = PlayerHands.Any( h => !(h is { IsBust: true }) || (h.IsBlackjack && !Dealer.IsBlackjack) );

            if ( playerHasActiveHand ) {
                while (Dealer.Total < 17 || (Dealer.Total == 17 && DealerHasSoft17())) {
                    Dealer.Add( m_shoe.Deal() );
                }
            }

            RoundFinished = true;
        }

        bool DealerHasSoft17() {
            // Assuming Ace is 11 points by default in Hand.Points
            // A soft 17 means the total is 17, and one of the Aces is counted as 11.
            // If all Aces were 1, the sum of c.Points would be less than 17 if an Ace is present.
            // Or, more directly: if Total is 17 and there's an Ace, and sum of c.Points is not 17.
            int hardTotal = Dealer.Cards.Sum( c => (c.Rank == Rank.Ace ? 1 : c.Points) ); // Calculate hard total
            int aceCount = Dealer.Cards.Count( c => c.Rank == Rank.Ace );

            // Check if any combination of Aces as 11 results in 17
            for (int i = 0; i <= aceCount; i++) {
                // i is the number of Aces counted as 11
                if ( hardTotal + i * 10 == 17 && i > 0 ) {
                    return true; // It's a soft 17 if at least one Ace is 11
                }
            }

            return false; // Dealer stands on hard 17 or soft 18+
        }


        // Settle needs to handle multiple hands and bets
        public List<(Hand playerHand, float bet, Outcome outcome, decimal multiplier)> Settle() {
            if ( !RoundFinished ) {
                throw new InvalidOperationException( "Round not finished." );
            }

            var results = new List<(Hand, float, Outcome, decimal)>();
            bool dealerBj = Dealer.IsBlackjack;

            for (int i = 0; i < PlayerHands.Count; i++) {
                Hand? playerHand = PlayerHands[i];
                float handBet = BetsPerHand[i];
                Outcome handOutcome;
                decimal handMultiplier;

                // A natural Blackjack (3:2 payout) only applies if:
                // 1. It's the player's original single hand (not from a split).
                // 2. It consists of two cards.
                bool isNaturalBlackjack = playerHand is { IsBlackjack: true } && PlayerHands.Count == 1 && !m_splitOccurredThisRound;
                // If it was a split hand that resulted in 21 with two cards (e.g. split Aces, got a Ten),
                // it's just 21, not a "natural Blackjack" for payout purposes. It pays 1:1.

                if ( isNaturalBlackjack && dealerBj ) {
                    handOutcome = Outcome.Push;
                    handMultiplier = 1m;
                }
                else if ( isNaturalBlackjack ) {
                    handOutcome = Outcome.PlayerWin;
                    handMultiplier = 2.5m; // 3:2 payout
                }
                else if ( dealerBj ) // Dealer natural beats any other player hand including 21 from split
                {
                    handOutcome = Outcome.DealerWin;
                    handMultiplier = 0m;
                }
                else if ( playerHand is { IsBust: true } ) {
                    handOutcome = Outcome.DealerWin;
                    handMultiplier = 0m;
                }
                else if ( Dealer.IsBust ) {
                    handOutcome = Outcome.PlayerWin;
                    handMultiplier = 2m; // 1:1 payout
                }
                else if ( playerHand != null && playerHand.Total > Dealer.Total ) {
                    handOutcome = Outcome.PlayerWin;
                    handMultiplier = 2m; // 1:1 payout
                }
                else if ( playerHand != null && playerHand.Total < Dealer.Total ) {
                    handOutcome = Outcome.DealerWin;
                    handMultiplier = 0m;
                }
                else // Push
                {
                    handOutcome = Outcome.Push;
                    handMultiplier = 1m;
                }

                if ( playerHand != null ) {
                    results.Add( (playerHand, handBet, handOutcome, handMultiplier) );
                }
            }

            return results;
        }

        public enum Outcome { PlayerWin, DealerWin, Push }
    }

    /* ───────────  runtime state  ─────────── */
    sealed class GameSession {
        public BlackjackGame Game { get; }
        public float InitialBet { get; } // This was the original bet amount
        public DateTimeOffset Started { get; } = DateTimeOffset.UtcNow;
        public GameSession(BlackjackGame game, float initialBet) {
            Game = game;
            InitialBet = initialBet;
        }
    }

    static readonly Random Rng = new();
    static readonly Shoe SharedShoe = new(6, Rng); // 6-deck shoe
    static readonly ConcurrentDictionary<ulong, GameSession> ActiveGames = new();

    [SlashCommand( "blackjack", "Play an interactive blackjack hand vs the house." )]
    public async Task BlackjackAsync(float bet) {
        if ( bet <= 0 ) {
            await RespondAsync( "Bet must be positive.", ephemeral: true );
            return;
        }

        if ( bet > 10000 ) {
            await RespondAsync( "Max bet is $10,000.", ephemeral: true );
            return;
        }

        if ( ActiveGames.ContainsKey( Context.User.Id ) ) {
            await RespondAsync( "You already have a hand in progress! Finish it first.", ephemeral: true );
            return;
        }

        if ( PlayersWallet.GetBalance( Context.Guild.Id, Context.User.Id ) < bet ) // Assuming PlayersWallet exists
        {
            await RespondAsync( $"{Context.User.Mention} doesn't have enough cash!", ephemeral: true );
            return;
        }

        PlayersWallet.SubtractFromBalance( Context.Guild.Id, Context.User.Id, bet ); // Deduct initial bet

        var game = new BlackjackGame( SharedShoe );
        var session = new GameSession( game, bet );
        game.InitialDeal( bet ); // Pass initial bet to game
        ActiveGames[Context.User.Id] = session;

        // Respond with the initial game state
        // Defer if the initial deal itself finishes the game (e.g. Blackjack)
        if ( session.Game.RoundFinished ) {
            await DeferAsync( ephemeral: true );
            await UpdateGameMessage( session, Context.Interaction ); // Will handle finished game
        }
        else {
            await RespondAsync(
                embed: BuildEmbed( Context.User, session, showDealerHole: false ),
                components: GetButtons( Context.User.Id, session.Game ).Build(),
                ephemeral: true
            );
        }
    }

    // Button Handlers
    [ComponentInteraction( "bj_hit_*" )]
    public async Task OnHit(string userIdRaw) {
        if ( !ValidateUserInteraction( userIdRaw, out var session ) ) {
            await DeferAsync();
            return;
        }

        await DeferAsync();
        session.Game.Hit();
        await UpdateGameMessage( session, Context.Interaction );
    }

    [ComponentInteraction( "bj_stand_*" )]
    public async Task OnStand(string userIdRaw) {
        if ( !ValidateUserInteraction( userIdRaw, out var session ) ) {
            await DeferAsync();
            return;
        }

        await DeferAsync();
        session.Game.Stand();
        await UpdateGameMessage( session, Context.Interaction );
    }

    [ComponentInteraction( "bj_doubledown_*" )]
    public async Task OnDoubleDown(string userIdRaw) {
        if ( !ValidateUserInteraction( userIdRaw, out var session ) ) {
            await DeferAsync();
            return;
        }

        float additionalBet = session.Game.GetCurrentBet(); // Bet to double is the current hand's bet
        if ( PlayersWallet.GetBalance( Context.Guild.Id, Context.User.Id ) < additionalBet ) {
            await RespondAsync( "You don't have enough funds to double down.", ephemeral: true );
            return;
        }

        await DeferAsync();
        bool success = session.Game.DoubleDown( betAmount => PlayersWallet.SubtractFromBalance( Context.Guild.Id, Context.User.Id, betAmount ) );
        if ( success ) {
            await UpdateGameMessage( session, Context.Interaction );
        }
        else {
            // Should not happen if CanDoubleDown was checked by button availability, but as a fallback:
            await FollowupAsync( "Cannot double down at this time.", ephemeral: true );
        }
    }

    [ComponentInteraction( "bj_split_*" )]
    public async Task OnSplit(string userIdRaw) {
        if ( !ValidateUserInteraction( userIdRaw, out var session ) ) {
            await DeferAsync();
            return;
        }

        float betForNewHand = session.Game.GetCurrentBet(); // New hand gets same bet as the one being split
        if ( PlayersWallet.GetBalance( Context.Guild.Id, Context.User.Id ) < betForNewHand ) {
            await RespondAsync( "You don't have enough funds for the new hand to split.", ephemeral: true );
            return;
        }

        await DeferAsync();
        bool success = session.Game.Split( betAmount => PlayersWallet.SubtractFromBalance( Context.Guild.Id, Context.User.Id, betAmount ) );
        if ( success ) {
            await UpdateGameMessage( session, Context.Interaction );
        }
        else {
            await FollowupAsync( "Cannot split at this time.", ephemeral: true );
        }
    }

    // ValidateUser for component interactions
    bool ValidateUserInteraction(string rawUserId, [NotNullWhen( true )] out GameSession? session) {
        session = null;
        if ( !ulong.TryParse( rawUserId, out ulong uid ) || uid != Context.User.Id ) {
            // Consider an ephemeral message to Context.User if rawUserId is valid but not them
            return false;
        }

        if ( !ActiveGames.TryGetValue( uid, out session ) || session.Game.RoundFinished ) {
            // Game ended or doesn't exist
            return false;
        }

        return true;
    }

    async Task UpdateGameMessage(GameSession s, IDiscordInteraction interaction) {
        var game = s.Game;
        bool finished = game.RoundFinished;

        var embed = BuildEmbed( Context.User, s, showDealerHole: finished );
        var buttons = finished ? null : GetButtons( Context.User.Id, game ).Build(); // No buttons if finished

        // Use ModifyOriginalResponseAsync for subsequent updates from button clicks
        await interaction.ModifyOriginalResponseAsync( m => {
                m.Embed = embed;
                if ( buttons != null ) {
                    m.Components = buttons;
                }
                else {
                    m.Components = new ComponentBuilder().Build(); // Clear buttons if null
                }
            }
        );

        if ( finished ) {
            var settlementResults = game.Settle();
            var summaryLines = new List<string>();
            decimal totalNetPlayerGain = 0; // Tracks net gain/loss relative to initial bets placed.

            for (int i = 0; i < settlementResults.Count; i++) {
                var result = settlementResults[i];
                string handIdentifier = game.PlayerHands.Count > 1 ? $"Hand {i + 1} ({result.playerHand.ToString()}) " : "";
                string handSummary;

                float payoutAmount = result.bet * (float)result.multiplier;
                float netGainForHand = payoutAmount - result.bet; // How much more (or less) than the bet was returned

                if ( result.multiplier > 0 ) // If any payout (push or win)
                {
                    PlayersWallet.AddToBalance(Context.Guild.Id, Context.User.Id, payoutAmount );
                }

                totalNetPlayerGain += (decimal)netGainForHand;

                switch (result.outcome) {
                    case BlackjackGame.Outcome.PlayerWin:
                        handSummary = $"{handIdentifier}wins **${netGainForHand:0.00}** (Total Payout: ${payoutAmount:0.00})";
                        if ( result.multiplier == 2.5m ) {
                            handSummary += " (Blackjack 3:2!)";
                        }

                        break;
                    case BlackjackGame.Outcome.DealerWin:
                        handSummary = $"{handIdentifier}loses **${result.bet:0.00}**";
                        break;
                    default: // Push
                        handSummary = $"{handIdentifier}pushes bet of **${result.bet:0.00}** (Bet Returned)";
                        break;
                }

                summaryLines.Add( handSummary );
            }

            string finalSummaryMessage = $"{Context.User.Mention}'s Blackjack Results:\n" + string.Join( "\n", summaryLines );
            if ( game.PlayerHands.Count > 1 || totalNetPlayerGain != (decimal)(s.InitialBet * (float)1.5m) && totalNetPlayerGain != (decimal)s.InitialBet && totalNetPlayerGain != (decimal)-s.InitialBet && totalNetPlayerGain != 0 ) {
                // Add overall only if complex
                if ( totalNetPlayerGain > 0 ) {
                    finalSummaryMessage += $"\n**Overall Net Gain: ${totalNetPlayerGain:0.00}**";
                }
                else if ( totalNetPlayerGain < 0 ) {
                    finalSummaryMessage += $"\n**Overall Net Loss: ${Math.Abs( totalNetPlayerGain ):0.00}**";
                }
                else {
                    finalSummaryMessage += "\n**Overall: Broke Even** (excluding pushes on original bet).";
                }
            }


            // Send a public message to the channel with the results.
            await Context.Channel.SendMessageAsync( finalSummaryMessage );
            ActiveGames.TryRemove( Context.User.Id, out _ );
        }
    }

    static ComponentBuilder GetButtons(ulong uid, BlackjackGame game) {
        var cb = new ComponentBuilder();
        if ( !game.RoundFinished && game.GetCurrentPlayerHand() != null ) {
            // Standard Actions
            cb.WithButton( "Hit", $"bj_hit_{uid}", ButtonStyle.Primary );
            cb.WithButton( "Stand", $"bj_stand_{uid}", ButtonStyle.Secondary );

            // Conditional Actions for the current hand
            if ( game.CanDoubleDown() ) {
                // Check wallet for double down amount in the handler, not here, to avoid race conditions.
                cb.WithButton( "Double Down", $"bj_doubledown_{uid}", ButtonStyle.Success );
            }

            if ( game.CanSplit() ) {
                // Check wallet for split amount in the handler.
                cb.WithButton( "Split", $"bj_split_{uid}", ButtonStyle.Success, row: (game.CanDoubleDown() ? 0 : 1) ); // Place on second row if double is also an option
            }
        }

        return cb;
    }

    static Embed BuildEmbed(SocketUser user, GameSession ses, bool showDealerHole) {
        var g = ses.Game;
        var embedBuilder = new EmbedBuilder();
        Hand? currentPlayerHand = g.GetCurrentPlayerHand(); // Might be null if the round just ended before player action

        // Title shows initial bet, could update to the current total bet if desired
        embedBuilder.WithTitle( $"Blackjack – Initial Bet: ${ses.InitialBet:0.00}" );
        embedBuilder.WithDescription( $"{user.Mention} vs House\n\u200b" ); // Invisible char for spacing

        // Dealer's Hand
        string dealerText;
        if ( showDealerHole || g.Dealer.Cards.Count == 0 ) {
            // Show full dealer hand if the round is over or no cards yet
            dealerText = g.Dealer.ToString();
        }
        else {
            dealerText = $"{g.Dealer.Cards[0]} ??"; // Show one card and a hole card
        }

        embedBuilder.AddField( "Dealer's Hand", dealerText, inline: false );

        // Player's Hand(s)
        if ( g.PlayerHands.Count == 0 ) {
            embedBuilder.AddField( "Your Hand", "Dealing...", inline: false );
        }
        else {
            for (int i = 0; i < g.PlayerHands.Count; i++) {
                var hand = g.PlayerHands[i];
                var handBet = g.BetsPerHand[i]; // Need to ensure BetsPerHand is populated correctly
                string handTitle = $"Your Hand {(g.PlayerHands.Count > 1 ? (i + 1).ToString() : "")} - Bet: ${handBet:0.00}";
                if ( !g.RoundFinished && i == g.CurrentPlayerHandIndex ) {
                    handTitle += " (Active)";
                }

                embedBuilder.AddField( handTitle, hand?.ToString(), inline: true ); // Inline true can fit 2 hands side-by-side
            }
        }

        // Footer
        string footerText = "Your move...";
        if ( g.RoundFinished ) {
            footerText = "Round finished. Results below.";
        }
        else if ( currentPlayerHand != null ) {
            if ( g.PlayerHands.Count > 1 ) {
                footerText = $"Playing Hand {g.CurrentPlayerHandIndex + 1} of {g.PlayerHands.Count}. Your move...";
            }
        }
        else if ( g.PlayerHands.Count > 0 && g.CurrentPlayerHandIndex >= g.PlayerHands.Count ) {
            footerText = "Dealer's turn..."; // All player hands played
        }


        embedBuilder.WithFooter( footerText );
        if ( !g.RoundFinished && currentPlayerHand != null && currentPlayerHand.IsBust ) {
            embedBuilder.AddField( "\u200b", "**BUSTED!** This hand is over.", false ); // \u200b is zero-width space
        }


        return embedBuilder.Build();
    }
}