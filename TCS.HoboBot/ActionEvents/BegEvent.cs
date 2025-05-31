using System.Security.Cryptography;
using Newtonsoft.Json;
namespace TCS.HoboBot.ActionEvents;

public static class ProstitutionEvents {
    // Immutable record representing one prostitution event
    record ProstitutionEvent(
        int Weight, // relative likelihood (larger = more common)
        Func<float> Delta, // returns the cash change for this event
        Func<float, string> Story); // builds a message with that change

    // ---------- Event table ----------
    // 2 events in total – a mix of good, bad, and neutral outcomes
    static readonly ProstitutionEvent[] Events = [
        // 1) Favorable outcome – a generous client
        new(
            9,
            () => RandomNumberGenerator.GetInt32( 50, 100 ),
            d => $"A kind client pays you a handsome tip of **${d:0.00}**!"
        ),
        // 2) Unfavorable outcome – a run-in with the law
        new(
            1,
            () => -20f,
            d => $"Trouble strikes as the law catches up with you – you lose **${MathF.Abs( d ):0.00}** in fines."
        ),

        // ... (other events omitted for brevity)
    ];

    static readonly int TotalWeight = ComputeTotalWeight();

    /// <summary>
    /// Randomly picks a prostitution event according to the weights.
    /// </summary>
    public static (float Delta, string Story) Roll() {
        int pick = RandomNumberGenerator.GetInt32( 0, TotalWeight ); // [0, _totalWeight)
        var tally = 0;
        foreach (var e in Events) {
            tally += e.Weight;
            if ( pick < tally ) {
                float delta = e.Delta();
                return (delta, e.Story( delta ));
            }
        }

        // Fallback in case no event has been selected.
        return (0f, "Nothing happens… the night is uneventful.");
    }

    static int ComputeTotalWeight() => Events.Sum( e => e.Weight );
}

public static class WorkEvents
{
    // Immutable record representing one work event
    record WorkEvent(
        int Weight,                 // relative likelihood (larger = more common)
        Func<float> Delta,          // returns the cash change for this event
        Func<float, string> Story); // builds a message with that change

    // ---------- Event table ----------
    // 20 events – ≈90 % positive, 5 % neutral, 5 % negative
    static readonly WorkEvent[] Events = [
        new(
            5,
            () => RandomNumberGenerator.GetInt32( 10, 100 ), // $10.01–100.00
            d => $"A man in a van picks you up like the mexican you are. He gives you **${d:0.00}** for a few hours of work."
        ),
        new(
            5,
            () => RandomNumberGenerator.GetInt32( 80, 150 ), // $10.01–100.00
            d => $"A man in a van picks you up like the mexican you are. He gives you **${d:0.00}** for a few hours of work."
        ),
        // 2) 20% – ignored
        new(
            1,
            () => 0f,
            _ => "Nobody seems to want to hire a bum like you. **No cash this time.**"
        ),
        // 1) Day-labour at a building site
        new(5,
            () => RandomNumberGenerator.GetInt32(100, 200),
            d => $"You haul bricks all morning on a construction site and earn **${d:0.00}**."),

        // 2) Weekend café shift
        new(5,
            () => RandomNumberGenerator.GetInt32(50, 120),
            d => $"A local café needs an extra pair of hands. You pocket **${d:0.00}** in wages and tips."),

        // 3) Yard-work for a neighbour
        new(5,
            () => RandomNumberGenerator.GetInt32(30, 80),
            d => $"You mow lawns and trim hedges, earning **${d:0.00}**."),

        // 4) Same-day package deliveries
        new(5,
            () => RandomNumberGenerator.GetInt32(60, 150),
            d => $"You spend the afternoon zipping around town with parcels and make **${d:0.00}**."),

        // 5) Bartending a private party
        new(5,
            () => RandomNumberGenerator.GetInt32(80, 180),
            d => $"You mix drinks at a birthday bash and walk away with **${d:0.00}** including tips."),

        // 6) Quick freelance coding fix
        new(5,
            () => RandomNumberGenerator.GetInt32(150, 300),
            d => $"You squash a bug in a client’s app and invoice **${d:0.00}**."),

        // 7) One-hour tutoring session
        new(5,
            () => RandomNumberGenerator.GetInt32(40, 90),
            d => $"You tutor a high-school student and earn **${d:0.00}**."),

        // 8) Overnight pet-sitting
        new(5,
            () => RandomNumberGenerator.GetInt32(25, 60),
            d => $"You feed and walk a neighbour’s dog and receive **${d:0.00}**."),

        // 9) Street performance tips
        new(5,
            () => RandomNumberGenerator.GetInt32(10, 70),
            d => $"Your guitar case fills with **${d:0.00}** in tips."),

        // 10) Temp office admin
        new(5,
            () => RandomNumberGenerator.GetInt32(70, 120),
            d => $"You file paperwork for an afternoon and get **${d:0.00}**."),

        // 11) Weekend car-wash pop-up
        new(5,
            () => RandomNumberGenerator.GetInt32(20, 50),
            d => $"Soapy buckets and elbow grease net you **${d:0.00}**."),

        // 12) Fence-painting job
        new(5,
            () => RandomNumberGenerator.GetInt32(30, 90),
            d => $"You paint a picket fence for **${d:0.00}**."),

        // 13) University research survey
        new(5,
            () => RandomNumberGenerator.GetInt32(15, 40),
            d => $"You answer questions in a psychology lab and earn **${d:0.00}**."),

        // 14) Heavy-lifting / moving furniture
        new(5,
            () => RandomNumberGenerator.GetInt32(80, 160),
            d => $"You help move a sofa up three flights of stairs and make **${d:0.00}**."),

        // 15) Weekend photo shoot assistant
        new(5,
            () => RandomNumberGenerator.GetInt32(120, 250),
            d => $"You hold reflectors and carry gear; the photographer pays **${d:0.00}**."),

        // 16) Part-time security shift
        new(5,
            () => RandomNumberGenerator.GetInt32(90, 180),
            d => $"You keep watch at an event and collect **${d:0.00}**."),

        // 17) Catering kitchen prep
        new(5,
            () => RandomNumberGenerator.GetInt32(60, 140),
            d => $"You chop vegetables for a banquet and take home **${d:0.00}**."),

        // 18) Remote data-entry sprint
        new(5,
            () => RandomNumberGenerator.GetInt32(25, 75),
            d => $"You hammer spreadsheets for a few hours and earn **${d:0.00}**."),

        // 19) Neutral – no gigs today
        new(5,
            () => 0f,
            _ => "You hand out résumés and scroll job boards, but nothing pans out. **No cash this time.**"),

        // 20) Negative – wallet stolen
        new(5,
            () => -RandomNumberGenerator.GetInt32(10, 40),
            d => $"A pick-pocket bumps you on a crowded bus. You lose **${-d:0.00}.**")
    ];

    static readonly int TotalWeight = ComputeTotalWeight();

    /// <summary>Pick a random event according to the weights.</summary>
    public static (float Delta, string Story) Roll()
    {
        int pick = RandomNumberGenerator.GetInt32(0, TotalWeight); // [0, totalWeight)
        var tally = 0;

        foreach (var e in Events)
        {
            tally += e.Weight;
            if (pick < tally)
            {
                float delta = e.Delta();
                return (delta, e.Story(delta));
            }
        }

        // Should never fall through
        return (0f, "Nothing happens… the streets are quiet.");
    }

    static int ComputeTotalWeight()
    {
        var sum = 0;
        foreach (var e in Events)
            sum += e.Weight;
        return sum;
    }
}


public enum DeltaType {
    Range,
    Constant,
}

public class BegEventInfo {
    public int Weight { get; set; }
    public DeltaType DeltaType { get; set; }
    // Used for Range events (values in cents for better precision)
    public int DeltaMinCents { get; set; }
    public int DeltaMaxCents { get; set; }
    // Used for Constant events
    public float ConstantDelta { get; set; }
    // A template where "{delta}" will be replaced with the computed delta
    public string StoryTemplate { get; set; } = string.Empty;
}

public static class BegEventsLoader {
    const string FILE_PATH = "beg_events.json";
    static string GetFilePath( ) => Path.Combine( "Data", "Events", FILE_PATH );
    public static List<BegEventInfo> LoadBegEvents() {
        string json = File.ReadAllText( GetFilePath() );
        List<BegEventInfo>? events = JsonConvert.DeserializeObject<List<BegEventInfo>>( json );
        return events ?? [];
    }

    public static (float Delta, string Story) Roll(List<BegEventInfo> events) {
        var totalWeight = 0;
        foreach (var ev in events) {
            totalWeight += ev.Weight;
        }

        int pick = RandomNumberGenerator.GetInt32( 0, totalWeight );
        var tally = 0;

        foreach (var ev in events) {
            tally += ev.Weight;
            if ( pick < tally ) {
                float delta = ev.DeltaType switch {
                    DeltaType.Range =>
                        RandomNumberGenerator.GetInt32( ev.DeltaMinCents, ev.DeltaMaxCents + 1 ) / 100f,
                    DeltaType.Constant => ev.ConstantDelta,
                    _ => 0f,
                };

                string story = ev.StoryTemplate.Replace( "{delta}", delta.ToString( "0.00" ) );
                return (delta, story);
            }
        }

        return (0f, "Nothing happens… the streets are quiet.");
    }
}

/// <summary>
/// A single hobo‑life “event” with a weight, a cash delta generator,
/// and a story template that turns that delta into flavored text.
/// </summary>
public static class BegEvents {
    // Immutable record representing one beg event
    record BegEvent(
        int Weight, // relative likelihood (larger = more common)
        Func<float> Delta, // returns the cash change for this event
        Func<float, string> Story); // builds a message with that change

    // ---------- Event table ----------
    // 50 events in total – a mix of good, bad, and neutral outcomes
    static readonly BegEvent[] Events = [
        // 1) 40% – spare change from a passer‑by
        new(
            40,
            () => RandomNumberGenerator.GetInt32( 10, 101 ) / 100f, // $0.10–1.00
            d => $"A passer‑by drops **${d:0.00}** into your tin can."
        ),

        // 2) 20% – ignored
        new(
            20,
            () => 0f,
            _ => "You rattle your cup, but everyone just hurries past… **no cash this time.**"
        ),

        // 3) 8% – pick‑pocket steals $0.05‑0.50
        new(
            8,
            () => -RandomNumberGenerator.GetInt32( 5, 51 ) / 100f,
            d => $"👤 A pick‑pocket nicks **${MathF.Abs( d ):0.00}** from your change!"
        ),

        // 4) 8% – street musician shares $2
        new(
            8,
            () => 2f,
            _ => "🎶 A busking guitarist takes pity and hands you **$2.00** from their hat."
        ),

        // 5) 8% – find a $5 bill
        new(
            8,
            () => 5f,
            _ => "🍀 Lucky find! A crumpled **$5.00** bill was lying near the curb!"
        ),

        // 6) 4% – police fine you $1 for loitering
        new(
            4,
            () => -1f,
            _ => "🚓 Uh‑oh! The police fine you **$1.00** for loitering."
        ),

        // 7) 1% – drop $10 down a storm drain
        new(
            1,
            () => -10f,
            _ => "😩 You fumble your coins and watch **$10.00** disappear down a storm drain!"
        ),

        // 8) 1% – jackpot $20 wallet
        new(
            1,
            () => 20f,
            _ => "💰 Jackpot! You spot an unattended wallet stuffed with **$20.00**!"
        ),

        // 9) child drops a few coins – $0.01‑0.10
        new(
            10,
            () => RandomNumberGenerator.GetInt32( 1, 11 ) / 100f,
            d => $"👦 A curious child giggles and drops **${d:0.00}** into your cup."
        ),

        // 10) generous tourist – $5‑15
        new(
            3,
            () => RandomNumberGenerator.GetInt32( 500, 1501 ) / 100f,
            d => $"🌍 A generous tourist slips you **${d:0.00}** after chatting about the city."
        ),

        // 11) spill your cup – lose $0.10‑0.75
        new(
            4,
            () => -RandomNumberGenerator.GetInt32( 10, 76 ) / 100f,
            d => $"😖 You jostle your cup, spilling **${MathF.Abs( d ):0.00}** onto the sidewalk!"
        ),

        // 12) cookies, no cash
        new(
            5,
            () => 0f,
            _ => "🍪 A kindly grandmother offers you warm cookies — no money, but they smell amazing."
        ),

        // 13) street magician tips $1
        new(
            6,
            () => 1f,
            _ => "🎩 A street magician finishes his set and flicks you **$1.00** with a flourish."
        ),

        // 14) taxi splashes puddle – lose $0.05
        new(
            7,
            () => -0.05f,
            _ => "🚕 A taxi hits a puddle, drenching you. In the commotion you drop **$0.05**."
        ),

        // 15) barista’s leftover +$0.25
        new(
            4,
            () => 0.25f,
            _ => "☕ A friendly barista hands you a day‑old muffin and **$0.25** in change."
        ),

        // 16) preacher gives pamphlet — no cash
        new(
            3,
            () => 0f,
            _ => "🙏 A street preacher offers a pamphlet but no money. You nod politely."
        ),

        // 17) winning scratch card $1‑25
        new(
            1,
            () => RandomNumberGenerator.GetInt32( 100, 2501 ) / 100f,
            d => $"🎉 You find a discarded scratch‑off ticket — it still pays out **${d:0.00}!**"
        ),

        // 18) dog steals hot‑dog → $2
        new(
            2,
            () => -2f,
            _ => "🐕 A stray dog snatches the hot‑dog you just bought! **-$2.00** gone."
        ),

        // 19) recycle cans $1‑3
        new(
            4,
            () => RandomNumberGenerator.GetInt32( 100, 301 ) / 100f,
            d => $"♻️ You cash in bottles and cans for **${d:0.00}** at the depot."
        ),

        // 20) security guard chases you — no cash
        new(
            4,
            () => 0f,
            _ => "🛑 A mall security guard chases you away — nothing earned."
        ),

        // 21) odd job +$5
        new(
            2,
            () => 5f,
            _ => "🔧 You help unload boxes for a local charity and earn **$5.00**."
        ),

        // 22) drunken banker tips +$10
        new(
            1,
            () => 10f,
            _ => "🍻 A drunk banker stumbles by and drops **$10.00** into your hand."
        ),

        // 23) gamblers’ coin toss ±$1
        new(
            2,
            () => RandomNumberGenerator.GetInt32( 0, 2 ) == 0 ? -1f : 1f,
            d => d > 0
                ? $"🪙 Heads! A group of gamblers flick you **${d:0.00}**."
                : $"🪙 Tails! You lose **${MathF.Abs( d ):0.00}** in a quick bet."
        ),

        // 24) tourist photo +$2
        new(
            5,
            () => 2f,
            _ => "📸 A tourist pays **$2.00** so you’ll pose for a quirky photo."
        ),

        // 25) buy bus fare - $2.50
        new(
            3,
            () => -2.5f,
            _ => "🚌 You need a ride across town and spend **$2.50** on bus fare."
        ),

        // 26) find lucky quarter +$0.25
        new(
            9,
            () => 0.25f,
            _ => "🍀 You spot a shiny quarter on the pavement — **$0.25** richer!"
        ),

        // 27) seagull snatches bill −$1
        new(
            1,
            () => -1f,
            _ => "🕊️ A seagull swoops down and steals a crumpled **$1.00** bill!"
        ),

        // 28) old friend gives $15
        new(
            1,
            () => 15f,
            _ => "🤝 An old friend recognizes you and presses **$15.00** into your hand."
        ),

        // 29) market stall fruit – no cash
        new(
            3,
            () => 0f,
            _ => "🍎 A vendor gives you bruised fruit — tasty, but no money."
        ),

        // 30) donating plasma +$30
        new(
            1,
            () => 30f,
            _ => "🩸 You donate plasma and collect **$30.00**."
        ),

        // 31) drop your last penny −$0.01
        new(
            12,
            () => -0.01f,
            _ => "🪙 A single penny slips through a grate — every cent counts! **-$0.01**."
        ),

        // 32) soup‑kitchen voucher – no cash
        new(
            2,
            () => 0f,
            _ => "🥣 Volunteers give you a meal voucher — no cash exchanged."
        ),

        // 33) pay street artist −$0.50
        new(
            4,
            () => -0.5f,
            _ => "🖌️ A street artist sketches your portrait; you tip **$0.50**."
        ),

        // 34) win dice game +$8
        new(
            1,
            () => 8f,
            _ => "🎲 Lucky roll! You win **$8.00** in a sidewalk dice game."
        ),

        // 35) lose dice game −$3
        new(
            1,
            () => -3f,
            _ => "🎲 Snake‑eyes… you lose **$3.00** gambling."
        ),

        // 36) vendor tip +$0.50
        new(
            6,
            () => 0.5f,
            _ => "🍡 A food‑truck vendor hands you **$0.50** with a free sample."
        ),

        // 37) cop apology quarter +$0.25
        new(
            2,
            () => 0.25f,
            _ => "🚓 A remorseful cop drops **$0.25** after nearly bumping you."
        ),

        // 38) kid steals sign −$0.10
        new(
            2,
            () => -0.1f,
            _ => "😾 A mischievous kid grabs your cardboard sign; in the scuffle you lose **$0.10**."
        ),

        // 39) cabbie rounds up +$0.75
        new(
            3,
            () => 0.75f,
            _ => "🚖 A grateful cabbie rounds up the fare he just asked you to watch, giving **$0.75**."
        ),

        // 40) news interview +$25
        new(
            1,
            () => 25f,
            _ => "📺 A local reporter pays **$25.00** to interview you about life on the street."
        ),

        // 41) fountain fishing +$0.50‑2.00
        new(
            5,
            () => RandomNumberGenerator.GetInt32( 50, 201 ) / 100f,
            d => $"⛲ You fish **${d:0.00}** in coins from a public fountain."
        ),

        // 42) promo rep +$1
        new(
            6,
            () => 1f,
            _ => "🎁 A street‑team rep hands you a promo **$1.00** bill."
        ),

        // 43) losing lottery ticket −$2
        new(
            2,
            () => -2f,
            _ => "🎫 You splurge on a lottery ticket… and immediately regret the **$2.00** loss."
        ),

        // 44) bottle deposit +$0.60‑1.80
        new(
            5,
            () => RandomNumberGenerator.GetInt32( 60, 181 ) / 100f,
            d => $"🧴 Bottle return nets you **${d:0.00}**."
        ),

        // 45) vendor short‑changes you −$0.30
        new(
            4,
            () => -0.3f,
            _ => "💸 A vendor short‑changes you by **$0.30** — too late to argue."
        ),

        // 46) wallet return +$3
        new(
            2,
            () => 3f,
            _ => "👍 You return a lost wallet; the owner rewards you with **$3.00**."
        ),

        // 47) courier knocks cup −$0.15‑0.60
        new(
            4,
            () => -RandomNumberGenerator.GetInt32( 15, 61 ) / 100f,
            d => $"🚴 A bike courier whizzes past, knocking over your cup — **${MathF.Abs( d ):0.00}** in coins scatter."
        ),

        // 48) bar crowd coins +$0.50‑2.00
        new(
            4,
            () => RandomNumberGenerator.GetInt32( 50, 201 ) / 100f,
            d => $"🍻 A rowdy bar crowd showers you with **${d:0.00}** in loose change."
        ),

        // 49) festival gift card +$5
        new(
            3,
            () => 5f,
            _ => "🎉 A street‑festival vendor hands you a **$5.00** food‑stall gift card."
        ),

        // 50) dud scratch‑ticket – no cash
        new(
            4,
            () => 0f,
            _ => "😑 You find another scratch‑off — alas, it’s a dud. No money today."
        ),

        // 3) 1% – mr beast gives $100
        new(
            1,
            () => 1000f,
            _ => "💵 Mr. Beast walks by and gives you **$1000.00** for no reason."
        ),
    ];

    static readonly int TotalWeight = ComputeTotalWeight();

    /// <summary>Pick a random event according to the weights.</summary>
    public static (float Delta, string Story) Roll() {
        int pick = RandomNumberGenerator.GetInt32( 0, TotalWeight ); // [0, totalWeight)
        var tally = 0;

        foreach (var e in Events) {
            tally += e.Weight;
            if ( pick >= tally ) {
                continue;
            }

            float delta = e.Delta();
            return (delta, e.Story( delta ));
        }

        // Should never fall through
        return (0f, "Nothing happens… the streets are quiet.");
    }

    static int ComputeTotalWeight() => Events.Sum( e => e.Weight );
}