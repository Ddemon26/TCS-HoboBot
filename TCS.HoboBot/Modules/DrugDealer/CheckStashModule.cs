using Discord.Interactions;
namespace TCS.HoboBot.Modules.DrugDealer;

public class CheckStashModule : InteractionModuleBase<SocketInteractionContext> {
    [SlashCommand( "check_stash", "Check your stash" )]
    public async Task CheckStashAsync() {
        var stash = PlayersStashes.GetStash( Context.Guild.Id, Context.User.Id );
        await RespondAsync( $"Your stash:\n{stash.GetDrugsString()}" );
    }
}