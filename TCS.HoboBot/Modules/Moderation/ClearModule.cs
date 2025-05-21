using Discord;
using Discord.Interactions;

namespace TCS.HoboBot.Modules.Moderation;

[DefaultMemberPermissions( GuildPermission.Administrator )] // ⬅️ who may *use* it
[RequireBotPermission( GuildPermission.ManageMessages )]
public class ClearModule : InteractionModuleBase<SocketInteractionContext> {
    [SlashCommand( "clear", "Clear all bot messages and dismiss open interactions." )]
    public async Task ClearAsync() {
        // 1️⃣ Tell Discord, “I’m on it – up to 15-min grace period.”
        await DeferAsync( ephemeral: true );

        // 2️⃣ Collect messages (max 14 days old)
        IEnumerable<IMessage>? messages = await Context.Channel.GetMessagesAsync( limit: 500 )
            .FlattenAsync();
        List<IMessage> botMessages = messages
            .Where( m => m.Author.IsBot )
            .Where( m => (DateTimeOffset.UtcNow - m.Timestamp).TotalDays < 14 )
            .ToList();

        // 3️⃣ Strip components to dismiss active interactions
        foreach (var msg in botMessages.OfType<IUserMessage>()
                     .Where( m => m.Components.Count > 0 )) {
            try {
                await msg.ModifyAsync( p => p.Components = new ComponentBuilder().Build() );
            }
            catch {
                /* ignore – maybe older than 15 min, already deleted, etc. */
            }
        }

        // 4️⃣  Bulk-delete in ≤100-message chunks
        var textChannel = (ITextChannel)Context.Channel;
        foreach (IMessage[] chunk in botMessages.Chunk( 100 )) {
            await textChannel.DeleteMessagesAsync( chunk );
        }

        // 5️⃣ Send the follow-up that satisfies the deferred interaction
        await FollowupAsync(
            $"🧹 Deleted **{botMessages.Count}** bot messages and dismissed their components.",
            ephemeral: true
        );
    }
}