using System.Collections.Concurrent;
using System.Text.Json;
using Discord.WebSocket;

namespace TCS.HoboBot.Data;

public class PlayerWallet {
    public string UserName { get; set; } = string.Empty;
    public float Cash { get; set; }
}

public static class PlayersWallet {
    static readonly ConcurrentDictionary<ulong, ConcurrentDictionary<ulong, float>> GlobalPlayerWallets = new();
    public static readonly ConcurrentDictionary<ulong, ConcurrentDictionary<ulong, PlayerWallet>> PlayerWallets = new();

    static readonly string FilePath = "wallets.json";
    static readonly string FilePath2 = "PlayerWallets.json";
    static string GetFilePath(ulong guildId) {
        return Path.Combine( "Data", guildId.ToString(), FilePath );
    }

    public static float GetBalance(ulong guildId, ulong userId) {
        // ConcurrentDictionary<ulong, float> guildWallets = GlobalPlayerWallets.GetOrAdd(
        //     guildId,
        //     _ => new ConcurrentDictionary<ulong, float>()
        // );
        //
        // return guildWallets.GetValueOrDefault( userId, 0f );

        //get from PlayerWallets
        if ( PlayerWallets.TryGetValue( guildId, out ConcurrentDictionary<ulong, PlayerWallet>? playerWallets ) ) {
            if ( playerWallets.TryGetValue( userId, out var playerWallet ) ) {
                return playerWallet.Cash;
            }
        }

        return 0f;
    }

    public static void AddToBalance(ulong guildId, ulong userId, float amount) {
        ConcurrentDictionary<ulong, float> guildWallets = GlobalPlayerWallets.GetOrAdd(
            guildId,
            _ => new ConcurrentDictionary<ulong, float>()
        );

        guildWallets[userId] = guildWallets.TryGetValue( userId, out float old ) ? old + amount : amount;

        //add to PlayerWallets as well
        if ( PlayerWallets.TryGetValue( guildId, out ConcurrentDictionary<ulong, PlayerWallet>? playerWallets ) ) {
            if ( playerWallets.TryGetValue( userId, out var playerWallet ) ) {
                playerWallet.Cash += amount;
            }
            else {
                playerWallet = new PlayerWallet {
                    Cash = amount,
                };
                playerWallets[userId] = playerWallet;
            }
        }
        
        //Console.Write(  $"Added {amount} to {userId} in {guildId} " );
    }

    public static void SubtractFromBalance(ulong guildId, ulong userId, float amount) {
        // Convert any negative amount to a positive value.
        amount = MathF.Abs(amount);

        ConcurrentDictionary<ulong, float> guildWallets = GlobalPlayerWallets.GetOrAdd(
            guildId,
            _ => new ConcurrentDictionary<ulong, float>()
        );

        float newAmount = guildWallets.TryGetValue(userId, out float old)
            ? MathF.Max(0f, old - amount)
            : 0f;
        guildWallets[userId] = newAmount;

        // Subtract from PlayerWallets as well
        if (PlayerWallets.TryGetValue(guildId, out ConcurrentDictionary<ulong, PlayerWallet>? playerWallets)) {
            if (playerWallets.TryGetValue(userId, out var playerWallet)) {
                playerWallet.Cash = MathF.Max(0f, playerWallet.Cash - amount);
            }
        }
    }

    public static async Task SaveAsync() {
        foreach (KeyValuePair<ulong, ConcurrentDictionary<ulong, float>> kv in GlobalPlayerWallets) {
            await SaveAsync( kv.Key );
        }
    }

    static async Task SaveAsync(ulong guildId) {
        string dir = Path.Combine( "Data", guildId.ToString() );
        Directory.CreateDirectory( dir );
        string path = GetFilePath( guildId );

        if ( !GlobalPlayerWallets.TryGetValue( guildId, out ConcurrentDictionary<ulong, float>? guildWallets ) ) {
            guildWallets = new ConcurrentDictionary<ulong, float>();
        }

        string json = Serialize( guildWallets );
        await File.WriteAllTextAsync( path, json );

        // string path2 = Path.Combine( "Data", guildId.ToString(), FilePath2 );
        // if ( !PlayerWallets.TryGetValue( guildId, out ConcurrentDictionary<ulong, PlayerWallet>? guildWallets2 ) ) {
        //     guildWallets2 = new ConcurrentDictionary<ulong, PlayerWallet>();  
        // }
        //
        // string json2 = Serialize( guildWallets2 );
        // await File.WriteAllTextAsync( path2, json2 );

        // migrate raw floats into PlayerWallets
        ConcurrentDictionary<ulong, float> floatWallets = GlobalPlayerWallets.GetOrAdd( guildId, _ => new ConcurrentDictionary<ulong, float>() );
        ConcurrentDictionary<ulong, PlayerWallet> migrated = new(
            floatWallets.Select( kv => new KeyValuePair<ulong, PlayerWallet>(
                                     kv.Key,
                                     new PlayerWallet {
                                         Cash = kv.Value,
                                     }
                                 )
            )
        );

// replace or fill the PlayerWallets entry
        PlayerWallets[guildId] = migrated;

// now serialize and save as before
        string path2 = Path.Combine( "Data", guildId.ToString(), FilePath2 );
        string json2 = Serialize( migrated );
        await File.WriteAllTextAsync( path2, json2 );
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
            ConcurrentDictionary<ulong, float>? loaded = Deserialize<ConcurrentDictionary<ulong, float>>( json );
            if ( loaded is null ) {
                continue;
            }

            GlobalPlayerWallets[guild.Id] = loaded;

            string path2 = Path.Combine( root, guild.Id.ToString(), FilePath2 );

            string json2 = await File.ReadAllTextAsync( path2 );
            ConcurrentDictionary<ulong, PlayerWallet>? loaded2 = Deserialize<ConcurrentDictionary<ulong, PlayerWallet>>( json2 );
            if ( loaded2 is null ) {
                continue;
            }

            PlayerWallets[guild.Id] = loaded2;
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