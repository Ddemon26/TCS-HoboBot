using Discord.Interactions;
using Discord.WebSocket;
using TCS.HoboBot.Data;
using TCS.HoboBot.Services;
namespace TCS.HoboBot.Modules.DrugDealer;

public struct DealerRoleUpgrade {
    public HoboBotRoles Role { get; }
    public int Amount { get; }

    public DealerRoleUpgrade(HoboBotRoles role, int amount) {
        Role = role;
        Amount = amount;
    }
}

public class SellStashModule : InteractionModuleBase<SocketInteractionContext> {
    public static readonly DealerRoleUpgrade[] RoleUpgrades = {
        new(HoboBotRoles.PettyDrugDealer, 10_000),
        new(HoboBotRoles.StreetDealer, 25_000),
        new(HoboBotRoles.Pimp, 50_000),
        new(HoboBotRoles.Kingpin, 250_000),
        new(HoboBotRoles.DrugLord, 500_000),
        new(HoboBotRoles.Underboss, 1_000_000),
        new(HoboBotRoles.Godfather, 2_500_000),
    };

    public HoboBotRoles GetDealerUpgrade(float amount) {
        for (int i = 0; i < RoleUpgrades.Length; i++) {
            if ( amount < RoleUpgrades[i].Amount ) {
                return i == 0 ? HoboBotRoles.LowLevelDealer : RoleUpgrades[i - 1].Role;
            }
        }

        return RoleUpgrades[^1].Role;
    }


    [SlashCommand( "sell_stash", "Sell your stash" )]
    public async Task SellStashAsync() {
        var stash = PlayersStashes.GetStash( Context.Guild.Id, Context.User.Id );
        if ( !stash.HasAnyDrugs() ) {
            await RespondAsync( "You have no drugs to sell." );
            return;
        }

        // Calculate total value of stash
        float totalValue = PlayersStashes.GetTotalSellAmountFromUser( Context.Guild.Id, Context.User.Id );
        //float totalValue = stash.TotalValue;
        PlayersWallet.AddToBalance( Context.Guild.Id, Context.User.Id , totalValue );

        stash.RemoveAllAmounts();
        stash.TotalCashAcquiredFromSelling += totalValue;
        // remember an old role
        var previousRole = stash.Role;
        // calculate and assign new role
        var newRole = GetDealerUpgrade( stash.TotalCashAcquiredFromSelling );
        stash.Role = newRole;

        // apply a role in Discord
        var user = Context.User as SocketGuildUser;
        await HoboBotRolesManager.AddRolesAsync( user, newRole );

        // save stash
        PlayersStashes.SaveStash(Context.Guild.Id, Context.User.Id, stash)/* = stash*/;

        // respond with a sale result
        await RespondAsync( $"You sold your stash for ${totalValue:N0}!" );

        // if upgraded, send a follow-up promotion message
        if ( newRole != previousRole ) {
            await FollowupAsync( $"Congratulations, you’ve been promoted to **{newRole}**!" );
        }
    }
}