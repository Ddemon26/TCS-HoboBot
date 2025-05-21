using System.Globalization;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using TCS.HoboBot.Data;

namespace TCS.HoboBot.Modules.CasinoGames;

/// <summary>
/// A simple 3‑reel slot‑machine game that mirrors the UX patterns of <see cref="BlackJackModule"/>.
/// ────────────────────────────────────────────────────────────────────────────────
/// • /slots <bet>      – Pull the handle once.
/// • Spin Again button – Instant re‑spin with the **same** bet.
/// </summary>
public sealed class SlotMachineModule : InteractionModuleBase<SocketInteractionContext> {
    /* ─────────── symbols / wheels ─────────── */

    enum Icon { Cherry, Lemon, Orange, Plum, Bell, Hotdog, Bar, Seven }

    static readonly string[] WheelEmojis = {
        "🍒", // Cherry
        "🍋", // Lemon
        "🍊", // Orange
        "🍑", // Plum
        "🔔", // Bell
        "🌭", // Hot dog (hot dog)
        "🍷", // Bar (wine glass ≈ bar)
        "7️⃣", // Seven
    };

    const int REELS = 3;
    static readonly Random Rng = new();

    /* ─────────── payout table ───────────
     * returns the *multiplier* applied to the original bet.
     * Any multiplier ≤ 1 results in a loss or push.
     *
     *  7 7 7  → 100×
     *  BAR x5 →  50×
     * Hot dog x3 →  30×
     *  🔔 x3  →  20×
     *  fruit x3 (🍒🍋🍊🍑) → 10×
     *  two 7s             →   5×
     *  any two of a kind  →   2×
     *  otherwise          →   0×
     */
    static decimal Payout(Icon[] r) {
        bool allEqual = r[0] == r[1] && r[1] == r[2];
        bool twoEqual = r.GroupBy( i => i ).Any( g => g.Count() == 2 );
        bool twoSevens = r.Count( i => i == Icon.Seven ) == 2;
        bool allFruits = allEqual && r[0] is Icon.Cherry or Icon.Lemon or Icon.Orange or Icon.Plum;

        return allEqual switch {
            true when r[0] == Icon.Seven => 100m,
            true when r[0] == Icon.Bar => 50m,
            // hotdog
            true when r[0] == Icon.Hotdog => 30m,
            true when r[0] == Icon.Bell => 20m,
            true when allFruits => 10m,
            _ => twoSevens ? 5m : twoEqual ? 2m : 0m,
        };
    }

    /* ─────────── slash command ─────────── */

    [SlashCommand( "slots", "Pull a three‑reel slot machine." )]
    public async Task SlotsAsync(float bet) {
        // Validate initial bet (slash command only)
        if ( !ValidateBet( ref bet, out string? error ) ) {
            await RespondAsync( error );
            return;
        }

        PlayersWallet.SubtractFromBalance( Context.User.Id, bet );
        await SpinAndRespondAsync( bet, isFollowUp: false );
    }

    /* ─────────── button interaction ─────────── */

    [ComponentInteraction( "slots_again_*" )]
    public async Task OnSpinAgain(string rawBet) {
        // Defer so we can later modify the original response.
        await DeferAsync( ephemeral: true );

        if ( !float.TryParse( rawBet, NumberStyles.Float, CultureInfo.InvariantCulture, out float bet ) ) {
            await Context.Interaction.ModifyOriginalResponseAsync( m => {
                    m.Content = "Invalid bet format.";
                    m.Components = new ComponentBuilder().Build();
                }
            );
            return;
        }

        // Quick funds check – user might have run out between spins.
        if ( !ValidateBet( ref bet, out string? error ) ) {
            await Context.Interaction.ModifyOriginalResponseAsync( m => {
                    m.Content = error;
                    m.Embed = new EmbedBuilder()
                        .WithTitle( "Slots – Game Over" )
                        .WithDescription( $"{Context.User.Mention} has ended the game due to insufficient funds." )
                        .Build();
                    m.Components = new ComponentBuilder().Build();
                }
            );
            return;
        }

        PlayersWallet.SubtractFromBalance( Context.User.Id, bet );
        await SpinAndRespondAsync( bet, isFollowUp: true );
    }

    [ComponentInteraction( "slots_end_*" )]
    public async Task OnEnd() {
        //close the interaction
        await DeferAsync( ephemeral: true );
        await Context.Interaction.ModifyOriginalResponseAsync( m => {
                m.Embed = new EmbedBuilder()
                    .WithTitle( "Slots – Game Over" )
                    .WithDescription( $"{Context.User.Mention} has ended the game." )
                    .Build();
                m.Components = new ComponentBuilder().Build();
            }
        );
    }

    /* ─────────── helpers ─────────── */

// Update ValidateBet to pass the bet by reference.
    bool ValidateBet(ref float bet, out string? error) {
        error = null;
        if ( bet <= 0 ) {
            error = "Bet must be positive.";
            return false;
        }

        // If a bet is greater than max, set it to max (10)
        if ( bet > 10 ) {
            bet = 10;
        }

        if ( PlayersWallet.GetBalance( Context.User.Id ) < bet ) {
            error = $"{Context.User.Mention} does’t have enough cash!";
            return false;
        }

        return true;
    }

    async Task SpinAndRespondAsync(float bet, bool isFollowUp) {
        Icon[] spin = SpinReels();
        decimal mult = Payout( spin );
        if ( mult > 0 ) {
            PlayersWallet.AddToBalance( Context.User.Id, bet * (float)mult );
        }

        var embed = BuildEmbed( Context.User, spin, bet, mult );
        var buttons = new ComponentBuilder()
            .WithButton(
                "Spin Again",
                $"slots_again_{bet.ToString( CultureInfo.InvariantCulture )}",
                style: ButtonStyle.Primary
            )
            .WithButton(
                "End",
                $"slots_end_{bet.ToString( CultureInfo.InvariantCulture )}",
                style: ButtonStyle.Danger
            );

        if ( isFollowUp ) {
            // interaction already deferred → we edit the original ephemeral message
            await Context.Interaction.ModifyOriginalResponseAsync( m => {
                    m.Embed = embed;
                    m.Components = buttons.Build();
                }
            );
        }
        else {
            await RespondAsync( embed: embed, components: buttons.Build(), ephemeral: true );
        }

        await AnnounceResult( mult, bet );
    }

    static Icon[] SpinReels() => Enumerable.Range( 0, REELS )
        .Select( _ => (Icon)Rng.Next( Enum.GetValues( typeof(Icon) ).Length ) )
        .ToArray();

    static Embed BuildEmbed(SocketUser u, Icon[] r, float bet, decimal mult) {
        string line = string.Join( " ", r.Select( i => WheelEmojis[(int)i] ) );
        string outcome = mult switch {
            0m => $"lost **${bet:0.00}**",
            _ => $"won **${bet * (float)(mult - 1):0.00}**",
        };

        return new EmbedBuilder()
            .WithTitle( $"Slots – ${bet:0.00} bet" )
            .WithDescription( $"{u.Mention} pulls the handle…\n**{line}**" )
            .WithFooter( mult > 0 ? $"Congratulations! You {outcome}." : $"Unlucky! You {outcome}." )
            .Build();
    }

    async Task AnnounceResult(decimal mult, float bet) {
        if ( mult < 5m ) {
            return;
        }

        var msg = $"{Context.User.Mention} wins **${bet * (float)(mult - 1):0.00}** on the slots!";
        await Context.Channel.SendMessageAsync( msg );
    }
}