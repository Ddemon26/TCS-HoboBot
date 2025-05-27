using System.Collections.Concurrent;
using System.Text.Json;
using Discord.WebSocket;
namespace TCS.HoboBot.Modules.DrugDealer;

// public enum DealerRole {
//     LowLevelDealer,
//     PettyDrugDealer,
//     StreetDealer,
//     Pimp,
//     Kingpin,
//     DrugLord,
//     Underboss,
//     Godfather
// }
public enum DrugType { Weed, Shrooms, Cocaine, Heroin, Crack, Meth, Lsd, Ecstasy, Dmt }

public static class PlayersStashes {
    //public static readonly ConcurrentDictionary<ulong, PlayerStash> Stash = new();
    // public static readonly ConcurrentDictionary<ulong, DateTimeOffset> NextWeedGrow = new();
    // public static readonly ConcurrentDictionary<ulong, DateTimeOffset> NextShroomGrow = new();
    //
    // public static readonly ConcurrentDictionary<ulong, DateTimeOffset> NextCocaineCook = new();
    // public static readonly ConcurrentDictionary<ulong, DateTimeOffset> NextHeroinCook = new();
    // public static readonly ConcurrentDictionary<ulong, DateTimeOffset> NextCrackCook = new();
    // public static readonly ConcurrentDictionary<ulong, DateTimeOffset> NextMethCook = new();
    // public static readonly ConcurrentDictionary<ulong, DateTimeOffset> NextLsdCook = new();
    // public static readonly ConcurrentDictionary<ulong, DateTimeOffset> NextEcstasyCook = new();
    // public static readonly ConcurrentDictionary<ulong, DateTimeOffset> NextDmtCook = new();


    public static readonly TimeSpan GrowCooldown = TimeSpan.FromMinutes( 30 );
    public static readonly TimeSpan CookCooldown = TimeSpan.FromMinutes( 30 );
    
    // (userId, drugType)  →  nextTime
    public static readonly ConcurrentDictionary<(ulong, DrugType), DateTimeOffset> NextGrow 
        = new();

    // public static readonly ConcurrentDictionary<(ulong, DrugType), DateTimeOffset> NextCook 
    //     = new();

// Helpers ----------------------------------------------------------

    static (ulong, DrugType) Key(ulong userId, DrugType drug) => (userId, drug);

    public static bool IsOnCooldown(ulong userId, DrugType drug, out TimeSpan remaining)
    {
        if (NextGrow.TryGetValue(Key(userId, drug), out var next))
        {
            remaining = next - DateTimeOffset.UtcNow;
            return remaining > TimeSpan.Zero;
        }
        remaining = TimeSpan.Zero;
        return false;
    }

    public static void StartCooldown(ulong userId, DrugType drug)
        => NextGrow[Key(userId, drug)] = DateTimeOffset.UtcNow + GrowCooldown;


    static readonly string FilePath = "playerStashes.json";
    static string GetFilePath(ulong guildId) => Path.Combine( "Data", guildId.ToString(), FilePath );

    static readonly ConcurrentDictionary<ulong, ConcurrentDictionary<ulong, PlayerStash>> GlobalStashCache = new();

    public static float GetPriceByType(DrugType type) {
        return type switch {
            DrugType.Weed => 10,
            DrugType.Shrooms => 15,
            DrugType.Cocaine => 100,
            DrugType.Heroin => 200,
            DrugType.Crack => 300,
            DrugType.Meth => 400,
            DrugType.Lsd => 500,
            DrugType.Ecstasy => 700,
            DrugType.Dmt => 1000,
            _ => throw new ArgumentOutOfRangeException( nameof(type), type, null ),
        };
    }

    public static float GetTotalSellAmountFromUser(ulong guildId, ulong userId) {
        var stash = GetStash( guildId, userId );
        return stash.WeedAmount * GetPriceByType( DrugType.Weed ) +
               stash.ShroomsAmount * GetPriceByType( DrugType.Shrooms ) +
               stash.CocaineAmount * GetPriceByType( DrugType.Cocaine ) +
               stash.HeroinAmount * GetPriceByType( DrugType.Heroin ) +
               stash.CrackAmount * GetPriceByType( DrugType.Crack ) +
               stash.MethAmount * GetPriceByType( DrugType.Meth ) +
               stash.LsdAmount * GetPriceByType( DrugType.Lsd ) +
               stash.EcstasyAmount * GetPriceByType( DrugType.Ecstasy ) +
               stash.DmtAmount * GetPriceByType( DrugType.Dmt );

    }

    public static PlayerStash GetStash(ulong guildId, ulong userId) {
        ConcurrentDictionary<ulong, PlayerStash> guildStashes = GlobalStashCache.GetOrAdd(
            guildId,
            _ => new ConcurrentDictionary<ulong, PlayerStash>()
        );

        if ( guildStashes.TryGetValue( userId, out var stash ) ) {
            return stash;
        }

        stash = new PlayerStash();
        guildStashes[userId] = stash;

        return stash;
    }

    //save stash to the cache
    public static void SaveStash(ulong guildId, ulong userId, PlayerStash stash) {
        ConcurrentDictionary<ulong, PlayerStash> guildStashes = GlobalStashCache.GetOrAdd(
            guildId,
            _ => new ConcurrentDictionary<ulong, PlayerStash>()
        );

        guildStashes[userId] = stash;
    }

    public static async Task SaveAsync() {
        foreach (KeyValuePair<ulong, ConcurrentDictionary<ulong, PlayerStash>> kv in GlobalStashCache) {
            await SaveAsync( kv.Key );
        }

        //clear the cache
        GlobalStashCache.Clear();
    }

    static async Task SaveAsync(ulong guildId) {
        string dir = Path.Combine( "Data", guildId.ToString() );
        Directory.CreateDirectory( dir );
        string path = GetFilePath( guildId );
        if ( !GlobalStashCache.TryGetValue( guildId, out ConcurrentDictionary<ulong, PlayerStash>? guildStashes ) ) {
            guildStashes = new ConcurrentDictionary<ulong, PlayerStash>();
        }

        string json = Serialize( guildStashes );
        await File.WriteAllTextAsync( path, json );
    }

    public static async Task LoadAsync(IReadOnlyCollection<SocketGuild> clientGuilds) {
        const string root = "Data";
        // clear the cache
        GlobalStashCache.Clear();

        foreach (var guild in clientGuilds) {
            string dir = Path.Combine( root, guild.Id.ToString() );
            Directory.CreateDirectory( dir );

            string path = GetFilePath( guild.Id );
            if ( !File.Exists( path ) ) {
                continue;
            }

            string json = await File.ReadAllTextAsync( path );
            ConcurrentDictionary<ulong, PlayerStash>? loaded = Deserialize<ConcurrentDictionary<ulong, PlayerStash>>( json );
            if ( loaded is null ) {
                continue;
            }

            GlobalStashCache[guild.Id] = loaded;
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

[Serializable] public class PlayerStash {
    public DealerRole Role { get; set; } = DealerRole.LowLevelDealer;
    public int WeedAmount { get; set; }
    public int ShroomsAmount { get; set; }
    public int CocaineAmount { get; set; }
    public int HeroinAmount { get; set; }
    public int CrackAmount { get; set; }
    public int MethAmount { get; set; }
    public int LsdAmount { get; set; }
    public int EcstasyAmount { get; set; }
    public int DmtAmount { get; set; }

    public float TotalCashAcquiredFromSelling { get; set; }

    public int GetAmountByType(DrugType type) {
        return type switch {
            DrugType.Weed => WeedAmount,
            DrugType.Shrooms => ShroomsAmount,
            DrugType.Cocaine => CocaineAmount,
            DrugType.Heroin => HeroinAmount,
            DrugType.Crack => CrackAmount,
            DrugType.Meth => MethAmount,
            DrugType.Lsd => LsdAmount,
            DrugType.Ecstasy => EcstasyAmount,
            DrugType.Dmt => DmtAmount,
            _ => throw new ArgumentOutOfRangeException( nameof(type), type, null ),
        };
    }

    //add drug amount by type
    public void AddAmountToType(DrugType type, int amount) {
        switch (type) {
            case DrugType.Weed:
                WeedAmount += amount;
                break;
            case DrugType.Shrooms:
                ShroomsAmount += amount;
                break;
            case DrugType.Cocaine:
                CocaineAmount += amount;
                break;
            case DrugType.Heroin:
                HeroinAmount += amount;
                break;
            case DrugType.Crack:
                CrackAmount += amount;
                break;
            case DrugType.Meth:
                MethAmount += amount;
                break;
            case DrugType.Lsd:
                LsdAmount += amount;
                break;
            case DrugType.Ecstasy:
                EcstasyAmount += amount;
                break;
            case DrugType.Dmt:
                DmtAmount += amount;
                break;
            default:
                throw new ArgumentOutOfRangeException( nameof(type), type, null );
        }
    }

    public void RemoveAllAmounts() {
        WeedAmount = 0;
        ShroomsAmount = 0;
        CocaineAmount = 0;
        HeroinAmount = 0;
        CrackAmount = 0;
        MethAmount = 0;
        LsdAmount = 0;
        EcstasyAmount = 0;
        DmtAmount = 0;
    }

    public bool HasAnyDrugs() {
        return WeedAmount > 0 || ShroomsAmount > 0 || CocaineAmount > 0 || HeroinAmount > 0 ||
               CrackAmount > 0 || MethAmount > 0 || LsdAmount > 0 || EcstasyAmount > 0 || DmtAmount > 0;
    }

    public string GetDrugsString() {
        List<string> lines = new();

        if ( WeedAmount > 0 ) {
            lines.Add( $"Weed: {10:N0} ({WeedAmount})" );
        }

        if ( ShroomsAmount > 0 ) {
            lines.Add( $"Shrooms: {15:N0} ({ShroomsAmount})" );
        }

        if ( CocaineAmount > 0 ) {
            lines.Add( $"Cocaine: {100:N0} ({CocaineAmount})" );
        }

        if ( HeroinAmount > 0 ) {
            lines.Add( $"Heroin: {200:N0} ({HeroinAmount})" );
        }

        if ( CrackAmount > 0 ) {
            lines.Add( $"Crack: {300:N0} ({CrackAmount})" );
        }

        if ( MethAmount > 0 ) {
            lines.Add( $"Meth: {400:N0} ({MethAmount})" );
        }

        if ( LsdAmount > 0 ) {
            lines.Add( $"Lsd: {500:N0} ({LsdAmount})" );
        }

        if ( EcstasyAmount > 0 ) {
            lines.Add( $"Ecstasy: {700:N0} ({EcstasyAmount})" );
        }

        if ( DmtAmount > 0 ) {
            lines.Add( $"Dmt: {1000:N0} ({DmtAmount})" );
        }

        return lines.Count == 0 ? "No drugs in stash." : string.Join( "\n", lines );
    }
}