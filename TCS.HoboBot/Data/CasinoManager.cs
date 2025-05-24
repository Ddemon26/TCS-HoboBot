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

public enum JackpotType {
    MegaJackpot,
    ProgressiveJackpot,
    MiniJackpot,
}

public static class CasinoManager {
    public class JackPots {
        public ulong GuildId { get; set; }
        public float SlotsMegaJackpot { get; set; }
        public float SlotProgressiveJackpot { get; set; }
        public float SlotMiniJackpot { get; set; }
        
        public override string ToString() {
            return $"**Current Jackpots**\n" +
                   $" Mega: {SlotsMegaJackpot}\n" +
                   $" Progressive: {SlotProgressiveJackpot}\n" +
                   $" Mini: {SlotMiniJackpot}";
        }
    }
    
    static readonly Random Rng = new();

    public static readonly ConcurrentDictionary<ulong, JackPots> JackPotsCache = new();

    // public const float MEGA_CHANCE = 0.0001f; // 1 in 1,000,000
    // public const float PROGRESSIVE_CHANCE = 0.001f; // 1 in 100,000
    // public const float MINI_CHANCE = 0.01f; // 1 in 10,000

    public const float MEGA_CHANCE = 0.001f; // 1 in 100,000
    public const float PROGRESSIVE_CHANCE = 0.01f; // 1 in 10,000
    public const float MINI_CHANCE = 0.1f; // 1 in 1,000

    // public const float MEGA_CHANCE = 10f; // 1 in 10
    // public const float PROGRESSIVE_CHANCE = 25f; // 1 in 4
    // public const float MINI_CHANCE = 50f; // 1 in 2

    const string FILE_PATH = "jackpots.json";
    static string GetFilePath(ulong guildId) => Path.Combine( "Data", guildId.ToString(), FILE_PATH );

    public static void AddToSlotsJackpots(ulong guildId, float amount) {
        float cutAmount = amount * 0.5f;
        if ( JackPotsCache.TryGetValue( guildId, out var jackpots ) ) {
            jackpots.GuildId = guildId;
            jackpots.SlotsMegaJackpot += cutAmount * 0.5f;
            jackpots.SlotProgressiveJackpot += cutAmount * 0.3f;
            jackpots.SlotMiniJackpot += cutAmount * 0.2f;

        }
        else {
            JackPotsCache[guildId] = new JackPots {
                GuildId = guildId,
                SlotsMegaJackpot = cutAmount * 0.5f,
                SlotProgressiveJackpot = cutAmount * 0.3f,
                SlotMiniJackpot = cutAmount * 0.2f,
            };
        }
    }

    public static bool GetJackpot(ulong guildId, JackpotType type, out float jackpot) {
        if ( JackPotsCache.TryGetValue( guildId, out var jackpots ) ) {
            float chance = type switch {
                JackpotType.MegaJackpot => MEGA_CHANCE,
                JackpotType.ProgressiveJackpot => PROGRESSIVE_CHANCE,
                JackpotType.MiniJackpot => MINI_CHANCE,
                _ => 0f,
            };

            double roll = Rng.NextDouble() * 100;
            if ( roll < chance ) {
                jackpot = type switch {
                    JackpotType.MegaJackpot => jackpots.SlotsMegaJackpot,
                    JackpotType.ProgressiveJackpot => jackpots.SlotProgressiveJackpot,
                    JackpotType.MiniJackpot => jackpots.SlotMiniJackpot,
                    _ => 0f,
                };

                // Reset the jackpot value after winning
                switch (type) {
                    case JackpotType.MegaJackpot:
                        jackpots.SlotsMegaJackpot = 0;
                        break;
                    case JackpotType.ProgressiveJackpot:
                        jackpots.SlotProgressiveJackpot = 0;
                        break;
                    case JackpotType.MiniJackpot:
                        jackpots.SlotMiniJackpot = 0;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException( nameof(type), type, null );
                }

                return true;
            }
        }

        jackpot = 0f;
        return false;
    }

    #region Save/Load
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
    #endregion
}