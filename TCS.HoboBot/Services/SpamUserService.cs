using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;

public class SpamUserService : BackgroundService {
    readonly DiscordSocketClient m_client;
    readonly ulong[] m_targets = [];
    static readonly TimeSpan Interval = TimeSpan.FromSeconds( 2.5 );

    public SpamUserService(DiscordSocketClient client) => m_client = client;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        // 1)  Wait until the bot is READY
        var readyTcs = new TaskCompletionSource();

        m_client.Ready += ReadyHandler;
        if ( m_client.ConnectionState == ConnectionState.Connected )
            readyTcs.TrySetResult(); // ready fired before we registered
        await readyTcs.Task;

        // 2)  Periodic loop with graceful cancellation
        var timer = new PeriodicTimer( Interval );
        try {
            while (await timer.WaitForNextTickAsync( stoppingToken ))
                await SendSpamAsync();
        }
        catch (OperationCanceledException) {
            /* shutting down */
        }

        return;

        Task ReadyHandler() {
            readyTcs.TrySetResult();
            return Task.CompletedTask;
        }
    }

    async Task SendSpamAsync() {
        foreach (ulong id in m_targets) {
            try {
                // Use REST so we’re not reliant on cache
                var user = await m_client.Rest.GetUserAsync( id );
                if ( user is null ) continue;

                var dm = await user.CreateDMChannelAsync();
                await dm.SendMessageAsync( "I HEARD YOU GOT MARRIED!" );
            }
            catch (Discord.Net.HttpException ex) when (
                ex.DiscordCode is DiscordErrorCode.CannotSendMessageToUser) {
                Console.WriteLine( $"Cannot DM {id}: {ex.Reason}" );
            }
            catch (Exception ex) {
                Console.WriteLine( $"Unexpected error DM’ing {id}: {ex}" );
            }
        }
    }
}