using System.Net.Http.Headers;
using Discord;
using Discord.Interactions;
using Newtonsoft.Json;

namespace TCS.HoboBot.Modules {
    public sealed class DamonModule : InteractionModuleBase<SocketInteractionContext> {
        const string MENU_CUSTOM_ID = "damon.menu";
        const string LEARN_MORE_ID = "damon.learn_more";
        const string CONTACT_ID = "damon.contact";
        const string INVITE_ID = "damon.invite";
        const string FEEDBACK_ID = "damon.feedback_button";
        const string FEEDBACK_MODAL_ID = "damon.feedback_modal";

        const string SERVER_INVITE = "https://discord.gg/knwtcq3N2a";
        const string PROFILE_URL = "https://github.com/Ddemon26";
        const string HUB_BOT_REPO = "https://github.com/Ddemon26/TCS-HoboBot";
        const string WEBSITE_URL = "https://tent-city-studio.github.io/TCS.Website/";
        const string LOGO_URL = "https://cdn.discordapp.com/attachments/1265826820880863433/1380517297411850410/TentCityStudio.png";

        const ulong FEEDBACK_CHANNEL_ID = 1374095974913278063;
        static readonly HttpClient Github = CreateGitHubClient();

        static HttpClient CreateGitHubClient() {
            var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd( "HoboBot (+https://github.com/Ddemon26/TCS-HoboBot)" );

            string? token = Environment.GetEnvironmentVariable( "GITHUB_TOKEN" );
            if ( !string.IsNullOrWhiteSpace( token ) ) {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue( "Bearer", token );
            }

            return client;
        }

        class GitHubUser {
            [JsonProperty( "public_repos" )] public int PublicRepos { get; set; }
            [JsonProperty( "followers" )] public int Followers { get; set; }
            [JsonProperty( "following" )] public int Following { get; set; }
            [JsonProperty( "html_url" )] public string HtmlUrl { get; set; } = string.Empty;
        }

        class GitHubRepo {
            [JsonProperty( "name" )] public string Name { get; set; } = string.Empty;
            [JsonProperty( "stargazers_count" )] public int Stars { get; set; }
            [JsonProperty( "html_url" )] public string HtmlUrl { get; set; } = string.Empty;
        }

        record GitStats(int PublicRepos, int TotalStars, string TopRepoName, int TopRepoStars, string TopRepoUrl, int Followers, int Following);

        async Task<GitStats?> FetchGitStatsAsync(string user) {
            try {
                string userJson = await Github.GetStringAsync( $"https://api.github.com/users/{user}" );
                var userObj = JsonConvert.DeserializeObject<GitHubUser>( userJson );
                if ( userObj == null ) {
                    return null;
                }

                string reposJson = await Github.GetStringAsync( $"https://api.github.com/users/{user}/repos?per_page=100" );
                List<GitHubRepo> repos = JsonConvert.DeserializeObject<List<GitHubRepo>>( reposJson ) ?? [];

                int totalStars = repos.Sum( r => r.Stars );
                var top = repos.OrderByDescending( r => r.Stars ).FirstOrDefault();

                return new GitStats(
                    userObj.PublicRepos,
                    totalStars,
                    top?.Name ?? "–",
                    top?.Stars ?? 0,
                    top?.HtmlUrl ?? userObj.HtmlUrl,
                    userObj.Followers,
                    userObj.Following
                );
            }
            catch (Exception ex) {
                await Console.Error.WriteLineAsync( $"[GitHub] Fetch failed: {ex.Message}" );
                return null;
            }
        }

        [SlashCommand( "damon", "Show a ✨flashy✨ profile card for Damon (Ddemon26)" )]
        public async Task ShowDamonAsync() {
            await DeferAsync();

            var stats = await FetchGitStatsAsync( "Ddemon26" );
            var embed = BuildEmbed( stats );
            var components = BuildComponents().Build();

            await FollowupAsync( embed: embed, components: components, ephemeral: false );
        }

        [ComponentInteraction( MENU_CUSTOM_ID )]
        public async Task HandleMenuAsync(string[] selected) {
            if ( selected.Length == 0 ) {
                return;
            }

            await DeferAsync( ephemeral: true );

            string response = selected[0] switch {
                LEARN_MORE_ID => GetLearnMoreMessage(),
                CONTACT_ID => GetContactMessage(),
                _ => string.Empty,
            };

            if ( !string.IsNullOrWhiteSpace( response ) ) {
                await FollowupAsync( response, ephemeral: true );
            }
        }

        [ComponentInteraction( LEARN_MORE_ID )]
        public async Task LearnMoreAsync() =>
            await RespondAsync( GetLearnMoreMessage(), ephemeral: true );

        [ComponentInteraction( CONTACT_ID )]
        public async Task ShowContactAsync() =>
            await RespondAsync( GetContactMessage(), ephemeral: true );

        [ComponentInteraction( INVITE_ID )]
        public async Task SendInviteAsync() =>
            await RespondAsync( $"Here’s your golden ticket! Join us → {SERVER_INVITE}", ephemeral: true );

        [ComponentInteraction( FEEDBACK_ID )]
        public async Task OpenFeedbackModalAsync() {
            var modal = new ModalBuilder()
                .WithTitle( "Drop your thoughts · Feedback for Damon" )
                .WithCustomId( FEEDBACK_MODAL_ID )
                .AddTextInput( "Your Name (optional)", "name", placeholder: "e.g. Alex", required: false, maxLength: 100 )
                .AddTextInput( "Feedback", "content", TextInputStyle.Paragraph, placeholder: "Fire away…", maxLength: 1000 );

            await RespondWithModalAsync( modal.Build() );
        }

        [ModalInteraction( FEEDBACK_MODAL_ID )]
        public async Task HandleFeedbackModalAsync(FeedbackModal modal) {
            string who = string.IsNullOrWhiteSpace( modal.Name ) ? "Anonymous" : modal.Name;

            if ( Context.Client.GetChannel( FEEDBACK_CHANNEL_ID ) is IMessageChannel ch ) {
                var embed = new EmbedBuilder()
                    .WithTitle( "📝 New Feedback" )
                    .WithColor( Color.Orange )
                    .AddField( "From", who, true )
                    .AddField( "User", $"<@{Context.User.Id}>", true )
                    .AddField( "Content", modal.Content )
                    .WithTimestamp( DateTimeOffset.UtcNow )
                    .Build();

                await ch.SendMessageAsync( embed: embed );
            }

            await RespondAsync( $"Thanks for your feedback, {who}! 🚀", ephemeral: true );
        }

        string GetLearnMoreMessage() =>
            "💡 **Damon** fuses art & code to birth open‑source magic for Unity, .NET and beyond.\n" +
            $"Peek under the hood → {PROFILE_URL}";

        string GetContactMessage() =>
            $"👋 **Need Damon’s attention?** Swing by the community HQ: {SERVER_INVITE}";

        static readonly Random Rng = new();
        static readonly uint[] Palette = [
            0xFEE440, // bright yellow
            0x00F5D4, // aqua
            0xFF80CC, // pink
            0xFFA630, // orange
            0x7F5AF0, // violet
        ];

        Embed BuildEmbed(GitStats? s) {
            uint colorHex = Palette[Rng.Next( Palette.Length )];
            var builder = new EmbedBuilder()
                .WithColor( new Color( colorHex ) )
                .WithAuthor( BuildAuthor() )
                .WithTitle( "👑 Damon · Creative Technomancer" )
                .WithDescription(
                    "Part storyteller, part engineer – Damon crafts **delightful** digital experiences & open‑source wonders.\n" +
                    "**Exploring now:** HoboBot **v2.0** – a playful, open‑source Discord bot for TCS.\n" +
                    "Runescape 2006 Private Server (RSPVP) – a nostalgic, open‑source PvP game."
                )
                .WithThumbnailUrl( LOGO_URL )
                .AddField( "✨ Creativity", "Turning wild ideas into polished prototypes", true )
                .AddField( "🔧 Tech Stack", "`C#` · `.NET 8` · `Unity` · `TypeScript` · `Java` · JavaScript", true )
                .AddField( "🚀 Highlights", "• Released *HoboBot* **v2.0** (May 2025)", false )
                .WithFooter( "Tap a button below to explore • card refreshes each summon", LOGO_URL )
                .WithTimestamp( DateTimeOffset.UtcNow );

            if ( s != null ) {
                builder.AddField(
                    "📊 GitHub Stats",
                    $"Repos **{s.PublicRepos}** · Stars **{s.TotalStars}**\n" +
                    $"Top ⭐ **[{s.TopRepoName}]({s.TopRepoUrl})** ({s.TopRepoStars}★)\n" +
                    $"Followers **{s.Followers}** · Following **{s.Following}**"
                );
            }

            return builder.Build();
        }

        static ComponentBuilder BuildComponents() {
            var menu = new SelectMenuBuilder()
                .WithPlaceholder( "Pick a path…" )
                .WithCustomId( MENU_CUSTOM_ID )
                .WithMinValues( 1 ).WithMaxValues( 1 )
                .AddOption( "Back‑story ✨", LEARN_MORE_ID, "Origin & skills" )
                .AddOption( "Contact 👋", CONTACT_ID, "Community links & DM" );

            return new ComponentBuilder()
                .WithSelectMenu( menu ) // SelectMenu will be on row 0
                .WithButton( "Join Server 💬", customId: INVITE_ID, style: ButtonStyle.Success, row: 1 )
                .WithButton( "Visit GitHub 🌐", url: PROFILE_URL, style: ButtonStyle.Link, row: 1 )
                .WithButton( "Website 🌍", url: WEBSITE_URL, style: ButtonStyle.Link, row: 1 )
                .WithButton( "HoboBot Repo⚙️", url: HUB_BOT_REPO, style: ButtonStyle.Link, row: 2 )
                .WithButton( "Send Feedback 📝", customId: FEEDBACK_ID, style: ButtonStyle.Secondary, row: 2 );
        }

        EmbedAuthorBuilder BuildAuthor() => new EmbedAuthorBuilder()
            .WithName( "Damon" )
            .WithIconUrl( LOGO_URL )
            .WithUrl( PROFILE_URL );
    }

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