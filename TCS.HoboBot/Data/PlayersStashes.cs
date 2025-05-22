using System.Collections.Concurrent;
using System.Text.Json;
using TCS.HoboBot.Modules.Moderation;
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
public enum DrugType { Weed, Cocaine, Heroin, Crack, Meth, Lsd, Shrooms, Ecstasy, }
public static class PlayersStashes {
    public static readonly ConcurrentDictionary<ulong, PlayerStash> Stash = new();
    public static readonly ConcurrentDictionary<ulong, DateTimeOffset> NextWeedGrow = new();
    public static readonly ConcurrentDictionary<ulong, DateTimeOffset> NextShroomGrow = new();

    public static readonly ConcurrentDictionary<ulong, DateTimeOffset> NextCocaineCook = new();
    public static readonly ConcurrentDictionary<ulong, DateTimeOffset> NextHeroinCook = new();
    public static readonly ConcurrentDictionary<ulong, DateTimeOffset> NextCrackCook = new();
    public static readonly ConcurrentDictionary<ulong, DateTimeOffset> NextMethCook = new();
    public static readonly ConcurrentDictionary<ulong, DateTimeOffset> NextLsdCook = new();
    public static readonly ConcurrentDictionary<ulong, DateTimeOffset> NextEcstasyCook = new();


    public static readonly TimeSpan GrowCooldown = TimeSpan.FromMinutes( 30 );
    public static readonly TimeSpan CookCooldown = TimeSpan.FromMinutes( 30 );

    static readonly string FilePath = "playerStashes.json";

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
            _ => throw new ArgumentOutOfRangeException( nameof(type), type, null )
        };
    }

    public static float GetTotalSellAmountFromUser(ulong userId) {
        var stash = GetStash( userId );
        return stash.WeedAmount * GetPriceByType( DrugType.Weed ) +
               stash.ShroomsAmount * GetPriceByType( DrugType.Shrooms ) +
               stash.CocaineAmount * GetPriceByType( DrugType.Cocaine ) +
               stash.HeroinAmount * GetPriceByType( DrugType.Heroin ) +
               stash.CrackAmount * GetPriceByType( DrugType.Crack ) +
               stash.MethAmount * GetPriceByType( DrugType.Meth ) +
               stash.LsdAmount * GetPriceByType( DrugType.Lsd ) +
               stash.EcstasyAmount * GetPriceByType( DrugType.Ecstasy );
    }

    public static PlayerStash GetStash(ulong userId) {
        if ( !Stash.TryGetValue( userId, out var stash ) ) {
            stash = new PlayerStash();
            Stash[userId] = stash;
        }

        return stash;
    }

    public static void ResetStash(ulong userId) {
        Stash[userId] = new PlayerStash();
    }


    public static async Task SaveAsync() {
        string json = Serialize( Stash );
        await File.WriteAllTextAsync( FilePath, json );
    }

    public static async Task LoadAsync() {
        if ( File.Exists( FilePath ) ) {
            string json = await File.ReadAllTextAsync( FilePath );
            ConcurrentDictionary<ulong, PlayerStash>? loaded = Deserialize<ConcurrentDictionary<ulong, PlayerStash>>( json );
            if ( loaded != null ) {
                foreach (KeyValuePair<ulong, PlayerStash> kv in loaded) {
                    Stash[kv.Key] = kv.Value;
                }
            }
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
            _ => throw new ArgumentOutOfRangeException( nameof(type), type, null )
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
    }

    public bool HasAnyDrugs() {
        return WeedAmount > 0 || ShroomsAmount > 0 || CocaineAmount > 0 || HeroinAmount > 0 ||
               CrackAmount > 0 || MethAmount > 0 || LsdAmount > 0 || EcstasyAmount > 0;
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

        return lines.Count == 0 ? "No drugs in stash." : string.Join( "\n", lines );
    }
}