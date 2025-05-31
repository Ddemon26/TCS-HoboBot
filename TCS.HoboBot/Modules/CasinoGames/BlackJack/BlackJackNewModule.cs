using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using HoldemPoker.Cards;
using TCS.HoboBot.Data;
using TCS.HoboBot.Modules.CasinoGames.Utils;
namespace TCS.HoboBot.Modules.CasinoGames.BlackJack;

public class BlackjackHand {
    readonly List<Card> m_cards = [];
    public IReadOnlyList<Card> Cards => m_cards;
    public void Add(Card c) => m_cards.Add( c );

    public static int GetCardPoints(Card c) {
        return c.Type switch {
            CardType.Deuce => 2,
            CardType.Three => 3,
            CardType.Four => 4,
            CardType.Five => 5,
            CardType.Six => 6,
            CardType.Seven => 7,
            CardType.Eight => 8,
            CardType.Nine => 9,
            CardType.Ten => 10,
            CardType.Jack => 10,
            CardType.Queen => 10,
            CardType.King => 10,
            CardType.Ace => 11,
            _ => 0,
        };
    }

    IEnumerable<int> PossibleTotals {
        get {
            int hardTotal = m_cards.Sum( GetCardPoints );
            int aces = m_cards.Count( c => c.Type == CardType.Ace );
            for (var soft = 0; soft <= aces; soft++)
                yield return hardTotal - soft * 10;
        }
    }
    public int Total => PossibleTotals.Where( t => t <= 21 )
        .DefaultIfEmpty( PossibleTotals.Min() )
        .Max();
    public bool IsBlackjack => m_cards.Count == 2 && Total == 21;
    public bool IsBust => Total > 21;

    public override string ToString() =>
        string.Join( " ", m_cards.Select( c => c.ToString() ) ) + $"  ({Total})";
}

public class BlackjackGame {
    readonly Shoe m_shoe;
    public List<BlackjackHand?> PlayerHands { get; } = []; // Player can have multiple hands
    public List<float> BetsPerHand { get; } = []; // Bet for each hand
    public BlackjackHand Dealer { get; } = new();

    public bool RoundFinished { get; private set; } // True when all player hands and dealer hand are complete
    public int CurrentPlayerHandIndex { get; private set; } // Tracks which hand the player is currently playing

    bool m_canSplitCurrentHand;
    bool m_splitOccurredThisRound; // Global flag for the "split once" rule

    public BlackjackGame(Shoe shoe) {
        m_shoe = shoe;
    }

    public BlackjackHand? GetCurrentPlayerHand() {
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
        PlayerHands.Clear(); // Ensure a fresh start
        BetsPerHand.Clear();

        var firstHand = new BlackjackHand();
        PlayerHands.Add( firstHand );
        BetsPerHand.Add( initialBet );
        CurrentPlayerHandIndex = 0;

        // Deal initial cards
        firstHand.Add( m_shoe.Draw() );
        Dealer.Add( m_shoe.Draw() );
        firstHand.Add( m_shoe.Draw() );
        Dealer.Add( m_shoe.Draw() ); // Dealer's second card is dealt but might be hidden initially

        UpdateActionFlags();

        // Check for immediate Blackjacks
        if ( firstHand.IsBlackjack || Dealer.IsBlackjack ) {
            // If a player has blackjack, they can't do other actions.
            // If the dealer has blackjack, the game ends.
            // If both, it's a push.
            // Stand will automatically play dealer and finish.
            Stand();
        }
    }

    void UpdateActionFlags() {
        var currentHand = GetCurrentPlayerHand();
        if ( currentHand == null || RoundFinished ) {
            m_canSplitCurrentHand = false;
            return;
        }

        // Can only split/double on the first two cards of a hand
        bool isFirstTwoCards = currentHand.Cards.Count == 2;

        // Splitting:
        // 1. First two cards.
        // 2. Cards have the same type.
        // 3. No split has occurred yet in this entire round.
        m_canSplitCurrentHand = isFirstTwoCards &&
                                !m_splitOccurredThisRound &&
                                currentHand.Cards[0].Type == currentHand.Cards[1].Type;
    }

    public bool CanDoubleDown() {
        var currentHand = GetCurrentPlayerHand();
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

        var currentHand = GetCurrentPlayerHand();
        currentHand?.Add( m_shoe.Draw() );

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

    public bool DoubleDown(Action<float> chargePlayerCallback) // Callback to deduct an additional bet
    {
        var currentHand = GetCurrentPlayerHand();
        if ( !CanDoubleDown() || currentHand == null ) {
            return false;
        }

        float currentBet = GetCurrentBet();
        chargePlayerCallback( currentBet ); // Deduct the additional bet from player's wallet

        BetsPerHand[CurrentPlayerHandIndex] = currentBet * 2;
        currentHand.Add( m_shoe.Draw() );

        m_canSplitCurrentHand = false; // Cannot split after doubling
        AdvanceToNextHandOrPlayDealer();
        return true;
    }

    public bool Split(Action<float> chargePlayerCallback) // Callback for the new hand's bet
    {
        var currentHandToSplit = GetCurrentPlayerHand();
        if ( !CanSplit() || currentHandToSplit == null ) {
            return false;
        }

        float originalBet = GetCurrentBet();
        chargePlayerCallback( originalBet ); // Deduct bet for the new hand

        m_splitOccurredThisRound = true; // Mark that a split has happened

        // Create the new hand
        var newHand = new BlackjackHand();
        var cardToMove = currentHandToSplit.Cards[1]; // Take the second card

        // Modify current hand: remove second card, deal a new one
        // Need a way to remove a card from Hand or reconstruct. Let's reconstruct.
        var firstCardOfSplitHand = currentHandToSplit.Cards[0];
        PlayerHands[CurrentPlayerHandIndex] = new BlackjackHand(); // New hand object for the first split hand
        PlayerHands[CurrentPlayerHandIndex]?.Add( firstCardOfSplitHand );
        PlayerHands[CurrentPlayerHandIndex]?.Add( m_shoe.Draw() );

        // Setup the second split hand
        newHand.Add( cardToMove );
        newHand.Add( m_shoe.Draw() );

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

    void AdvanceToNextHandOrPlayDealer() {
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

    void PlayDealerHand() {
        // Dealer only plays if at least one player hand is not bust
        // (or if a player hand was a natural blackjack that hasn't been beaten by dealer blackjack yet)
        bool playerHasActiveHand = PlayerHands.Any( h => !(h is { IsBust: true }) || (h.IsBlackjack && !Dealer.IsBlackjack) );

        if ( playerHasActiveHand ) {
            while (Dealer.Total < 17 || (Dealer.Total == 17 && DealerHasSoft17())) {
                Dealer.Add( m_shoe.Draw() );
            }
        }

        RoundFinished = true;
    }

    bool DealerHasSoft17() {
        // Count Aces as 1 for the hard total, others via GetCardPoints
        int hardTotal = Dealer.Cards
            .Sum( c => c.Type == CardType.Ace
                      ? 1
                      : BlackjackHand.GetCardPoints( c )
            );

        int aceCount = Dealer.Cards.Count( c => c.Type == CardType.Ace );

        // If any Ace can be counted as 11 (adding 10) to make 17, it's a soft 17
        for (var i = 1; i <= aceCount; i++) {
            if ( hardTotal + i * 10 == 17 )
                return true;
        }

        return false;
    }


    // Settle needs to handle multiple hands and bets
    public List<(BlackjackHand playerHand, float bet, Outcome outcome, decimal multiplier)> Settle() {
        if ( !RoundFinished ) {
            throw new InvalidOperationException( "Round not finished." );
        }

        List<(BlackjackHand, float, Outcome, decimal)> results = [];
        bool dealerBj = Dealer.IsBlackjack;

        for (var i = 0; i < PlayerHands.Count; i++) {
            var playerHand = PlayerHands[i];
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
public class GameSession {
    public BlackjackGame Game { get; }
    public float InitialBet { get; } // This was the original bet amount
    public DateTimeOffset Started { get; } = DateTimeOffset.UtcNow;
    public GameSession(BlackjackGame game, float initialBet) {
        Game = game;
        InitialBet = initialBet;
    }
}

public class BlackJackNewModule : InteractionModuleBase<SocketInteractionContext> {
    const float MAX_BET = 10000;
    const float MIN_BET = 0;
    static readonly ConcurrentDictionary<ulong, GameSession> ActiveGames = new();
    static readonly Shoe SharedShoe = new(6); // 6-deck shoe

    [SlashCommand( "blackjack", "Play an interactive blackjack hand vs the house." )]
    public async Task BlackjackAsync(float bet) {
        if ( bet <= MIN_BET ) {
            await RespondAsync( "Bet must be positive.", ephemeral: true );
            return;
        }

        if ( bet > MAX_BET ) {
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

    // [ComponentInteraction( "bj_playagain_*" )]
    // public async Task OnPlayAgain(string userIdRaw) {
    //     if ( !ValidateUserInteraction( userIdRaw, out var session ) ) {
    //         await DeferAsync();
    //         return;
    //     }
    //
    //     // Reset the game state for a new hand
    //     ActiveGames.TryRemove( Context.User.Id, out _ );
    //     await BlackjackAsync( session.InitialBet ); // Reuse the initial bet to start a new game
    // }

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

        // Use ModifyOriginalResponseAsync for the following updates from button clicks
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
            List<(BlackjackHand playerHand, float bet, BlackjackGame.Outcome outcome, decimal multiplier)> settlementResults = game.Settle();
            List<string> summaryLines = [];
            decimal totalNetPlayerGain = 0; // Tracks net gain/loss relative to initial bets placed.

            for (var i = 0; i < settlementResults.Count; i++) {
                var result = settlementResults[i];
                string handIdentifier = game.PlayerHands.Count > 1 ? $"Hand {i + 1} ({result.playerHand.ToString()}) " : "";
                string handSummary;

                float payoutAmount = result.bet * (float)result.multiplier;
                float netGainForHand = payoutAmount - result.bet; // How much more (or less) than the bet was returned

                if ( result.multiplier > 0 ) // If any payout (push or win)
                {
                    PlayersWallet.AddToBalance( Context.Guild.Id, Context.User.Id, payoutAmount );
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
                    case BlackjackGame.Outcome.Push:
                    default: // Push
                        handSummary = $"{handIdentifier}pushes bet of **${result.bet:0.00}** (Bet Returned)";
                        break;
                }

                summaryLines.Add( handSummary );
            }

            string finalSummaryMessage = $"{Context.User.Mention}'s Blackjack Results:\n" + string.Join( "\n", summaryLines );
            if ( game.PlayerHands.Count > 1 || totalNetPlayerGain != (decimal)(s.InitialBet * (float)1.5m) && totalNetPlayerGain != (decimal)s.InitialBet && totalNetPlayerGain != (decimal)-s.InitialBet && totalNetPlayerGain != 0 ) {
                finalSummaryMessage += totalNetPlayerGain switch {
                    // Add overall only if complex
                    > 0 => $"\n**Overall Net Gain: ${totalNetPlayerGain:0.00}**",
                    < 0 => $"\n**Overall Net Loss: ${Math.Abs( totalNetPlayerGain ):0.00}**",
                    _ => "\n**Overall: Broke Even** (excluding pushes on original bet).",
                };
            }


            // Send a public message to the channel with the results.
            await Context.Channel.SendMessageAsync( finalSummaryMessage );
            ActiveGames.TryRemove( Context.User.Id, out _ );
        }
    }

    static ComponentBuilder GetButtons(ulong uid, BlackjackGame game) {
        var cb = new ComponentBuilder();
        if ( game.RoundFinished || game.GetCurrentPlayerHand() == null ) return cb;

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

        return cb;
    }

    static Embed BuildEmbed(SocketUser user, GameSession ses, bool showDealerHole) {
        var g = ses.Game;
        var embedBuilder = new EmbedBuilder();
        var currentPlayerHand = g.GetCurrentPlayerHand(); // Might be null if the round just ended before player action

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
            for (var i = 0; i < g.PlayerHands.Count; i++) {
                var hand = g.PlayerHands[i];
                float handBet = g.BetsPerHand[i]; // Need to ensure BetsPerHand is populated correctly
                var handTitle = $"Your Hand {(g.PlayerHands.Count > 1 ? (i + 1).ToString() : "")} - Bet: ${handBet:0.00}";
                if ( !g.RoundFinished && i == g.CurrentPlayerHandIndex ) {
                    handTitle += " (Active)";
                }

                embedBuilder.AddField( handTitle, hand?.ToString(), inline: true ); // Inline true can fit 2 hands side-by-side
            }
        }

        // Footer
        var footerText = "Your move...";
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
        if ( !g.RoundFinished && currentPlayerHand is { IsBust: true } ) {
            embedBuilder.AddField( "\u200b", "**BUSTED!** This hand is over.", false ); // \u200b is zero-width space
        }


        return embedBuilder.Build();
    }
}