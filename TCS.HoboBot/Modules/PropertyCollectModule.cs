using Discord.Interactions;
using Discord.WebSocket;
using TCS.HoboBot.Data;

namespace TCS.HoboBot.Modules;

public class PropertyCheckModule : InteractionModuleBase<SocketInteractionContext> {
    [SlashCommand( "property_check", "Check your properties" )]
    public async Task CheckPropertyAsync(SocketUser? user = null) {
        // determine whose properties to check
        var target = user ?? Context.User;

        MonopolyProperty[] owned = PlayersProperties.GetOwnedProperties( target.Id );
        if ( owned.Length == 0 ) {
            await RespondAsync( $"{target.GlobalName} doesn't own any properties.", ephemeral: true );
            return;
        }

        string propertyList = string.Join(
            Environment.NewLine,
            owned.Select( p => $"- **{p.Name}**  (Collects: ${p.CollectAmount:N0})" )
        );

        var now = DateTimeOffset.UtcNow;
        string cooldownMessage;

        if ( PlayersProperties.NextCollect.TryGetValue( target.Id, out var nextTime ) ) {
            var remaining = nextTime - now;
            string format = remaining.TotalHours >= 1 ? @"hh\:mm\:ss" : @"mm\:ss";
            cooldownMessage = $"⏳ You can collect money in **{remaining.ToString( format )}**.";
        }
        else {
            cooldownMessage = "⏳ You can collect money **NOW**.";
        }

        await RespondAsync(
            $"🏠 {target.Mention} owns the following properties:{Environment.NewLine}{propertyList}\n" +
            $"Total collect amount: **${owned.Sum( p => p.CollectAmount ):N0}**\n" +
            cooldownMessage,
            ephemeral: false
        );
    }
}

public class PropertyCollectModule : InteractionModuleBase<SocketInteractionContext> {
    [SlashCommand( "property_collect", "Collect money from your properties" )]
    public async Task CollectPropertyAsync() {
        // 1 – get the user’s properties as *objects*, not indices
        MonopolyProperty[] owned = PlayersProperties.GetOwnedProperties( Context.User.Id );
        if ( owned.Length == 0 ) {
            await RespondAsync( "You don't own any properties.", ephemeral: true );
            return;
        }

        // 2 – cool-down check (unchanged)
        var now = DateTimeOffset.UtcNow;
        if ( PlayersProperties.NextCollect.TryGetValue( Context.User.Id, out var nextCollect ) &&
             now < nextCollect ) {
            var remaining = nextCollect - now;
            await RespondAsync(
                $"⏳ You need to wait **{remaining:mm\\:ss}** before collecting again.",
                ephemeral: true
            );
            return;
        }

        // 3 – collect
        float total = owned.Sum( p => p.CollectAmount );
        PlayersWallet.AddToBalance( Context.User.Id, total );
        PlayersProperties.NextCollect[Context.User.Id] = now + PlayersProperties.CollectCooldown;

        await RespondAsync(
            $"✅ {Context.User.Mention} collected **${total:N0}** from all their properties!",
            ephemeral: false
        );
    }
}