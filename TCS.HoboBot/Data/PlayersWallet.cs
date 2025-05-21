using System.Collections.Concurrent;
using System.Text.Json;

public static class PlayersWallet {
    public static readonly ConcurrentDictionary<ulong, float> Cash = new();
    
    public static readonly ConcurrentDictionary<ulong, DateTimeOffset> NextBeg = new();
    public static readonly TimeSpan BegCooldown = TimeSpan.FromSeconds( 5 );
    
    public static readonly ConcurrentDictionary<ulong, DateTimeOffset> NextJob = new();
    public static readonly TimeSpan JobCooldown = TimeSpan.FromMinutes( 10 );
    
    public static ConcurrentDictionary<ulong, DateTimeOffset> NextRob = new();
    public static readonly TimeSpan RobCooldown = TimeSpan.FromMinutes( 10 );
    static readonly string FilePath = "wallets.json";

    public static float GetBalance(ulong userId) => Cash.GetValueOrDefault( userId, 0f );
    public static void SetBalance(ulong userId, float amount) => Cash[userId] = amount;
    public static void AddToBalance(ulong userId, float amount) {
        Cash.AddOrUpdate( userId, amount, (_, old) => old + amount );
    }
    public static void SubtractFromBalance(ulong userId, float amount) {
        Cash.AddOrUpdate( userId, 0f, (_, old) => MathF.Max( 0f, old - amount ) );
    }
    public static void ResetBalance(ulong userId) => Cash[userId] = 0f;
    public static void ResetAllBalances() => Cash.Clear();

    public static async Task LoadAsync() {
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

    public static async Task SaveAsync() {
        string json = Serialize( Cash );
        await File.WriteAllTextAsync( FilePath, json );
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