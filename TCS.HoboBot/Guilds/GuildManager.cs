using Discord.Rest;
using Discord.WebSocket;
namespace TCS.HoboBot.Guilds;

public static class GuildManager {
    static DiscordSocketClient? m_client;
    
    public static Task<bool> Initialize(DiscordSocketClient client) {
        if ( m_client != null ) {
            return Task.FromResult( false ); // Already initialized
        }

        m_client = client ?? throw new ArgumentNullException( nameof(client) );
        return Task.FromResult( true );
    }

    public static async Task<RestGuildUser?> GetGuildMember(ulong guildId, ulong userId) {
        if ( m_client == null ) return null;
        var user = await m_client.Rest.GetGuildUserAsync( guildId, userId );

        return user;
    }
}