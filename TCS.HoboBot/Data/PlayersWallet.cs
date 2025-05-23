using System.Collections.Concurrent;
using System.Text.Json;
using Discord.WebSocket;

namespace TCS.HoboBot.Data;

public static class PlayersWallet {
    //public static readonly ConcurrentDictionary<ulong, float> Cash = new();
    static readonly ConcurrentDictionary<ulong, Dictionary<ulong, float>> GlobalPlayerWallets = new();

    public static readonly ConcurrentDictionary<ulong, DateTimeOffset> NextBeg = new();
    public static readonly TimeSpan BegCooldown = TimeSpan.FromSeconds( 5 );

    public static readonly ConcurrentDictionary<ulong, DateTimeOffset> NextJob = new();
    public static readonly TimeSpan JobCooldown = TimeSpan.FromMinutes( 10 );

    public static readonly ConcurrentDictionary<ulong, DateTimeOffset> NextRob = new();
    public static readonly TimeSpan RobCooldown = TimeSpan.FromMinutes( 10 );
    static readonly string FilePath = "wallets.json";
    static string GetFilePath(ulong guildId) {
        return Path.Combine( "Data", guildId.ToString(), FilePath );
    }

    public static float GetBalance(ulong guildId, ulong userId) {
        Dictionary<ulong, float> guildWallets = GlobalPlayerWallets.GetOrAdd(
            guildId,
            _ => new Dictionary<ulong, float>()
        );

        return guildWallets.GetValueOrDefault( userId, 0f );
    }

    public static void SetBalance(ulong guildId, ulong userId, float amount) {
        Dictionary<ulong, float> guildWallets = GlobalPlayerWallets.GetOrAdd(
            guildId,
            _ => new Dictionary<ulong, float>()
        );

        guildWallets[userId] = amount;
    }

    public static void AddToBalance(ulong guildId, ulong userId, float amount) {
        Dictionary<ulong, float> guildWallets = GlobalPlayerWallets.GetOrAdd(
            guildId,
            _ => new Dictionary<ulong, float>()
        );

        guildWallets[userId] = guildWallets.TryGetValue( userId, out float old ) ? old + amount : amount;
    }

    public static void SubtractFromBalance(ulong guildId, ulong userId, float amount) {
        Dictionary<ulong, float> guildWallets = GlobalPlayerWallets.GetOrAdd(
            guildId,
            _ => new Dictionary<ulong, float>()
        );

        float newAmount = guildWallets.TryGetValue( userId, out float old )
            ? MathF.Max( 0f, old - amount )
            : 0f;

        guildWallets[userId] = newAmount;
    }

    public static void ResetBalance(ulong guildId, ulong userId) {
        Dictionary<ulong, float> guildWallets = GlobalPlayerWallets.GetOrAdd(
            guildId,
            _ => new Dictionary<ulong, float>()
        );

        guildWallets[userId] = 0f;
    }

    public static void ResetAllBalances(ulong guildId) {
        if ( GlobalPlayerWallets.TryGetValue( guildId, out Dictionary<ulong, float>? guildWallets ) )
            guildWallets.Clear();
    }
    
    public static string GetTopTenHobos(ulong guildId)
    {   
        Dictionary<ulong, float> guildWallets = GlobalPlayerWallets.GetOrAdd(
            guildId,
            _ => new Dictionary<ulong, float>()
        );
    
        string[] topHobos = guildWallets
            .OrderByDescending(kv => kv.Value)
            .Take(10)
            .Select(kv => $"{kv.Key}: ${kv.Value:0.00}")
            .ToArray();
    
        string result = string.Join("\n", topHobos);
        return result;
    }

    /*public static async Task LoadAsync() {
        if ( File.Exists( FilePath ) ) {
            string json = await File.ReadAllTextAsync( FilePath );
            ConcurrentDictionary<ulong, float>? loaded = Deserialize<ConcurrentDictionary<ulong, float>>( json );
            if ( loaded != null ) {
                foreach (KeyValuePair<ulong, float> kv in loaded) {
                    Cash[kv.Key] = kv.Value;
                }
            }
        }
    }

    public static async Task LoadAsync(ulong guildId) {
        string path = GetFilePath( guildId );
        if ( File.Exists( path ) ) {
            string json = await File.ReadAllTextAsync( path );
            ConcurrentDictionary<ulong, float>? loaded = Deserialize<ConcurrentDictionary<ulong, float>>( json );
            if ( loaded != null ) {
                foreach (KeyValuePair<ulong, float> kv in loaded) {
                    Cash[kv.Key] = kv.Value;
                }
            }
        }
    }*/

    public static async Task SaveAsync() {
        foreach (KeyValuePair<ulong, Dictionary<ulong, float>> kv in GlobalPlayerWallets) {
            await SaveAsync( kv.Key );
        }
    }

    public static async Task SaveAsync(ulong guildId) {
        string dir = Path.Combine( "Data", guildId.ToString() );
        Directory.CreateDirectory( dir );
        string path = GetFilePath( guildId );

        if ( !GlobalPlayerWallets.TryGetValue( guildId, out Dictionary<ulong, float>? guildWallets ) ) {
            guildWallets = new Dictionary<ulong, float>();
        }

        string json = Serialize( guildWallets );
        await File.WriteAllTextAsync( path, json );
    }

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
            Dictionary<ulong, float>? loaded = Deserialize<Dictionary<ulong, float>>( json );
            if ( loaded is null ) {
                continue;
            }

            GlobalPlayerWallets[guild.Id] = loaded;
        }
    }


    static readonly JsonSerializerOptions WriteOptions = new() {
        WriteIndented = true,
    };

    static readonly JsonSerializerOptions? ReadOptions = new() {
        AllowTrailingCommas = true,
    };

    static string Serialize<T>(T value) => JsonSerializer.Serialize( value, WriteOptions );
    static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>( json, ReadOptions );
}