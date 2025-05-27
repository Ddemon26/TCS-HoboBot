using System.Collections.Concurrent;
using System.Text.Json;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using TCS.HoboBot.Data;
namespace TCS.HoboBot.Modules.Util;

public static class WeaponShop {
    public record WeaponInfo(
        string Name, // Name of the weapon
        decimal Price // Price of the weapon in the shop
    );

    static readonly ConcurrentDictionary<ulong, ConcurrentDictionary<ulong, int>> PlayerWeapons = new();
    public static readonly ConcurrentDictionary<ulong, ConcurrentDictionary<ulong, int>> PendingSelections = new();
    const string FILE_PATH = "PlayerWeapons.json";

    static string GetFilePath(ulong guildId) => Path.Combine( "Data", guildId.ToString(), FILE_PATH );

    public static readonly Dictionary<int, WeaponInfo> Weapons = new()
    {
        // TIER 1 – STREET MISCHIEF ─────────────────────
        {  0, new WeaponInfo("Rubber Band Snapper",          100m) },
        {  1, new WeaponInfo("Brass Knuckles",               500m) },
        {  2, new WeaponInfo("Rubber Chicken",             1_000m) },
        {  3, new WeaponInfo("Baseball Bat",               1_500m) },
        {  4, new WeaponInfo("Combat Knife",               2_500m) },

        // TIER 2 – SIDEARMS & SMALL ARMS ────────────────
        {  5, new WeaponInfo("Handgun",                    5_000m) },
        {  6, new WeaponInfo("Revolver",                   7_500m) },
        {  7, new WeaponInfo("Silly-String Blaster",      10_000m) },
        {  8, new WeaponInfo("Pump Shotgun",              13_000m) },
        {  9, new WeaponInfo("Assault Rifle",             17_000m) },

        // TIER 3 – SERIOUS FIREPOWER ────────────────────
        { 10, new WeaponInfo("Sniper Rifle",              25_000m) },
        { 11, new WeaponInfo("Light Machine Gun",         32_000m) },
        { 12, new WeaponInfo("Frag Grenade",              40_000m) },
        { 13, new WeaponInfo("Banana-Peel Bomb",          50_000m) },
        { 14, new WeaponInfo("C4 Explosive",              60_000m) },

        // TIER 4 – HEAVY / CROWD-CONTROL ────────────────
        { 15, new WeaponInfo("Rocket Launcher",           75_000m) },
        { 16, new WeaponInfo("Flamethrower",              90_000m) },
        { 17, new WeaponInfo("Minigun",                  110_000m) },
        { 18, new WeaponInfo("Water-Balloon Cannon",     130_000m) },
        { 19, new WeaponInfo("Confetti Cannon",          150_000m) },

        // TIER 5 – PRESTIGE / RIDICULOUS ────────────────
        { 20, new WeaponInfo("Whoopee-Cushion Mine",     175_000m) },
        { 21, new WeaponInfo("Pie Launcher 3000",        200_000m) },
        { 22, new WeaponInfo("Air-Horn Howitzer",        225_000m) },
        { 23, new WeaponInfo("Giant Rubber-Duck Torpedo",240_000m) },
        { 24, new WeaponInfo("Golden AK-47",             250_000m) },
    };



    public static bool CanAfford(ulong guildId, ulong userId, int weapon) {
        decimal price = Weapons[weapon].Price;
        return PlayersWallet.GetBalance( guildId, userId ) >= (float)price;
    }

    public static void BuyWeapon(ulong guildId, ulong userId, int weapon) {
        if ( !CanAfford( guildId, userId, weapon ) ) {
            return;
        }

        decimal price = Weapons[weapon].Price;
        PlayersWallet.SubtractFromBalance( guildId, userId, (float)price );
        ConcurrentDictionary<ulong, int> guildWeapons = PlayerWeapons
            .GetOrAdd( guildId, _ => new ConcurrentDictionary<ulong, int>() );

        guildWeapons[userId] = weapon;
    }
    
    public static int GetHighestWeapon(ulong guildId, ulong userId) {
        if ( PlayerWeapons.TryGetValue( guildId, out ConcurrentDictionary<ulong, int>? guildWeapons ) ) {
            if ( guildWeapons.TryGetValue( userId, out int weapon ) ) {
                return weapon;
            }
        }
        return -1; // No weapon found
    }

    //save and load
    public static async Task SaveAsync() {
        foreach (ulong guildId in PlayerWeapons.Keys) {
            await SaveWeapons( guildId );
        }
    }
    public static async Task SaveWeapons(ulong guildId) {
        string filePath = GetFilePath( guildId );
        Directory.CreateDirectory( Path.GetDirectoryName( filePath )! );
        await File.WriteAllTextAsync( filePath, Serialize( PlayerWeapons[guildId] ) );
    }

    public static async Task LoadWeapons(IReadOnlyCollection<SocketGuild> guilds) {
        foreach (var guild in guilds) {
            ulong guildId = guild.Id;
            string filePath = GetFilePath( guildId );
            if ( !File.Exists( filePath ) ) {
                continue;
            }

            string json = await File.ReadAllTextAsync( filePath );
            ConcurrentDictionary<ulong, int>? loadedWeapons = Deserialize<ConcurrentDictionary<ulong, int>>( json );
            if ( loadedWeapons != null ) {
                PlayerWeapons[guildId] = loadedWeapons;
            }
        }
    }

    static readonly JsonSerializerOptions WriteOptions = new() {
        WriteIndented = true,
    };
    static readonly JsonSerializerOptions? ReadOptions = new() {
        AllowTrailingCommas = true,
    };

    static string Serialize<T>(T value) => JsonSerializer.Serialize( value, WriteOptions );
    static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>( json, ReadOptions );
}

public class WeaponBuyModule : InteractionModuleBase<SocketInteractionContext> {
    const string CUSTOM_ID_PREFIX = "buy_weapon";

    [SlashCommand( "weapon_buy", "Buy a weapon" )]
    public async Task BuyWeaponAsync() {
        // Build the select-menu options for each weapon in the shop.
        List<SelectMenuOptionBuilder> options = WeaponShop.Weapons
            .Select( weapon => new SelectMenuOptionBuilder {
                    Label = weapon.Value.Name,
                    Value = weapon.Key.ToString(),
                    Description = $"Price: ${weapon.Value.Price:0.00}",
                }
            ).ToList();

        var select = new SelectMenuBuilder()
            .WithCustomId( $"{CUSTOM_ID_PREFIX}:select:{Context.User.Id}" )
            .WithPlaceholder( "Select a weapon to purchase" )
            .WithMinValues( 1 )
            .WithMaxValues( 1 )
            .WithOptions( options );

        var buttons = new ComponentBuilder()
            .WithSelectMenu( select )
            .WithButton( "Buy", $"{CUSTOM_ID_PREFIX}:buy:{Context.User.Id}", ButtonStyle.Success )
            .WithButton( "Exit", $"{CUSTOM_ID_PREFIX}:exit:{Context.User.Id}", ButtonStyle.Danger );

        await RespondAsync( "Choose a weapon, then press **Buy** or **Exit**:", components: buttons.Build(), ephemeral: true );
    }

    [ComponentInteraction( "buy_weapon:select:*" )]
    public async Task HandleSelectAsync(string targetUserId, string[] selections) {
        if ( Context.User.Id.ToString() != targetUserId ) {
            await RespondAsync( "This select menu isn\\'t for you.", ephemeral: true );
            return;
        }

        if ( selections.Length == 0 || !int.TryParse( selections[0], out int selectedWeapon ) || !WeaponShop.Weapons.ContainsKey( selectedWeapon ) ) {
            await RespondAsync( "Invalid selection.", ephemeral: true );
            return;
        }

        WeaponShop.PendingSelections
            .GetOrAdd( Context.Guild.Id, _ => new ConcurrentDictionary<ulong, int>() )
            [Context.User.Id] = selectedWeapon;
        await DeferAsync( ephemeral: true );
    }

    [ComponentInteraction( "buy_weapon:buy:*" )]
    public async Task HandleBuyAsync(string targetUserId) {
        if ( Context.User.Id.ToString() != targetUserId ) {
            await RespondAsync( "These buttons aren\\'t for you.", ephemeral: true );
            return;
        }

        ConcurrentDictionary<ulong, int> guildSelections = WeaponShop.PendingSelections.GetOrAdd( Context.Guild.Id, _ => new ConcurrentDictionary<ulong, int>() );
        if ( !guildSelections.TryRemove( Context.User.Id, out int selectedWeapon ) ) {
            await RespondAsync( "Please pick a weapon first.", ephemeral: true );
            return;
        }

        ulong guildId = Context.Guild.Id;
        ulong userId = Context.User.Id;

        if ( !WeaponShop.CanAfford( guildId, userId, selectedWeapon ) ) {
            await RespondAsync( "You don't have enough funds for that weapon.", ephemeral: true );
            return;
        }

        try {
            WeaponShop.BuyWeapon( guildId, userId, selectedWeapon );
        }
        catch (InvalidOperationException ex) {
            await RespondAsync( ex.Message, ephemeral: true );
            return;
        }
        
// After
        var weaponInfo = WeaponShop.Weapons[selectedWeapon];

        await RespondAsync( $"✅ {Context.User.Mention} purchased **{weaponInfo.Name}** " +
                            $"for ${weaponInfo.Price:0.00}!", ephemeral: false );
    }

    [ComponentInteraction( "buy_weapon:exit:*" )]
    public async Task HandleExitAsync(string targetUserId) {
        if ( Context.User.Id.ToString() != targetUserId ) {
            await RespondAsync( "These buttons aren\\'t for you.", ephemeral: true );
            return;
        }

        ConcurrentDictionary<ulong, int> guildSelections = WeaponShop
            .PendingSelections.GetOrAdd( Context.Guild.Id, _ => new ConcurrentDictionary<ulong, int>() );

        guildSelections.TryRemove( Context.User.Id, out _ );
        await DeferAsync( ephemeral: true );
        await Context.Interaction.ModifyOriginalResponseAsync( m => {
                m.Embed = new EmbedBuilder()
                    .WithTitle( "Weapon Purchase – Transaction Ended" )
                    .WithDescription( $"{Context.User.Mention} has ended the transaction." )
                    .Build();
                m.Components = new ComponentBuilder().Build();
            }
        );
    }
}

public class RobUserModule : InteractionModuleBase<SocketInteractionContext> {
    static readonly Random Rng = new();

    [SlashCommand( "rob", "Attempt to rob a user (max 30% of their total, rare chance)." )]
    public async Task RobAsync(SocketUser target) {
        ulong userId = Context.User.Id;
        var now = DateTimeOffset.UtcNow;
        var next = Cooldowns.Get( Context.Guild.Id, userId, CooldownKind.Rob );
        if ( now < next ) {
            var remaining = next - now;
            await RespondAsync(
                $"⏳ You need to wait **{remaining:mm\\:ss}** before robbing again.",
                ephemeral: true
            );
            return;
        }

        Cooldowns.Set( Context.Guild.Id, userId, CooldownKind.Rob, now + Cooldowns.Cooldown( CooldownKind.Rob ) );

        // Cannot rob yourself.
        if ( target.Id == Context.User.Id ) {
            await RespondAsync( "You cannot rob yourself!" );
            return;
        }

        float targetBalance = PlayersWallet.GetBalance( Context.Guild.Id, target.Id );
        if ( targetBalance <= 0 ) {
            await RespondAsync( $"{target.Mention} has no cash to steal!" );
            return;
        }
        
        float chance = WeaponShop.GetHighestWeapon( Context.Guild.Id, Context.User.Id ) * 100 / 100f;
        
        if ( Rng.NextDouble() < chance ) {
            float minSteal = targetBalance * 0.10f;
            float maxSteal = targetBalance * 0.30f;
            float amount = minSteal + (float)Rng.NextDouble() * (maxSteal - minSteal);

            PlayersWallet.SubtractFromBalance( Context.Guild.Id, target.Id, amount );
            PlayersWallet.AddToBalance( Context.Guild.Id, Context.User.Id, amount );

            await RespondAsync(
                $"{Context.User.Mention} successfully robbed {target.Mention} for **${amount:0.00}**!"
            );
        }
        else {
            await RespondAsync(
                $"{Context.User.Mention} attempted to rob {target.Mention} but got caught!"
            );
        }
    }
}