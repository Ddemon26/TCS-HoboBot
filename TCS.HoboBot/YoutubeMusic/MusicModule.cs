using System.Collections.Concurrent;
using System.Diagnostics;
using Discord;
using Discord.Audio;
using Discord.Commands;
using Discord.Interactions;
using RunMode = Discord.Interactions.RunMode;

namespace TCS.HoboBot.YoutubeMusic;

public sealed class MusicModule : InteractionModuleBase<SocketInteractionContext> {
    static readonly ConcurrentDictionary<ulong, GuildAudio> GuildAudioData = new();

    // ──────────────────────────  /play  ──────────────────────────
    [SlashCommand( "play", "Play a YouTube video in your voice channel.", runMode: RunMode.Async )]
    public async Task PlayAsync([Remainder] string url) {
        await DeferAsync();

        // caller must be in VC
        var vc = (Context.User as IGuildUser)?.VoiceChannel;
        if ( vc is null ) {
            await FollowupAsync( "❌ Join a voice channel first!" );
            return;
        }

        // per-guild state
        var ga = GuildAudioData.GetOrAdd( Context.Guild.Id, _ => new GuildAudio() );

        // connect (or reuse)
        if ( ga.Client is null || ga.Client.ConnectionState != ConnectionState.Connected ) {
            ga.Client = await vc.ConnectAsync( selfDeaf: true, selfMute: false );
        }

        // queue and start loop
        ga.Queue.Enqueue( url );
        _ = Task.Run( () => PlaybackLoopAsync( Context.Guild.Id ) );
        await FollowupAsync( $"▶️ Queued: {url}" );
    }

    // ──────────────────────────  /skip  ──────────────────────────
    [SlashCommand( "skip", "Skip the current track.", runMode: RunMode.Async )]
    public async Task SkipAsync() {
        if ( GuildAudioData.TryGetValue( Context.Guild.Id, out var ga ) ) {
            await ga.CancelToken?.CancelAsync()!;
        }

        await RespondAsync( "⏭️ Skipping…" );
    }

    // ──────────────────────────  /leave  ─────────────────────────
    [SlashCommand( "leave", "Disconnect the bot from the voice channel.", runMode: RunMode.Async )]
    public async Task LeaveAsync() {
        if ( GuildAudioData.TryRemove( Context.Guild.Id, out var ga ) ) {
            await ga.DisposeAsync();
        }

        await RespondAsync( "👋 Left the voice channel." );
    }

    // ────────────────────────  playback loop  ────────────────────
    static async Task PlaybackLoopAsync(ulong guildId) {
        if ( !GuildAudioData.TryGetValue( guildId, out var ga ) ) {
            return;
        }

        if ( !await ga.LoopGate.WaitAsync( 0 ) ) {
            return;
        }

        try {
            while (ga.Queue.TryDequeue( out string? url )) {
                using var cts = new CancellationTokenSource();
                ga.CancelToken = cts;

                try {
                    string streamUrl = await GetDirectAudioUrlAsync( url, cts.Token );
                    await StreamToDiscordAsync( ga.Client!, streamUrl, cts.Token );
                }
                catch (OperationCanceledException) {
                    /* skip/leave */
                }
                catch (Exception ex) {
                    Console.WriteLine( $"[Music] {ex.GetType().Name}: {ex.Message}" );
                }
            }

            if ( GuildAudioData.TryRemove( guildId, out var gone ) ) {
                await gone.DisposeAsync();
            }
        }
        finally {
            ga.LoopGate.Release();
        }
    }

    // ─────────────────────  yt-dlp ➜ direct URL  ─────────────────
    static async Task<string> GetDirectAudioUrlAsync(string videoUrl, CancellationToken ct) {
        var psi = new ProcessStartInfo {
            FileName = "yt-dlp",
            Arguments = $"--no-warnings --no-playlist -f bestaudio -g \"{videoUrl}\"",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var proc = Process.Start( psi ) ?? throw new Exception( "yt-dlp failed to start." );
        string? direct = await proc.StandardOutput.ReadLineAsync( ct );

        try { proc.Kill( entireProcessTree: true ); }
        catch {
            /* ignore */
        }

        return string.IsNullOrWhiteSpace( direct ) ? throw new Exception( "yt-dlp returned no URL." ) : direct!;
    }

    // ────────────────────────  FFmpeg ➜ Discord  ─────────────────
    static async Task StreamToDiscordAsync(IAudioClient client, string directUrl, CancellationToken ct) {
        var psi = new ProcessStartInfo {
            FileName = "ffmpeg",
            Arguments =
                "-hide_banner -loglevel warning " +
                "-reconnect 1 -reconnect_streamed 1 -reconnect_delay_max 5 " +
                $"-i \"{directUrl}\" -vn -ac 2 -ar 48000 -f s16le pipe:1",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var ffmpeg = Process.Start( psi ) ?? throw new Exception( "FFmpeg failed to start." );
        await using var discord = client.CreatePCMStream( AudioApplication.Music, 96_000 ); // ← fixed bitrate

        try {
            await ffmpeg.StandardOutput.BaseStream.CopyToAsync( discord, ct );
        }
        finally {
            await discord.FlushAsync( ct );
            try { ffmpeg.Kill( entireProcessTree: true ); }
            catch {
                /* ignore */
            }
        }
    }


    // ────────────────────  per-guild state holder  ───────────────
    sealed class GuildAudio : IAsyncDisposable {
        public IAudioClient? Client;
        public readonly ConcurrentQueue<string> Queue = new();
        public readonly SemaphoreSlim LoopGate = new(1, 1);
        public CancellationTokenSource? CancelToken;

        public async ValueTask DisposeAsync() {
            CancelToken?.Cancel();
            if ( Client is { } c ) {
                await c.StopAsync();
            }

            LoopGate.Dispose();
            CancelToken?.Dispose();
        }
    }
}