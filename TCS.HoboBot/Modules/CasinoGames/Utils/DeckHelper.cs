//
// DeckHelper + PlayingDeck + Shoe
// --------------------------------
// • DeckHelper.CreateDeck()  – builds one 52-card deck
// • DeckHelper.Shuffle()     – cryptographic Fisher–Yates, fluent
// • DeckHelper.PlayingDeck – single-deck draw-until-empty helper
// • Shoe – multi-deck shoe with cut-card penetration
//
using System.Security.Cryptography;
using HoldemPoker.Cards;
namespace TCS.HoboBot.Modules.CasinoGames.Utils;

public static class DeckHelper {
    /* ------------------------------------------------------------
     *  Core helpers
     * ---------------------------------------------------------- */

    /// <summary>Create a fresh 52-card deck.</summary>
    public static List<Card> CreateDeck() =>
        Enum.GetValues( typeof(CardType) ).Cast<CardType>()
            .SelectMany( t => Enum.GetValues( typeof(CardColor) )
                             .Cast<CardColor>()
                             .Select( c => new Card( t, c ) )
            )
            .ToList();

    /// <summary>In-place Fisher–Yates shuffle (cryptographic RNG); returns the same list for fluent chaining.</summary>
    public static IList<Card> Shuffle(this IList<Card> cards) {
        for (int n = cards.Count; n > 1; n--) {
            int k = RandomNumberGenerator.GetInt32( n ); // 0 ≤ k < n
            (cards[n - 1], cards[k]) = (cards[k], cards[n - 1]);
        }

        return cards;
    }
    
    public static Card Draw(this Deck card) => card.Draw();

    public static bool TryDraw(this Deck cards, out Card? card) => cards.TryDraw(out card);

    /* ------------------------------------------------------------
     *  Single-deck playing helper
     * ---------------------------------------------------------- */

    /// <summary>Factory: grab a fresh single deck ready for play.</summary>
    public static Deck NewDeck() => new();
}

public sealed class Deck {
    readonly List<Card> m_cards;
    int m_next; // index of the next card to deal

    internal Deck() {
        m_cards = DeckHelper.CreateDeck().Shuffle().ToList();
        m_next = 0;
    }

    /// <summary>Remaining cards before the next deal.</summary>
    public int CardsRemaining => m_cards.Count - m_next;

    /// <summary>
    /// Deal one card. If the previous deal exhausted the deck,
    /// we reshuffle automatically before serving the next card.
    /// </summary>
    public Card Draw() {
        if ( m_next >= m_cards.Count ) {
            throw new InvalidOperationException("No cards left to deal.");
        }

        return m_cards[m_next++];
    }
    
    /// <summary>
    /// Attempts to draw a card from the deck. If a card is available, it is returned
    /// via the `out` parameter and the method returns true. If no cards are left,
    /// the method returns false and the `out` parameter is set to null.
    /// </summary>
    /// <param name="card">
    /// An output parameter that will hold the drawn card if successful, or null if no cards are left.
    /// </param>
    /// <returns>
    /// True if a card was successfully drawn; false if the deck is empty.
    /// </returns>
    public bool TryDraw(out Card? card) {
        if ( m_next < m_cards.Count ) {
            card = m_cards[m_next++];
            return true;
        }

        card = null;
        return false;
    }

    /// <summary>Manual reshuffle (optional).</summary>
    public void Reshuffle() {
        m_cards.Shuffle();
        m_next = 0;
    }

    public bool NeedsShuffle() => m_next >= m_cards.Count;
}

/* ------------------------------------------------------------
 *  Multi-deck shoe with optional penetration (cut-card) logic
 * ---------------------------------------------------------- */

public sealed class Shoe {
    readonly List<Card> m_cards;
    int m_next;
    readonly double m_penetration; // e.g., 0.75 ⇒ shuffle at 75 %

    /// <param name="decks">Number of 52-card decks in the shoe (≥ 1).</param>
    /// <param name="penetration">
    /// Fraction (0 &lt; p ≤ 1) of the shoe that may be dealt before an automatic
    /// reshuffle. A typical casino cut-card is around 0.75.
    /// </param>
    public Shoe(int decks = 1, double penetration = 0.75) {
        if ( decks <= 0 ) {
            throw new ArgumentOutOfRangeException( nameof(decks), "Deck count must be positive." );
        }

        if ( penetration <= 0 || penetration > 1 ) {
            throw new ArgumentOutOfRangeException( nameof(penetration), "Penetration must be between 0 (exclusive) and 1 (inclusive)." );
        }

        m_penetration = penetration;
        m_cards = Enumerable.Range( 0, decks )
            .SelectMany( _ => DeckHelper.CreateDeck() )
            .ToList();
        m_cards.Shuffle();
        m_next = 0;
    }

    /// <summary>Remaining cards before the next reshuffle.</summary>
    public int CardsRemaining => m_cards.Count - m_next;

    /// <summary>Deal one card; reshuffles automatically once a penetration threshold is reached.</summary>
    public Card Draw() {
        if ( NeedsShuffle() ) {
            Reshuffle();
        }

        return m_cards[m_next++];
    }

    /// <summary>Manual reshuffle of the entire shoe.</summary>
    public void Reshuffle() {
        m_cards.Shuffle();
        m_next = 0;
    }

    bool NeedsShuffle() => m_next >= m_cards.Count * m_penetration;
}