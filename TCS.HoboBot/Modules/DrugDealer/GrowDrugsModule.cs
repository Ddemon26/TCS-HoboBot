using System.Collections.Concurrent;
using Discord.Interactions;
using TCS.HoboBot.Modules.Moderation;
using TCS.HoboBot.Services;
namespace TCS.HoboBot.Modules.DrugDealer;

public class CookDrugsModule : InteractionModuleBase<SocketInteractionContext> {
    public enum CookType { Cocaine, Heroin, Crack, Meth, Lsd, Ecstasy }
    static readonly Random Rng = new();

    [SlashCommand( "cook", "Cook some drugs" )]
    public async Task CookAsync(CookType type) {
        ulong userId = Context.User.Id;
        var stash = PlayersStashes.GetStash(Context.Guild.Id,  userId );

        var requiredRole = type switch {
            CookType.Cocaine => DealerRole.PettyDrugDealer,
            CookType.Heroin => DealerRole.StreetDealer,
            CookType.Crack => DealerRole.Pimp,
            CookType.Meth => DealerRole.Kingpin,
            CookType.Lsd => DealerRole.DrugLord,
            CookType.Ecstasy => DealerRole.Underboss,
            _ => throw new ArgumentOutOfRangeException( nameof(type), type, null ),
        };

        if ( stash.Role < requiredRole ) {
            await RespondAsync(
                $"🚫 You need to be at least **{requiredRole}** to cook {type.ToString().ToLower()}.",
                ephemeral: true
            );
            return;
        }
        
        var drug = type switch {
            CookType.Cocaine => DrugType.Cocaine,
            CookType.Heroin => DrugType.Heroin,
            CookType.Crack => DrugType.Crack,
            CookType.Meth => DrugType.Meth,
            CookType.Lsd => DrugType.Lsd,
            CookType.Ecstasy => DrugType.Ecstasy,
            _ => throw new ArgumentOutOfRangeException( nameof(type), type, null ),
        };
        
        // Use the new API to check cooldown
        if (PlayersStashes.IsOnCooldown(userId, drug, out var remaining)) {
            await RespondAsync(
                $"⏳ You need to wait **{remaining:mm\\:ss}** before you can cook more {type}.",
                ephemeral: true
            );
            return;
        }
        PlayersStashes.StartCooldown(userId, drug);
        
        int amount = Rng.Next( 2, 10 );
        stash.AddAmountToType( drug, amount );
        PlayersStashes.SaveStash( Context.Guild.Id, userId, stash );

        await RespondAsync( $"You cooked {amount}g of {type.ToString().ToLower()}!" );
    }
}

public class GrowDrugsModule : InteractionModuleBase<SocketInteractionContext> {
    public enum GrowType { Weed, Shrooms, Dmt }
    static readonly Random Rng = new();

    [SlashCommand( "grow", "Grow some weed or shrooms" )]
    public async Task GrowAsync(GrowType type) {
        ulong userId = Context.User.Id;
        var stash = PlayersStashes.GetStash( Context.Guild.Id, userId );

        var requiredRole = type switch {
            GrowType.Weed => DealerRole.LowLevelDealer,
            GrowType.Shrooms => DealerRole.PettyDrugDealer,
            GrowType.Dmt => DealerRole.Godfather,
            _ => throw new ArgumentOutOfRangeException( nameof(type), type, null ),
        };

        if ( stash.Role < requiredRole ) {
            await RespondAsync(
                $"🚫 You need to be at least **{requiredRole}** to grow {type.ToString().ToLower()}.",
                ephemeral: true
            );
            return;
        }

        // Use the new API to check cooldown
        var drug = type switch {
            GrowType.Weed => DrugType.Weed,
            GrowType.Shrooms => DrugType.Shrooms,
            GrowType.Dmt => DrugType.Dmt,
            _ => throw new ArgumentOutOfRangeException( nameof(type), type, null ),
        };
        
        if (PlayersStashes.IsOnCooldown(userId, drug, out var remaining)) {
            await RespondAsync(
                $"⏳ You need to wait **{remaining:mm\\:ss}** before you can cook more {type}.",
                ephemeral: true
            );
            return;
        }
        PlayersStashes.StartCooldown(userId, drug);

        int amount = Rng.Next( 5, 26 );
        stash.AddAmountToType( drug, amount );
        PlayersStashes.SaveStash( Context.Guild.Id, userId, stash );

        await RespondAsync( $"You grew {amount}g of {type.ToString().ToLower()}!" );
    }
}