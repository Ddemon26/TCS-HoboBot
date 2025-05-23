using Discord;
using Discord.WebSocket;
using Discord.Interactions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using TCS.HoboBot;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((_, services) =>
    {
        services.Configure<HostOptions>(opt => opt.ShutdownTimeout = TimeSpan.FromSeconds(30));

        var discordConfig = new DiscordSocketConfig
        {
            GatewayIntents = GatewayIntents.Guilds |
                             GatewayIntents.GuildMessages |
                             GatewayIntents.MessageContent |
                             GatewayIntents.GuildIntegrations |
                             GatewayIntents.GuildMembers
        };
        services.AddSingleton(discordConfig);
        services.AddSingleton<DiscordSocketClient>();

        var interactionConfig = new InteractionServiceConfig
        {
            LogLevel = LogSeverity.Info,
            UseCompiledLambda = true,
            DefaultRunMode = RunMode.Async
        };
        services.AddSingleton(sp =>
            new InteractionService(sp.GetRequiredService<DiscordSocketClient>(), interactionConfig));

        services.AddHostedService<BotService>();
        services.AddHostedService<MessageResponder>();
        services.AddHostedService<RoleService>();
    })
    .UseConsoleLifetime() // registers Ctrl+C/SIGTERM handlers
    .Build();

// Hook into the host’s stopping event
// var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
// lifetime.ApplicationStopping.Register(() =>
// {
//     var bot = host.Services.GetRequiredService<BotService>();
//     bot.SaveDataOnShutdown().GetAwaiter().GetResult();
// });

await host.RunAsync();