using System.Reflection;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using TCS.HoboBot.Data;
using TCS.HoboBot.Modules;

namespace TCS.HoboBot;

public class BotService : IHostedService {
    readonly DiscordSocketClient m_client;
    readonly InteractionService m_interactions;
    readonly IServiceProvider m_services;
    readonly IConfiguration m_config;

    //const ulong GUILD_ID = 1047781241010794506;

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
        var config = new ConfigurationBuilder()
            .AddUserSecrets<Program>()
            .Build();

        bool tryParse = ulong.TryParse(config["GUILD_ID"], out ulong guildId);
        // //log the guildId
        // Console.WriteLine( $"GUILD_ID: {guildId}" );
        //
        // if ( !tryParse ) {
        //     Console.WriteLine( "Error: GUILD_ID is not a valid ulong." );
        //     return;
        // }

        // register slash commands when the gateway is ready
        m_client.Ready += async () => {
            //log the guildId
            Console.WriteLine( $"GUILD_ID: {guildId}" );
        
            if ( !tryParse ) {
                Console.WriteLine( "Error: GUILD_ID is not a valid ulong." );
                return;
            }
            
            // Instant test-guild registration
            await m_interactions.RegisterCommandsToGuildAsync( guildId, deleteMissing: true );

            // Global registration takes up to 1 h – keep it commented for now (For production ready)
            // await _interactions.RegisterCommandsGloballyAsync();

            Console.WriteLine( $"✅  Logged in as {m_client.CurrentUser} ({m_client.CurrentUser.Id})" );
        };

        // interaction dispatcher
        m_client.InteractionCreated += async inter => {
            var ctx = new SocketInteractionContext( m_client, inter );
            await m_interactions.ExecuteCommandAsync( ctx, m_services );
        };

        await PlayersWallet.LoadAsync();
        await PlayersProperties.LoadAsync();
        

        string? token = config["DISCORD_TOKEN"];
        if ( string.IsNullOrEmpty( token ) ) {
            Console.WriteLine( "Error: DISCORD_TOKEN is missing." );
            return;
        }

        await m_client.LoginAsync( TokenType.Bot, token );
        await m_client.StartAsync();
    }

    public async Task StopAsync(CancellationToken ct) {
        await PlayersWallet.SaveAsync();
        await PlayersProperties.SaveAsync();

        await m_client.LogoutAsync();
        await m_client.StopAsync();
    }

    static Task LogAsync(LogMessage msg) {
        Console.WriteLine( msg );
        return Task.CompletedTask;
    }
}