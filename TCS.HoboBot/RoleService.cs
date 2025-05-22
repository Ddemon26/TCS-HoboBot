// RoleService.cs  – .NET 8  •  Discord.Net 3
#nullable enable
using System.Collections.Concurrent;
using System.Text.Json;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
namespace TCS.HoboBot;

//────────────────────────── ENUM & RECORD ──────────────────────────//

public enum DealerRole {
    LowLevelDealer,
    PettyDrugDealer,
    StreetDealer,
    Pimp,
    Kingpin,
    DrugLord,
    Underboss,
    Godfather
}

public readonly record struct RoleSpec(
    DealerRole Key,
    string Name,
    GuildPermissions Permissions,
    Color? Color = null,
    bool Hoist = false,
    bool Mentionable = false);

//────────────────────────── ROLE SERVICE ───────────────────────────//

public sealed class RoleService : IHostedService, IDisposable {
    /*── canonical spec list – tweak to taste ──*/
    static readonly RoleSpec[] Specs = [
        new RoleSpec( DealerRole.LowLevelDealer, "LowLevelDealer", GuildPermissions.None, new Color( 0x3498db ) ),
        new(DealerRole.PettyDrugDealer, "PettyDrugDealer", GuildPermissions.None, new Color( 0x2ecc71 )),
        new(DealerRole.StreetDealer, "StreetDealer", GuildPermissions.None, new Color( 0x95a5a6 )),
        new(DealerRole.Pimp, "Pimp", GuildPermissions.None, new Color( 0xe67e22 )),
        new(DealerRole.Kingpin, "Kingpin", GuildPermissions.None, new Color( 0xe74c3c )),
        new(DealerRole.DrugLord, "DrugLord", GuildPermissions.None, new Color( 0x8e44ad )),
        new(DealerRole.Underboss, "Underboss", GuildPermissions.None, new Color( 0x2c3e50 )),
        new(DealerRole.Godfather, "Godfather", GuildPermissions.None, new Color( 0x34495e )),
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

            Dictionary<DealerRole, ulong> map = HoboRolesHandler.Cache.GetOrAdd( guild.Id, _ => new Dictionary<DealerRole, ulong>() );

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

    //────────────────────────── internals ─────────────────────────//

    Task OnGuildEventAsync(SocketGuild guild)
        => EnsureRolesAsync( guild, m_cts.Token );

    async Task OnReadyAsync() {
        foreach (var g in m_client.Guilds)
            await EnsureRolesAsync( g, m_cts.Token );
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
            HoboRolesHandler.Cache = JsonSerializer.Deserialize<
                                         ConcurrentDictionary<ulong, Dictionary<DealerRole, ulong>>>( json, Json )!
                                     ?? new ConcurrentDictionary<ulong, Dictionary<DealerRole, ulong>>();
        }
        catch (Exception ex) {
            Console.WriteLine( $"[RoleService] cache load failed: {ex.Message}" );
        }
    }

    void SaveCache() {
        try {
            string json = JsonSerializer.Serialize( HoboRolesHandler.Cache, Json );
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