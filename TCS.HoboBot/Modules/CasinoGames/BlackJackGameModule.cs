using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using TCS.HoboBot.Data;

namespace TCS.HoboBot.Modules.CasinoGames;

public class BlackJackModule : InteractionModuleBase<SocketInteractionContext> {
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
        static readonly Dictionary<Suit, string> SuitGlyph = new() {
            [Suit.Clubs] = "♣",
            [Suit.Diamonds] = "♦",
            [Suit.Hearts] = "♥",
            [Suit.Spades] = "♠",
        };

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

        public override string ToString() => $"{RankText( Rank )}{SuitGlyph[Suit]}";
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
                foreach (Suit s in Enum.GetValues( typeof(Suit) ))
                foreach (Rank r in Enum.GetValues( typeof(Rank) ))
                    tmp.Add( new Card( r, s ) );

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
        public Hand Player { get; } = new();
        public Hand Dealer { get; } = new();
        public bool Finished { get; private set; }
        public BlackjackGame(Shoe shoe) { m_shoe = shoe; }

        public void InitialDeal() {
            Player.Add( m_shoe.Deal() );
            Dealer.Add( m_shoe.Deal() );
            Player.Add( m_shoe.Deal() );
            Dealer.Add( m_shoe.Deal() );
            if ( Player.IsBlackjack || Dealer.IsBlackjack ) {
                Stand(); // auto-resolve naturals
            }
        }

        public void Hit() {
            if ( Finished ) {
                return;
            }

            Player.Add( m_shoe.Deal() );
            if ( Player.IsBust ) {
                Stand();
            }
        }

        public void Stand() {
            if ( Finished ) {
                return;
            }

            while (Dealer.Total < 17 || (Dealer.Total == 17 && DealerHasSoft17()))
                Dealer.Add( m_shoe.Deal() );
            Finished = true;
        }

        bool DealerHasSoft17() {
            int hard = Dealer.Cards.Sum( c => c.Points );
            return Dealer.Total == 17 && hard != 17;
        }

        public (Outcome outcome, decimal multiplier) Settle() {
            if ( !Finished ) {
                throw new InvalidOperationException( "Round not finished." );
            }

            bool pBj = Player.IsBlackjack, dBj = Dealer.IsBlackjack;
            if ( pBj && dBj ) {
                return (Outcome.Push, 1);
            }

            if ( pBj ) {
                return (Outcome.PlayerWin, 2.5m); // 3 : 2
            }

            if ( dBj ) {
                return (Outcome.DealerWin, 0);
            }

            if ( Player.IsBust ) {
                return (Outcome.DealerWin, 0);
            }

            if ( Dealer.IsBust ) {
                return (Outcome.PlayerWin, 2);
            }

            if ( Player.Total > Dealer.Total ) {
                return (Outcome.PlayerWin, 2);
            }

            if ( Player.Total < Dealer.Total ) {
                return (Outcome.DealerWin, 0);
            }

            return (Outcome.Push, 1);
        }

        public enum Outcome { PlayerWin, DealerWin, Push }
    }

    /* ───────────  runtime state  ─────────── */

    static readonly Random Rng = new();
    static readonly Shoe SharedShoe = new(6, Rng); // 6-deck shoe
    static readonly ConcurrentDictionary<ulong, GameSession> ActiveGames = new();

    sealed class GameSession {
        public BlackjackGame Game { get; }
        public float Bet { get; }
        public DateTimeOffset Started { get; } = DateTimeOffset.UtcNow;
        public GameSession(BlackjackGame game, float bet) {
            Game = game;
            Bet = bet;
        }
    }

    /* ───────────  slash command  ─────────── */

    [SlashCommand( "blackjack", "Play an interactive blackjack hand vs the house." )]
    public async Task BlackjackAsync(float bet) {
        if ( bet <= 0 ) {
            await RespondAsync( "Bet must be positive." );
            return;
        }

        if ( bet > 10000 ) {
            await RespondAsync( "Max bet is $10,000." );
            return;
        }

        if ( ActiveGames.ContainsKey( Context.User.Id ) ) {
            await RespondAsync( "You already have a hand in progress! Finish it first." );
            return;
        }

        if ( PlayersWallet.GetBalance( Context.User.Id ) < bet ) {
            await RespondAsync( $"{Context.User.Mention} does’t have enough cash!" );
            return;
        }

        PlayersWallet.SubtractFromBalance( Context.User.Id, bet );

        var session = new GameSession( new BlackjackGame( SharedShoe ), bet );
        session.Game.InitialDeal();
        ActiveGames[Context.User.Id] = session;

        await DeferAsync( ephemeral: true );

        // ── single source of truth: every UI update (including the very first one)
        //    is funneled through UpdateGameMessage so settlement and cleanup always run
        await UpdateGameMessage( session );
    }

    /* ───────────  buttons  ─────────── */

    [ComponentInteraction( "bj_hit_*" )]
    public async Task OnHit(string userIdRaw) {
        if ( !ValidateUser( userIdRaw, out var session ) ) {
            await DeferAsync();
            return;
        }

        await DeferAsync();
        session.Game.Hit();
        await UpdateGameMessage( session );
    }

    [ComponentInteraction( "bj_stand_*" )]
    public async Task OnStand(string userIdRaw) {
        if ( !ValidateUser( userIdRaw, out var session ) ) {
            await DeferAsync();
            return;
        }

        await DeferAsync();
        session.Game.Stand();
        await UpdateGameMessage( session );
    }

    /* ───────────  helpers  ─────────── */

    bool ValidateUser(string raw, [NotNullWhen( true )] out GameSession? session) {
        session = null;
        return ulong.TryParse( raw, out ulong uid )
               && uid == Context.User.Id
               && ActiveGames.TryGetValue( uid, out session );
    }

    async Task UpdateGameMessage(GameSession s) {
        bool finished = s.Game.Finished;

        var embed = BuildEmbed( Context.User, s, showDealerHole: finished );
        var buttons = GetButtons( Context.User.Id, finished );

        await Context.Interaction.ModifyOriginalResponseAsync( m => {
                m.Embed = embed;
                m.Components = buttons.Build();
            }
        );

        if ( finished ) {
            (var outcome, decimal mult) = s.Game.Settle();
            if ( mult > 0 ) {
                PlayersWallet.AddToBalance( Context.User.Id, s.Bet * (float)mult );
            }

            string summary = outcome switch {
                BlackjackGame.Outcome.PlayerWin => $"{Context.User.Mention} wins **${s.Bet * (float)(mult - 1):0.00}** at blackjack!",
                BlackjackGame.Outcome.DealerWin => $"{Context.User.Mention} loses **${s.Bet:0.00}** at blackjack.",
                _ => $"{Context.User.Mention} pushes their blackjack bet of **${s.Bet:0.00}**.",
            };
            await Context.Channel.SendMessageAsync( summary );

            ActiveGames.TryRemove( Context.User.Id, out _ );
        }
    }

    static ComponentBuilder GetButtons(ulong uid, bool finished) {
        var cb = new ComponentBuilder();
        if ( !finished ) {
            cb.WithButton( "Hit", $"bj_hit_{uid}" )
                .WithButton( "Stand", $"bj_stand_{uid}", ButtonStyle.Danger );
        }

        return cb;
    }

    static Embed BuildEmbed(SocketUser user, GameSession ses, bool showDealerHole) {
        var g = ses.Game;
        string dealerText = showDealerHole
            ? g.Dealer.ToString()
            : $"{g.Dealer.Cards[0]} ??";

        return new EmbedBuilder()
            .WithTitle( $"Blackjack – ${ses.Bet:0.00} bet" )
            .WithDescription( $"{user.Mention} vs House\n\u200b" )
            .AddField( "Your hand", g.Player.ToString(), inline: false )
            .AddField( "Dealer", dealerText, inline: false )
            .WithFooter( showDealerHole ? "Round finished" : "Your move…" )
            .Build();
    }
}