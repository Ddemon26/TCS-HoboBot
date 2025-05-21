using System.Collections.Concurrent;
using System.Text.Json;
using Discord;
using Discord.Interactions;

public class CollectPropertyModule : InteractionModuleBase<SocketInteractionContext> {
    [SlashCommand( "property_collect", "Collect money from your properties" )]
    public async Task CollectPropertyAsync() {
        if ( !PlayersProperties.OwnedProperties.TryGetValue( Context.User.Id, out Property[]? owned ) || owned.Length == 0 ) {
            await RespondAsync( "You don't own any properties.", ephemeral: true );
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if ( PlayersProperties.NextCollect.TryGetValue( Context.User.Id, out var nextCollect ) && now < nextCollect ) {
            await RespondAsync( $"⏳ You need to wait **{nextCollect:mm\\:ss}** before collecting again.", ephemeral: true );
            return;
        }

        // Collect all property amounts
        float total = owned.Sum( p => p.CollectAmount );
        PlayersWallet.AddToBalance( Context.User.Id, total );
        PlayersProperties.NextCollect[Context.User.Id] = now + PlayersProperties.CollectCooldown;

        await RespondAsync(
            $"✅ {Context.User.Mention} Collected ${total:N0} from all his properties!",
            ephemeral: false
        );
    }
}

public record struct Property {
    public string Name { get; init; }
    public float Price { get; init; }
    public float CollectAmount { get; init; }

    const float COLLECT_BUFFER = 200f;

    public Property(string name, float price, float? collectAmount = null) {
        Name = name;
        Price = price;
        CollectAmount = collectAmount ?? price / COLLECT_BUFFER;
    }
}

public static class PlayersProperties {
    public static readonly ConcurrentDictionary<ulong, Property[]> OwnedProperties = new();
    public static readonly ConcurrentDictionary<ulong, DateTimeOffset> NextCollect = new();
    public static readonly TimeSpan CollectCooldown = TimeSpan.FromHours( 1 );

    static Property[] s_allProperties = [
        new() { Name = "Cardboard Box", Price = 50, CollectAmount = 5 },
        new() { Name = "Hobo Tent", Price = 250, CollectAmount = 20 },
        new() { Name = "The Local Dumpster", Price = 1000, CollectAmount = 50 },
        new("Shabby Shack", 25_000),
        new("Leaky Cabin", 29200),
        new("Rusty Trailer", 34000),
        new("Derelict Bunker", 39600),
        new("Seaside Cottage", 46200),
        new("Suburban House", 54000),
        new("Urban Duplex", 62800),
        new("Lakeside Villa", 73200),
        new("Countryside Farm", 85600),
        new("Downtown Loft", 99600),
        new("Boutique Shop", 116400),
        new("Corner Café", 135600),
        new("Small Warehouse", 158000),
        new("Roadside Motel", 184400),
        new("Office Suite", 215000),
        new("Medical Clinic", 250800),
        new("Strip Mall", 292400),
        new("Mid-Rise Apartments", 341000),
        new("Four-Star Hotel", 397600),
        new("Casino Floor", 463600),
        new("Solar Farm", 540800),
        new("Hobo Mansion", 1_000_000),
    ];

    static readonly string FilePath = "OwnedProperties.json";

    //get all properties
    public static Property[] GetAllProperties() {
        if ( s_allProperties.Length == 0 ) {
            // Load properties from a file
            if ( File.Exists( FilePath ) ) {
                string json = File.ReadAllText( FilePath );
                Property[]? loaded = Deserialize<Property[]>( json );
                if ( loaded != null ) {
                    s_allProperties = loaded;
                }
            }
        }

        return s_allProperties;
    }

    //save properties to a file
    public static async Task SaveAsync() {
        string json = Serialize( OwnedProperties );
        await File.WriteAllTextAsync( FilePath, json );
    }

    //load properties from a file
    public static async Task LoadAsync() {
        if ( File.Exists( FilePath ) ) {
            string json = await File.ReadAllTextAsync( FilePath );
            ConcurrentDictionary<ulong, Property[]>? loaded = Deserialize<ConcurrentDictionary<ulong, Property[]>>( json );
            if ( loaded != null ) {
                foreach (KeyValuePair<ulong, Property[]> kv in loaded) {
                    OwnedProperties[kv.Key] = kv.Value;
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

public class BuyPropertyModule : InteractionModuleBase<SocketInteractionContext> {
    // ---------- constants & in-memory state ----------
    const string CUSTOM_ID_PREFIX = "buy_prop";
    // Keeps track of the property selected in the menu until the user clicks the Buy button
    static readonly ConcurrentDictionary<ulong, int> PendingSelections = new();

    // ---------- /buy_property ----------
    [SlashCommand( "property_buy", "Buy a property" )]
    public async Task BuyPropertyAsync() {
        Property[] props = PlayersProperties.GetAllProperties();
        if ( props.Length == 0 ) {
            await RespondAsync( "No properties are currently available.", ephemeral: true );
            return;
        }

        // Build the select-menu
        List<SelectMenuOptionBuilder> options = props
            .Select( (p, i) =>
                         new SelectMenuOptionBuilder
                         (
                             $"{p.Name} – ${p.Price:N0}",
                             i.ToString(),
                             $"Cost: ${p.Price:N0} \nCollect: ${p.CollectAmount:N0} per hour"
                         )
            )
            .ToList();

        var select = new SelectMenuBuilder()
            .WithCustomId( $"{CUSTOM_ID_PREFIX}:select:{Context.User.Id}" )
            .WithPlaceholder( "Select a property to purchase" )
            .WithMinValues( 1 )
            .WithMaxValues( 1 )
            .WithOptions( options );

        var buttons = new ComponentBuilder()
            .WithSelectMenu( select )
            .WithButton( "Buy", $"{CUSTOM_ID_PREFIX}:buy:{Context.User.Id}", ButtonStyle.Success )
            .WithButton( "Exit", $"{CUSTOM_ID_PREFIX}:exit:{Context.User.Id}", ButtonStyle.Danger );

        await RespondAsync(
            "Choose a property, then press **Buy** or **Exit**:",
            components: buttons.Build(),
            ephemeral: true
        );
    }

    // ---------- when a property is picked ----------
    [ComponentInteraction( "buy_prop:select:*" )]
    public async Task HandleSelectAsync(string targetUserId, string[] selections) {
        if ( Context.User.Id.ToString() != targetUserId ) {
            await RespondAsync( "This select menu isn’t for you.", ephemeral: true );
            return;
        }

        if ( selections.Length == 0 || !int.TryParse( selections[0], out int index ) ) {
            await RespondAsync( "Invalid selection.", ephemeral: true );
            return;
        }

        // Remember which property this user picked until they press Buy
        PendingSelections[Context.User.Id] = index;
        await DeferAsync( ephemeral: true );
    }

    // ---------- Buy button ----------
    [ComponentInteraction( "buy_prop:buy:*" )]
    public async Task HandleBuyAsync(string targetUserId) {
        if ( Context.User.Id.ToString() != targetUserId ) {
            await RespondAsync( "These buttons aren’t for you.", ephemeral: true );
            return;
        }

        if ( !PendingSelections.TryRemove( Context.User.Id, out int selectedIndex ) ) {
            await RespondAsync( "Please pick a property first.", ephemeral: true );
            return;
        }

        Property[] props = PlayersProperties.GetAllProperties();
        if ( selectedIndex < 0 || selectedIndex >= props.Length ) {
            await RespondAsync( "Invalid property.", ephemeral: true );
            return;
        }

        var chosen = props[selectedIndex];

        // Duplicate & funds checks (same logic you already had) …
        Property[] owned = PlayersProperties.OwnedProperties.GetValueOrDefault( Context.User.Id, [] );
        if ( owned.Any( p => p.Name == chosen.Name ) ) {
            await RespondAsync( $"You already own **{chosen.Name}**.", ephemeral: true );
            return;
        }

        float balance = PlayersWallet.GetBalance( Context.User.Id );
        if ( balance < chosen.Price ) {
            await RespondAsync(
                $"You don't have enough money. You need ${chosen.Price - balance:N0} more.",
                ephemeral: true
            );
            return;
        }

        // Perform purchase
        PlayersWallet.SubtractFromBalance( Context.User.Id, chosen.Price );
        PlayersProperties.OwnedProperties.AddOrUpdate(
            Context.User.Id,
            _ => [chosen],
            (_, old) => old.Append( chosen ).ToArray()
        );
        _ = PlayersWallet.SaveAsync();
        _ = PlayersProperties.SaveAsync();

        //await DeferAsync( ephemeral: true );
        // Public confirmation (replace RespondAsync + new message)
        await Context.Interaction.DeferAsync();

        // Build your embed
        var embed = new EmbedBuilder()
            .WithTitle( "Property Purchase – Transaction Ended" )
            .WithDescription( $"{Context.User.Mention} has bought **{chosen.Name}** for ${chosen.Price:N0}!" )
            .Build();

        // Update the original message, clearing all components
        await Context.Interaction.ModifyOriginalResponseAsync( messageProperties => {
                messageProperties.Embed = embed;
                messageProperties.Components = new ComponentBuilder().Build();
            }
        );

        // send a public follow-up in chat
        await FollowupAsync(
            $"✅ {Context.User.Mention} has bought **{chosen.Name}** for ${chosen.Price:N0}!",
            ephemeral: false
        );
    }

    // ---------- Exit button ----------
    [ComponentInteraction( "buy_prop:exit:*" )]
    public async Task HandleExitAsync(string targetUserId) {
        if ( Context.User.Id.ToString() != targetUserId ) {
            await RespondAsync( "These buttons aren’t for you.", ephemeral: true );
            return;
        }

        // Forget any pending selection
        PendingSelections.TryRemove( Context.User.Id, out _ );

        await DeferAsync( ephemeral: true );
        await Context.Interaction.ModifyOriginalResponseAsync( m => {
                m.Embed = new EmbedBuilder()
                    .WithTitle( "Property Purchase – Transaction Ended" )
                    .WithDescription( $"{Context.User.Mention} has ended the transaction." )
                    .Build();
                m.Components = new ComponentBuilder().Build();
            }
        );
    }
}