using System.Globalization;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using TCS.HoboBot.Data;

namespace TCS.HoboBot.Modules.CasinoGames;

/// <summary>
/// Baccarat game module for Discord with side‑bets (Dragon Bonus &amp; Pair bets).
/// • /baccarat &lt;main_bet_amount&gt; &lt;player|banker|tie&gt; [side_bet_type] [side_bet_amount]
///   – starts a round, debits the wallet and shows a "Deal Cards" button.
/// • Deal Cards button – deals hands, evaluates the main bet + side‑bet(s) &amp; settles.
/// • End Game button – terminates interaction.
/// </summary>
public sealed class BaccaratModule : InteractionModuleBase<SocketInteractionContext> {
    /* ══════════════════════  Card&Game Elements ══════════════════════ */

    static readonly string[] SuitSymbols = ["♠", "♥", "♦", "♣"];
    static readonly string[] RankSymbols = [
        "?", "A", "2", "3", "4", "5", "6", "7", "8", "9", "10", "J", "Q", "K",
    ]; // Index 0 unused.

    static readonly Random Rng = new();
    const float MAX_MAIN_BET = 200f;
    const float MAX_SIDE_BET = 50f;

    /* ─────────── Bet types ─────────── */

    public enum BaccaratBetType {
        Player,
        Banker,
        Tie,
    }

    public enum SideBetType {
        None,
        PlayerDragon, // Dragon Bonus on Player hand
        BankerDragon, // Dragon Bonus on Banker hand
        PlayerPair,
        BankerPair,
        EitherPair,
    }

    readonly record struct Card(int Rank, int Suit) {
        public override string ToString() => $"{RankSymbols[Rank]}{SuitSymbols[Suit]}";
        public int BaccaratValue() => Rank >= 10 ? 0 : Rank; // 10, J, Q, K = 0
    }

    /* ══════════════════════  Slash Command  ══════════════════════ */

    [SlashCommand( "baccarat", "Play Baccarat with optional side‑bets." )]
    public async Task BaccaratInitialBetAsync(
        [Summary( "bet", "Main bet amount." )] float mainBet,
        [Summary( "bet_type", "Main bet: Player / Banker / Tie" )] BaccaratBetType betType,
        [Summary( "side_bet", "Side‑bet amount (defaults to 0)" )] float sideBetAmount = 0f,
        [Summary( "side_bet_type", "Optional side‑bet type" ),
         Choice( "None", 0 ), Choice( "PlayerDragon", 1 ),
         Choice( "BankerDragon", 2 ), Choice( "PlayerPair", 3 ),
         Choice( "BankerPair", 4 ), Choice( "EitherPair", 5 )]
        SideBetType sideBetType = SideBetType.None
    ) {
        float initialMainBet = mainBet;
        float initialSideBet = sideBetAmount;

        if ( !ValidateBets( ref mainBet, ref sideBetAmount, out string? error ) ) {
            await RespondAsync( error!, ephemeral: true );
            return;
        }

        // Debit wallet up‑front
        float totalDebit = mainBet + sideBetAmount;
        PlayersWallet.SubtractFromBalance( Context.User.Id, totalDebit );

        await RespondWithDealButtonAsync( mainBet, betType, sideBetType, sideBetAmount, initialMainBet, initialSideBet, isFollowUp: false );
    }

    /* ══════════════════════  Button Interactions  ══════════════════════ */

    // ID pattern: baccarat_deal_main, betType, sideType, sideBet
    [ComponentInteraction( "baccarat_deal_*,*,*,*" )]
    public async Task OnDealButtonAsync(string rawMainBet, string rawMainType, string rawSideType, string rawSideBet) {
        await DeferAsync( ephemeral: true );

        if ( !float.TryParse( rawMainBet, NumberStyles.Float, CultureInfo.InvariantCulture, out float mainBet ) ||
             !int.TryParse( rawMainType, out int mainTypeInt ) ||
             !Enum.IsDefined( typeof(BaccaratBetType), mainTypeInt ) ||
             !int.TryParse( rawSideType, out int sideTypeInt ) ||
             !Enum.IsDefined( typeof(SideBetType), sideTypeInt ) ||
             !float.TryParse( rawSideBet, NumberStyles.Float, CultureInfo.InvariantCulture, out float sideBet ) ) {

            await ModifyOriginalResponseAsync( m => {
                    m.Content = "Error processing game state – please start a new game.";
                    m.Components = new ComponentBuilder().Build();
                }
            );
            return;
        }

        await PerformDealAndRespondAsync( mainBet, (BaccaratBetType)mainTypeInt, (SideBetType)sideTypeInt, sideBet, isFollowUp: true );
    }

    [ComponentInteraction( "baccarat_end_*" )]
    public async Task OnEndGameAsync(string _) {
        await DeferAsync( ephemeral: true );
        await Context.Interaction.ModifyOriginalResponseAsync( m => {
                m.Embed = new EmbedBuilder()
                    .WithTitle( "Baccarat – Game Ended" )
                    .WithDescription( $"{Context.User.Mention} has ended the game." )
                    .Build();
                m.Components = new ComponentBuilder().Build();
            }
        );
    }

    /* ══════════════════════  Validation  ══════════════════════ */

    bool ValidateBets(ref float mainBet, ref float sideBet, out string? error) {
        error = null;
        if ( mainBet <= 0 ) {
            error = "Main bet must be positive.";
            return false;
        }

        if ( mainBet > MAX_MAIN_BET ) {
            mainBet = MAX_MAIN_BET;
        }

        if ( sideBet < 0 ) {
            sideBet = 0;
        }

        if ( sideBet > MAX_SIDE_BET ) {
            sideBet = MAX_SIDE_BET;
        }

        float totalNeeded = mainBet + sideBet;
        if ( PlayersWallet.GetBalance( Context.User.Id ) < totalNeeded ) {
            error = $"{Context.User.Mention} does’t have enough cash for that total bet (${totalNeeded:0.00}).";
            return false;
        }

        return true;
    }

    /* ══════════════════════  Deal & Outcome  ══════════════════════ */

    async Task PerformDealAndRespondAsync(float mainBet, BaccaratBetType betType, SideBetType sideBetType, float sideBetAmount, bool isFollowUp) {
        // Deal initial two cards each
        List<Card> playerHand = [DealCard(), DealCard()];
        List<Card> bankerHand = [DealCard(), DealCard()];

        int playerTotal = HandTotal( playerHand );
        int bankerTotal = HandTotal( bankerHand );
        bool natural = playerTotal >= 8 || bankerTotal >= 8;

        Card? playerThird = null;
        Card? bankerThird = null;

        if ( !natural ) {
            if ( playerTotal <= 5 ) {
                playerThird = DealCard();
                playerHand.Add( playerThird.Value );
                playerTotal = HandTotal( playerHand );
            }

            if ( ShouldBankerDraw( bankerTotal, playerThird ) ) {
                bankerThird = DealCard();
                bankerHand.Add( bankerThird.Value );
                bankerTotal = HandTotal( bankerHand );
            }
        }

        /* ─────────── Evaluate Main Bet ─────────── */
        decimal mainPayoutMultiplier;
        string mainOutcomeMsg;

        if ( playerTotal == bankerTotal ) {
            if ( betType == BaccaratBetType.Tie ) {
                mainPayoutMultiplier = 9m; // 8:1 + stake
                mainOutcomeMsg = "Tie! Tie bet wins.";
            }
            else {
                mainPayoutMultiplier = 1m; // push
                mainOutcomeMsg = "Tie! Main bet returned.";
            }
        }
        else if ( playerTotal > bankerTotal ) {
            if ( betType == BaccaratBetType.Player ) {
                mainPayoutMultiplier = 2m; // 1:1
                mainOutcomeMsg = "Player wins!";
            }
            else {
                mainPayoutMultiplier = 0m;
                mainOutcomeMsg = betType == BaccaratBetType.Banker ? "Player wins – Banker bet loses." : "Player wins – Tie bet loses.";
            }
        }
        else {
            // Banker wins
            if ( betType == BaccaratBetType.Banker ) {
                mainPayoutMultiplier = 1.95m; // 1:1 minus 5% commission
                mainOutcomeMsg = "Banker wins! (5% commission).";
            }
            else {
                mainPayoutMultiplier = 0m;
                mainOutcomeMsg = betType == BaccaratBetType.Player ? "Banker wins – Player bet loses." : "Banker wins – Tie bet loses.";
            }
        }

        /* ─────────── Evaluate Side Bet ─────────── */
        decimal sidePayoutMultiplier = 0m; // 0 = lost; 1 = push; >1 = win incl stake
        string sideOutcomeMsg = sideBetType == SideBetType.None ? "" : "Side‑bet loses.";

        if ( sideBetType != SideBetType.None && sideBetAmount <= 0 ) {
            sideOutcomeMsg = "(Side‑bet invalid / stake 0)";
        }
        else if ( sideBetType != SideBetType.None ) {
            switch (sideBetType) {
                case SideBetType.PlayerPair:
                    bool playerPair = playerHand[0].Rank == playerHand[1].Rank;
                    if ( playerPair ) {
                        sidePayoutMultiplier = 12m; // 11:1 + stake
                        sideOutcomeMsg = "Player Pair! Side‑bet wins.";
                    }

                    break;
                case SideBetType.BankerPair:
                    bool bankerPair = bankerHand[0].Rank == bankerHand[1].Rank;
                    if ( bankerPair ) {
                        sidePayoutMultiplier = 12m;
                        sideOutcomeMsg = "Banker Pair! Side‑bet wins.";
                    }

                    break;
                case SideBetType.EitherPair:
                    bool eitherPair = playerHand[0].Rank == playerHand[1].Rank || bankerHand[0].Rank == bankerHand[1].Rank;
                    if ( eitherPair ) {
                        sidePayoutMultiplier = 6m; // 5:1 + stake
                        sideOutcomeMsg = "Pair detected! Side‑bet wins.";
                    }

                    break;
                case SideBetType.PlayerDragon:
                case SideBetType.BankerDragon:
                    bool playerSide = sideBetType == SideBetType.PlayerDragon;
                    int winDiff = Math.Abs( playerTotal - bankerTotal );
                    bool sideWinsMain = (playerSide && playerTotal > bankerTotal) || (!playerSide && bankerTotal > playerTotal);
                    if ( sideWinsMain && winDiff >= 4 ) {
                        sidePayoutMultiplier = DragonBonusMultiplier( winDiff );
                        sideOutcomeMsg = $"Dragon Bonus! Win by {winDiff}.";
                    }
                    else if ( sideWinsMain && natural && winDiff == 0 ) {
                        sidePayoutMultiplier = 0m; // push lost (natural tie)
                        sideOutcomeMsg = "Natural tie – Dragon pushes.";
                    }

                    break;
            }
        }

        /* ─────────── Wallet Settlement ─────────── */
        if ( mainPayoutMultiplier > 0m ) {
            PlayersWallet.AddToBalance( Context.User.Id, mainBet * (float)mainPayoutMultiplier );
        }

        if ( sidePayoutMultiplier > 0m ) {
            PlayersWallet.AddToBalance( Context.User.Id, sideBetAmount * (float)sidePayoutMultiplier );
        }
        else if ( sidePayoutMultiplier == 1m ) {
            PlayersWallet.AddToBalance( Context.User.Id, sideBetAmount ); // push returned
        }

        // Announce big win
        float netWin = (mainBet * ((float)mainPayoutMultiplier - 1)) + (sideBetAmount * ((float)sidePayoutMultiplier - 1));
        if ( netWin >= 10f ) {
            await AnnounceBaccaratWin( netWin );
        }

        // Build & push embed
        await RespondWithDealButtonAsync( mainBet, betType, sideBetType, sideBetAmount, mainBet, sideBetAmount, isFollowUp, mainOutcomeMsg, sideOutcomeMsg, playerHand, bankerHand, gameConcluded: true );
    }

    /* ══════════════════════  Helpers  ══════════════════════ */

    Card DealCard() => new(Rng.Next( 1, 14 ), Rng.Next( 0, 4 ));

    static int HandTotal(List<Card> hand) => hand.Sum( c => c.BaccaratValue() ) % 10;

    static bool ShouldBankerDraw(int bankerTotal, Card? playerThird) {
        if ( bankerTotal >= 7 ) {
            return false;
        }

        if ( bankerTotal <= 2 ) {
            return true;
        }

        if ( playerThird == null ) {
            return bankerTotal <= 5;
        }

        int p = playerThird.Value.BaccaratValue();
        return bankerTotal switch {
            3 => p != 8,
            4 => p is >= 2 and <= 7,
            5 => p is >= 4 and <= 7,
            6 => p is 6 or 7,
            _ => false,
        };
    }

    static decimal DragonBonusMultiplier(int diff) => diff switch {
        4 => 2m, // 1:1 + stake
        5 => 3m,
        6 => 5m,
        7 => 7m,
        8 => 11m,
        9 => 31m,
        _ => 0m,
    };

    async Task RespondWithDealButtonAsync(float mainBet, BaccaratBetType betType, SideBetType sideBetType, float sideBetAmount, float displayMain, float displaySide, bool isFollowUp, string? mainOutcome = null, string? sideOutcome = null, List<Card>? playerH = null, List<Card>? bankerH = null, bool gameConcluded = false) {
        var embed = BuildEmbed( Context.User, displayMain, betType, displaySide, sideBetType, mainOutcome, sideOutcome, playerH, bankerH, gameConcluded );
        var components = new ComponentBuilder();

        if ( !gameConcluded ) {
            string id = $"baccarat_deal_{mainBet.ToString( CultureInfo.InvariantCulture )},{(int)betType},{(int)sideBetType},{sideBetAmount.ToString( CultureInfo.InvariantCulture )}";
            components.WithButton( "Deal Cards", id, ButtonStyle.Primary );
        }

        components.WithButton( "End Game", $"baccarat_end_{Context.User.Id}", ButtonStyle.Danger );

        if ( isFollowUp ) {
            await Context.Interaction.ModifyOriginalResponseAsync( m => {
                    m.Embed = embed;
                    m.Components = components.Build();
                }
            );
        }
        else {
            await RespondAsync( embed: embed, components: components.Build(), ephemeral: true );
        }
    }

    static Embed BuildEmbed(SocketUser user, float mainBet, BaccaratBetType betType, float sideBet, SideBetType sideType, string? mainOutcome, string? sideOutcome, List<Card>? pH, List<Card>? bH, bool concluded) {
        var eb = new EmbedBuilder()
            .WithAuthor( user.ToString(), user.GetAvatarUrl() ?? user.GetDefaultAvatarUrl() )
            .WithTitle( $"Baccarat – ${mainBet:0.00} on {betType}{(sideType != SideBetType.None ? $", ${sideBet:0.00} on {sideType}" : "")}" );

        var desc = $"{user.Mention} is playing Baccarat!\n";

        if ( pH != null && bH != null ) {
            desc += $"\n**Player:** {string.Join( " ", pH )}  (Total {HandTotal( pH )})";
            desc += $"\n**Banker:** {string.Join( " ", bH )}  (Total {HandTotal( bH )})\n";
        }

        if ( mainOutcome != null ) {
            desc += $"\nMain: {mainOutcome}";
        }

        if ( sideOutcome != null && sideOutcome.Length > 0 ) {
            desc += $"\nSide: {sideOutcome}";
        }

        if ( !concluded ) {
            desc += "\n\nClick **Deal Cards** to deal.";
        }
        else {
            desc += "\n\nRound over – use `/baccarat` to play again.";
        }

        eb.WithDescription( desc );
        eb.WithColor( concluded ? OutcomeToColor( mainOutcome ) : Color.Purple );
        if ( concluded ) {
            eb.WithFooter( "Round Concluded" );
        }

        return eb.Build();
    }

    static Color OutcomeToColor(string? outcome) {
        if ( outcome == null ) {
            return Color.Default;
        }

        if ( outcome.Contains( "wins" ) && !outcome.Contains( "loses" ) ) {
            return Color.Green;
        }

        if ( outcome.Contains( "loses" ) ) {
            return Color.Red;
        }

        if ( outcome.Contains( "returned" ) ) {
            return Color.LightGrey;
        }

        return Color.Default;
    }

    async Task AnnounceBaccaratWin(float net) {
        if ( net >= 10f ) {
            await Context.Channel.SendMessageAsync( $"🎉 {Context.User.Mention} just won **${net:0.00}** in Baccarat!" );
        }
    }
}