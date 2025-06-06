// RoleService.cs  – .NET 8  •  Discord.Net 3
using System.Collections.Concurrent;
using System.Text.Json;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
namespace TCS.HoboBot.Services;

//────────────────────────── ENUM & RECORD ──────────────────────────//

public enum HoboBotRoles {
    //Default = 0, // 0x54DA0D
    LowLevelDealer, // 0x3498db
    PettyDrugDealer, // 0x2ecc71
    StreetDealer, // 0x95a5a6
    Pimp, // 0xe67e22
    Kingpin, // 0xe74c3c
    DrugLord, // 0x8e44ad
    Underboss, // 0x2c3e50 
    Godfather, // 0x34495e
}

public readonly record struct RoleSpec(
    HoboBotRoles Key,
    string Name,
    GuildPermissions Permissions,
    Color? Color = null,
    bool Hoist = false,
    bool Mentionable = false);

//────────────────────────── ROLE SERVICE ───────────────────────────//

public sealed class RoleService : IHostedService, IDisposable {
    /*── canonical spec list – tweak to taste ──*/
    static readonly RoleSpec[] Specs = [
        new(HoboBotRoles.LowLevelDealer, "LowLevelDealer", GuildPermissions.None, new Color( 0x3498db )),
        new(HoboBotRoles.PettyDrugDealer, "PettyDrugDealer", GuildPermissions.None, new Color( 0x2ecc71 )),
        new(HoboBotRoles.StreetDealer, "StreetDealer", GuildPermissions.None, new Color( 0x95a5a6 )),
        new(HoboBotRoles.Pimp, "Pimp", GuildPermissions.None, new Color( 0xe67e22 )),
        new(HoboBotRoles.Kingpin, "Kingpin", GuildPermissions.None, new Color( 0xe74c3c )),
        new(HoboBotRoles.DrugLord, "DrugLord", GuildPermissions.None, new Color( 0x8e44ad )),
        new(HoboBotRoles.Underboss, "Underboss", GuildPermissions.None, new Color( 0x2c3e50 )),
        new(HoboBotRoles.Godfather, "Godfather", GuildPermissions.None, new Color( 0x34495e )),
    ];

    static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };

    //───────────────────────── infrastructure ──────────────────────//
    readonly DiscordSocketClient m_client;
    readonly string m_cachePath;
    readonly CancellationTokenSource m_cts = new();

    // GuildId → ( CanonRole → RoleId )
    readonly ConcurrentDictionary<ulong, SemaphoreSlim> m_guildLocks = new();
    public RoleService(DiscordSocketClient client, IHostEnvironment env) {
        m_client = client ?? throw new ArgumentNullException( nameof(client) );
        m_cachePath = Path.Combine( env.ContentRootPath, "RolesCache.json" );

        LoadCache();
    }

    //────────────────────────── PUBLIC API ─────────────────────────//

    async Task EnsureRolesAsync(SocketGuild guild, CancellationToken ct = default) {
        if ( !guild.CurrentUser.GuildPermissions.ManageRoles ) {
            return;
        }

        var gate = m_guildLocks.GetOrAdd( guild.Id, _ => new SemaphoreSlim( 1, 1 ) );
        await gate.WaitAsync( ct ); // ⬅️ serialise per guild
        try {
            // ***** capture roles AFTER we hold the lock *****
            Dictionary<string, IRole> rolesByName = guild.Roles.ToDictionary<SocketRole, string, IRole>(
                r => r.Name, r => r, StringComparer.OrdinalIgnoreCase
            );

            Dictionary<HoboBotRoles, ulong> map = HoboBotRolesManager.GetCache().GetOrAdd( guild.Id, _ => new Dictionary<HoboBotRoles, ulong>() );

            foreach (var spec in Specs) {
                ct.ThrowIfCancellationRequested();
                IRole? role = null;

                // 1️⃣ cached ID
                if ( map.TryGetValue( spec.Key, out ulong id ) ) {
                    role = guild.GetRole( id );
                }

                // 2️⃣ name match
                role ??= rolesByName.GetValueOrDefault( spec.Name );

                // 3️⃣ create if missing
                if ( role is null ) {
                    role = await guild.CreateRoleAsync(
                        spec.Name, spec.Permissions, spec.Color, spec.Hoist,
                        spec.Mentionable, new RequestOptions { CancelToken = ct }
                    );

                    // add to the local lookup so a second spec pass sees it
                    rolesByName[spec.Name] = role;
                }

                // 4️⃣ sync attributes
                await SyncAttributesAsync( role, spec, ct );

                // 5️⃣ cache ID
                map[spec.Key] = role.Id;
            }

            SaveCache();
        }
        finally {
            gate.Release();
        }
    }
    
    /*async Task AssignDefaultRoleToUnrankedAsync(SocketGuild guild, CancellationToken ct = default) {
        if ( !guild.CurrentUser.GuildPermissions.ManageRoles ) {
            return;
        }

        // Get the role mapping cache for the guild.
        ConcurrentDictionary<ulong, Dictionary<HoboBotRoles, ulong>> cache = HoboBotRolesManager.GetCache();
        if ( !cache.TryGetValue( guild.Id, out Dictionary<HoboBotRoles, ulong>? roleMapping ) ) {
            return;
        }

        // Retrieve the default role.
        if ( !roleMapping.TryGetValue( HoboBotRoles.Default, out ulong defaultRoleId ) ) {
            return;
        }

        IRole? defaultRole = guild.GetRole( defaultRoleId );
        if ( defaultRole is null ) {
            return;
        }

        // Iterate all guild members.
        foreach (var member in guild.Users) {
            ct.ThrowIfCancellationRequested();

            var hasRank = false;
            foreach (IRole role in member.Roles) {
                // If user already has any role from the rank mapping other than default.
                if ( roleMapping.ContainsValue( role.Id ) && role.Id != defaultRole.Id ) {
                    hasRank = true;
                    break;
                }
            }

            // If member has no rank roles, add the default role.
            if ( !hasRank ) {
                await member.AddRoleAsync( defaultRole, new RequestOptions { CancelToken = ct } );
            }
        }
    }*/

    //────────────────────────── internals ─────────────────────────//

    Task OnGuildEventAsync(SocketGuild guild)
        => EnsureRolesAsync( guild, m_cts.Token );

    async Task OnReadyAsync() {
        foreach (var g in m_client.Guilds) {
            await EnsureRolesAsync( g, m_cts.Token );
        }
    }

    static async Task SyncAttributesAsync(IRole role, RoleSpec spec, CancellationToken ct) {
        bool permsMatch = role.Permissions.RawValue == spec.Permissions.RawValue;
        bool colorMatch = !spec.Color.HasValue && role.Color == Color.Default ||
                          spec.Color.HasValue && role.Color == spec.Color.Value;

        if ( permsMatch &&
             colorMatch &&
             role.IsHoisted == spec.Hoist &&
             role.IsMentionable == spec.Mentionable &&
             role.Name == spec.Name ) {
            return;
        }

        await role.ModifyAsync(
            p => {
                p.Name = spec.Name;
                p.Permissions = new Optional<GuildPermissions>( spec.Permissions );
                p.Color = spec.Color.HasValue
                    ? new Optional<Color>( spec.Color.Value )
                    : Optional<Color>.Unspecified;
                p.Hoist = spec.Hoist;
                p.Mentionable = spec.Mentionable;
            },
            new RequestOptions { CancelToken = ct }
        );
    }

    //────────────────────────── simple JSON cache ─────────────────//

    void LoadCache() {
        if ( !File.Exists( m_cachePath ) ) {
            return;
        }

        try {
            string json = File.ReadAllText( m_cachePath );
            ConcurrentDictionary<ulong, Dictionary<HoboBotRoles, ulong>> newCache = JsonSerializer.Deserialize<ConcurrentDictionary<ulong, Dictionary<HoboBotRoles, ulong>>>( json, Json )
                                                                                  ?? new ConcurrentDictionary<ulong, Dictionary<HoboBotRoles, ulong>>();

            ConcurrentDictionary<ulong, Dictionary<HoboBotRoles, ulong>> cache = HoboBotRolesManager.GetCache();
            cache.Clear();
            foreach (KeyValuePair<ulong, Dictionary<HoboBotRoles, ulong>> entry in newCache) {
                cache.TryAdd( entry.Key, entry.Value );
            }
        }
        catch (Exception ex) {
            Console.WriteLine( $"[RoleService] cache load failed: {ex.Message}" );
        }
    }

    void SaveCache() {
        try {
            string json = JsonSerializer.Serialize( HoboBotRolesManager.GetCache(), Json );
            File.WriteAllText( m_cachePath, json );
        }
        catch (Exception ex) {
            Console.WriteLine( $"[RoleService] cache save failed: {ex.Message}" );
        }
    }

    //────────────────────────── IHostedService ────────────────────//

    public Task StartAsync(CancellationToken ct) {
        // Run once for every guild when the client is ready
        m_client.Ready += OnReadyAsync;
        // Run when the bot is invited while it is already running
        m_client.JoinedGuild += g => EnsureRolesAsync( g, m_cts.Token );

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) {
        // Undo subscriptions and persist cache.
        m_client.GuildAvailable -= OnGuildEventAsync;
        m_client.JoinedGuild -= OnGuildEventAsync;
        m_client.Ready -= OnReadyAsync;

        SaveCache();
        m_cts.Cancel();
        return Task.CompletedTask;
    }

    //────────────────────────── IDisposable ───────────────────────//

    public void Dispose() {
        m_cts.Cancel();
        m_cts.Dispose();
        // Just in case StopAsync wasn’t awaited.
        m_client.GuildAvailable -= OnGuildEventAsync;
        m_client.JoinedGuild -= OnGuildEventAsync;
        m_client.Ready -= OnReadyAsync;
    }
}