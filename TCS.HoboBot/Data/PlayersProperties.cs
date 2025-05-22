using System.Collections.Concurrent;
using System.Text.Json;
namespace TCS.HoboBot.Modules;

public static class PlayersProperties {
    public static readonly ConcurrentDictionary<ulong, int[]> OwnedProperties = new();
    public static readonly ConcurrentDictionary<ulong, DateTimeOffset> NextCollect = new();
    public static readonly TimeSpan CollectCooldown = TimeSpan.FromHours( 1 );

    static MonopolyProperty[] s_allProperties = [
        new() { Name = "Cardboard Box", Price = 50, CollectAmount = 5 },
        new() { Name = "Hobo Tent", Price = 250, CollectAmount = 20 },
        new("The Local Dumpster", 10_000),
        new("Shabby Shack", 25_000),
        new("Leaky Cabin", 29_200),
        new("Rusty Trailer", 34_000),
        new("Derelict Bunker", 39_600),
        new("Seaside Cottage", 46_200),
        new("Suburban House", 54_000),
        new("Urban Duplex", 62_800),
        new("Lakeside Villa", 73_200),
        new("Countryside Farm", 85_600),
        new("Downtown Loft", 99_600),
        new("Boutique Shop", 116_400),
        new("Corner Café", 135_600),
        new("Small Warehouse", 158_000),
        new("Roadside Motel", 184_400),
        new("Office Suite", 215_000),
        new("Medical Clinic", 250_800),
        new("Strip Mall", 292_400),
        new("Mid-Rise Apartments", 341_000),
        new("Four-Star Hotel", 397_600),
        new("Casino Floor", 463_600),
        new("Solar Farm", 540_800),
        new("Hobo Mansion", 1_000_000),
    ];

    public static Dictionary<int, MonopolyProperty> Properties { get; } = s_allProperties
        .Select( (property, index) => new { Index = index, Property = property } )
        .ToDictionary( x => x.Index, x => x.Property );

    static readonly string FilePath = "OwnedProperties.json";

    //get all properties
    public static MonopolyProperty[] GetAllProperties() {
        if ( s_allProperties.Length == 0 ) {
            // Load properties from a file
            if ( File.Exists( FilePath ) ) {
                string json = File.ReadAllText( FilePath );
                MonopolyProperty[]? loaded = Deserialize<MonopolyProperty[]>( json );
                if ( loaded != null ) {
                    s_allProperties = loaded;
                }
            }
        }

        return s_allProperties;
    }

    public static MonopolyProperty[] GetOwnedProperties(ulong userId)
        => !OwnedProperties.TryGetValue( userId, out int[]? idx ) ? [] : idx.Select( i => Properties[i] ).ToArray();

    public static void AddProperty(ulong userId, int propertyIndex) {
        if ( OwnedProperties.TryGetValue( userId, out int[]? idx ) ) {
            if ( idx.Contains( propertyIndex ) ) {
                return; // already owns this property
            }

            idx = idx.Append( propertyIndex ).ToArray();
        }
        else {
            idx = [propertyIndex];
        }

        OwnedProperties[userId] = idx;
    }

    public static void RemoveProperty(ulong userId, int propertyIndex) {
        if ( OwnedProperties.TryGetValue( userId, out int[]? idx ) ) {
            if ( !idx.Contains( propertyIndex ) ) {
                return; // doesn't own this property
            }

            idx = idx.Where( i => i != propertyIndex ).ToArray();
        }
        else {
            return; // doesn't own any properties
        }

        OwnedProperties[userId] = idx;
    }


    // 2-A  ─ SAVE  (no other changes)
    public static async Task SaveAsync() {
        string json = Serialize( OwnedProperties ); // <-- now Dictionary<ulong,int[]>
        await File.WriteAllTextAsync( FilePath, json );
    }

// 2-B  ─ LOAD
    public static async Task LoadAsync() {
        if ( !File.Exists( FilePath ) ) {
            return;
        }

        string json = await File.ReadAllTextAsync( FilePath );
        ConcurrentDictionary<ulong, int[]>? loaded = Deserialize<ConcurrentDictionary<ulong, int[]>>( json );
        if ( loaded is null ) {
            return;
        }

        foreach (KeyValuePair<ulong, int[]> kv in loaded)
            OwnedProperties[kv.Key] = kv.Value;
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