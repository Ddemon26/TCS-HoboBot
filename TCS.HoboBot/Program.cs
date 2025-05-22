using Discord;
using Discord.WebSocket;
using Discord.Interactions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using TCS.HoboBot;
using TCS.HoboBot.Data;
using TCS.HoboBot.Modules;
using TCS.HoboBot.Modules.Moderation;

var host = Host.CreateDefaultBuilder( args )
    .ConfigureServices( (_, services) => {
            // Configure the shutdown timeout for the host
            services.Configure<HostOptions>(opt => 
                                                opt.ShutdownTimeout = TimeSpan.FromSeconds(30));

            
            // DiscordSocketClient config
            var discordSocketConfig = new DiscordSocketConfig {
                GatewayIntents = GatewayIntents.Guilds |
                                 GatewayIntents.GuildMessages |
                                 GatewayIntents.MessageContent |
                                 GatewayIntents.GuildIntegrations |
                                 GatewayIntents.GuildMembers
                // | GatewayIntents.All,
            };
            services.AddSingleton( discordSocketConfig );
            services.AddSingleton<DiscordSocketClient>();

            // InteractionService config explicitly provided
            var interactionServiceConfig = new InteractionServiceConfig {
                LogLevel = LogSeverity.Info,
                UseCompiledLambda = true,
                DefaultRunMode = RunMode.Async,
            };

            // InteractionService with explicit constructor
            services.AddSingleton<InteractionService>( sp => {
                    var client = sp.GetRequiredService<DiscordSocketClient>();
                    return new InteractionService( client, interactionServiceConfig );
                }
            );

            services.AddHostedService<BotService>();
            services.AddHostedService<MessageResponder>();
            services.AddHostedService<RoleService>();
        }
    )
    .UseConsoleLifetime()
    .Build();

await host.RunAsync();