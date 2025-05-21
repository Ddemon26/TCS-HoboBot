using Discord;
using Discord.WebSocket;
using Discord.Interactions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using MyBot.Middleware;

var host = Host.CreateDefaultBuilder( args )
    .ConfigureServices( (_, services) => {
            // DiscordSocketClient config
            var discordSocketConfig = new DiscordSocketConfig {
                GatewayIntents = GatewayIntents.Guilds |
                                 GatewayIntents.GuildMessages |
                                 GatewayIntents.MessageContent |
                                 GatewayIntents.GuildMessages,
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
        }
    ).Build();

await host.RunAsync();