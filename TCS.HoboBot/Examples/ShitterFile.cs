/*
using System.Collections.Concurrent;
using Discord.Interactions;
using Discord.WebSocket;
using Newtonsoft.Json;
namespace TCS.HoboBot;

public static class StaticShitterFile {
    public static ConcurrentDictionary<ulong, float> shmeckles = new();
    public static ConcurrentDictionary<ulong, int[]> boughtStuff = new();
    static readonly string FilePath = "PlayerShmeckles.json";
    static string GetFilePath => Path.Combine( "Data", FilePath );

    static List<int> exampleList = new() {0,1,453453452,3,4,5,5,5};
    static int[] exampleArray = {0,1,2,67657567,4,5};
    static int someInt = 0;
    
    public static void AddToShmeckles( ulong userId, float amount) {
        shmeckles[userId] = shmeckles.TryGetValue( userId, out float old ) ? old + amount : amount;
    }
    
    public static void RemoveFromShmeckles( ulong userId, float amount) {
        
        float newAmount = shmeckles.TryGetValue( userId, out float old2 ) ? MathF.Max( 0f, old2 - amount ) : 0f;
        
        shmeckles[userId] = newAmount;
    }

    #region IGNORE JUST SAVE AND LOAD
    public static async Task SaveAsync() {
        string dir = Path.Combine( "Data" );
        Directory.CreateDirectory( dir );
        string json = Serialize( shmeckles );
        await File.WriteAllTextAsync( GetFilePath, json );
    }
    
    public static async Task LoadAsync() {
        if ( !File.Exists( GetFilePath ) ) {
            return;
        }

        string json = await File.ReadAllTextAsync( GetFilePath );
        ConcurrentDictionary<ulong, float>? loaded = Deserialize<ConcurrentDictionary<ulong, float>>( json );
        if ( loaded is null ) {
            return;
        }

        foreach (KeyValuePair<ulong, float> kv in loaded) {
            shmeckles[kv.Key] = kv.Value;
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

public class ShitterChange : InteractionModuleBase<SocketInteractionContext> {
    [SlashCommand( "hayko_echo", "some gay shit" )]
    public async Task SecondCommand(string thing) {
        await RespondAsync( $"Hayko Echo: {thing}" );
    }
}

public class ShitterFile : InteractionModuleBase<SocketInteractionContext> {
    
    [SlashCommand( "hayko", "some gay shit" )]
    public async Task  FirstCommand(SocketGuildUser user) { 
        await RespondAsync( $"Hayko Mentions: {user.Mention}" );
    }
}// The schmeckle

public class ShitFile : InteractionModuleBase<SocketInteractionContext> {
    
    [SlashCommand( "add_shmeckles", "give yourself some shmeckles" )]
    public async Task  FirstCommand(SocketGuildUser user, float amount) {
        StaticShitterFile.AddToShmeckles( user.Id, amount );

        await RespondAsync( $"{user.Mention} gave  **{amount} shmeckles**" );
    }
    
    [SlashCommand( "remove_shmeckles", "give yourself some shmeckles" )]
    public async Task  SecCommand(SocketGuildUser user, float amount) {
        StaticShitterFile.RemoveFromShmeckles( user.Id, amount );

        await RespondAsync( $"{user.Mention} removed  **{amount} shmeckles**" );
    }

    [SlashCommand( "check_shmeckles", "give yourself some shmeckles" )]
    public async Task SecCommand(SocketGuildUser user) {

        if ( StaticShitterFile.shmeckles.TryGetValue( user.Id, out float data ) ) {
            await RespondAsync( $"{user.Mention} has **{data} shmeckles**" );
        } else {
            await RespondAsync( $"{user.Mention} has **0 shmeckles**" );
        }
    }
    
    public enum BuyType { Ride, Food, Drink, Item, }
    
    float BuyTypeCost(BuyType type) {
        return type switch {
            BuyType.Ride => 5f,
            BuyType.Food => 10f,
            BuyType.Drink => 15f,
            BuyType.Item => 20f,
            _ => 0f
        };
    }
    
    [SlashCommand( "spend_shmeckles", "give yourself some shmeckles" )]
    public async Task ThreeCommand(BuyType type) {

        if ( StaticShitterFile.shmeckles.TryGetValue( Context.User.Id, out float data ) ) {
            await RespondAsync( $"" );
        } else {
            await RespondAsync( $"{Context.User.Mention} has **0 shmeckles**" );
        }
    }
}
*/


