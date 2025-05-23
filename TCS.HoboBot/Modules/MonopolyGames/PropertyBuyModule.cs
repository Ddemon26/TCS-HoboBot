using System.Collections.Concurrent;
using Discord;
using Discord.Interactions;
using TCS.HoboBot.Data;
namespace TCS.HoboBot.Modules;

public record struct MonopolyProperty {
    public string Name { get; init; }
    public float Price { get; init; }
    public float CollectAmount { get; init; }

    const float COLLECT_BUFFER = 200f;

    public MonopolyProperty(string name, float price, float? collectAmount = null) {
        Name = name;
        Price = price;
        CollectAmount = collectAmount ?? price / COLLECT_BUFFER;
    }
}

public class PropertyBuyModule : InteractionModuleBase<SocketInteractionContext> {
    // ---------- constants & in-memory state ----------
    const string CUSTOM_ID_PREFIX = "buy_prop";
    // Keeps track of the property selected in the menu until the user clicks the Buy button
    static readonly ConcurrentDictionary<ulong, int> PendingSelections = new();

    // ---------- /buy_property ----------
    [SlashCommand( "property_buy", "Buy a property" )]
    public async Task BuyPropertyAsync() {
        MonopolyProperty[] props = PlayersProperties.GetAllProperties();
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
                             $"Cost: ${p.Price:N0}    Collect: ${p.CollectAmount:N0}"
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

        MonopolyProperty[] props = PlayersProperties.GetAllProperties();
        if ( selectedIndex < 0 || selectedIndex >= props.Length ) {
            await RespondAsync( "Invalid property.", ephemeral: true );
            return;
        }

        var chosen = props[selectedIndex];

        // Duplicate & funds checks (same logic you already had) …
        // 3-A  ─ inside HandleBuyAsync -- duplicate-check
        int[] owned = PlayersProperties.GetOwnedPropertiesInt( Context.Guild.Id, Context.User.Id );
        if ( owned.Contains( selectedIndex ) ) {
            await RespondAsync( $"You already own **{chosen.Name}**.", ephemeral: true );
            return;
        }

        float balance = PlayersWallet.GetBalance( Context.Guild.Id, Context.User.Id );
        if ( balance < chosen.Price ) {
            await RespondAsync(
                $"You don't have enough money. You need ${chosen.Price - balance:N0} more.",
                ephemeral: true
            );
            return;
        }

        // Perform purchase
        PlayersWallet.SubtractFromBalance( Context.Guild.Id, Context.User.Id, chosen.Price );
        // 3-B  ─ add the new index
        PlayersProperties.AddProperty( Context.Guild.Id, Context.User.Id, selectedIndex );
        // _ = PlayersWallet.SaveAsync();
        // _ = PlayersProperties.SaveAsync();

        //await DeferAsync( ephemeral: true );
        // Public confirmation (replace RespondAsync + new message)
        // await Context.Interaction.DeferAsync();

        // Build your embed
        // var embed = new EmbedBuilder()
        //     .WithTitle( "Property Purchase – Transaction Ended" )
        //     .WithDescription( $"{Context.User.Mention} has bought **{chosen.Name}** for ${chosen.Price:N0}!" )
        //     .Build();

        // // Update the original message, clearing all components
        // await Context.Interaction.ModifyOriginalResponseAsync( messageProperties => {
        //         messageProperties.Embed = embed;
        //         messageProperties.Components = new ComponentBuilder().Build();
        //     }
        // );

        // send a public follow-up in chat
        await RespondAsync(
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