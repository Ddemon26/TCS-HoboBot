/*#nullable enable
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Discord;
using Discord.Audio;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using RunMode = Discord.Interactions.RunMode;

namespace TCS.HoboBot.YoutubeMusic;

/// <summary>
/// A class to hold all information about a music track.
/// </summary>
public sealed class TrackInfo {
    public required string Title { get; init; }
    public required string Url { get; init; }
    public required string Thumbnail { get; init; }
    public required TimeSpan Duration { get; init; }
    public required IUser Requester { get; init; }
}

public static class GuildMusicService {
    //public static readonly ConcurrentDictionary<ulong, GuildMusicState> GuildStates = new();
    public static ConcurrentDictionary<ulong, GuildMusicState> GuildStates { get; } = new();
    static DiscordSocketClient? s_client;

    public static void Init(DiscordSocketClient? client) {
        //GuildStates = new ConcurrentDictionary<ulong, GuildMusicState>();
        s_client = client;
    }

    public static GuildMusicState GetOrCreateGuildState(ulong guildId) {
        return GuildStates.GetOrAdd( guildId, _ => new GuildMusicState() );
    }

    public static async Task<Embed> PlayAsync(IGuild guild, IVoiceChannel userVoiceChannel, ITextChannel? responseChannel, string query) {
        var guildState = GetOrCreateGuildState( guild.Id );

        // Connect if not already connected
        if ( guildState.AudioClient is null || guildState.AudioClient.ConnectionState != ConnectionState.Connected ) {
            try {
                guildState.AudioClient = await userVoiceChannel.ConnectAsync( selfDeaf: true, selfMute: false );
            }
            catch (Exception ex) {
                return new EmbedBuilder()
                    .WithColor( Color.Red )
                    .WithTitle( "❌ Connection Failed" )
                    .WithDescription( $"I couldn't connect to the voice channel.\n`{ex.Message}`" )
                    .Build();
            }
        }

        // If user is not in the same VC as the bot, move the bot.
        var botVcId = (
                await guild.GetAFKChannelAsync())?.Id ?? (
                await guild.GetVoiceChannelsAsync())
            .FirstOrDefault( x => s_client != null && x.GetUserAsync( s_client.CurrentUser.Id ).Result is not null )?.Id;

        if ( botVcId is not null && botVcId != userVoiceChannel.Id ) {
            try {
                await guildState.AudioClient!.StopAsync();
                guildState.AudioClient = await userVoiceChannel.ConnectAsync( selfDeaf: true, selfMute: false );
            }
            catch (Exception ex) {
                return new EmbedBuilder()
                    .WithColor( Color.Red )
                    .WithTitle( "❌ Connection Failed" )
                    .WithDescription( $"I couldn't move to the voice channel.\n`{ex.Message}`" )
                    .Build();
            }
        }


        // Get track(s) using yt-dlp
        if ( s_client != null ) {
            var tracks = await GetTracksAsync( query, s_client.CurrentUser );
            if ( !tracks.Any() ) {
                return new EmbedBuilder()
                    .WithColor( Color.Red )
                    .WithTitle( "❌ No Results" )
                    .WithDescription( $"I couldn't find anything matching your query." )
                    .Build();
            }

            foreach (var track in tracks) {
                guildState.Queue.Enqueue( track );
            }

            // Start the playback loop if it's not already running
            _ = Task.Run( () => PlaybackLoopAsync( guild.Id, responseChannel ) );

            // Create response embed
            var builder = new EmbedBuilder().WithColor( Color.Green );
            if ( tracks.Count > 1 ) {
                builder.WithTitle( $"🎶 Queued Playlist" )
                    .WithDescription( $"Added **{tracks.Count}** tracks to the queue from the playlist." )
                    .WithThumbnailUrl( tracks.First().Thumbnail );
            }
            else {
                var track = tracks.First();
                builder.WithTitle( $"▶️ Queued Track" )
                    .WithDescription( $"**[{track.Title}]({track.Url})**" )
                    .WithThumbnailUrl( track.Thumbnail )
                    .AddField( "Duration", track.Duration == TimeSpan.Zero ? "Livestream" : track.Duration.ToString( @"hh\:mm\:ss" ), true )
                    .AddField( "Position in queue", guildState.Queue.Count.ToString(), true )
                    .WithFooter( $"Requested by {track.Requester.Username}", track.Requester.GetAvatarUrl() );
            }

            return builder.Build();
        }
        else {
            return new EmbedBuilder()
                .WithColor( Color.Red )
                .WithTitle( "❌ yt-dlp Not Found" )
                .WithDescription( "Please install yt-dlp and make sure it's in your PATH." )
                .Build();
        }

    }

    public static async Task<Embed?> SkipTrackAsync(ulong guildId) {
        if ( GuildStates.TryGetValue( guildId, out var guildState ) && guildState.CurrentTrack is not null ) {
            await guildState.CancellationTokenSource?.CancelAsync()!;
            return new EmbedBuilder()
                .WithColor( Color.Blue )
                .WithTitle( "⏭️ Track Skipped" )
                .WithDescription( $"Skipped **{guildState.CurrentTrack.Title}**" )
                .Build();
        }

        return new EmbedBuilder()
            .WithColor( Color.Red )
            .WithTitle( "❌ Nothing to Skip" )
            .WithDescription( "There is no track currently playing." )
            .Build();
    }

    public static async Task<Embed> StopAsync(ulong guildId) {
        if ( GuildStates.TryRemove( guildId, out var guildState ) ) {
            await guildState.DisposeAsync();
            return new EmbedBuilder()
                .WithColor( Color.Blue )
                .WithTitle( "👋 Playback Stopped" )
                .WithDescription( "Queue has been cleared and I've left the voice channel." )
                .Build();
        }

        return new EmbedBuilder()
            .WithColor( Color.Red )
            .WithTitle( "❌ Not Playing" )
            .WithDescription( "I'm not currently playing any music in this server." )
            .Build();
    }

    public static Embed? GetQueue(ulong guildId) {
        if ( !GuildStates.TryGetValue( guildId, out var guildState ) || guildState.Queue.IsEmpty ) {
            return new EmbedBuilder()
                .WithColor( Color.Red )
                .WithDescription( "The queue is currently empty." )
                .Build();
        }

        var builder = new EmbedBuilder().WithColor( Color.Blue ).WithTitle( "🎶 Music Queue" );
        if ( guildState.CurrentTrack != null ) {
            builder.AddField( "Now Playing", $"**[{guildState.CurrentTrack.Title}]({guildState.CurrentTrack.Url})** | `{guildState.CurrentTrack.Duration:hh\\:mm\\:ss}` | Requested by `{guildState.CurrentTrack.Requester.Username}`" );
        }

        var queueList = guildState.Queue.Select( (track, index) =>
                                                     $"`{index + 1}.` **[{track.Title}]({track.Url})** | `{track.Duration:hh\\:mm\\:ss}` | Requested by `{track.Requester.Username}`"
        ).ToList();

        // Discord embed field values have a 1024 character limit.
        var queueString = string.Join( "\n", queueList );
        if ( queueString.Length > 1024 ) {
            queueString = string.Join( "\n", queueList.Take( 10 ) );
            queueString += $"\nAnd **{queueList.Count - 10}** more...";
        }

        builder.AddField( "Up Next", queueString );

        return builder.Build();
    }

    public static Embed? GetNowPlaying(ulong guildId) {
        if ( !GuildStates.TryGetValue( guildId, out var guildState ) || guildState.CurrentTrack is null ) {
            return new EmbedBuilder()
                .WithColor( Color.Red )
                .WithDescription( "Nothing is currently playing." )
                .Build();
        }

        var track = guildState.CurrentTrack;
        return new EmbedBuilder()
            .WithColor( Color.Blue )
            .WithTitle( "🎵 Now Playing" )
            .WithDescription( $"**[{track.Title}]({track.Url})**" )
            .WithThumbnailUrl( track.Thumbnail )
            .AddField( "Duration", track.Duration == TimeSpan.Zero ? "Livestream" : track.Duration.ToString( @"hh\:mm\:ss" ), true )
            .AddField( "Requested by", track.Requester.Mention, true )
            .WithFooter( $"Volume: {guildState.Volume * 100}%" )
            .Build();
    }

    public static Embed SetVolume(ulong guildId, int volume) {
        if ( volume < 1 || volume > 150 ) {
            return new EmbedBuilder()
                .WithColor( Color.Red )
                .WithTitle( "❌ Invalid Volume" )
                .WithDescription( "Please set a volume between 1 and 150." )
                .Build();
        }

        if ( GuildStates.TryGetValue( guildId, out var guildState ) ) {
            guildState.Volume = volume / 100.0f;
            // Volume change will be applied on the next track. We can also restart the current one.
            // For simplicity, we'll let it apply on the next song. A more advanced implementation
            // would restart the ffmpeg process for the current song with the new volume filter.

            return new EmbedBuilder()
                .WithColor( Color.Green )
                .WithTitle( "🔊 Volume Changed" )
                .WithDescription( $"Volume set to **{volume}%**.\nIt will be applied to the next track." )
                .Build();
        }

        return new EmbedBuilder()
            .WithColor( Color.Red )
            .WithTitle( "❌ Not Playing" )
            .WithDescription( "I'm not currently playing any music in this server." )
            .Build();
    }


    private static async Task PlaybackLoopAsync(ulong guildId, ITextChannel? channel) {
        if ( !GuildStates.TryGetValue( guildId, out var guildState ) ) return;

        // Prevent multiple loops from running for the same guild.
        if ( !await guildState.LoopGate.WaitAsync( 0 ) ) return;

        try {
            while (guildState.Queue.TryDequeue( out var track )) {
                guildState.CurrentTrack = track;
                guildState.CancellationTokenSource = new CancellationTokenSource();

                try {
                    if ( channel != null ) {
                        await channel.SendMessageAsync(
                            embed: new EmbedBuilder()
                                .WithColor( Color.Blue )
                                .WithTitle( "🎵 Now Playing" )
                                .WithDescription( $"**[{track.Title}]({track.Url})**" )
                                .WithThumbnailUrl( track.Thumbnail )
                                .AddField( "Duration", track.Duration == TimeSpan.Zero ? "Livestream" : track.Duration.ToString( @"hh\:mm\:ss" ), true )
                                .AddField( "Requested by", track.Requester.Mention, true )
                                .Build()
                        );
                    }

                    await SendAudioAsync( guildState.AudioClient!, track, guildState.Volume, guildState.CancellationTokenSource.Token );
                }
                catch (OperationCanceledException) {
                    // Caused by SkipAsync or StopAsync, which is expected.
                }
                catch (Exception ex) {
                    if ( channel != null ) await channel.SendMessageAsync( $"Playback error: `{ex.Message}`" );
                }
                finally {
                    guildState.CurrentTrack = null;
                    guildState.CancellationTokenSource?.Dispose();
                }
            }
        }
        finally {
            // When queue is empty, disconnect and clean up.
            if ( GuildStates.TryRemove( guildId, out var finishedState ) ) {
                await finishedState.DisposeAsync();
                if ( channel != null ) {
                    await channel.SendMessageAsync(
                        embed: new EmbedBuilder()
                            .WithColor( Color.Blue )
                            .WithDescription( "Queue finished. Leaving the voice channel." )
                            .Build()
                    );
                }
            }

            guildState.LoopGate.Release();
        }
    }

    private static async Task SendAudioAsync(IAudioClient client, TrackInfo track, float volume, CancellationToken ct) {
        var ffmpegArgs =
            $"-hide_banner -loglevel warning -i \"{track.Url}\" -ac 2 -f s16le -ar 48000 -vn -af volume={volume:0.00}";

        using var ffmpeg = Process.Start(
            new ProcessStartInfo {
                FileName = "ffmpeg",
                Arguments = ffmpegArgs,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        ) ?? throw new Exception( "FFmpeg failed to start." );

        await using var ffmpegStream = ffmpeg.StandardOutput.BaseStream;
        await using var discordStream = client.CreatePCMStream( AudioApplication.Music, 128 * 1024 ); // 128kbps for high quality

        try {
            await ffmpegStream.CopyToAsync( discordStream, ct );
        }
        finally {
            await discordStream.FlushAsync( ct );
            if ( !ffmpeg.HasExited ) {
                ffmpeg.Kill( true );
            }
        }
    }

    private static async Task<IReadOnlyList<TrackInfo>> GetTracksAsync(string query, IUser requester) {
        var trackInfos = new List<TrackInfo>();
        var ytDlpArgs = $"--default-search \"ytsearch\" --dump-single-json --no-playlist \"{query}\"";

        // Handle explicit playlist URLs
        if ( query.Contains( "playlist?list=" ) ) {
            ytDlpArgs = $"--dump-single-json --flat-playlist \"{query}\"";
        }

        var psi = new ProcessStartInfo {
            FileName = "yt-dlp",
            Arguments = ytDlpArgs,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start( psi ) ?? throw new Exception( "yt-dlp failed to start." );
        await using var stream = process.StandardOutput.BaseStream;

        try {
            var document = await JsonDocument.ParseAsync( stream );
            // Check if it's a playlist or a single video
            if ( document.RootElement.TryGetProperty( "_type", out var type ) && type.GetString() == "playlist" ) {
                var entries = document.RootElement.GetProperty( "entries" );
                foreach (var entry in entries.EnumerateArray()) {
                    if ( entry.TryGetProperty( "url", out var urlElement ) ) {
                        var fullTrack = await GetSingleTrackInfoAsync( urlElement.GetString()!, requester );
                        if ( fullTrack != null ) {
                            trackInfos.Add( fullTrack );
                        }
                    }
                }
            }
            else {
                var track = CreateTrackInfo( document.RootElement, requester );
                if ( track != null ) trackInfos.Add( track );
            }
        }
        catch (JsonException) {
            // yt-dlp might not have found anything, resulting in empty output
        }
        catch (Exception ex) {
            Console.WriteLine( $"[MusicService] Error parsing yt-dlp output: {ex.Message}" );
        }

        return trackInfos;
    }

    private static async Task<TrackInfo?> GetSingleTrackInfoAsync(string url, IUser requester) {
        var psi = new ProcessStartInfo {
            FileName = "yt-dlp",
            Arguments = $"--dump-single-json \"{url}\"",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start( psi ) ?? throw new Exception( "yt-dlp failed to start." );
        await using var stream = process.StandardOutput.BaseStream;
        try {
            var document = await JsonDocument.ParseAsync( stream );
            return CreateTrackInfo( document.RootElement, requester );
        }
        catch { return null; }
    }


    private static TrackInfo? CreateTrackInfo(JsonElement element, IUser requester) {
        try {
            var title = element.GetProperty( "title" ).GetString() ?? "Unknown Title";
            var url = element.GetProperty( "webpage_url" ).GetString()!;
            var thumbnail = element.GetProperty( "thumbnail" ).GetString() ?? "";
            var duration = element.TryGetProperty( "duration", out var durElement ) && durElement.ValueKind == JsonValueKind.Number
                ? TimeSpan.FromSeconds( durElement.GetDouble() )
                : TimeSpan.Zero;

            return new TrackInfo {
                Title = title,
                Url = url,
                Thumbnail = thumbnail,
                Duration = duration,
                Requester = requester
            };
        }
        catch {
            return null;
        }
    }

    public static void Dispose() {
        // Dispose of all guild states
        foreach (var guildState in GuildStates.Values) {
            guildState.DisposeAsync().GetAwaiter().GetResult();
        }

        GuildStates.Clear();

        // foreach (var guildMusicState in GuildStates) {
        //     guildMusicState.Value.DisposeAsync().GetAwaiter().GetResult();
        // }
        //
        // GuildStates.Clear();
    }
}

public sealed class MusicService : IHostedService, IDisposable {
    // readonly ConcurrentDictionary<ulong, GuildMusicState> _guildStates;
    readonly DiscordSocketClient? _client;

    public MusicService(DiscordSocketClient client) {
        // _guildStates = new ConcurrentDictionary<ulong, GuildMusicState>();
        _client = client;
    }

    public Task StartAsync(CancellationToken cancellationToken) {
        // Initialize the music service with the Discord client
        // Register the service with the Discord client
        if ( _client != null ) {
            _client.Log += LogAsync;
            _client.Ready += OnReadyAsync;
        }

        return Task.CompletedTask;
    }

    Task OnReadyAsync() {
        if ( _client == null ) return Task.CompletedTask;

        GuildMusicService.Init( _client );
        // Load all guild states when the client is ready
        foreach (var guild in _client.Guilds) {
            GuildMusicService.GetOrCreateGuildState( guild.Id );
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) {
        // Dispose of all guild states
        // foreach (var guildState in _guildStates.Values) {
        //     guildState.DisposeAsync().GetAwaiter().GetResult();
        // }

        GuildMusicService.Dispose();

        return Task.CompletedTask;
    }

    private Task LogAsync(LogMessage log) {
        Console.WriteLine( $"[MusicService] {log}" );
        return Task.CompletedTask;
    }

    public void Dispose() {

        // Unsubscribe from events
        if ( _client != null ) {
            _client.Log -= LogAsync;
        }

        // Dispose of all guild states
        // foreach (var guildState in _guildStates.Values) {
        //     guildState.DisposeAsync().GetAwaiter().GetResult();
        // }
        //
        // _guildStates.Clear();

        GuildMusicService.Dispose();
    }
}

/// <summary>
/// Holds the music state for a single guild.
/// </summary>
public sealed class GuildMusicState : IAsyncDisposable {
    public IAudioClient? AudioClient { get; set; }
    public TrackInfo? CurrentTrack { get; set; }
    public CancellationTokenSource? CancellationTokenSource { get; set; }
    public float Volume { get; set; } = 1.0f; // Default 100%

    public readonly ConcurrentQueue<TrackInfo> Queue = new();
    public readonly SemaphoreSlim LoopGate = new(1, 1);

    public async ValueTask DisposeAsync() {
        // Cancel any ongoing playback
        if ( CancellationTokenSource != null && !CancellationTokenSource.IsCancellationRequested ) {
            await CancellationTokenSource.CancelAsync();
            CancellationTokenSource.Dispose();
        }

        // Disconnect the audio client
        if ( AudioClient is { } client ) {
            await client.StopAsync();
            client.Dispose();
            AudioClient = null;
        }

        Queue.Clear();
        LoopGate.Dispose();
        GC.SuppressFinalize( this );
    }
}

/// <summary>
/// The Discord Interaction Module containing all music commands.
/// </summary>
[Group( "music", "Commands for playing music." )]
public sealed class MusicModuleTwo : InteractionModuleBase<SocketInteractionContext> {
    // The MusicService is a singleton, so we can access it via its static instance.
    // private readonly MusicService _musicService = MusicService.Instance;

    [SlashCommand( "music_play", "Play a song from YouTube by URL or search query.", runMode: RunMode.Async )]
    public async Task PlayAsync([Summary( "query", "A YouTube URL or a search query." )] string query) {
        await DeferAsync();

        var userVoiceChannel = (Context.User as IGuildUser)?.VoiceChannel;
        if ( userVoiceChannel is null ) {
            await FollowupAsync( "❌ You must be in a voice channel to use this command." );
            return;
        }

        var embed = await GuildMusicService.PlayAsync( Context.Guild, userVoiceChannel, Context.Channel as ITextChannel, query );
        await FollowupAsync( embed: embed );
    }

    [SlashCommand( "music_skip", "Skips the current track.", runMode: RunMode.Async )]
    public async Task SkipAsync() {
        await DeferAsync();
        var embed = await GuildMusicService.SkipTrackAsync( Context.Guild.Id );
        await FollowupAsync( embed: embed );
    }

    [SlashCommand( "music_stop", "Stops playback, clears the queue, and leaves the voice channel.", runMode: RunMode.Async )]
    public async Task StopAsync() {
        await DeferAsync();
        var embed = await GuildMusicService.StopAsync( Context.Guild.Id );
        await FollowupAsync( embed: embed );
    }

    [SlashCommand( "music_queue", "Shows the current music queue.", runMode: RunMode.Async )]
    public async Task QueueAsync() {
        await DeferAsync();
        var embed = GuildMusicService.GetQueue( Context.Guild.Id );
        await FollowupAsync( embed: embed );
    }

    [SlashCommand( "music_nowplaying", "Shows details about the currently playing track.", runMode: RunMode.Async )]
    public async Task NowPlayingAsync() {
        await DeferAsync();
        var embed = GuildMusicService.GetNowPlaying( Context.Guild.Id );
        await FollowupAsync( embed: embed );
    }

    [SlashCommand( "music_volume", "Sets the playback volume (1-150%).", runMode: RunMode.Async )]
    public async Task VolumeAsync([Summary( "percentage", "The volume percentage." )] int volume) {
        await DeferAsync();
        var embed = GuildMusicService.SetVolume( Context.Guild.Id, volume );
        await FollowupAsync( embed: embed );
    }
}*/
