using Discord;
using Discord.Interactions;

namespace TCS.HoboBot.Modules {
    /// <summary>
    /// Interactive profile card for Damon – now featuring a feedback modal
    /// and refreshed copy/highlights for 2025.
    /// Demonstrates **strongly‑typed modal** handling via <see cref="FeedbackModal"/>.
    /// </summary>
    public sealed class DamonModule : InteractionModuleBase<SocketInteractionContext> {
        // Component & modal IDs -------------------------------------------------
        const string MENU_CUSTOM_ID = "damon.menu";
        const string LEARN_MORE_ID = "damon.learn_more";
        const string CONTACT_ID = "damon.contact";
        const string FEEDBACK_ID = "damon.feedback_button";
        const string FEEDBACK_MODAL_ID = "damon.feedback_modal";

        // External links -------------------------------------------------------
        const string SERVER_INVITE = "https://discord.gg/knwtcq3N2a";
        const string REPO_URL = "https://github.com/Ddemon26/TCS-HoboBot";
        const string GIF_URL = "https://media.discordapp.net/attachments/1044528528777019434/1171785166151221288/Untitled_video_-_Made_with_Clipchamp_2.gif";
        const string LOGO_URL = "https://cdn.discordapp.com/attachments/1265826820880863433/1380517297411850410/TentCityStudio.png";

        const ulong DAMON_ID = 268654531452207105; // Damon's user‑ID

        // Home‑server routing ---------------------------------------------------
        /// <summary>
        /// Channel that will receive every feedback submission. **Replace** with the
        /// real channel‑ID from the bot's home guild.
        /// </summary>
        const ulong FEEDBACK_CHANNEL_ID = 1374095974913278063;

        // ---------------------------------------------------------------------
        // Utility helpers
        string GetLearnMoreMessage() =>
            "\uD83D\uDCA1 **Damon** is a fictional persona of boundless creativity, code‑wizardry and exquisite taste.\n" +
            $"Check out the latest projects (v2.0+) on GitHub:\n{REPO_URL}";

        string GetContactMessage() =>
            $"\uD83D\uDC4B Want to reach Damon? Join the community server for questions & collabs:\n{SERVER_INVITE}";

        // ---------------------------------------------------------------------
        // Slash command entrypoint
        [SlashCommand( "damon", "Show an interactive profile card for Damon" )]
        public async Task ShowDamonAsync() {
            var embed = BuildEmbed();
            var components = BuildComponents().Build();

            await RespondAsync( embed: embed, components: components, ephemeral: false );
        }

        // ---------------------------------------------------------------------
        // Select‑menu interaction handler
        [ComponentInteraction( MENU_CUSTOM_ID )]
        public async Task HandleMenuAsync(string[] selected) {
            if ( selected.Length == 0 ) return;

            await DeferAsync( ephemeral: true ); // Defer the menu interaction

            string responseText = selected[0] switch {
                LEARN_MORE_ID => GetLearnMoreMessage(),
                CONTACT_ID => GetContactMessage(),
                _ => string.Empty,
            };

            if ( !string.IsNullOrWhiteSpace( responseText ) )
                await FollowupAsync( responseText, ephemeral: true ); // Send follow‑up for the menu interaction
        }

        // ---------------------------------------------------------------------
        // Button interactions
        [ComponentInteraction( LEARN_MORE_ID )]
        public async Task LearnMoreAsync() =>
            await RespondAsync( GetLearnMoreMessage(), ephemeral: true );

        [ComponentInteraction( CONTACT_ID )]
        public async Task ShowContactAsync() =>
            await RespondAsync( GetContactMessage(), ephemeral: true );

        /// <summary>
        /// Opens the feedback modal when the "Send Feedback" button is pressed.
        /// </summary>
        [ComponentInteraction( FEEDBACK_ID )]
        public async Task OpenFeedbackModalAsync() {
            var modal = new ModalBuilder()
                .WithTitle( "Send Feedback to Damon" )
                .WithCustomId( FEEDBACK_MODAL_ID )
                .AddTextInput( label: "Your Name (optional)", customId: "name", placeholder: "e.g. Alex", required: false, maxLength: 100 )
                .AddTextInput( label: "Feedback", customId: "content", style: TextInputStyle.Paragraph, placeholder: "Tell us what you think…", maxLength: 1000 );

            await RespondWithModalAsync( modal.Build() );
        }

        // ---------------------------------------------------------------------
        // Modal submission handler – **strongly‑typed**
        [ModalInteraction( FEEDBACK_MODAL_ID )]
        public async Task HandleFeedbackModalAsync(FeedbackModal modal) {
            // Figure out who sent it (fallback ⇒ Anonymous)
            string who = string.IsNullOrWhiteSpace( modal.Name ) ? "Anonymous" : modal.Name;

            // 1️⃣ Forward to the home‑server channel
            if ( Context.Client.GetChannel( FEEDBACK_CHANNEL_ID ) is IMessageChannel target ) {
                var embed = new EmbedBuilder()
                    .WithTitle( "📝 New Feedback" )
                    .WithColor( Color.Orange )
                    .AddField( "From", who, true )
                    .AddField( "User", $"<@{Context.User.Id}>", true )
                    .AddField( "Content", modal.Content )
                    .WithTimestamp( DateTimeOffset.UtcNow )
                    .Build();

                await target.SendMessageAsync( embed: embed );
            }

            // 2️⃣ Ack the user (ephemeral so only they see it)
            await RespondAsync( $"Thanks for your feedback, {who}! 🚀", ephemeral: true );
        }

        // ---------------------------------------------------------------------
        // Embed & Component builders
        Embed BuildEmbed() {
            return new EmbedBuilder()
                .WithColor( new Color( 0xFEE440 ) )
                .WithAuthor( BuildAuthor() )
                .WithTitle( "👑 Damon · Creative Technomancer" )
                .WithDescription(
                    "Part storyteller, part engineer – Damon crafts delightful digital experiences and open‑source wonders.\n" +
                    "**Currently exploring:** AI agents · WebAssembly bots · Real‑time collaboration"
                )
                .WithThumbnailUrl( LOGO_URL )
                .AddField( "✨ Creativity", "Turning wild ideas into polished prototypes", true )
                .AddField( "🛠️  Tech Stack", "`C#` · `.NET 8` · `TypeScript` · `Python`", true )
                .AddField(
                    name: "🚀 Highlights",
                    value:
                    "• Released *HoboBot* **v2.0** (May 2025)\n" +
                    "• Keynote Speaker @ DevCon 2025\n" +
                    "• 3× Hackathon winner",
                    inline: false
                )
                .WithFooter( "Tap a button below to explore", GIF_URL )
                .Build();
        }

        static ComponentBuilder BuildComponents() {
            var menu = new SelectMenuBuilder()
                .WithPlaceholder( "Pick an option…" )
                .WithCustomId( MENU_CUSTOM_ID )
                .WithMinValues( 1 ).WithMaxValues( 1 )
                .AddOption( "Learn more", LEARN_MORE_ID, "Damon's back‑story & skills" )
                .AddOption( "Contact", CONTACT_ID, "Community & support" );

            return new ComponentBuilder()
                .WithSelectMenu( menu )
                .WithButton( label: "Visit Website 🌐", style: ButtonStyle.Link, url: "https://example.com" )
                .WithButton( label: "Contact 🚀", customId: CONTACT_ID, style: ButtonStyle.Primary )
                .WithButton( label: "Send Feedback 📝", customId: FEEDBACK_ID, style: ButtonStyle.Secondary );
        }

        EmbedAuthorBuilder BuildAuthor() =>
            new EmbedAuthorBuilder()
                .WithName( "Damon" )
                .WithIconUrl( GIF_URL )
                .WithUrl( REPO_URL );
    }

    // -------------------------------------------------------------------------
    // Strongly‑typed modal definition ----------------------------------------

    /// <summary>
    /// DTO used by <see cref="HandleFeedbackModalAsync"/>. Each property is mapped
    /// to a text‑input component via attribute metadata. Discord.Net populates the
    /// object automatically when the modal is submitted.
    /// </summary>
    public sealed class FeedbackModal : IModal {
        public string Title => "Send Feedback to Damon";

        [InputLabel( "Your Name (optional)" )]
        [ModalTextInput( "name", placeholder: "e.g. Alex", maxLength: 100 )]
        public string? Name { get; set; }

        [InputLabel( "Feedback" )]
        [ModalTextInput( "content", TextInputStyle.Paragraph, placeholder: "Tell us what you think…", maxLength: 1000 )]
        public string Content { get; set; } = string.Empty;
    }
}