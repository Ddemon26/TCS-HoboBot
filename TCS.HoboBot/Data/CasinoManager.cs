using System.Collections.Concurrent;
using Discord.Rest;
using Discord.WebSocket;
using Newtonsoft.Json;
namespace TCS.HoboBot.Modules.CasinoGames.Slots;

public static class DiscordGuildMemberCache {
    public static readonly ConcurrentDictionary<ulong, RestGuildUser[]> GuildMembers = new();

    public static void AddMembers(ulong guildId, RestGuildUser[] members) {
        GuildMembers[guildId] = members;
    }

    public static RestGuildUser? GetUser(ulong guildId, ulong userId)
        => GuildMembers
            .TryGetValue( guildId, out RestGuildUser?[]? members ) ? members
            .OfType<RestGuildUser>()
            .FirstOrDefault( member => member.Id == userId ) : null;
}

public static class CasinoManager {
    public class JackPots {
        public ulong GuildId { get; set; }
        public float MegaJackpot { get; set; }
        public float ProgressiveJackpot { get; set; }
        public float MiniJackpot { get; set; }
    }

    static readonly ConcurrentDictionary<ulong, JackPots> JackPotsCache = new();

    public const float MEGA_CHANCE = 0.0001f;
    public const float PROGRESSIVE_CHANCE = 0.001f;
    public const float MINI_CHANCE = 0.01f;

    const string FILE_PATH = "jackpots.json";
    static string GetFilePath(ulong guildId) => Path.Combine( "Data", guildId.ToString(), FILE_PATH );

    public static void AddToSlotsJackpots(ulong guildId, float amount) {
        if ( JackPotsCache.TryGetValue( guildId, out var jackpots ) ) {
            jackpots.GuildId = guildId;
            jackpots.MegaJackpot += amount * 0.5f;
            jackpots.ProgressiveJackpot += amount * 0.3f;
            jackpots.MiniJackpot += amount * 0.2f;

        }
        else {
            JackPotsCache[guildId] = new JackPots {
                GuildId = guildId,
                MegaJackpot = amount * 0.5f,
                ProgressiveJackpot = amount * 0.3f,
                MiniJackpot = amount * 0.2f,
            };
        }
    }

    // we are given our guild id when saving the jackpots
    public static async Task SaveAsync() {
        foreach ((ulong guildId, var jackpots) in JackPotsCache) {
            string dir = Path.Combine( "Data", guildId.ToString() );
            Directory.CreateDirectory( dir );

            string path = GetFilePath( guildId );
            string json = Serialize( jackpots );

            await File.WriteAllTextAsync( path, json );
        }
    }

    // we are given our guild id when loading the jackpots
    public static async Task LoadAsync(IReadOnlyCollection<SocketGuild> clientGuilds) {
        const string root = "Data";

        foreach (var guild in clientGuilds) {
            string dir = Path.Combine( root, guild.Id.ToString() );
            Directory.CreateDirectory( dir );

            string path = GetFilePath( guild.Id );
            if ( !File.Exists( path ) ) {
                continue;
            }

            string json = await File.ReadAllTextAsync( path );
            var loaded = Deserialize<JackPots>( json );
            if ( loaded is null ) {
                continue;
            }

            JackPotsCache[guild.Id] = loaded;
        }
    }

    static readonly JsonSerializerSettings WriteSettings = new() {
        Formatting = Formatting.Indented,
    };

    static readonly JsonSerializerSettings ReadSettings = new() {
        MissingMemberHandling = MissingMemberHandling.Ignore,
        FloatParseHandling = FloatParseHandling.Double,
    };

    static string Serialize<T>(T value) => JsonConvert.SerializeObject( value, WriteSettings );

    static T? Deserialize<T>(string json) => JsonConvert.DeserializeObject<T>( json, ReadSettings );
}