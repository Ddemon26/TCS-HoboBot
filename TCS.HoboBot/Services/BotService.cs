using System.Reflection;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TCS.HoboBot.Data;
using TCS.HoboBot.Guilds;
using TCS.HoboBot.Modules;
using TCS.HoboBot.Modules.CasinoGames.Slots;
using TCS.HoboBot.Modules.DrugDealer;
using TCS.HoboBot.Modules.Util;

namespace TCS.HoboBot.Services;

public class BotService : BackgroundService {
    // Changed from IHostedService
    readonly DiscordSocketClient m_client;
    readonly InteractionService m_interactions;
    readonly IServiceProvider m_services;
    readonly IConfiguration m_config;
    readonly ILogger<BotService> m_logger;

    const bool IS_GLOBAL_REGISTRY = false;

    readonly List<ulong> m_guildIds = [];
    Timer? m_timer;
    const float SAVE_INTERVAL = 30f; // In minutes
    TimeSpan SaveInterval => TimeSpan.FromMinutes( SAVE_INTERVAL );

    public BotService(
        DiscordSocketClient client,
        InteractionService interactions,
        IServiceProvider services,
        IConfiguration config,
        ILogger<BotService> logger // Optional: Inject a logger for better logging practices
    ) {
        m_client = client;
        m_interactions = interactions;
        m_services = services;
        m_config = config;
        m_logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        try {
            // Logging
            m_client.Log += LogAsync;
            m_interactions.Log += LogAsync;

            // Load modules
            await m_interactions.AddModulesAsync( Assembly.GetExecutingAssembly(), m_services );

            // Token handling
            string? token = m_config["DISCORD_TOKEN"];
            if ( string.IsNullOrEmpty( token ) ) {
                m_logger.LogError( "Error: DISCORD_TOKEN is missing. BotService will not start." );
                return; // Service will stop if a token is missing
            }

            // Event handlers
            m_client.Ready += Client_ReadyAsync;
            m_client.InteractionCreated += Client_InteractionCreatedAsync;

            // Process exit handler for last-chance save
            AppDomain.CurrentDomain.ProcessExit += CrashHandler;

            // Setup periodic data saving
            m_timer = new Timer(
                SaveDataPeriodically,
                null,
                TimeSpan.Zero, // Start immediately for the first save
                SaveInterval
            );

            // Log in and start the client
            await m_client.LoginAsync( TokenType.Bot, token );
            await m_client.StartAsync();

            m_logger.LogInformation( "BotService started. Waiting for cancellation signal..." );
            // Keep the service running until cancellation is requested
            await Task.Delay( Timeout.Infinite, stoppingToken );

        }
        catch (TaskCanceledException) {
            m_logger.LogInformation( "BotService execution was canceled. (Expected during shutdown)" );
        }
        catch (Exception ex) {
            m_logger.LogError( ex, "Unhandled exception in BotService.ExecuteAsync" );
        }
        finally {
            m_logger.LogInformation( "BotService is stopping. Performing cleanup..." );

            // Unsubscribe from events to prevent memory leaks or unwanted behavior
            m_client.Log -= LogAsync;
            m_interactions.Log -= LogAsync;
            m_client.Ready -= Client_ReadyAsync;
            m_client.InteractionCreated -= Client_InteractionCreatedAsync;
            AppDomain.CurrentDomain.ProcessExit -= CrashHandler; // Unsubscribe from static event

            // Stop the timer
            m_timer?.Change( Timeout.Infinite, 0 );

            // Perform final data save
            await SaveDataOnShutdownAsync();

            // Logout and stop the client gracefully
            if ( m_client.ConnectionState is ConnectionState.Connected or ConnectionState.Connecting ) {
                await m_client.LogoutAsync();
                await m_client.StopAsync();
            }

            m_logger.LogInformation( "BotService cleanup complete." );
        }
    }

    async Task Client_ReadyAsync() {
        try {
            if ( !IS_GLOBAL_REGISTRY ) {
                m_guildIds.Clear(); // Clear previous IDs if any
                foreach (var guild in m_client.Guilds) {
                    m_guildIds.Add( guild.Id );
                    m_logger.LogInformation( "Guild: {GuildName} ({GuildId})", guild.Name, guild.Id );
                }

                foreach (ulong guildId in m_guildIds) {
                    m_logger.LogInformation( "Registering commands to guild {GuildId}", guildId );
                    await m_interactions.RegisterCommandsToGuildAsync( guildId, deleteMissing: true );
                }

                if ( await GuildManager.Initialize( m_client ) ) {
                    m_logger.LogInformation( "Guild members loaded for {GuildCount} guilds.", m_guildIds.Count );
                }
                else {
                    m_logger.LogWarning( "GuildManager initialization failed. No guild members loaded." );
                }
            }
#pragma warning disable CS0162 // Unreachable code detected - This is intentional based on IS_GLOBAL_REGISTRY
            else {
                m_logger.LogInformation( "Registering commands globally..." );
                await m_interactions.RegisterCommandsGloballyAsync( deleteMissing: true );
            }
#pragma warning restore CS0162 // Unreachable code detected

            m_logger.LogInformation( "Loading data for {GuildCount} guild(s)...", m_client.Guilds.Count );
            await LoadDataAsync();
            m_logger.LogInformation( "All data loaded successfully." );

            m_logger.LogInformation(
                "Bot is ready and logged in as {Username} ({UserId})",
                m_client.CurrentUser.Username, m_client.CurrentUser.Id
            );

        }
        catch (Exception ex) {
            m_logger.LogError( ex, "Error during Client_ReadyAsync" );
            // Consider more robust error logging
        }
    }

    async Task Client_InteractionCreatedAsync(SocketInteraction interaction) {
        try {
            var ctx = new SocketInteractionContext( m_client, interaction );
            await m_interactions.ExecuteCommandAsync( ctx, m_services );
        }
        catch (Exception ex) {
            m_logger.LogError( ex, "Error handling interaction: {InteractionId}", interaction.Id );
            // Optionally, respond to the user about the error if the interaction hasn't been responded to yet
            if ( !interaction.HasResponded ) {
                try {
                    await interaction.RespondAsync( "An unexpected error occurred while processing your command.", ephemeral: true );
                }
                catch (Exception iex) {
                    m_logger.LogError( iex, "Error responding to interaction after an error: {InteractionId}", interaction.Id );
                }
            }
        }
    }

    void CrashHandler(object? sender, EventArgs e) {
        // Log the shutdown event
        m_logger.LogInformation( "Process exiting – saving data (CrashHandler)" );
        // This runs synchronously as ProcessExit has limited time.
        SaveDataOnShutdownAsync().GetAwaiter().GetResult();
    }

    async Task SaveDataOnShutdownAsync() {
        // Renamed for clarity
        m_logger.LogInformation( "Saving data..." );
        await PlayersWallet.SaveAsync();
        await PlayersProperties.SaveAsync();
        await PlayersStashes.SaveAsync();
        await CasinoManager.SaveAsync();
        await WeaponShop.SaveAsync();
        m_logger.LogInformation( "All data saving process initiated/completed on shutdown." );
    }

    async Task LoadDataAsync() {
        await PlayersWallet.LoadAsync( m_client.Guilds );
        await PlayersProperties.LoadAsync( m_client.Guilds );
        await PlayersStashes.LoadAsync( m_client.Guilds );
        await CasinoManager.LoadAsync( m_client.Guilds );
        await WeaponShop.LoadWeapons( m_client.Guilds );

        // var catalogue = await ItemLoader.LoadAsync();
        // Console.WriteLine("--- Loaded Item Catalogue ---");
        // foreach (var w in catalogue.Weapons) {
        //     Console.WriteLine($"Weapon: {w.Name} (Tier {w.Tier})");
        // }
        // foreach (var a in catalogue.Armours) {
        //     Console.WriteLine($"Armour: {a.Name} – {a.Slot} (Tier {a.Tier})");
        // }
        // Console.WriteLine("--- Item Catalogue End ---");
    }

    void SaveDataPeriodically(object? state) {
        m_logger.LogInformation( "Performing periodic data save..." );
        try {
            // Using .GetAwaiter().GetResult() to call async methods from a sync timer callback.
            // This will block the timer thread until saving is complete.
            PlayersWallet.SaveAsync().GetAwaiter().GetResult();
            PlayersProperties.SaveAsync().GetAwaiter().GetResult();
            PlayersStashes.SaveAsync().GetAwaiter().GetResult();
            CasinoManager.SaveAsync().GetAwaiter().GetResult();
            WeaponShop.SaveAsync().GetAwaiter().GetResult();
            m_logger.LogInformation( "Periodic data save complete." );
        }
        catch (Exception ex) {
            m_logger.LogError( ex, "Error during periodic data save" );
        }
    }

    Task LogAsync(LogMessage msg) {
        m_logger.Log(
            msg.Severity == LogSeverity.Error ? LogLevel.Error :
            msg.Severity == LogSeverity.Warning ? LogLevel.Warning :
            msg.Severity == LogSeverity.Info ? LogLevel.Information :
            msg.Severity == LogSeverity.Verbose ? LogLevel.Debug :
            LogLevel.Trace,
            msg.Exception, // Include exception details if available
            msg.Message // Include the message
        );
        return Task.CompletedTask;
    }

    // Override Dispose to clean up managed resources, like the timer.
    // BackgroundService itself handles IDisposable pattern.
    public override void Dispose() {
        m_logger.LogInformation( "Disposing BotService (called from BackgroundService.Dispose)..." );
        m_timer?.Dispose();
        m_timer = null; // Ensure timer is null after disposal
        base.Dispose(); // Important: call the base class's Dispose method.
        m_logger.LogInformation( "BotService disposed." );
    }
}