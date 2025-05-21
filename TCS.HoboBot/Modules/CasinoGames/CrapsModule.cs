using System.Globalization;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;

// Assuming PlayersWallet exists in a similar fashion as for SlotMachineModule
// e.g., namespace YourBotNamespace;
// public static class PlayersWallet {
//     public static void AddToBalance(ulong userId, float amount) { /* ... */ }
//     public static void SubtractFromBalance(ulong userId, float amount) { /* ... */ }
//     public static float GetBalance(ulong userId) { /* ... */ return 0f; }
// }

/// <summary>
/// A simplified Craps game.
/// ────────────────────────────────────────────────────────────────────────────────
/// • /craps <bet_amount> <passline|dontpass> – Start a new game of Craps.
/// • Roll button – Roll the dice.
/// • End Game button - Stops the current game interaction.
/// </summary>
public sealed class CrapsModule : InteractionModuleBase<SocketInteractionContext> {
    /* ─────────── Dice & Game Elements ─────────── */

    static readonly string[] DiceEmojis = {
        "❔", // Placeholder for 0, should not happen with 2 dice.
        "⚀", // 1
        "⚁", // 2
        "⚂", // 3
        "⚃", // 4
        "⚄", // 5
        "⚅", // 6
    };

    static readonly Random Rng = new();
    const float MAX_BET_CRAPS = 100f; // Max bet for Craps

    public enum CrapsBetType {
        PassLine,
        DontPassLine,
    }

    /* ─────────── Slash Command ─────────── */

    [SlashCommand( "craps", "Play a game of Craps." )]
    public async Task CrapsInitialBetAsync(
        [Summary( "bet", "The amount you want to bet." )] float betAmount,
        [Summary( "bet_type", "Choose your bet type: Pass Line or Don't Pass Line." )] CrapsBetType betType
    ) {

        float initialBet = betAmount; // Store original for messages before capping
        if ( !ValidateBet( ref betAmount, out string? error ) ) {
            await RespondAsync( error!, ephemeral: true );
            return;
        }

        // Defer if wallet operations could be slow, though typically they are fast.
        // For consistency with slots when game actually starts, we can defer.
        // However, initial response is fine here before any game state is truly active.

        PlayersWallet.SubtractFromBalance( Context.User.Id, betAmount );

        // Initial roll is a Come-Out roll, so point is 0.
        await RespondWithRollButtonAsync(betAmount, initialBet, betType, 0, isFollowUp: false);  
    }

    /* ─────────── Button Interactions ─────────── */

    [ComponentInteraction( "craps_roll_*,*,*" )] // bet_amount,bet_type_int,point
    public async Task OnRollButtonAsync(string rawBetAmount, string rawBetType, string rawPoint) {
        await DeferAsync( ephemeral: true );

        if ( !float.TryParse( rawBetAmount, NumberStyles.Float, CultureInfo.InvariantCulture, out float betAmount ) ||
             !int.TryParse( rawBetType, out int betTypeInt ) ||
             !Enum.IsDefined( typeof(CrapsBetType), betTypeInt ) ||
             !int.TryParse( rawPoint, out int point ) ) {

            await ModifyOriginalResponseAsync( m => {
                    m.Content = "Error processing game state from button. Please start a new game.";
                    m.Components = new ComponentBuilder().Build();
                }
            );
            return;
        }

        var betType = (CrapsBetType)betTypeInt;
        float originalBetForDisplay = betAmount; // Already capped if needed by initial command

        // No need to ValidateBet amount here as it was validated and subtracted at the start or previous roll.
        // Fund check was done. If they spam click after losing, wallet will handle it or game ends.

        await PerformRollAndRespondAsync( betAmount, originalBetForDisplay, betType, point, isFollowUp: true );
    }

    [ComponentInteraction( "craps_end_*" )]
    public async Task OnEndGameAsync(string _) {
        // Parameter can be ignored if not used
        await DeferAsync( ephemeral: true );
        await Context.Interaction.ModifyOriginalResponseAsync( m => {
                m.Embed = new EmbedBuilder()
                    .WithTitle( "Craps – Game Ended" )
                    .WithDescription( $"{Context.User.Mention} has ended the game." )
                    .Build();
                m.Components = new ComponentBuilder().Build();
            }
        );
    }

    /* ─────────── Game Logic & Helpers ─────────── */

    bool ValidateBet(ref float bet, out string? error) {
        error = null;
        if ( bet <= 0 ) {
            error = "Bet must be positive.";
            return false;
        }

        if ( bet > MAX_BET_CRAPS ) {
            // Use Craps specific max bet
            // error = $"Bet exceeds maximum of ${MaxBetCraps:0.00}. Your bet has been adjusted.";
            // Optionally notify of adjustment, or just cap it. Slots caps it.
            bet = MAX_BET_CRAPS;
        }

        if ( PlayersWallet.GetBalance( Context.User.Id ) < bet ) {
            error = $"{Context.User.Mention} doesn’t have enough cash for that bet!";
            return false;
        }

        return true;
    }

    async Task RespondWithRollButtonAsync(float currentBet, float originalBetDisplay, CrapsBetType betType, int currentPoint, bool isFollowUp, string? diceRollMessage = null, string? outcomeMessage = null, bool gameConcluded = false) {
        var embed = BuildCrapsEmbed( Context.User, currentBet, originalBetDisplay, betType, currentPoint, diceRollMessage, outcomeMessage, gameConcluded );
        var components = new ComponentBuilder();

        if ( !gameConcluded ) {
            var buttonId = $"craps_roll_{currentBet.ToString( CultureInfo.InvariantCulture )},{(int)betType},{currentPoint}";
            components.WithButton( "Roll Dice", buttonId, ButtonStyle.Primary );
        }

        components.WithButton( "End Game", $"craps_end_{Context.User.Id}", ButtonStyle.Danger ); // Unique ID for end

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

    async Task PerformRollAndRespondAsync(float betAmount, float originalBetForDisplay, CrapsBetType betType, int currentPoint, bool isFollowUp) {
        int[] dice = RollTwoDice();
        int total = dice.Sum();
        var diceStr = $"{DiceEmojis[dice[0]]} {DiceEmojis[dice[1]]} (Total: {total})";

        var payoutMultiplier = 0m; // 0 = loss, 1 = push, 2 = 1:1 win (gets bet back + winnings)
        string outcomeMessage;
        var gameConcluded = false;
        int nextPoint = currentPoint;

        if ( currentPoint == 0 ) {
            // Come-Out Roll
            switch (total) {
                case 7:
                case 11:
                    if ( betType == CrapsBetType.PassLine ) {
                        payoutMultiplier = 2m;
                        outcomeMessage = "Natural! Pass Line wins!";
                    }
                    else {
                        payoutMultiplier = 0m;
                        outcomeMessage = "Seven Out on Come-Out! Don't Pass loses to a 7.";
                    }

                    gameConcluded = true;
                    break;
                case 2:
                case 3:
                    if ( betType == CrapsBetType.PassLine ) {
                        payoutMultiplier = 0m;
                        outcomeMessage = "Craps! Pass Line loses.";
                    }
                    else {
                        payoutMultiplier = 2m;
                        outcomeMessage = "Craps! Don't Pass wins!";
                    }

                    gameConcluded = true;
                    break;
                case 12:
                    if ( betType == CrapsBetType.PassLine ) {
                        payoutMultiplier = 0m;
                        outcomeMessage = "Craps! Pass Line loses.";
                    }
                    else {
                        payoutMultiplier = 1m;
                        outcomeMessage = "Twelve! Don't Pass pushes (bet returned).";
                    } // Push for Don't Pass on 12

                    gameConcluded = true;
                    break;
                default: // 4, 5, 6, 8, 9, 10
                    nextPoint = total;
                    outcomeMessage = $"Point is set to {nextPoint}! Roll for point.";
                    // Game continues, no payout change yet for the main bet.
                    break;
            }
        }
        else {
            // Point Roll
            if ( total == currentPoint ) {
                if ( betType == CrapsBetType.PassLine ) {
                    payoutMultiplier = 2m;
                    outcomeMessage = $"Hit Point {currentPoint}! Pass Line wins!";
                }
                else {
                    payoutMultiplier = 0m;
                    outcomeMessage = $"Hit Point {currentPoint}! Don't Pass loses.";
                }

                gameConcluded = true;
            }
            else if ( total == 7 ) {
                if ( betType == CrapsBetType.PassLine ) {
                    payoutMultiplier = 0m;
                    outcomeMessage = "Seven Out! Pass Line loses.";
                }
                else {
                    payoutMultiplier = 2m;
                    outcomeMessage = "Seven Out! Don't Pass wins!";
                }

                gameConcluded = true;
            }
            else {
                outcomeMessage = $"Rolled {total}. Point is still {currentPoint}. Roll again.";
                // Game continues
            }
        }

        if ( gameConcluded && payoutMultiplier > 0m ) {
            PlayersWallet.AddToBalance( Context.User.Id, betAmount * (float)payoutMultiplier );
        }

        if ( gameConcluded ) {
            // Announce if it was a winning outcome
            if ( payoutMultiplier > 1m ) {
                // Win (not push)
                float netWin = betAmount * ((float)payoutMultiplier - 1);
                outcomeMessage += $"\nYou won **${netWin:0.00}**!";
                await AnnounceCrapsWin( netWin );
            }
            else if ( payoutMultiplier == 0m ) {
                // Loss
                outcomeMessage += $"\nYou lost **${betAmount:0.00}**.";
            }
            else {
                // Push
                outcomeMessage += $"\nYour bet of **${betAmount:0.00}** was returned.";
            }
        }

        await RespondWithRollButtonAsync( betAmount, originalBetForDisplay, betType, nextPoint, isFollowUp, diceStr, outcomeMessage, gameConcluded );
    }


    static int[] RollTwoDice() {
        return new int[] { Rng.Next( 1, 7 ), Rng.Next( 1, 7 ) }; // 1-6
    }

    static Embed BuildCrapsEmbed(SocketUser user, float currentBet, float originalBetDisplay, CrapsBetType betType, int point, string? diceRollMessage, string? outcomeMessage, bool gameConcluded) {
        var embedBuilder = new EmbedBuilder()
            .WithAuthor( user.ToString(), user.GetAvatarUrl() ?? user.GetDefaultAvatarUrl() )
            .WithTitle( $"Craps – Bet: ${originalBetDisplay:0.00} on {FormatBetType( betType )}" );

        var description = $"{user.Mention} is playing Craps!\n";
        if ( point > 0 ) {
            description += $"**Point is: {point}**\n";
        }
        else if ( !gameConcluded ) {
            description += "**Come-Out Roll!**\n";
        }

        if ( !string.IsNullOrEmpty( diceRollMessage ) ) {
            description += $"\n🎲 Rolled: **{diceRollMessage}**\n";
        }

        if ( !string.IsNullOrEmpty( outcomeMessage ) ) {
            description += $"\n{outcomeMessage}\n";
        }
        else if ( !gameConcluded ) {
            description += "\nClick 'Roll Dice' to roll.";
        }

        if ( gameConcluded ) {
            description += "\nGame over. Use `/craps` to play again.";
            embedBuilder.WithFooter( "Game Concluded." );
            embedBuilder.WithColor( PayoutMultiplierToColor( diceRollMessage, outcomeMessage ) ); // Color based on win/loss
        }
        else {
            embedBuilder.WithColor( Color.Blue );
        }

        embedBuilder.WithDescription( description );
        return embedBuilder.Build();
    }

    // Helper to determine color based on outcome for the embed
    static Color PayoutMultiplierToColor(string? diceRoll, string? outcome) {
        if ( outcome == null ) {
            return Color.Default;
        }

        if ( outcome.Contains( "won" ) || outcome.Contains( "wins!" ) ) {
            return Color.Green;
        }

        if ( outcome.Contains( "lost" ) || outcome.Contains( "loses." ) ) {
            return Color.Red;
        }

        if ( outcome.Contains( "pushes" ) ) {
            return Color.LightGrey;
        }

        return Color.Default;
    }

    static string FormatBetType(CrapsBetType betType) {
        return betType switch {
            CrapsBetType.PassLine => "Pass Line",
            CrapsBetType.DontPassLine => "Don't Pass Line",
            _ => "Unknown Bet",
        };
    }

    async Task AnnounceCrapsWin(float netWinAmount) {
        // Announce if win is significant, e.g., > $10 or some other threshold
        if ( netWinAmount >= 10f ) {
            var msg = $"🎉 {Context.User.Mention} just won **${netWinAmount:0.00}** in Craps!";
            await Context.Channel.SendMessageAsync( msg );
        }
    }
}