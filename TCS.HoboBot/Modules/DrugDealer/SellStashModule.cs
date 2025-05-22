using Discord.Interactions;
using Discord.WebSocket;
using TCS.HoboBot.Data;
using TCS.HoboBot.Modules.Moderation;
namespace TCS.HoboBot.Modules.DrugDealer;

public class SellStashModule : InteractionModuleBase<SocketInteractionContext> {
    [SlashCommand( "sell_stash", "Sell your stash" )]
    public async Task SellStashAsync() {
        var stash = PlayersStashes.GetStash( Context.User.Id );
        if ( !stash.HasAnyDrugs() ) {
            await RespondAsync( "You have no drugs to sell." );
            return;
        }

        // Calculate total value of stash
        float totalValue = PlayersStashes.GetTotalSellAmountFromUser( Context.User.Id );
        //float totalValue = stash.TotalValue;
        PlayersWallet.AddToBalance( Context.User.Id, totalValue );

        stash.RemoveAllAmounts();
        stash.TotalCashAcquiredFromSelling += totalValue;
        var user = Context.User as SocketGuildUser;
        await HoboRolesHandler.AddRolesAsync( user, stash.Role );
        PlayersStashes.Stash[Context.User.Id] = stash;

        await RespondAsync( $"You sold your stash for ${totalValue:N0}!" );
    }
}