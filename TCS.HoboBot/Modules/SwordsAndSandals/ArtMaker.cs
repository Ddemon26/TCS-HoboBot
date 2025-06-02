using System.Text;
namespace TCS.HoboBot.Services;

public static class ArtMaker {
    /// <summary>
    /// Returns two stick figures with spacing and underscore count derived from a single factor.
    /// </summary>
    /// <param name="baseSize">Determines the number of underscores for the legs.
    /// This value is also used as the length of the spacing gap inserted between
    /// the main components (head, arms) of the two figures.</param>
    /// <returns>A string representing the two stick figures.</returns>
    public static string GetStickFigures(int baseSize = 1) {
        // if ( msg.Content.Contains( "stickman", StringComparison.OrdinalIgnoreCase ) ) {
        //     string art = ArtMaker.GetStickFigures();
        //     // Wrap the art in a Markdown code block
        //     await msg.Channel.SendMessageAsync(
        //         "Here are two stick figures with a gap between them:\n```" +
        //         art +
        //         "```"
        //     );
        //     return;
        // }
            
            
        if ( baseSize < 1 )
            throw new ArgumentOutOfRangeException(
                nameof(baseSize),
                "baseSize must be at least 1."
            );

        // 1) “Padded” arm and leg shapes (each is exactly 5 characters wide):
        //    - armsBlock  = "␣/|\␣"  (length = 5, center '|' at index 2)
        //    - legsBlock  = "␣/ ␣\␣" (length = 5, center between '/' and '\' at index 2)
        const string armsBlock = " /|\\ "; // C# literal: space, '/', '|', '\', space
        const string legsBlock = " / \\ "; // C# literal: space, '/', space, '\', space

        // 2) How many spaces “slot in” between the two arm‐blocks (head is handled separately):
        var gapSpaces = new string( ' ', baseSize );

        // 3) How many underscores between the two leg‐blocks:
        var legUnderscores = new string( '_', baseSize );

        var sb = new StringBuilder();

        //
        // —— 1) HEAD ROW —— 
        //
        // We want each 'O' directly above the '|' at index 2 of its armBlock.
        //   • left armBlock starts at index 0, so its center (|) is at index 2.
        //   • right armBlock starts at index (armsBlock.Length + baseSize) = 5 + baseSize.
        //     → its center ('|') is at (5 + baseSize) + 2 = baseSize + 7.
        //
        // Therefore:
        //   – Place first 'O' at index 2 → write PadLeft(2) + "O".
        //   – Then from index 3 we need to go up to (baseSize + 7) to put the second 'O'.
        //     → gap length = (baseSize + 7) − 3 = baseSize + 4.
        //   – Finally write the second "O".
        //
        const int centerOffset = 2; // index where 'O' sits above the first '|'
        int head2Position = baseSize + 7; // index where second 'O' must land

        //  Write “  O”  (two spaces + 'O') so that first 'O' is at index 2
        sb.Append( new string( ' ', centerOffset ) );
        sb.Append( 'O' );

        //  Next, insert exactly (baseSize + 4) spaces so that the second 'O' arrives at index (baseSize + 7):
        int betweenHeads = head2Position - (centerOffset + 1);
        sb.Append( new string( ' ', betweenHeads ) );
        sb.Append( 'O' );
        sb.AppendLine();

        //
        // —— 2) ARM ROW —— 
        //
        // Left armBlock goes at index 0 → its center ('|') is at index 2.
        // Right armBlock should go at index (armsBlock.Length + baseSize) = 5 + baseSize → its center at baseSize + 7.
        //
        sb.Append( armsBlock ); // length = 5, occupies indices [0..4]; center '|' at index 2
        sb.Append( gapSpaces ); // length = baseSize
        sb.Append( armsBlock ); // second block at indices [5 + baseSize .. 9 + baseSize]
        sb.AppendLine();

        //
        // —— 3) LEG ROW —— 
        //
        // Each legsBlock is 5 chars wide, with its “feet” ("/  \ ") spanning the same center alignment as the arms.
        // Insert legUnderscores (baseSize many “_”) between the two leg blocks.
        //
        sb.Append( legsBlock ); // left legsBlock at indices [0..4]; this “\” sits under the same |
        sb.Append( legUnderscores ); // underscores count = baseSize
        sb.Append( legsBlock ); // right legsBlock at indices [5 + baseSize .. 9 + baseSize]

        return sb.ToString();
    }
}