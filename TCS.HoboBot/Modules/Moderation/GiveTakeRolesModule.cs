using Discord;
using Discord.Interactions;
using Discord.WebSocket;
namespace TCS.HoboBot.Modules.Moderation;

[DefaultMemberPermissions( GuildPermission.Administrator )] // ⬅️ who may *use* it
[RequireBotPermission( GuildPermission.ManageRoles )]
public class GiveTakeRolesModule : InteractionModuleBase<SocketInteractionContext> {
    [SlashCommand( "giverole", "Give a role to a member" )]
    public async Task GiveRoleAsync(
        [Summary( "member", "The member to modify" )] SocketGuildUser member,
        [Summary( "role", "Role to add" )] IRole role
    ) {
        await member.AddRoleAsync( role );
        await RespondAsync( $"✅ Added **{role.Name}** to **{member.DisplayName}**" );
    }

    [SlashCommand( "takerole", "Remove a role from a member" )]
    public async Task TakeRoleAsync(
        SocketGuildUser member,
        IRole role
    ) {
        await member.RemoveRoleAsync( role );
        await RespondAsync( $"🚫 Removed **{role.Name}** from **{member.DisplayName}**" );
    }

    public static async Task AddRoleAsync(SocketGuild guild, ulong userId, ulong roleId) {
        // 1) Fetch the user (or use Context.User if already in a command).
        var user = guild.GetUser( userId ) ?? await ((IGuild)guild).GetUserAsync( userId );
        if ( user is null ) {
            throw new InvalidOperationException( "User not found." );
        }

        // 2) Fetch the role (or use guild.Roles.First(...) by name).
        var role = guild.GetRole( roleId );
        if ( role is null ) {
            throw new InvalidOperationException( "Role not found." );
        }

        // 3) Give it.
        await user.AddRoleAsync( role );
    }

    public static async Task RemoveRoleAsync(SocketGuild guild, ulong userId, ulong roleId) {
        var user = guild.GetUser( userId ) ?? await ((IGuild)guild).GetUserAsync( userId );
        var role = guild.GetRole( roleId );
        await user.RemoveRoleAsync( role );
    }
}