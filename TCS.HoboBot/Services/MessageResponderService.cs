using System.Collections.Concurrent;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
namespace TCS.HoboBot.Services;

/// <summary>
///     Watches incoming messages, replies to simple keywords, **and** rate‑limits exact‑duplicate spam.
///     Users are muted <strong>only in the channel where the spam happened</strong> by adding a
///     temporary permission overwrite that denies <c>SendMessages</c> for <c>TimeoutDuration</c>.
/// </summary>
public class MessageResponderService : IHostedService {
    readonly DiscordSocketClient m_client;

    // ─── Anti‑spam settings ──────────────────────────────────────────────
    static readonly TimeSpan DuplicateWindow = TimeSpan.FromSeconds( 10 );
    const int DUPLICATE_TOLERANCE = 3;
    static readonly TimeSpan TimeoutDuration = TimeSpan.FromMinutes( 1 );

    // (userId, channelId) → tracker
    readonly ConcurrentDictionary<(ulong, ulong), MessageTracker> m_trackers = new();

    public MessageResponderService(DiscordSocketClient client) => m_client = client;

    public Task StartAsync(CancellationToken _) {
        m_client.MessageReceived += OnMessageAsync;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken _) {
        m_client.MessageReceived -= OnMessageAsync;
        return Task.CompletedTask;
    }

    // ──────────────────────────────────────────────────────────────────────
    async Task OnMessageAsync(SocketMessage s) {
        if ( s.Author.IsBot || s is not SocketUserMessage msg ) {
            return;
        }

        /*var key = (msg.Author.Id, msg.Channel.Id);
        var tracker = m_trackers.GetOrAdd( key, _ => new MessageTracker() );

        tracker.ExpireIfNeeded( DuplicateWindow );

        if ( tracker.IsDuplicate( msg.Content ) ) {
            tracker.Count++;
            if ( tracker.Count > DUPLICATE_TOLERANCE ) {
                await ApplyChannelTimeoutAsync( msg );
                tracker.Reset();
            }
        }
        else {
            tracker.Reset( msg.Content );
        }*/

        // ─── Keyword responses ───────────────────────────────────────────
        if ( msg.Content.Equals( "!ping", StringComparison.OrdinalIgnoreCase ) ) {
            await msg.Channel.SendMessageAsync( "Pong! 🏓" );
            return;
        }

        if ( msg.Content.Contains( "jimmy", StringComparison.OrdinalIgnoreCase ) ) {
            await msg.Channel.SendMessageAsync( "Did someone say Jimmy?" );
            return;
        }

        if ( msg.Content.Contains( "bowman", StringComparison.OrdinalIgnoreCase ) ) {
            await msg.Channel.SendMessageAsync( "Bowman is a legend." );
            return;
        }

        if ( msg.Content.Contains( "dalton", StringComparison.OrdinalIgnoreCase ) ) {
            await msg.Channel.SendMessageAsync( "Dalton the goat." );
            return;
        }

        if ( msg.Content.Contains( "damon", StringComparison.OrdinalIgnoreCase ) ) {
            await msg.Channel.SendMessageAsync( "Damon" );
            return;
        }

        if ( msg.Content.Contains( "redux", StringComparison.OrdinalIgnoreCase ) ) {
            await msg.Channel.SendMessageAsync( "Redux is gay." );
            return;
        }

        if ( msg.Content.Contains( "Hayko", StringComparison.OrdinalIgnoreCase ) ) {
            await msg.Channel.SendMessageAsync( "5 foot 4 and a total whore" );
            return;
        }

        // if ( msg.Content.Contains( "stickman", StringComparison.OrdinalIgnoreCase ) ) {
        //     var spaces = 25; // You can adjust this value to change the gap
        //     string art = ArtMaker.GetStickFigures( spaces );
        //     await msg.Channel.SendMessageAsync( "Here are two stick figures with a gap between them:\n" + art );
        //     return;
        // }
    }

    // ──────────────────────────────────────────────────────────────────────
    /// <summary>
    ///     Denies <c>SendMessages</c> for the offending user in the current text channel
    ///     and schedules automatic un‑mute after <see cref="TimeoutDuration"/>.
    ///     Requires the bot to have <c>ManageChannels</c> permission in that channel.
    /// </summary>
    static async Task ApplyChannelTimeoutAsync(SocketUserMessage msg) {
        if ( msg.Channel is not SocketTextChannel textChannel ) {
            return; // DM or group: ignore
        }

        if ( msg.Author is not SocketGuildUser gUser ) {
            return; // Should always be true in guild
        }

        // Create (or update) an overwrite that denies SendMessages
        var denySend = new OverwritePermissions( sendMessages: PermValue.Deny );
        await textChannel.AddPermissionOverwriteAsync(
            gUser, denySend,
            new RequestOptions { AuditLogReason = "Duplicate message spam" }
        );

        await textChannel.SendMessageAsync(
            $"{gUser.Mention} muted here for {TimeoutDuration.TotalMinutes:N0} min (duplicate spam)."
        );

        // Schedule un‑mute
        _ = Task.Run( async () => {
                try {
                    await Task.Delay( TimeoutDuration );
                    // Remove only if it still matches what we set (user may have been manually un‑muted)
                    OverwritePermissions? currentOverwrite = textChannel.GetPermissionOverwrite( gUser );
                    if ( currentOverwrite?.SendMessages == PermValue.Deny ) {
                        await textChannel.RemovePermissionOverwriteAsync(
                            gUser,
                            new RequestOptions { AuditLogReason = "Timeout expired" }
                        );
                    }
                }
                catch (Exception ex) {
                    await Console.Error.WriteLineAsync( $"[Timeout‑cleanup] {ex}" );
                }
            }
        );
    }

    // ──────────────────────────────────────────────────────────────────────
    sealed class MessageTracker {
        public string LastContent { get; private set; } = string.Empty;
        public DateTimeOffset FirstSeen { get; private set; } = DateTimeOffset.UtcNow;
        public int Count { get; set; } = 1;

        public bool IsDuplicate(string content) => string.Equals( content, LastContent, StringComparison.Ordinal );
        public void Reset(string content = "") {
            LastContent = content;
            FirstSeen = DateTimeOffset.UtcNow;
            Count = 1;
        }
        public void ExpireIfNeeded(TimeSpan window) {
            if ( DateTimeOffset.UtcNow - FirstSeen > window ) {
                Reset( LastContent );
            }
        }
    }

    /*public static class ArtMaker {
        /// <summary>
        /// Returns two stick figures with spacing and underscore count derived from a single factor.
        /// </summary>
        /// <param name="baseSize">Determines the number of underscores for the legs.
        /// This value is also used as the length of the spacing gap inserted between
        /// the main components (head, arms) of the two figures.</param>
        /// <returns>A string representing the two stick figures.</returns>
        public static string GetStickFigures(int baseSize) {
            if ( baseSize < 1 ) // Underscore count and gap size should be at least 1
                throw new ArgumentOutOfRangeException(
                    nameof(baseSize),
                    "baseSize must be positive."
                );

            // Define the parts of a single stick figure
            const char head = 'O';
            const string arms = @" /|\ ";
            const string legs = @" / \ ";

            // The internalGap (space between full head/arm blocks) is set to baseSize.
            int internalGap = baseSize;
            var gapSpaces = new string( ' ', internalGap );

            // The number of underscores is also set to baseSize.
            int numberOfUnderscores = baseSize;
            var legRowUnderscores = new string( '_', numberOfUnderscores );

            var sb = new StringBuilder();

            // Append head row
            sb.Append( head ).Append( gapSpaces ).Append( head ).AppendLine();

            // Append arm row
            sb.Append( arms ).Append( gapSpaces ).Append( arms ).AppendLine();

            // Append leg row
            sb.Append( legs.TrimEnd() ) // " / \"
                .Append( legRowUnderscores ) // e.g., "__________" if baseSize = 10
                .Append( legs.TrimStart() ); // "/ \ "

            return sb.ToString();
        }
    }*/
}