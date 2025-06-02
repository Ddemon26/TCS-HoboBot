using Discord.Interactions;
using TCS.HoboBot.ActionEvents;
using TCS.HoboBot.Data;
namespace TCS.HoboBot.Modules.Util;

public class WorkModule : InteractionModuleBase<SocketInteractionContext> {
    //readonly GeminiService _geminiService;
    //const int DISCORD_MESSAGE_LIMIT = 2000;

    // public WorkModule(
    //     GeminiService geminiService
    // ) {
    //     _geminiService = geminiService;
    // }

    [SlashCommand( "work", "Work hard for your money!" )]
    public async Task WorkAsync() {
        ulong userId = Context.User.Id;
        var now = DateTimeOffset.UtcNow;

        // Cool-down check
        var next = Cooldowns.Get( Context.Guild.Id, userId, CooldownKind.Job );
        if ( now < next ) {
            var remaining = next - now;
            await RespondAsync(
                $"⏳ Easy there, hobo! Try again in **{remaining:mm\\:ss}**.",
                ephemeral: true
            );
            return;
        }

        // ---------------- Roll event ----------------
        (float delta, string story) = WorkEvents.Roll();

        // Acknowledge immediately to avoid timeout
        // await DeferAsync();
        // var systemInstruction = $"your story must be under 500 chars but above 100 chars, " +
        //                         $"Please create a bad story using this backstory about {Context.User.Username}. " +
        //                         $"Format responses clearly, using Markdown where appropriate for readability (e.g., bolding, bullet points, code blocks).";
        //
        // systemInstruction += "\n\n" + story;
        // string prompt = await GeminiGameMessages.AskGeminiAsync(
        //     _geminiService,
        //     systemInstruction,
        //     maxTokens: 100
        // );

        if ( delta >= 0f ) {
            PlayersWallet.AddToBalance( Context.Guild.Id, userId, delta );
        }
        else {
            PlayersWallet.SubtractFromBalance( Context.Guild.Id, userId, -delta );
        }

        float newBalance = PlayersWallet.GetBalance( Context.Guild.Id, userId );

        // Record next allowed to beg time
        Cooldowns.Set( Context.Guild.Id, userId, CooldownKind.Job, now + Cooldowns.Cooldown( CooldownKind.Job ) );

        // ---------------- Reply ----------------
        string deltaText = delta switch {
            > 0 => $"(+${delta:0.00})",
            < 0 => $"(-${Math.Abs( delta ):0.00})",
            _ => "($0.00)",
        };

        await RespondAsync(
            $"{Context.User.Mention} {story}\n" +
            $"Your hobo wallet now holds **${newBalance:0.00}** {deltaText}"
        );

        // string fullReply = $"{Context.User.Mention} {prompt}\n" +
        //                    $"Your hobo wallet now holds **${newBalance:0.00}** {deltaText}";
        //
        // var parts = SplitMessage(fullReply).ToList();
        // foreach (var part in parts)
        // {
        //     await FollowupAsync(part);
        // }
    }

    // private IEnumerable<string> SplitMessage(string text, int maxLength = DISCORD_MESSAGE_LIMIT) {
    //     for (int i = 0; i < text.Length; i += maxLength) {
    //         yield return text.Substring( i, Math.Min( maxLength, text.Length - i ) );
    //     }
    // }
}