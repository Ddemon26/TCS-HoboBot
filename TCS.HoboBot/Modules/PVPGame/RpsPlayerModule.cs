/*using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using TCS.HoboBot.Data; // Assuming this namespace from your original code for PlayersWallet and Cooldowns
using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace TCS.HoboBot.Modules.Util // Assuming this is your project's namespace
{
    public enum RpsChoice {
        Rock,
        Paper,
        Scissors,
    }

    public class RpsGameState {
        public RpsChoice ChallengerChoice { get; set; }
        public RpsChoice? OpponentChoice { get; set; } // Nullable until opponent makes a choice
        public required SocketUser Challenger { get; set; }
        public required SocketUser Opponent { get; set; }
        public float Bet { get; set; }
        public ulong GuildId { get; set; }

        public string GetOutcomeMessage() {
            if ( !OpponentChoice.HasValue ) {
                return "Error: Opponent has not made a choice yet.";
            }

            if ( ChallengerChoice == OpponentChoice.Value ) {
                return "It's a tie!";
            }

            bool challengerWins = (ChallengerChoice == RpsChoice.Rock && OpponentChoice.Value == RpsChoice.Scissors) ||
                                  (ChallengerChoice == RpsChoice.Paper && OpponentChoice.Value == RpsChoice.Rock) ||
                                  (ChallengerChoice == RpsChoice.Scissors && OpponentChoice.Value == RpsChoice.Paper);

            if ( challengerWins ) {
                return $"{Challenger.Mention} wins!";
            }
            else {
                return $"{Opponent.Mention} wins!";
            }
        }

        public SocketUser? GetWinner() {
            if ( !OpponentChoice.HasValue || ChallengerChoice == OpponentChoice.Value ) {
                return null; // Tie or game not finished
            }

            bool challengerWins = (ChallengerChoice == RpsChoice.Rock && OpponentChoice.Value == RpsChoice.Scissors) ||
                                  (ChallengerChoice == RpsChoice.Paper && OpponentChoice.Value == RpsChoice.Rock) ||
                                  (ChallengerChoice == RpsChoice.Scissors && OpponentChoice.Value == RpsChoice.Paper);
            return challengerWins ? Challenger : Opponent;
        }
    }

    public class RpsPlayerModule : InteractionModuleBase<SocketInteractionContext> {
        private static readonly ConcurrentDictionary<string, RpsGameState> ActiveGames = new();

        // Define Custom IDs for buttons
        private const string RpsAcceptPrefix = "rps_accept";
        private const string RpsDeclinePrefix = "rps_decline";
        private const string RpsOpponentChoicePrefix = "rps_opponent_choice";

        [SlashCommand( "rpsplayer", "Play Rock-Paper-Scissors against another user." )]
        public async Task RpsPlayerAsync(SocketUser opponent, RpsChoice choice, float bet) {
            if ( Context.User.Id == opponent.Id ) {
                await RespondAsync( "You cannot challenge yourself!", ephemeral: true );
                return;
            }

            if ( opponent.IsBot ) {
                await RespondAsync( "You cannot challenge a bot!", ephemeral: true );
                return;
            }

            if ( bet <= 0 ) {
                await RespondAsync( "You must bet a positive amount of cash!", ephemeral: true );
                return;
            }

            ulong guildId = Context.Guild.Id;
            ulong challengerId = Context.User.Id;
            ulong opponentId = opponent.Id;
            var now = DateTimeOffset.UtcNow;

            // ... (All your cooldown and wallet checks remain the same) ...

            if ( PlayersWallet.GetBalance( guildId, challengerId ) < bet ) {
                await RespondAsync( $"You don't have enough cash for this bet! Your balance: ${PlayersWallet.GetBalance( guildId, challengerId ):N2}", ephemeral: true );
                return;
            }

            if ( PlayersWallet.GetBalance( guildId, opponentId ) < bet ) {
                await RespondAsync( $"{opponent.Mention} doesn't have enough cash for this bet! Their balance: ${PlayersWallet.GetBalance( guildId, opponentId ):N2}", ephemeral: true );
                return;
            }

            string gameId = Guid.NewGuid().ToString();
            var gameState = new RpsGameState {
                Challenger = Context.User as SocketUser,
                Opponent = opponent,
                ChallengerChoice = choice, // Challenger's choice is stored immediately
                Bet = bet,
                GuildId = guildId
            };

            if ( !ActiveGames.TryAdd( gameId, gameState ) ) {
                await RespondAsync( "Failed to start the game due to a conflict. Please try again.", ephemeral: true );
                return;
            }

            var components = new ComponentBuilder()
                .WithButton( "Accept Challenge!", $"{RpsAcceptPrefix}:{gameId}", ButtonStyle.Success )
                .WithButton( "Decline", $"{RpsDeclinePrefix}:{gameId}", ButtonStyle.Danger )
                .Build();

            // Respond publicly with a challenge message that pings the opponent
            await RespondAsync(
                text: $"{opponent.Mention}, you have been challenged to Rock-Paper-Scissors by {Context.User.Mention} for **${bet:N2}**!",
                components: components,
                ephemeral: false // This message MUST be public for the opponent to see it.
            );
        }

        [ComponentInteraction( $"{RpsAcceptPrefix}:*" )]
        public async Task HandleChallengeAcceptAsync(string gameId) {
            if ( !ActiveGames.TryGetValue( gameId, out var gameState ) ) {
                await RespondAsync( "This game could not be found or has expired.", ephemeral: true );
                // Also update the original message to show it's expired.
                await ((SocketMessageComponent)Context.Interaction).Message.ModifyAsync( p => {
                        p.Content = "This challenge has expired and can no longer be accepted.";
                        p.Components = null;
                    }
                );
                return;
            }

            if ( Context.User.Id != gameState.Opponent.Id ) {
                await RespondAsync( "This challenge is not for you!", ephemeral: true );
                return;
            }

            // Disable buttons on the original challenge message
            var disabledComponents = new ComponentBuilder()
                .WithButton( "Accepted!", $"{RpsAcceptPrefix}:{gameId}", ButtonStyle.Success, disabled: true )
                .Build();
            await ((SocketMessageComponent)Context.Interaction).Message.ModifyAsync( p => p.Components = disabledComponents );

            var components = new ComponentBuilder()
                .WithButton( "Rock", $"{RpsOpponentChoicePrefix}:{gameId}:{RpsChoice.Rock}", ButtonStyle.Primary, emote: Emoji.Parse( "🪨" ) )
                .WithButton( "Paper", $"{RpsOpponentChoicePrefix}:{gameId}:{RpsChoice.Paper}", ButtonStyle.Primary, emote: Emoji.Parse( "📄" ) )
                .WithButton( "Scissors", $"{RpsOpponentChoicePrefix}:{gameId}:{RpsChoice.Scissors}", ButtonStyle.Primary, emote: Emoji.Parse( "✂️" ) )
                .Build();

            // **THE KEY STEP**: Respond ephemerally to the opponent who clicked the button.
            // This message with the choice buttons is now private to them.
            await RespondAsync( "Please make your choice. Your selection will be hidden.", components: components, ephemeral: true );
        }

        [ComponentInteraction( $"{RpsDeclinePrefix}:*" )]
        public async Task HandleChallengeDeclineAsync(string gameId) {
            if ( !ActiveGames.TryGetValue( gameId, out var gameState ) ) {
                await RespondAsync( "This game could not be found or has already expired.", ephemeral: true );
                return;
            }

            // Allow either the opponent or the original challenger to decline/cancel
            if ( Context.User.Id != gameState.Opponent.Id && Context.User.Id != gameState.Challenger.Id ) {
                await RespondAsync( "You cannot decline a challenge that is not for you.", ephemeral: true );
                return;
            }

            // Update the original message to show it was declined
            var originalMessage = ((SocketMessageComponent)Context.Interaction).Message;
            await originalMessage.ModifyAsync( p => {
                    p.Content = $"The challenge from {gameState.Challenger.Mention} to {gameState.Opponent.Mention} was declined by {Context.User.Mention}.";
                    p.Components = null; // Remove buttons
                }
            );

            ActiveGames.TryRemove( gameId, out _ ); // Clean up the cancelled game
            await DeferAsync(); // Acknowledge the interaction
        }

        [ComponentInteraction( $"{RpsOpponentChoicePrefix}:*,*" )]
        public async Task HandleOpponentChoiceAsync(string gameId, string choiceString) {
            if ( !Enum.TryParse<RpsChoice>( choiceString, out var opponentActualChoice ) ) {
                await RespondAsync( "Invalid choice selected.", ephemeral: true );
                return;
            }

            if ( !ActiveGames.TryGetValue( gameId, out var gameState ) ) {
                await RespondAsync( "This game was not found or has expired.", ephemeral: true );
                return;
            }

            if ( Context.User.Id != gameState.Opponent.Id ) {
                await RespondAsync( "This is not your game to respond to!", ephemeral: true );
                return;
            }

            if ( gameState.OpponentChoice.HasValue ) {
                await RespondAsync( "You have already made your choice for this game.", ephemeral: true );
                return;
            }

            gameState.OpponentChoice = opponentActualChoice;
            ulong guildId = gameState.GuildId;

            // Acknowledge the choice by updating the ephemeral message
            await ((SocketMessageComponent)Context.Interaction).UpdateAsync( p => {
                    p.Content = $"You chose **{opponentActualChoice}**. The results will be announced publicly.";
                    p.Components = null;
                }
            );

            // Set cooldowns now that the game is confirmed and played
            var now = DateTimeOffset.UtcNow;
            var cooldownDuration = TimeSpan.FromMinutes( 1 );
            Cooldowns.Set( guildId, gameState.Challenger.Id, CooldownKind.Versus, now + cooldownDuration );
            Cooldowns.Set( guildId, gameState.Opponent.Id, CooldownKind.Versus, now + cooldownDuration );

            // Determine winner and outcome
            SocketUser? winner = gameState.GetWinner();
            string outcomeSummary = gameState.GetOutcomeMessage();

            string finalMessage = $"**Rock-Paper-Scissors Result!**\n\n" +
                                  $"{gameState.Challenger.Mention} (Challenger) chose **{gameState.ChallengerChoice}**.\n" +
                                  $"{gameState.Opponent.Mention} (Opponent) chose **{gameState.OpponentChoice.Value}**.\n\n" +
                                  $"**{outcomeSummary}**\n";

            if ( winner != null ) {
                if ( winner.Id == gameState.Challenger.Id ) {
                    PlayersWallet.AddToBalance( guildId, gameState.Challenger.Id, gameState.Bet );
                    PlayersWallet.SubtractFromBalance( guildId, gameState.Opponent.Id, gameState.Bet );
                    finalMessage += $"\n{gameState.Challenger.Mention} wins **${gameState.Bet:N2}**!";
                }
                else // Opponent won
                {
                    PlayersWallet.AddToBalance( guildId, gameState.Opponent.Id, gameState.Bet );
                    PlayersWallet.SubtractFromBalance( guildId, gameState.Challenger.Id, gameState.Bet );
                    finalMessage += $"\n{gameState.Opponent.Mention} wins **${gameState.Bet:N2}**!";
                }
            }
            else // Tie
            {
                finalMessage += $"\nThe bet of **${gameState.Bet:N2}** is returned to both players.";
            }

            finalMessage += $"\n\n{gameState.Challenger.Mention}'s new balance: **${PlayersWallet.GetBalance( guildId, gameState.Challenger.Id ):N2}**";
            finalMessage += $"\n{gameState.Opponent.Mention}'s new balance: **${PlayersWallet.GetBalance( guildId, gameState.Opponent.Id ):N2}**";

            // Post the final results as a new public message in the channel.
            await FollowupAsync( finalMessage, ephemeral: false );

            ActiveGames.TryRemove( gameId, out _ );
        }

        // Embed builders are no longer needed for this flow but can be kept for other uses
    }
}*/