using System.Globalization;
using System.Text;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using TCS.HoboBot.Data;
namespace TCS.HoboBot.Modules.CasinoGames;

public enum RouletteBetType {
    StraightUp, Split, Street, Corner, SixLine, TopLine, // Inside Bets
    Red, Black, Even, Odd, Low18, High18, // Outside Bets (1:1)
    Dozen1, Dozen2, Dozen3, // Outside Bets (2:1)
    Column1, Column2, Column3, // Outside Bets (2:1)
}

public class RouletteBet {
    public RouletteBetType BetType { get; set; }
    public float Amount { get; init; }
    public List<int> NumbersBetOn { get; set; } = [];
    public required string Description { get; set; }

    public override string ToString() {
        return $"{Description} - ${Amount:0.00}";
    }
}

public static class RouletteWheel {
    public static readonly List<(string Name, int Value, string Color)> Pockets = [];
    static readonly Random Rng = new();

    static readonly Dictionary<int, string> Reds = new() {
        { 1, "Red" }, { 3, "Red" }, { 5, "Red" }, { 7, "Red" }, { 9, "Red" }, { 12, "Red" },
        { 14, "Red" }, { 16, "Red" }, { 18, "Red" }, { 19, "Red" }, { 21, "Red" }, { 23, "Red" },
        { 25, "Red" }, { 27, "Red" }, { 30, "Red" }, { 32, "Red" }, { 34, "Red" }, { 36, "Red" },
    };

    static RouletteWheel() {
        Pockets.Add( ("0", 0, "Green") );
        Pockets.Add( ("00", -1, "Green") ); // Using -1 to represent 00 internally
        for (var i = 1; i <= 36; i++) {
            Pockets.Add( (i.ToString(), i, Reds.ContainsKey( i ) ? "Red" : "Black") );
        }
    }

    public static (string Name, int Value, string Color) Spin() {
        return Pockets[Rng.Next( Pockets.Count )];
    }

    public static bool IsRed(int value) => value > 0 && Reds.ContainsKey( value );
    public static bool IsBlack(int value) => value > 0 && !Reds.ContainsKey( value );
    public static bool IsEven(int value) => value > 0 && value % 2 == 0;
    public static bool IsOdd(int value) => value > 0 && value % 2 != 0;
    public static bool IsLow18(int value) => value >= 1 && value <= 18;
    public static bool IsHigh18(int value) => value >= 19 && value <= 36;
    public static int GetDozen(int value) {
        // 1, 2, or 3; 0 if not in a dozen
        if ( value >= 1 && value <= 12 ) {
            return 1;
        }

        if ( value >= 13 && value <= 24 ) {
            return 2;
        }

        if ( value >= 25 && value <= 36 ) {
            return 3;
        }

        return 0;
    }
    public static int GetColumn(int value) {
        // 1, 2, or 3; 0 if not in a column
        if ( value <= 0 ) {
            return 0;
        }

        if ( value % 3 == 1 ) {
            return 1;
        }

        if ( value % 3 == 2 ) {
            return 2;
        }

        if ( value % 3 == 0 ) {
            return 3;
        }

        return 0; // Should not happen for 1-36
    }
}

public sealed class RouletteModule : InteractionModuleBase<SocketInteractionContext> {
    // Store bets temporarily. For a real bot, use a database.
    static readonly Dictionary<ulong, List<RouletteBet>> ActivePlayerBets = new();
    const float MAX_BET_PER_PLACEMENT = 100f;
    const float MIN_BET_PER_PLACEMENT = 1f;

    static readonly Dictionary<RouletteBetType, int> PayoutRates = new() {
        { RouletteBetType.StraightUp, 35 }, { RouletteBetType.Split, 17 }, { RouletteBetType.Street, 11 },
        { RouletteBetType.Corner, 8 }, { RouletteBetType.SixLine, 5 }, { RouletteBetType.TopLine, 6 }, // 0,00,1,2,3
        { RouletteBetType.Red, 1 }, { RouletteBetType.Black, 1 }, { RouletteBetType.Even, 1 },
        { RouletteBetType.Odd, 1 }, { RouletteBetType.Low18, 1 }, { RouletteBetType.High18, 1 },
        { RouletteBetType.Dozen1, 2 }, { RouletteBetType.Dozen2, 2 }, { RouletteBetType.Dozen3, 2 },
        { RouletteBetType.Column1, 2 }, { RouletteBetType.Column2, 2 }, { RouletteBetType.Column3, 2 },
    };

    [SlashCommand( "roulette", "Play a game of Roulette." )]
    public async Task StartRoulette() {
        ulong userId = Context.User.Id;
        if ( !ActivePlayerBets.ContainsKey( userId ) ) {
            ActivePlayerBets[userId] = [];
        }

        await RespondAsync( embed: BuildManageBetsEmbed( userId ), components: BuildManageBetsComponents( userId ), ephemeral: true );
    }

    [ComponentInteraction( "roulette_place_bet_btn" )]
    public async Task PlaceBetButton() {
        await Context.Interaction.RespondWithModalAsync<RouletteBetModal>( "roulette_bet_modal" );
    }

    [ModalInteraction( "roulette_bet_modal" )]
    public async Task OnBetModalSubmit(RouletteBetModal modal) {
        await DeferAsync( ephemeral: true );
        ulong userId = Context.User.Id;

        if ( !float.TryParse( modal.BetAmountStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float amount ) || amount < MIN_BET_PER_PLACEMENT || amount > MAX_BET_PER_PLACEMENT ) {
            await FollowupAsync( $"Invalid bet amount. Must be between ${MIN_BET_PER_PLACEMENT:0.00} and ${MAX_BET_PER_PLACEMENT:0.00}.", ephemeral: true );
            // Optionally, re-show the main embed
            await Context.Interaction.ModifyOriginalResponseAsync( m => {
                    m.Embed = BuildManageBetsEmbed( userId );
                    m.Components = BuildManageBetsComponents( userId );
                }
            );
            return;
        }

        if ( PlayersWallet.GetBalance(Context.Guild.Id, userId ) < amount ) {
            await FollowupAsync( "You don't have enough funds for this bet.", ephemeral: true );
            await Context.Interaction.ModifyOriginalResponseAsync( m => {
                    m.Embed = BuildManageBetsEmbed( userId );
                    m.Components = BuildManageBetsComponents( userId );
                }
            );
            return;
        }

        (var parsedBet, string? error) = ParseBetDescription( modal.BetDescription.Trim(), amount );

        if ( parsedBet == null ) {
            await FollowupAsync( $"Invalid bet description: {error}. Examples: 'Red', 'Straight 5', 'Split 8 9', 'Column 1', '00'.", ephemeral: true );
        }
        else {
            if ( !ActivePlayerBets.ContainsKey( userId ) ) {
                ActivePlayerBets[userId] = [];
            }

            ActivePlayerBets[userId].Add( parsedBet );
            PlayersWallet.SubtractFromBalance(Context.Guild.Id, userId, amount );
            // No followup needed here, modify the original
        }

        await Context.Interaction.ModifyOriginalResponseAsync( m => {
                m.Embed = BuildManageBetsEmbed( userId );
                m.Components = BuildManageBetsComponents( userId );
            }
        );
    }

    [ComponentInteraction( "roulette_clear_bets_btn" )]
    public async Task ClearBetsButton() {
        await DeferAsync( ephemeral: true );
        ulong userId = Context.User.Id;
        if ( ActivePlayerBets.TryGetValue( userId, out List<RouletteBet>? bets ) && bets.Count != 0 ) {
            float totalRefund = 0;
            foreach (var bet in bets) {
                totalRefund += bet.Amount;
            }

            PlayersWallet.AddToBalance( Context.Guild.Id, userId, totalRefund );
            bets.Clear();
        }

        await Context.Interaction.ModifyOriginalResponseAsync( m => {
                m.Embed = BuildManageBetsEmbed( userId );
                m.Components = BuildManageBetsComponents( userId );
            }
        );
    }

    [ComponentInteraction( "roulette_spin_btn" )]
    public async Task SpinButton() {
        await DeferAsync( ephemeral: true );
        ulong userId = Context.User.Id;

        if ( !ActivePlayerBets.TryGetValue( userId, out List<RouletteBet>? currentBets ) || currentBets.Count == 0 ) {
            await FollowupAsync( "Please place some bets before spinning!", ephemeral: true );
            await Context.Interaction.ModifyOriginalResponseAsync( m => {
                    m.Embed = BuildManageBetsEmbed( userId );
                    m.Components = BuildManageBetsComponents( userId );
                }
            );
            return;
        }

        var spinResult = RouletteWheel.Spin();
        var resultEmbed = new EmbedBuilder()
            .WithTitle( $"Roulette Spin Result: {spinResult.Name} ({spinResult.Color})" )
            .WithColor( spinResult.Color switch {
                "Red" => Color.Red,
                "Black" => Color.DarkGrey,
                _ => Color.Green,
            } )
            .WithDescription( $"The ball landed on **{spinResult.Name} {spinResult.Color}**!\n\n**Your Bets:**" );

        float totalPayout = 0;
        float totalBetAmount = currentBets.Sum( b => b.Amount );
        List<string> winningBetDescriptions = [];

        foreach (var bet in currentBets) {
            if ( CheckWin( bet, spinResult.Value ) ) {
                int payoutRate = PayoutRates[bet.BetType];
                float winningsOnThisBet = bet.Amount * payoutRate;
                float returnedAmount = bet.Amount + winningsOnThisBet;
                totalPayout += returnedAmount;
                winningBetDescriptions.Add( $"{bet.Description} - Won ${winningsOnThisBet:0.00} (Returned ${returnedAmount:0.00})" );
            }
        }

        // Refund winnings
        if ( totalPayout > 0 ) {
            PlayersWallet.AddToBalance( Context.Guild.Id, userId, totalPayout );
        }

        // Publicly announce any net win
        float netResult = totalPayout - totalBetAmount;
        if ( netResult > 0 ) {
            await Context.Channel.SendMessageAsync( $"{Context.User.Mention} won **${netResult:0.00}** on Roulette!" );
        }

        if ( winningBetDescriptions.Count != 0 ) {
            resultEmbed.AddField( "🎉 Winning Bets! 🎉", string.Join( "\n", winningBetDescriptions ) );
        }
        else {
            resultEmbed.AddField( "No Winning Bets This Round", "Better luck next time!" );
        }

        resultEmbed.WithFooter( $"Total Bet: ${totalBetAmount:0.00} | Total Returned: ${totalPayout:0.00} | Net: ${netResult:N2}" );
        ActivePlayerBets.Remove( userId );

        var endComponents = new ComponentBuilder()
            .WithButton( "Play Again", "roulette_play_again_btn", ButtonStyle.Success )
            .WithButton( "End", "roulette_end_game_btn", ButtonStyle.Secondary );

        await Context.Interaction.ModifyOriginalResponseAsync( m => {
                m.Embed = resultEmbed.Build();
                m.Components = endComponents.Build();
            }
        );
    }

    [ComponentInteraction( "roulette_play_again_btn" )]
    public async Task PlayAgainButton() {
        ulong userId = Context.User.Id;
        if ( !ActivePlayerBets.TryGetValue( userId, out List<RouletteBet>? bet ) ) {
            ActivePlayerBets[userId] = [];
        }
        else {
            bet.Clear();
        }

        // Acknowledge the component interaction and edit *that* message
        var component = (SocketMessageComponent)Context.Interaction;
        await component.UpdateAsync( msg => {
                msg.Embed = BuildManageBetsEmbed( userId );
                msg.Components = BuildManageBetsComponents( userId );
            }
        );
    }


    [ComponentInteraction( "roulette_end_game_btn" )]
    public async Task EndGameButton() {
        await DeferAsync( ephemeral: true );
        ActivePlayerBets.Remove( Context.User.Id ); // Clean up if any residual state
        await Context.Interaction.ModifyOriginalResponseAsync( m => {
                m.Content = "Thanks for playing Roulette!";
                m.Embed = null;
                m.Components = null;
            }
        );
    }


    Embed BuildManageBetsEmbed(ulong userId) {
        var embed = new EmbedBuilder()
            .WithTitle( "Roulette Table - Place Your Bets!" )
            .WithColor( Color.DarkBlue )
            .WithDescription( "Use the buttons below to manage your bets before spinning the wheel." );

        // Add a simple representation of the wheel/table or betting areas.
        embed.AddField(
            "Betting Guide (Examples)",
            "- `Red` or `Black`\n" +
            "- `Even` or `Odd`\n" +
            "- `Low` (1-18) or `High` (19-36)\n" +
            "- `Dozen1` (1-12), `Dozen2` (13-24), `Dozen3` (25-36)\n" +
            "- `Col1`, `Col2`, `Col3` (Columns)\n" +
            "- `Straight <number>` (e.g., `Straight 17`, `Straight 0`, `Straight 00`)\n" +
            "- `Split <n1> <n2>` (e.g., `Split 8 9`)\n" +
            "- `Street <n1> <n2> <n3>` (e.g., `Street 1 2 3`)\n" +
            "- `Corner <n1> <n2> <n3> <n4>` (e.g., `Corner 1 2 4 5`)\n" +
            "- `Sixline <n1>...<n6>` (e.g., `Sixline 1 2 3 4 5 6`)\n" +
            "- `Topline` (0, 00, 1, 2, 3)"
        );


        if ( ActivePlayerBets.TryGetValue( userId, out List<RouletteBet>? bets ) && bets.Count != 0 ) {
            var sb = new StringBuilder();
            float totalBet = 0;
            foreach (var bet in bets) {
                sb.AppendLine( bet.ToString() );
                totalBet += bet.Amount;
            }

            embed.AddField( "Your Current Bets:", sb.ToString().Length > 1024 ? "Too many bets to display all." : sb.ToString() );
            embed.WithFooter( $"Total Bet Amount: ${totalBet:0.00} | Balance: ${PlayersWallet.GetBalance( Context.Guild.Id, userId ):0.00}" );
        }
        else {
            embed.AddField( "Your Current Bets:", "No bets placed yet." );
            embed.WithFooter( $"Balance: ${PlayersWallet.GetBalance( Context.Guild.Id, userId ):0.00}" );
        }

        return embed.Build();
    }

    MessageComponent BuildManageBetsComponents(ulong userId) {
        bool hasBets = ActivePlayerBets.TryGetValue( userId, out List<RouletteBet>? bets ) && bets.Count != 0;
        return new ComponentBuilder()
            .WithButton( "Place Bet", "roulette_place_bet_btn", ButtonStyle.Success )
            .WithButton( "Clear All My Bets", "roulette_clear_bets_btn", ButtonStyle.Danger, disabled: !hasBets )
            .WithButton( "Spin the Wheel!", "roulette_spin_btn", disabled: !hasBets )
            .Build();
    }

    (RouletteBet?, string? error) ParseBetDescription(string input, float amount) {
        var bet = new RouletteBet { Amount = amount, Description = string.Empty };
        input = input.ToLowerInvariant().Trim();
        string[] parts = input.Split( new[] { ' ', ',', '-' }, StringSplitOptions.RemoveEmptyEntries );

        // Simple outside bets
        if ( parts.Length == 1 ) {
            switch (parts[0]) {
                case "red":
                    bet.BetType = RouletteBetType.Red;
                    bet.Description = "Red";
                    return (bet, null);
                case "black":
                    bet.BetType = RouletteBetType.Black;
                    bet.Description = "Black";
                    return (bet, null);
                case "even":
                    bet.BetType = RouletteBetType.Even;
                    bet.Description = "Even";
                    return (bet, null);
                case "odd":
                    bet.BetType = RouletteBetType.Odd;
                    bet.Description = "Odd";
                    return (bet, null);
                case "low":
                case "low18":
                case "1-18":
                    bet.BetType = RouletteBetType.Low18;
                    bet.Description = "Low (1-18)";
                    return (bet, null);
                case "high":
                case "high18":
                case "19-36":
                    bet.BetType = RouletteBetType.High18;
                    bet.Description = "High (19-36)";
                    return (bet, null);
                case "dozen1":
                    bet.BetType = RouletteBetType.Dozen1;
                    bet.Description = "1st Dozen (1-12)";
                    return (bet, null);
                case "dozen2":
                    bet.BetType = RouletteBetType.Dozen2;
                    bet.Description = "2nd Dozen (13-24)";
                    return (bet, null);
                case "dozen3":
                    bet.BetType = RouletteBetType.Dozen3;
                    bet.Description = "3rd Dozen (25-36)";
                    return (bet, null);
                case "col1":
                case "column1":
                    bet.BetType = RouletteBetType.Column1;
                    bet.Description = "1st Column";
                    return (bet, null);
                case "col2":
                case "column2":
                    bet.BetType = RouletteBetType.Column2;
                    bet.Description = "2nd Column";
                    return (bet, null);
                case "col3":
                case "column3":
                    bet.BetType = RouletteBetType.Column3;
                    bet.Description = "3rd Column";
                    return (bet, null);
                case "topline":
                    bet.BetType = RouletteBetType.TopLine;
                    bet.NumbersBetOn.AddRange( [-1, 0, 1, 2, 3] ); // 00, 0, 1, 2, 3
                    bet.Description = "Top Line (0,00,1,2,3)";
                    return (bet, null);
            }

            // Straight up 0 or 00 if entered directly
            if ( parts[0] == "0" || parts[0] == "00" ) {
                bet.BetType = RouletteBetType.StraightUp;
                bet.NumbersBetOn.Add( parts[0] == "00" ? -1 : 0 );
                bet.Description = $"Straight Up {parts[0]}";
                return (bet, null);
            }
        }

        // Bets with "straight", "split", etc. prefix
        if ( parts.Length > 1 ) {
            string command = parts[0];
            List<int> nums = [];
            for (var i = 1; i < parts.Length; i++) {
                if ( parts[i] == "00" ) {
                    nums.Add( -1 );
                }
                else if ( int.TryParse( parts[i], out int n ) && ((n >= 0 && n <= 36) || n == -1) ) {
                    nums.Add( n );
                }
                else {
                    return (null, $"Invalid number '{parts[i]}'.");
                }
            }

            nums = nums.Distinct().OrderBy( n => n ).ToList(); // Ensure distinct and ordered for some validations

            switch (command) {
                case "straight":
                    if ( nums.Count == 1 && ((nums[0] >= 0 && nums[0] <= 36) || nums[0] == -1) ) {
                        bet.BetType = RouletteBetType.StraightUp;
                        bet.NumbersBetOn = nums;
                        bet.Description = $"Straight Up {(nums[0] == -1 ? "00" : nums[0].ToString())}";
                        return (bet, null);
                    }

                    break;
                case "split":
                    if ( nums.Count == 2 && IsValidSplit( nums[0], nums[1] ) ) {
                        // Implement IsValidSplit
                        bet.BetType = RouletteBetType.Split;
                        bet.NumbersBetOn = nums;
                        bet.Description = $"Split {(nums[0] == -1 ? "00" : nums[0].ToString())}-{(nums[1] == -1 ? "00" : nums[1].ToString())}";
                        return (bet, null);
                    }

                    break;
                case "street":
                    if ( nums.Count == 3 && IsValidStreet( nums ) ) {
                        // Implement IsValidStreet
                        bet.BetType = RouletteBetType.Street;
                        bet.NumbersBetOn = nums;
                        bet.Description = $"Street {string.Join( ",", nums.Select( n => n == -1 ? "00" : n.ToString() ) )}";
                        return (bet, null);
                    }

                    break;
                case "corner":
                    if ( nums.Count == 4 && IsValidCorner( nums ) ) {
                        // Implement IsValidCorner
                        bet.BetType = RouletteBetType.Corner;
                        bet.NumbersBetOn = nums;
                        bet.Description = $"Corner {string.Join( ",", nums.Select( n => n == -1 ? "00" : n.ToString() ) )}";
                        return (bet, null);
                    }

                    break;
                case "sixline":
                    if ( nums.Count == 6 && IsValidSixLine( nums ) ) {
                        // Implement IsValidSixLine
                        bet.BetType = RouletteBetType.SixLine;
                        bet.NumbersBetOn = nums;
                        bet.Description = $"Six Line {string.Join( ",", nums.Select( n => n == -1 ? "00" : n.ToString() ) )}";
                        return (bet, null);
                    }

                    break;
            }
        }

        return (null, "Unrecognized bet type or format.");
    }

    // Basic validation helpers (can be expanded)
    // For simplicity, these are not exhaustive table validation logic.
    bool IsValidSplit(int n1, int n2) {
        // Very basic: checks if numbers are numerically adjacent or one is 0/00 and the other is 1/2/3 or 0 and 00
        if ( (n1 == 0 && n2 == -1) || (n1 == -1 && n2 == 0) ) {
            return true; // 0-00 split
        }

        if ( n1 == 0 || n1 == -1 ) {
            // 0 or 00 with 1,2,3
            return n2 >= 1 && n2 <= 3; // Check adjacency if one is 0 or 00
        }

        if ( n2 == 0 || n2 == -1 ) {
            return (n1 >= 1 && n1 <= 3);
        }

        if ( n1 > 0 && n2 > 0 ) {
            // Both are 1-36
            int diff = Math.Abs( n1 - n2 );
            return diff == 1 || diff == 3; // Horizontal or Vertical adjacency on typical layout
        }

        return false; // default deny
    }
    bool IsValidStreet(List<int> nums) => nums.Count == 3 && nums[0] > 0 && nums[1] == nums[0] + 1 && nums[2] == nums[0] + 2 && (nums[0] - 1) % 3 == 0;
    bool IsValidCorner(List<int> nums) {
        // simplified
        if ( nums.Count != 4 ) {
            return false;
        }

        // Example: 1,2,4,5 (diffs: 1, 3, 1) or 0,00,1,2 (less strict)
        return true; // Needs proper layout logic
    }
    bool IsValidSixLine(List<int> nums) => nums.Count == 6 && nums[0] > 0 && (nums[0] - 1) % 3 == 0 && nums.SequenceEqual( Enumerable.Range( nums[0], 6 ) );


    bool CheckWin(RouletteBet bet, int winningPocketValue) {
        return bet.BetType switch {
            RouletteBetType.StraightUp => bet.NumbersBetOn.Contains( winningPocketValue ),
            RouletteBetType.Split => bet.NumbersBetOn.Contains( winningPocketValue ),
            RouletteBetType.Street => bet.NumbersBetOn.Contains( winningPocketValue ),
            RouletteBetType.Corner => bet.NumbersBetOn.Contains( winningPocketValue ),
            RouletteBetType.SixLine => bet.NumbersBetOn.Contains( winningPocketValue ),
            RouletteBetType.TopLine => bet.NumbersBetOn.Contains( winningPocketValue ) // 0,00,1,2,3
            ,
            RouletteBetType.Red => RouletteWheel.IsRed( winningPocketValue ),
            RouletteBetType.Black => RouletteWheel.IsBlack( winningPocketValue ),
            RouletteBetType.Even => RouletteWheel.IsEven( winningPocketValue ),
            RouletteBetType.Odd => RouletteWheel.IsOdd( winningPocketValue ),
            RouletteBetType.Low18 => RouletteWheel.IsLow18( winningPocketValue ),
            RouletteBetType.High18 => RouletteWheel.IsHigh18( winningPocketValue ),
            RouletteBetType.Dozen1 => RouletteWheel.GetDozen( winningPocketValue ) == 1,
            RouletteBetType.Dozen2 => RouletteWheel.GetDozen( winningPocketValue ) == 2,
            RouletteBetType.Dozen3 => RouletteWheel.GetDozen( winningPocketValue ) == 3,
            RouletteBetType.Column1 => RouletteWheel.GetColumn( winningPocketValue ) == 1,
            RouletteBetType.Column2 => RouletteWheel.GetColumn( winningPocketValue ) == 2,
            RouletteBetType.Column3 => RouletteWheel.GetColumn( winningPocketValue ) == 3,
            _ => false,
        };
    }
}

// `RouletteModule.cs`
public class RouletteBetModal : IModal {
    public string Title => "Place Your Roulette Bet";

    [InputLabel( "Bet Description" )]
    [ModalTextInput(
        "bet_description_input",
        placeholder: "e.g. Red, Straight 00, Split 8 9"
    )]
    public string BetDescription { get; set; } = string.Empty;

    [InputLabel( "Bet Amount" )]
    [ModalTextInput(
        "bet_amount_input",
        placeholder: "e.g. 10"
    )]
    public string BetAmountStr { get; set; } = string.Empty;
}