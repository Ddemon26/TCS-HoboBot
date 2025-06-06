using System.Collections.Concurrent;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TCS.HoboBot.Services;

/// <summary>
///     Watches incoming messages, replies to simple keywords, <strong>and</strong> rate‑limits exact‑duplicate spam.
///     Users are muted <strong>only in the channel where the spam happened</strong> by adding a
///     temporary permission overwrite that denies <c>SendMessages</c> for <c>TimeoutDuration</c>.
/// </summary>
public class MessageResponderService : BackgroundService // Changed from IHostedService
{
    readonly DiscordSocketClient m_client;
    readonly ILogger<MessageResponderService> m_logger; // Optional: For logging

    // ─── Anti‑spam settings ──────────────────────────────────────────────
    static readonly TimeSpan DuplicateWindow = TimeSpan.FromSeconds( 10 );
    const int DUPLICATE_TOLERANCE = 3;
    static readonly TimeSpan TimeoutDuration = TimeSpan.FromMinutes( 1 );

    // (userId, channelId) → tracker
    readonly ConcurrentDictionary<(ulong, ulong), MessageTracker> m_trackers = new();

    public MessageResponderService(DiscordSocketClient client, ILogger<MessageResponderService> logger) {
        m_client = client;
        m_logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        m_logger?.LogInformation( "Message Responder Service is starting." ); // Optional logging
        
        // // register cleanup on shutdown
        // stoppingToken.Register(() => {
        //     _ = Task.Run(CleanupPermissionsAsync, stoppingToken );
        // });
        // m_client.MessageReceived += OnTrackMessageAsync;

        m_client.MessageReceived += OnMessageAsync;

        try {
            // This keeps the service running until a cancellation request is received
            await Task.Delay( Timeout.Infinite, stoppingToken );
        }
        catch (TaskCanceledException) {
            // This exception is expected when the service is stopping
            m_logger?.LogInformation( "Message Responder Service is stopping due to cancellation request." ); // Optional logging
        }
        catch (Exception ex) {
            m_logger?.LogError( ex, "An unhandled exception occurred in Message Responder Service." ); // Optional logging
        }
        finally {
            m_client.MessageReceived -= OnMessageAsync;
            m_logger?.LogInformation( "Message Responder Service has stopped and cleaned up resources." ); // Optional logging
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    async Task OnMessageAsync(SocketMessage s) {
        if ( s.Author.IsBot || s is not SocketUserMessage msg ) {
            return;
        }

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
        
        ulong m_goodUser = 268654531452207105; // Example guild ID, replace with actual
        if ( msg.Author.Id == m_goodUser ) {
            await msg.AddReactionAsync( new Emoji( "👍" ));
        }
        
        /*ulong[] m_customEmoteIds = {
            1122031638461812807,
            1277021148013658234,
            1277034375871074377,
        };
        
        if (msg.Author.Id == m_goodUser)
        {
            var allEmojiUnicodes = NamesAndUnicodes.Values;
            var emotesToAdd = allEmojiUnicodes.Select(unicode => new Discord.Emoji(unicode));
            //pick 20 emojis randomly
            var random = new Random();
            var reactions = emotesToAdd.OrderBy(_ => random.Next()).Take(20).ToArray();
            var options = new RequestOptions { AuditLogReason = "Adding reactions to message" };
            try
            {
                // Your existing code to add reactions
                await msg.AddReactionsAsync(reactions, options);
            }
            catch (Discord.Net.HttpException ex) when (ex.HttpCode == System.Net.HttpStatusCode.BadRequest && ex.DiscordCode == Discord.DiscordErrorCode.MaximumReactionsReached)
            {
                // Log that the maximum number of reactions was reached
                Console.WriteLine($"Could not add reactions to message {s.Id}: Maximum reactions reached.");
                // Optionally, inform the user or take other actions
            }
            catch (Exception ex)
            {
                // Handle other potential exceptions
                Console.WriteLine($"An error occurred: {ex.Message}");
            }
        }*/
        
        // Emoji[] m_customEmotes = {
        //     new Emoji( "👁️" ),
        //     new Emoji( "👃" ),
        //     new Emoji( "🧿" ),
        // };
        
        // if ( msg.Author.Id == m_goodUser ) {
        //     await msg.AddReactionsAsync( m_customEmotes );
        // }

        /*if ( msg.Author.Id == m_goodUser ){
            List<IEmote> emotesToReactWith = new();
            foreach (var emoteId in m_customEmoteIds)
            {
                IEmote? foundEmote = null;
                foreach (var guild in m_client.Guilds)
                {
                    var emote = guild.Emotes.FirstOrDefault(e => e.Id == emoteId);
                    if (emote != null)
                    {
                        foundEmote = emote;
                        break; 
                    }
                }

                if (foundEmote != null)
                {
                    emotesToReactWith.Add(foundEmote);
                }
                else
                {
                    m_logger?.LogWarning($"Custom emote with ID {emoteId} not found or not accessible by the bot.");
                }
            }

            if (emotesToReactWith.Any())
            {
                await msg.AddReactionsAsync(emotesToReactWith.ToArray());
            }
            else
            {
                m_logger?.LogWarning($"None of the specified custom emotes were found or accessible by the bot.");
            }
            return;
        }*/
        //880163694443659356
        // Emoji[] m_badCustomEmotes = {
        //     new Emoji( "🇳" ),
        //     new Emoji( "🇮" ),
        //     new Emoji( "🇬" ),
        //     new Emoji( "🔂" ),
        //     new Emoji( "🇪" ),
        //     new Emoji( "🇷" ),
        // };
        
        ulong[] m_badUsers = {
            880163694443659356, // Example user ID, replace with actual
        };
        if ( m_badUsers.Contains( msg.Author.Id ) ) {
            // react to the message with a thumbs down
            //await msg.AddReactionsAsync( m_badCustomEmotes );
            await msg.AddReactionAsync( new Emoji("👎") );
            return;
        }
    }
    
    #region Anti‑spam logic
    async Task CleanupPermissionsAsync() {
        foreach ((ulong userId, ulong channelId) in m_trackers.Keys) {
            if (m_client.GetChannel(channelId) is not SocketTextChannel textChannel) 
                continue;

            var gUser = textChannel.Guild.GetUser(userId);
            if (gUser is null) 
                continue;

            OverwritePermissions? current = textChannel.GetPermissionOverwrite(gUser);
            if (current?.SendMessages == PermValue.Deny) {
                await textChannel.RemovePermissionOverwriteAsync(
                    gUser,
                    new RequestOptions { AuditLogReason = "Service shutdown cleanup" }
                );
            }
        }
    }
    
    async Task OnTrackMessageAsync(SocketMessage s) {
        if ( s.Author.IsBot || s is not SocketUserMessage msg ) {
            return;
        }
        
        var key = (msg.Author.Id, msg.Channel.Id);
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
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    /// <summary>
    ///     Denies <c>SendMessages</c> for the offending user in the current text channel
    ///     and schedules automatic un‑mute after <see cref="TimeoutDuration"/>.
    ///     Requires the bot to have <c>ManageChannels</c> permission in that channel.
    /// </summary>
    async Task ApplyChannelTimeoutAsync(SocketUserMessage msg) // Made private as it's an internal helper
    {
        if ( msg.Channel is not SocketTextChannel textChannel ) {
            return; // DM or group: ignore
        }

        if ( msg.Author is not SocketGuildUser gUser ) {
            return; // Should always be true in guild
        }

        // Create (or update) an overwrite that denies SendMessages
        var denySend = new OverwritePermissions( sendMessages: PermValue.Deny );
        try {
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
                        await Task.Delay( TimeoutDuration, CancellationToken.None ); // Consider passing a CancellationToken if appropriate
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
                        // Using ILogger for errors is preferred over Console.Error.WriteLineAsync
                        m_logger?.LogError( ex, "[Timeout-cleanup] Error during timeout cleanup for user {UserId} in channel {ChannelId}", gUser.Id, textChannel.Id );
                        // Fallback if logger is not available or for critical direct output:
                        // await Console.Error.WriteLineAsync( $"[Timeout‑cleanup] {ex}" );
                    }
                }
            );
        }
        catch (Exception ex) {
            m_logger?.LogError( ex, "Failed to apply channel timeout for user {UserId} in channel {ChannelId}", gUser.Id, textChannel.Id );
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    sealed class MessageTracker // Made private as it's only used by MessageResponderService
    {
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
    #endregion
}