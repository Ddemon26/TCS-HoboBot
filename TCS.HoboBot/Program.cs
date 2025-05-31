using Discord;
using Discord.WebSocket;
using Discord.Interactions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using TCS.HoboBot.Services;

var host = Host.CreateDefaultBuilder( args )
    .ConfigureAppConfiguration( (context, config) => {
            config.AddUserSecrets<Program>();
        }
    )
    .ConfigureServices( (context, services) => {
            services.Configure<HostOptions>( opt => opt.ShutdownTimeout = TimeSpan.FromSeconds( 30 ) );

            var discordConfig = new DiscordSocketConfig {
                GatewayIntents = GatewayIntents.Guilds
                                 | GatewayIntents.GuildMessages
                                 | GatewayIntents.MessageContent
                                 | GatewayIntents.GuildIntegrations
                                 | GatewayIntents.GuildMembers
                                 | GatewayIntents.GuildVoiceStates,
            };

            services.AddSingleton( discordConfig );
            services.AddSingleton<DiscordSocketClient>();

            var interactionConfig = new InteractionServiceConfig {
                LogLevel = LogSeverity.Info,
                UseCompiledLambda = true,
                DefaultRunMode = RunMode.Async,
            };

            services.AddSingleton
            ( sp => new InteractionService
              (
                  sp.GetRequiredService<DiscordSocketClient>(),
                  interactionConfig
              )
            );

            // My Custom Entry Points
            services.AddHostedService<BotService>();
            services.AddHostedService<MessageResponderService>();
            services.AddHostedService<RoleService>();
        }
    )
    .UseConsoleLifetime() // registers Ctrl+C/SIGTERM handlers
    .Build();

await host.RunAsync();