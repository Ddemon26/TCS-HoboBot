using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Discord;
using Discord.Interactions;
namespace TCS.HoboBot.Modules.SwordsAndSandals;

public class SwordAndSandalsModule : InteractionModuleBase<SocketInteractionContext> {
    public readonly ConcurrentDictionary<ulong, SasGame> Games = new();
    [SlashCommand( "swordsandsandals", "Start a Swords & Sandals duel." )]
    public async Task SwordsAndSandalsAsync() 
    {
        await DeferAsync( ephemeral: true );
        
        // Check if the user already has a game
        if ( Games.TryGetValue( Context.User.Id, out var existingGame ) ) {
            var uthEmbed = BuildUthEmbed(
                existingGame,
                "Swords & Sandals Duel",
                $"You already have an active game with Gladiator: {existingGame.Gladiator}",
                "Use `/swordsandsandals reset` to start over.",
                Color.Orange
            );
            await RespondAsync( embed: uthEmbed.Build(), ephemeral: true );
            return;
        }

        // Create a new game for the user
        var game = new SasGame(Context.User.Id);
        Games[Context.User.Id] = game;

        var embed = BuildUthEmbed(
            game,
            "Swords & Sandals Duel",
            "Your gladiator is ready! Use `/swordsandsandals fight` to start the duel.",
            "Good luck!",
            Color.Green
        );
        await RespondAsync( embed: embed.Build() , ephemeral: true );
        
    }
    
    public async Task OnEndGameAsync() {
        await DeferAsync();
        await ModifyOriginalResponseAsync( m => {
                m.Embed = new EmbedBuilder()
                    .WithTitle( "Ultimate Texas Hold'em – Game Over" )
                    .WithDescription( $"{Context.User.Mention} ended the game." )
                    .WithColor( Color.DarkGrey )
                    .Build();
                m.Components = new ComponentBuilder().Build();
            }
        );
    }
    
    EmbedBuilder BuildUthEmbed(SasGame game, string title, string description, string footer, Color color) {
        var embed = new EmbedBuilder()
            .WithAuthor( Context.User.GlobalName, Context.User.GetAvatarUrl() )
            .WithTitle( title )
            .WithDescription( description )
            .WithCurrentTimestamp()
            .WithColor( color )
            .WithFooter( footer, Context.User.GetAvatarUrl() );

        return embed;
    }
}

public class SasGame {
    public Guid Id { get; private set; }
    public ulong DiscordUserId { get; set; }
    public int Gold { get; set; }
    public int Wins { get; set; }
    
    public Gladiator Gladiator { get; }

    public SasGame(ulong discordUserId) {
        Id = Guid.NewGuid();
        DiscordUserId = discordUserId;
        Gold = 0;
        Wins = 0;
        Gladiator = new Gladiator();
    }
}

/// <summary>
/// Represents a gladiator in Swords& SandalsI, including all core stats and currently‑equipped gear.
/// Target runtime: .NET 8 / C#13.
/// </summary>
public sealed class Gladiator {
    // ──────────────────────────  Core progression  ──────────────────────────
    public int Level { get; private set; } = 1;
    public int Experience { get; private set; }

    // ──────────────────────────  Primary attributes  ────────────────────────
    public int Strength { get; private set; } // Bonus damage applied to every weapon swing
    public int Agility { get; private set; } // Movement range per turn, jump distance
    public int Attack { get; private set; } // Chance‑to‑hit with melee attacks
    public int Defence { get; private set; } // Reduces enemy hit chance
    public int Vitality { get; private set; } // Increases maximum health pool
    public int Charisma { get; private set; } // Crowd gold, Taunt potency, shop discount
    public int Stamina { get; private set; } // Size of the energy bar / recovery per Rest

    // ──────────────────────────  Equipment  ─────────────────────────────────
    public Weapon? Weapon { get; private set; }

    readonly Dictionary<ArmorSlot, Armor?> m_armour = new() {
        { ArmorSlot.Helmet, null },
        { ArmorSlot.Chest, null },
        { ArmorSlot.Shoulders, null },
        { ArmorSlot.Gloves, null },
        { ArmorSlot.Greaves, null },
        { ArmorSlot.Boots, null },
        { ArmorSlot.Shield, null }
    };
    public IReadOnlyDictionary<ArmorSlot, Armor?> Armour => m_armour;

    // ──────────────────────────  Public API  ───────────────────────────────

    /// <summary>
    /// Allocate free stat points earned on level‑up or at character creation.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">If <paramref name="points"/> ≤ 0.</exception>
    /// <exception cref="ArgumentException">If <paramref name="statName"/> is unknown.</exception>
    public void AllocateStat(string statName, int points) {
        if ( points <= 0 ) throw new ArgumentOutOfRangeException( nameof(points) );

        switch (statName.ToLowerInvariant()) {
            case "str" or "strength": Strength += points; break;
            case "agi" or "agility": Agility += points; break;
            case "atk" or "attack": Attack += points; break;
            case "def" or "defence": Defence += points; break;
            case "vit" or "vitality": Vitality += points; break;
            case "cha" or "charisma": Charisma += points; break;
            case "sta" or "stamina": Stamina += points; break;
            default:
                throw new ArgumentException( $"Unknown stat '{statName}'.", nameof(statName) );
        }
    }

    /// <summary>
    /// Add experience, automatically handling incremental level‑ups.
    /// </summary>
    public void GainExperience(int amount) {
        if ( amount <= 0 ) return;
        Experience += amount;
        while (Experience >= ExperienceToNextLevel( Level )) {
            Experience -= ExperienceToNextLevel( Level );
            Level++;
        }
    }

    /// <summary>
    /// Attempt to equip a weapon. Throws if level requirements are not met.
    /// </summary>
    public void Equip(Weapon weapon) {
        if ( weapon.LevelRequirement > Level )
            throw new InvalidOperationException( "Level too low to equip this weapon." );
        Weapon = weapon;
    }

    /// <summary>
    /// Attempt to equip an armor piece. Throws if level requirements are not met.
    /// </summary>
    public void Equip(Armor armour) {
        if ( armour.LevelRequirement > Level )
            throw new InvalidOperationException( "Level too low to equip this armour." );
        m_armour[armour.Slot] = armour;
    }

    public void UnequipWeapon() => Weapon = null;
    public void Unequip(ArmorSlot slot) => m_armour[slot] = null;

    /// <summary>Aggregate armour value across all equipped pieces.</summary>
    public int TotalArmour() {
        var total = 0;
        foreach (var piece in m_armour.Values)
            if ( piece is not null )
                total += piece.ArmorValue;
        return total;
    }

    /// <summary>Weapon damage range including Strength bonus; returns (min,max).</summary>
    public (int Min, int Max) WeaponDamage() =>
        Weapon is null
            ? (1 + Strength, 2 + Strength)
            : (Weapon.MinDamage + Strength, Weapon.MaxDamage + Strength);

    // ──────────────────────────  Helpers  ───────────────────────────────────
    static int ExperienceToNextLevel(int currentLevel) => 100 + currentLevel * 50;
}

// ──────────────────────────  Supporting types  ─────────────────────────────
public enum WeaponType { Sword, Axe, Mace, Exotic }
public enum ArmorSlot { Helmet, Chest, Shoulders, Gloves, Greaves, Boots, Shield }

public sealed class Weapon {
    public required string Name { get; init; }
    public WeaponType Type { get; init; }
    public int Tier { get; init; }
    public int MinDamage { get; init; }
    public int MaxDamage { get; init; }
    public int LevelRequirement { get; init; }
}

public sealed class Armor {
    public required string Name { get; init; }
    public ArmorSlot Slot { get; init; }
    public int Tier { get; init; }
    public int ArmorValue { get; init; }
    public int LevelRequirement { get; init; }
}

public static class ItemLoader {
    /// <summary>
    /// Asynchronously loads <see cref="ItemCatalogue"/> from a JSON file on disk.
    /// </summary>
    /// <exception cref="InvalidDataException">If the file is missing mandatory sections or is empty.</exception>
    public static async Task<ItemCatalogue> LoadAsync(CancellationToken ct = default) {
        // TCS.HoboBot/Modules/SwordsAndSandals/Items/Items.json
        string repoRoot = Path.GetFullPath(
            Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", ".."
            )
        );

        string itemsFile = Path.Combine(
            repoRoot,
            "TCS.HoboBot", // project folder
            "Modules",
            "SwordsAndSandals",
            "Items",
            "Items.json"
        );

        if ( !File.Exists( itemsFile ) )
            throw new FileNotFoundException( $"Item file not found at: {itemsFile}." );

        await using var fs = File.OpenRead( itemsFile );

        var root = new Root();

        root = await JsonSerializer.DeserializeAsync<Root>( fs, JsonOpts, ct )
               ?? throw new InvalidDataException( "Item file is empty or corrupt." );

        // Sanity-check: at least *something* to work with
        if ( (root.Weapons?.Count ?? 0) == 0 && (root.Armours?.Count ?? 0) == 0 )
            throw new InvalidDataException( "Item file contains no weapons or armour." );

        return new ItemCatalogue( root.Weapons ?? [], root.Armours ?? [] );
    }

    static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web) {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = {
            new JsonStringEnumConverter( JsonNamingPolicy.CamelCase )
        }
    };
}

// ----------  Private DTO matching the on-disk shape ----------
public sealed class Root {
    [JsonPropertyName( "weapons" )] public List<Weapon>? Weapons { get; init; }
    [JsonPropertyName( "armours" )] public List<Armor>? Armours { get; init; }
}

/// <summary>
/// Reads the "master" item catalogue (weapons and armour) from a single JSON file.
/// Thread-safe after construction.
/// </summary>
public sealed class ItemCatalogue {
    // ────────── Public read-only views ──────────
    public IReadOnlyList<Weapon> Weapons { get; }
    public IReadOnlyList<Armor> Armours { get; }

    // Quick indices for fast gameplay look-ups
    readonly Dictionary<string, Weapon> m_weaponByName;
    readonly Dictionary<string, Armor> m_armourByName;
    readonly Dictionary<(ArmorSlot Slot, int Tier), Armor> m_armourBySlotTier;

    // ----------  Construction ----------
    public ItemCatalogue(List<Weapon> weapons, List<Armor> armours) {
        Weapons = weapons;
        Armours = armours;

        m_weaponByName = weapons.ToDictionary( w => w.Name, StringComparer.OrdinalIgnoreCase );
        m_armourByName = armours.ToDictionary( a => a.Name, StringComparer.OrdinalIgnoreCase );
        m_armourBySlotTier = armours.ToDictionary( a => (a.Slot, a.Tier) );
    }

    // ----------  Lookup helpers (optional but handy) ----------
    public Weapon GetWeapon(string name) => m_weaponByName[name];
    public Armor GetArmour(string name) => m_armourByName[name];
    public Armor? TryGetArmour(ArmorSlot slot, int tier) =>
        m_armourBySlotTier.GetValueOrDefault( (slot, tier) );
}

/// <summary>
/// Lightweight, single‑file battle engine that plugs into <see cref="Gladiator"/> and mimics the
/// 2005 Swords & Sandals turn system. Inspired by the original manual fileciteturn0file0.
/// </summary>
public enum CombatAction { Charge, QuickAttack, NormalAttack, PowerAttack, Taunt, Rest }

/// <summary>
/// Runtime state wrapper around a <see cref="Gladiator"/> – tracks mutable Health, Energy and arena position.
/// </summary>
public sealed class BattleGladiator {
    public Gladiator Data { get; }
    public int MaxHealth { get; }
    public int Health { get; private set; }
    public int MaxEnergy { get; }
    public int Energy { get; private set; }
    public int Position { get; set; } // tiles from arena centre (‑ve = west, +ve = east)

    public bool IsAlive => Health > 0;

    public BattleGladiator(Gladiator data, int startPosition = 0) {
        Data = data ?? throw new ArgumentNullException( nameof(data) );
        MaxHealth = 50 + data.Vitality * 10; // baseline + VIT scaling
        Health = MaxHealth;
        MaxEnergy = 100 + data.Stamina * 5; // baseline + STA scaling
        Energy = MaxEnergy;
        Position = startPosition;
    }

    public void SpendEnergy(int amount) => Energy = Math.Max( 0, Energy - amount );
    public void RecoverEnergy(int amount) => Energy = Math.Min( MaxEnergy, Energy + amount );
    public void Heal(int amount) => Health = Math.Min( MaxHealth, Health + amount );
    public void TakeDamage(int amount) => Health -= amount;
    public override string ToString() => $"Lvl {Data.Level} Gladiator";
}

/// <summary>
/// Resolves a full duel (or round‑robin turns if you want to integrate with an external UI).
/// </summary>
public sealed class BattleEngine {
    static readonly Random Rng = new();

    // ──────────  Tweakables – straight from the design doc  ──────────
    static readonly Dictionary<CombatAction, int> EnergyCost = new() {
        [CombatAction.Charge] = 30, // High
        [CombatAction.QuickAttack] = 10, // Low
        [CombatAction.NormalAttack] = 20, // Moderate
        [CombatAction.PowerAttack] = 35, // High
        [CombatAction.Taunt] = 0,
        [CombatAction.Rest] = 0
    };

    static readonly Dictionary<CombatAction, int> BaseAccuracy = new() {
        [CombatAction.Charge] = 50, // Low
        [CombatAction.QuickAttack] = 90, // Very High
        [CombatAction.NormalAttack] = 70, // Average
        [CombatAction.PowerAttack] = 40, // Lowest
        [CombatAction.Taunt] = 100, // Always “lands” (but may be resisted)
        [CombatAction.Rest] = 100
    };

    static readonly Dictionary<CombatAction, float> DmgMultiplier = new() {
        [CombatAction.Charge] = 1.20f,
        [CombatAction.QuickAttack] = 0.80f,
        [CombatAction.NormalAttack] = 1.00f,
        [CombatAction.PowerAttack] = 1.50f,
        [CombatAction.Taunt] = 0.00f,
        [CombatAction.Rest] = 0.00f
    };

    const int ArenaHalfWidth = 5; // 11‑tile board ➜ spikes at ±6

    readonly BattleGladiator _left;
    readonly BattleGladiator _right;

    public BattleEngine(BattleGladiator left, BattleGladiator right) {
        _left = left;
        _right = right;
    }

    /// <summary>
    /// Runs until one combatant dies or is ring‑out killed. Returns a full turn‑by‑turn transcript.
    /// </summary>
    public IReadOnlyList<string> Fight(
        Func<BattleGladiator, CombatAction> pickLeft,
        Func<BattleGladiator, CombatAction> pickRight
    ) {
        List<string> log = new List<string>();
        var round = 1;

        while (_left.IsAlive && _right.IsAlive) {
            log.Add( $"\n—— Round {round} ———————————————" );

            ResolveTurn( _left, _right, pickLeft( _left ), log );
            if ( !_right.IsAlive ) break;

            ResolveTurn( _right, _left, pickRight( _right ), log );
            if ( !_left.IsAlive ) break;

            round++;
        }

        return log;
    }

    void ResolveTurn(BattleGladiator actor, BattleGladiator target, CombatAction action, List<string> log) {
        if ( actor.Energy < EnergyCost[action] && action != CombatAction.Rest ) {
            // Allow Rest even if 0 energy
            log.Add( $"{actor} is exhausted – they Rest instead." );
            action = CombatAction.Rest;
        }

        actor.SpendEnergy( EnergyCost[action] );

        switch (action) {
            case CombatAction.Rest:
                actor.RecoverEnergy( 30 + actor.Data.Stamina * 3 ); // Base + Stamina scaling [cite: 24, 53]
                actor.Heal( 10 + actor.Data.Vitality * 2 ); // Base + Vitality scaling (indirectly) [cite: 22, 53]
                log.Add( $"{actor} takes a breather (+HP, +Energy)." );
                break;

            case CombatAction.Taunt:
                actor.RecoverEnergy( 10 ); // "also restores a sliver of Energy" [cite: 50]
                log.Add( $"{actor} uses Taunt." );

                // Anvil chance: 1/10,000 [cite: 49]
                if ( Rng.Next( 0, 10000 ) == 0 ) {
                    // The text doesn't specify anvil damage. Let's make it a very high fixed amount.
                    // It could also be an instant kill or percentage of health.
                    var anvilDamage = 150;
                    target.TakeDamage( anvilDamage );
                    log.Add( $"A DREADED ANVIL falls from the sky, crushing {target} for {anvilDamage} damage!" );
                    // No other taunt effects if anvil drops.
                }
                else {
                    // If no anvil, Taunt Varies: can push, pull, or damage [cite: 49]
                    // We'll use a random roll to decide the effect.
                    int effectRoll = Rng.Next( 0, 3 );
                    switch (effectRoll) {
                        case 0: // Push
                            var pushAmount = 1; // Standard push amount for taunt
                            int targetOriginalPosition = target.Position;
                            if ( actor == _left ) {
                                // Actor on left pushes target (on right) further right
                                target.Position += pushAmount;
                            }
                            else {
                                // Actor on right pushes target (on left) further left
                                target.Position -= pushAmount;
                            }

                            log.Add( $"{actor}'s taunt is so intimidating it pushes {target} back {pushAmount} tile(s)!" );

                            // Check for ring-out after push [cite: 54]
                            if ( Math.Abs( target.Position ) > ArenaHalfWidth ) {
                                target.TakeDamage( target.Health ); // Force 0 HP for ring-out
                                log.Add( $"{target} is taunted into the spikes – instant death!" );
                            }

                            break;
                        case 1: // Pull
                            var pullAmount = 1; // Standard pull amount for taunt
                            int currentDistance = Math.Abs( actor.Position - target.Position );

                            if ( currentDistance > 1 ) {
                                // Can only pull if not already adjacent
                                if ( actor == _left ) {
                                    // Actor on left pulls target (on right) to the left
                                    target.Position -= pullAmount;
                                }
                                else {
                                    // Actor on right pulls target (on left) to the right
                                    target.Position += pullAmount;
                                }

                                log.Add( $"{actor}'s taunt is so alluring it pulls {target} forward {pullAmount} tile(s)!" );
                            }
                            else if ( currentDistance == 1 ) {
                                log.Add( $"{actor} tries to taunt-pull {target}, but they are already adjacent!" );
                            }
                            else {
                                // currentDistance == 0; should ideally not happen if positions are managed.
                                log.Add( $"{actor} tries to taunt-pull {target}, but they are on the same tile!" );
                            }

                            break;
                        case 2: // Damage
                            // Charisma makes Taunt deadlier [cite: 23]
                            // The "Charisma tank" build can do 300-600 true damage late-game with Taunt [cite: 26]
                            // This implies Charisma can get very high, and the damage scales well.
                            // The formula Rng.Next(1,5) + actor.Data.Charisma is a direct interpretation.
                            int tauntDmg = Rng.Next( 1, 5 ) + actor.Data.Charisma;
                            target.TakeDamage( tauntDmg ); // Damage from Taunt is often "true" or bypasses armour.
                            log.Add( $"{actor} taunts {target} for {tauntDmg} moral damage – the crowd loves it!" );
                            break;
                    }
                }

                break;

            case CombatAction.Charge:
            case CombatAction.QuickAttack:
            case CombatAction.NormalAttack:
            case CombatAction.PowerAttack:
                // Default case removed as all CombatActions are handled or fall into Attack
                Attack( actor, target, action, log );
                break;
        }

        if ( !target.IsAlive && action != CombatAction.Taunt && action != CombatAction.Rest ) {
            // Check if attack killed, taunt handles its own kill messages
            // Taunt has its own logic for logging death (anvil, push to spikes)
            if ( Math.Abs( target.Position ) <= ArenaHalfWidth ) {
                // Only log "has fallen" if not by ring-out (already logged)
                log.Add( $"{target} has fallen – {actor} claims victory!" );
            }
        }
        else if ( target is { IsAlive: false, Health: <= 0 } && action is CombatAction.Taunt or CombatAction.Rest ) {
            // This handles cases where Taunt's direct damage (not anvil/spikes) or other effects might kill.
            // Or if some other passive effect caused death during a Rest/Taunt turn (though not modeled here).
            if ( Math.Abs( target.Position ) <= ArenaHalfWidth && !log.Last().Contains( "spikes" ) && !log.Last().Contains( "anvil" ) ) {
                log.Add( $"{target} succumbs to their wounds after {actor}'s {action} – {actor} claims victory!" );
            }
        }
    }

    void Attack(BattleGladiator atk, BattleGladiator def, CombatAction mode, List<string> log) {
        int acc = BaseAccuracy[mode] + atk.Data.Attack - def.Data.Defence;
        acc = Math.Clamp( acc, 5, 95 );
        bool hit = Rng.Next( 0, 100 ) < acc;

        if ( !hit ) {
            log.Add( $"{atk}’s {mode} misses!" );
            return;
        }

        (int min, int max) = atk.Data.WeaponDamage();
        var dmg = (int)(Rng.Next( min, max + 1 ) * DmgMultiplier[mode]);

        dmg = Math.Max( 1, dmg - def.Data.TotalArmour() ); // flat soak
        def.TakeDamage( dmg );
        log.Add( $"{atk} lands a {mode} for {dmg} damage." );

        // Knock‑back and ring‑out
        int kb = mode switch {
            CombatAction.PowerAttack => 3,
            CombatAction.Charge => 2,
            CombatAction.NormalAttack => 1,
            _ => 0,
        };
        if ( kb > 0 )
            def.Position += atk == _left ? kb : -kb;

        if ( Math.Abs( def.Position ) > ArenaHalfWidth ) {
            def.TakeDamage( def.Health ); // force 0 HP
            log.Add( $"{def} is knocked into the spikes – instant death!" );
        }
    }
}