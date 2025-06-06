﻿using System.Collections.Concurrent;
using System.Text.Json;
using Discord.WebSocket;
namespace TCS.HoboBot.Modules;

public static class PlayersProperties {
    static readonly ConcurrentDictionary<ulong, Dictionary<ulong, int[]>> GlobalOwnedProperties = new();

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

    const string FILE_PATH = "OwnedProperties.json";

    static string GetFilePath(ulong guildId) {
        return Path.Combine( "Data", guildId.ToString(), FILE_PATH );
    }

    public static int[] GetOwnedPropertiesInt(ulong guildId, ulong userId) {
        if ( GlobalOwnedProperties.TryGetValue( guildId, out Dictionary<ulong, int[]>? owned ) ) {
            if ( owned.TryGetValue( userId, out int[]? idx ) ) {
                return idx;
            }
        }

        return [];
    }
    //get all properties
    public static MonopolyProperty[] GetAllProperties() {
        if ( s_allProperties.Length == 0 ) {
            if ( File.Exists( FILE_PATH ) ) {
                string json = File.ReadAllText( FILE_PATH );
                MonopolyProperty[]? loaded = Deserialize<MonopolyProperty[]>( json );
                if ( loaded != null ) {
                    s_allProperties = loaded;
                }
            }
        }

        return s_allProperties;
    }

    public static MonopolyProperty[] GetOwnedProperties(ulong guildId, ulong userId) {
        Dictionary<ulong, int[]> guildProps = GlobalOwnedProperties.GetOrAdd(
            guildId,
            _ => new Dictionary<ulong, int[]>()
        );

        if ( !guildProps.TryGetValue( userId, out int[]? idx ) ) {
            return [];
        }

        return idx.Select( i => Properties[i] ).ToArray();
    }

    public static void AddProperty(ulong guildId, ulong userId, int propertyIndex) {
        Dictionary<ulong, int[]> guildProps = GlobalOwnedProperties.GetOrAdd(
            guildId,
            _ => new Dictionary<ulong, int[]>()
        );

        if ( guildProps.TryGetValue( userId, out int[]? idx ) ) {
            if ( idx.Contains( propertyIndex ) ) {
                return; // already owns
            }

            idx = idx.Append( propertyIndex ).ToArray();
        }
        else {
            idx = [propertyIndex];
        }

        guildProps[userId] = idx;
    }

    public static void RemoveProperty(ulong guildId, ulong userId, int propertyIndex) {
        Dictionary<ulong, int[]> guildProps = GlobalOwnedProperties.GetOrAdd(
            guildId,
            _ => new Dictionary<ulong, int[]>()
        );

        if ( !guildProps.TryGetValue( userId, out int[]? idx ) || !idx.Contains( propertyIndex ) ) {
            return; // nothing to remove
        }

        idx = idx.Where( i => i != propertyIndex ).ToArray();
        guildProps[userId] = idx;
    }


    // 2-A  ─ SAVE  (no other changes)
    public static async Task SaveAsync() {
        foreach (KeyValuePair<ulong, Dictionary<ulong, int[]>> kv in GlobalOwnedProperties) {
            await SaveAsync( kv.Key );
        }
    }

    public static async Task SaveAsync(ulong guildId) {
        string dir = Path.Combine( "Data", guildId.ToString() );
        Directory.CreateDirectory( dir );
        string path = GetFilePath( guildId );
        if ( !GlobalOwnedProperties.TryGetValue( guildId, out Dictionary<ulong, int[]>? guildStashes ) ) {
            guildStashes = new Dictionary<ulong, int[]>();
        }

        string json = Serialize( guildStashes );
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
            Dictionary<ulong, int[]>? loaded = Deserialize<Dictionary<ulong, int[]>>( json );
            if ( loaded is null ) {
                continue;
            }

            GlobalOwnedProperties[guild.Id] = loaded;
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