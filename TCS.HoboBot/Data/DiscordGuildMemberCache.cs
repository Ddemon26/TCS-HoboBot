using System.Collections.Concurrent;
using Discord.Rest;
namespace TCS.HoboBot.Modules.CasinoGames.Slots;

public static class DiscordGuildMemberCache {
    public static readonly ConcurrentDictionary<ulong, RestGuildUser[]> GuildMembers = new();

    public static void AddMembers(ulong guildId, RestGuildUser[] members) => GuildMembers[guildId] = members;

    public static RestGuildUser? GetUser(ulong guildId, ulong userId) =>
        GuildMembers.TryGetValue( guildId, out RestGuildUser?[]? members )
            ? members.OfType<RestGuildUser>().FirstOrDefault( m => m.Id == userId )
            : null;
}