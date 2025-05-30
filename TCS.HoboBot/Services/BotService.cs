using System.Reflection;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

using TCS.HoboBot.Data;
using TCS.HoboBot.Modules;
using TCS.HoboBot.Modules.CasinoGames.Slots;
using TCS.HoboBot.Modules.DrugDealer;
using TCS.HoboBot.Modules.SwordsAndSandals;
using TCS.HoboBot.Modules.Util;

namespace TCS.HoboBot.Services;

public class BotService : IHostedService, IDisposable {
    readonly DiscordSocketClient m_client;
    readonly InteractionService m_interactions;
    readonly IServiceProvider m_services;
    readonly IConfiguration m_config;

    const bool IS_GLOBAL_REGISTRY = false;

    readonly List<ulong> m_guildIds = [];
    Timer? m_timer;
    const float SAVE_INTERVAL = 30f;

    public BotService(
        DiscordSocketClient client,
        InteractionService interactions,
        IServiceProvider services,
        IConfiguration config
    ) {
        m_client = client;
        m_interactions = interactions;
        m_services = services;
        m_config = config;
    }

    public async Task StartAsync(CancellationToken ct) {
        // logging
        m_client.Log += LogAsync;
        m_interactions.Log += LogAsync;

        // load modules
        await m_interactions.AddModulesAsync( Assembly.GetExecutingAssembly(), m_services );

        // ---- token handling -------------------------------------------------
        // use the injected configuration for token handling
        string? token = m_config["DISCORD_TOKEN"];
        if ( string.IsNullOrEmpty( token ) ) {
            Console.WriteLine( "Error: DISCORD_TOKEN is missing." );
            return;
        }

        // register slash commands when the gateway is ready
        m_client.Ready += async () => {
            // Instant test-guild registration
            if ( !IS_GLOBAL_REGISTRY ) {
                foreach (var guild in m_client.Guilds) {
                    m_guildIds.Add( guild.Id );
                    Console.WriteLine( $"Guild: {guild.Name} ({guild.Id})" );
                }

                //Dictionary<ulong, RestGuildUser[]> restGuildUsers = new();

                foreach (ulong guild in m_guildIds) {
                    Console.WriteLine( $"Registering to guild {guild}" );
                    await m_interactions.RegisterCommandsToGuildAsync( guild, deleteMissing: true );

                    // // 2. hop from the socket world to the REST-only client
                    // var restGuild = await m_client.Rest.GetGuildAsync( guild ); // RestGuild
                    //
                    // // 3. pull every member in batches of 1 000 and flatten them
                    // IReadOnlyCollection<RestGuildUser> members = (
                    //     await restGuild // RestGuild
                    //         .GetUsersAsync() // IAsyncEnumerable<[…]>
                    //         .FlattenAsync()
                    // ).ToList(); // Convert to IReadOnlyCollection<RestGuildUser>
                    //
                    // restGuildUsers.Add( guild, members.ToArray() );
                }

                // //log every username in every guild
                // foreach (KeyValuePair<ulong, RestGuildUser[]> guild in restGuildUsers) {
                //     Console.WriteLine( $"Guild: {guild.Key}" );
                //     foreach (var user in guild.Value) {
                //         Console.WriteLine( $"User: {user.DisplayName} ({user.Id})" );
                //     }
                // }
            }
#pragma warning disable CS0162 // Unreachable code detected
            else {
                // Global registration takes up to 1 h – keep it commented for now (For production ready)
                await m_interactions.RegisterCommandsGloballyAsync();
            }
#pragma warning restore CS0162 // Unreachable code detected

            // now the gateway is up and Guilds is populated:
            Console.WriteLine( $"Loading data for {m_client.Guilds.Count} guild(s)..." );
            await LoadDataAsync();
            Console.WriteLine( "✅  All data loaded." );

            Console.WriteLine( $"✅  Logged in as {m_client.CurrentUser} ({m_client.CurrentUser.Id})" );
        };

        // interaction dispatcher
        m_client.InteractionCreated += async inter => {
            var ctx = new SocketInteractionContext( m_client, inter );
            await m_interactions.ExecuteCommandAsync( ctx, m_services );
        };

        AppDomain.CurrentDomain.ProcessExit += m_crashHandler;

        m_timer = new Timer(
            SaveDataPeriodically,
            null,
            TimeSpan.Zero,
            TimeSpan.FromMinutes( SAVE_INTERVAL )
        );

        await m_client.LoginAsync( TokenType.Bot, token );
        await m_client.StartAsync();

        // string? dataToken = m_config["MONGODB_URI"];
        // if ( string.IsNullOrEmpty( token ) ) {
        //     Console.WriteLine( "Error: MONGODB_URI is missing." );
        //     return;
        // }
        //
        // Testserver.Init(dataToken);
    }
    void m_crashHandler(object? sender, EventArgs e) {
        Console.WriteLine( "Process exiting – saving data" );
        SaveDataOnShutdown().GetAwaiter().GetResult();
    }

    public async Task StopAsync(CancellationToken ct) {
        Console.WriteLine( "Shutting down..." );
        m_timer?.Change( Timeout.Infinite, 0 ); // Stop the timer from firing again
        await SaveDataOnShutdown();
        await m_client.LogoutAsync();
        await m_client.StopAsync();
    }

    public static async Task SaveDataOnShutdown() {
        await PlayersWallet.SaveAsync();
        await PlayersProperties.SaveAsync();
        await PlayersStashes.SaveAsync();
        await CasinoManager.SaveAsync();
        await WeaponShop.SaveAsync();
    }

    async Task LoadDataAsync() {
        await PlayersWallet.LoadAsync( m_client.Guilds );
        await PlayersProperties.LoadAsync( m_client.Guilds );
        await PlayersStashes.LoadAsync( m_client.Guilds );
        await CasinoManager.LoadAsync( m_client.Guilds );
        await WeaponShop.LoadWeapons( m_client.Guilds );

        var catalogue = await ItemLoader.LoadAsync();

        foreach (var w in catalogue.Weapons) {
            Console.WriteLine( $"Weapon: {w.Name} (Tier {w.Tier})" );
        }

        foreach (var a in catalogue.Armours) {
            Console.WriteLine( $"Armour: {a.Name} – {a.Slot} (Tier {a.Tier})" );
        }

    }

    void SaveDataPeriodically(object? state) {
        Console.WriteLine( "Performing periodic data save..." );
        try {
            // Since the save methods are async, we need to wait for them to complete.
            // .GetAwaiter().GetResult() is one way to do this from a synchronous method.
            PlayersWallet.SaveAsync().GetAwaiter().GetResult();
            PlayersProperties.SaveAsync().GetAwaiter().GetResult();
            PlayersStashes.SaveAsync().GetAwaiter().GetResult();
            CasinoManager.SaveAsync().GetAwaiter().GetResult();
            WeaponShop.SaveAsync().GetAwaiter().GetResult();
            Console.WriteLine( "✅ Periodic data save complete." );
        }
        catch (Exception ex) {
            Console.WriteLine( $"Error during periodic data save: {ex.Message}" );
        }
    }

    static Task LogAsync(LogMessage msg) {
        Console.WriteLine( msg );
        return Task.CompletedTask;
    }

    public void Dispose() {
        Console.WriteLine( "Disposing BotService..." );
        // Unsubscribe from events
        m_client.Log -= LogAsync;
        m_interactions.Log -= LogAsync;
        AppDomain.CurrentDomain.ProcessExit -= m_crashHandler;

        // Dispose Timer if it exists
        m_timer?.Dispose();

        // Clean up Discord resources
        m_client.Dispose();
        m_interactions.Dispose();

        GC.SuppressFinalize( this );
    }
}