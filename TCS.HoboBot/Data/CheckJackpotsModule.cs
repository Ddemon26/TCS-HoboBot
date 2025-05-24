using Discord.Interactions;
namespace TCS.HoboBot.Modules.CasinoGames.Slots;

public class CheckJackpotsModule : InteractionModuleBase<SocketInteractionContext> {
    [SlashCommand( "jackpots_check", "Check the current jackpots for this guild")]
    public async Task CheckJackPotsAsync() {
        if ( CasinoManager.JackPotsCache.TryGetValue( Context.Guild.Id, out var jackpots ) ) {
            await RespondAsync( jackpots.ToString() );
        }
        else {
            await RespondAsync( "No jackpots found for this guild." );
        }
    }
}