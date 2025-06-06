﻿using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using System.Globalization;
using TCS.HoboBot.Data;
using TCS.HoboBot.Modules.CasinoGames.Slots;

namespace TCS.HoboBot.Modules.CasinoGames {
    public abstract class BaseSlotMachineModule<TSymbol> : InteractionModuleBase<SocketInteractionContext> where TSymbol : Enum {
        protected static readonly Random Rng = new();

        // Abstract properties to be implemented by derived classes
        protected abstract string GameName { get; }
        protected abstract string GameCommandPrefix { get; } // Used for button IDs, e.g., "slots"
        //protected abstract IReadOnlyList<string> SymbolToEmojiMap { get; } // Index by TSymbol cast to int
        protected abstract  IReadOnlyDictionary<TSymbol, string> IconToEmojiMap { get; } // Maps TSymbol to emoji strings
        //protected abstract IReadOnlyList<TSymbol> Symbols { get; } // All possible symbols
        protected abstract int NumberOfReels { get; }
        protected virtual int NumberOfRows => 1; // Default for non-grid slots
        protected virtual float MaxBet => 10f;
        protected virtual float MinBet => 0.25f;

        // Abstract methods for game-specific logic
        public abstract TSymbol[][] SpinReelsInternal();
        public abstract (decimal payoutMultiplier, string winDescription) CalculatePayoutInternal(TSymbol[][] currentSpin, float bet);
        protected abstract Embed BuildGameEmbedInternal(SocketUser user, TSymbol[][] currentSpin, float bet, decimal payoutMultiplier, string winDescription, decimal totalWinnings);
        
        protected string GetEmojiForSymbol(TSymbol symbol) => IconToEmojiMap.GetValueOrDefault( symbol, "❓" ); // Fallback emoji if not found

        protected virtual async Task PlaySlotsAsync(float bet, bool isSpinAgainRequest = false, SocketInteraction? interactionToModify = null) {
            float processingBet = bet; // Use a local variable for processing
            if ( !isSpinAgainRequest ) {
                if ( !ValidateBet( ref processingBet, out string? error ) ) // processingBet might be capped by ValidateBet
                {
                    await RespondAsync( error, ephemeral: true );
                    return;
                }
            }
            else if ( interactionToModify != null ) // This means it's a button interaction for spin again
            {
                if ( PlayersWallet.GetBalance( Context.Guild.Id, Context.User.Id ) < processingBet ) {
                    var error = $"{Context.User.Mention} doesn't have enough cash for another spin at ${processingBet:0.00}!";
                    await interactionToModify.ModifyOriginalResponseAsync( m => {
                            m.Content = error;
                            m.Embed = new EmbedBuilder()
                                .WithTitle( $"{GameName} – Game Over" )
                                .WithDescription( $"{Context.User.Mention} has ended the game due to insufficient funds." )
                                .Build();
                            m.Components = new ComponentBuilder().Build();
                        }
                    );
                    return;
                }
            }

            PlayersWallet.SubtractFromBalance( Context.Guild.Id, Context.User.Id, processingBet );
            await SpinAndRespondAsync( processingBet, isSpinAgainRequest || interactionToModify != null, interactionToModify );
        }

        protected virtual async Task HandleSpinAgainAsync(string rawBetFromButton) {
            // Defer must be called by the method with the [ComponentInteraction] attribute.
            // await DeferAsync(ephemeral: true); // Call DeferAsync in the actual button handler in derived class

            if ( !float.TryParse( rawBetFromButton, NumberStyles.Float, CultureInfo.InvariantCulture, out float bet ) ) {
                await Context.Interaction.ModifyOriginalResponseAsync( m => {
                        m.Content = "Invalid bet format in button.";
                        m.Components = new ComponentBuilder().Build();
                    }
                );
                return;
            }

            // Pass Context.Interaction for modification
            await PlaySlotsAsync( bet, isSpinAgainRequest: true, interactionToModify: Context.Interaction );
        }

        protected virtual async Task HandleEndGameAsync() {
            // await DeferAsync(ephemeral: true); // Call DeferAsync in the actual button handler in derived class
            await Context.Interaction.ModifyOriginalResponseAsync( m => {
                    m.Embed = new EmbedBuilder()
                        .WithTitle( $"{GameName} – Game Over" )
                        .WithDescription( $"{Context.User.Mention} has ended the game." )
                        .Build();
                    m.Components = new ComponentBuilder().Build();
                }
            );
        }

        protected virtual bool ValidateBet(ref float bet, out string? error) {
            error = null;
            if ( bet < MinBet ) {
                error = $"Bet must be at least ${MinBet:0.00}.";
                return false;
            }

            if ( bet > MaxBet ) {
                // Silently cap the bet to MaxBet
                bet = MaxBet;
            }

            if ( PlayersWallet.GetBalance( Context.Guild.Id, Context.User.Id ) < bet ) {
                error = $"{Context.User.Mention} does’t have enough cash! Your balance is ${PlayersWallet.GetBalance( Context.Guild.Id, Context.User.Id ):C2}. You tried to bet ${bet:C2}.";
                return false;
            }

            return true;
        }

        protected virtual async Task SpinAndRespondAsync(float bet, bool isFollowUpOrButton, SocketInteraction? interactionToModify = null) {
            TSymbol[][] spinResult = SpinReelsInternal();
            (decimal payoutMultiplier, string winDescription) = CalculatePayoutInternal( spinResult, bet );
            var totalWinningsValue = 0m; // This is the total amount returned for the spin (bet * multiplier)

            if ( payoutMultiplier > 0 ) {
                totalWinningsValue = (decimal)bet * payoutMultiplier;

                if ( CasinoManager.GetJackpot( Context.Guild.Id, bet, out var type, out float jackpotValue ) ) {
                    winDescription += $"\n\n🎉 YOU hit the **{type} Jackpot** of **{jackpotValue:C2}!**";
                    // Announce the jackpot win
                    totalWinningsValue += (decimal)jackpotValue;
                    var msg = $"🎉 {Context.User.Mention} has hit the **{type} Jackpot** of **{jackpotValue:C2}** on {GameName}!";
                    await Context.Channel.SendMessageAsync( msg ); // Send it as a new message to the channel
                }

                PlayersWallet.AddToBalance( Context.Guild.Id, Context.User.Id, (float)totalWinningsValue );
            }
            else {
                CasinoManager.AddToSlotsJackpots( Context.Guild.Id, bet );
            }

            var embed = BuildGameEmbedInternal( Context.User, spinResult, bet, payoutMultiplier, winDescription, totalWinningsValue );
            var buttons = new ComponentBuilder()
                .WithButton( "Spin Again", $"{GameCommandPrefix}_again_{bet.ToString( CultureInfo.InvariantCulture )}", style: ButtonStyle.Primary )
                .WithButton( "End", $"{GameCommandPrefix}_end", style: ButtonStyle.Danger );

            if ( isFollowUpOrButton && interactionToModify != null ) {
                await interactionToModify.ModifyOriginalResponseAsync( m => {
                        m.Embed = embed;
                        m.Components = buttons.Build();
                    }
                );
            }
            else {
                await RespondAsync( embed: embed, components: buttons.Build(), ephemeral: true );
            }

            // Announce significant wins publicly
            // A multiplier of >= 5x the bet is considered significant.
            if ( payoutMultiplier >= 5m ) {
                decimal profitAmount = totalWinningsValue - (decimal)bet;
                if ( profitAmount > 0 ) // Only announce if there's actual profit
                {
                    await AnnouncePublicWin( Context.User, profitAmount, payoutMultiplier );
                }
            }
        }

        protected virtual async Task AnnouncePublicWin(SocketUser user, decimal profitAmount, decimal payoutMultiplier) {
            var msg = $"🎰 {user.Mention} wins **{profitAmount:C2}** with a multiplier of **{payoutMultiplier}x** on {GameName}!";
            await Context.Channel.SendMessageAsync( msg ); // Send as a new message to the channel
        }
    }
}