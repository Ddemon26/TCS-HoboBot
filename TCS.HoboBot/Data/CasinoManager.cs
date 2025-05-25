using System.Collections.Concurrent;
using Discord.WebSocket;
using Newtonsoft.Json;

namespace TCS.HoboBot.Modules.CasinoGames.Slots;

public enum JackpotType { Mega, Minor, Mini }

public static class CasinoManager {
    /* ───────────────────────────  CONFIG  ─────────────────────────── */

    /// <summary>All tunables for a single jackpot tier.</summary>
    record TierSetting(
        double BaseProb, // hit chance for a 1-credit bet
        double ScalingBet, // bet where chance stops increasing
        double MaxMultiplier, // chance at ScalingBet = BaseProb × MaxMultiplier
        float ResetValue, // meter value after a hit
        double ContributionShare // cut of each wager that feeds this meter
    );

    static readonly Dictionary<JackpotType, TierSetting> Settings = new() {
        [JackpotType.Mega] = new TierSetting(1.0 / 100_000, 10_000, 10, 100_000f, 0.50),
        [JackpotType.Minor] = new TierSetting(1.0 / 10_000, 10_000, 10, 25_000f, 0.30),
        [JackpotType.Mini] = new TierSetting(1.0 / 5_000, 10_000, 10, 10_000f, 0.20),
    };

    const double CUT = 0.01;   // 5 %

    /* ───────────────────────  DATA STRUCTURES  ────────────────────── */

    public class JackPots {
        public ulong GuildId { get; set; }
        public float SlotsMegaJackpot { get; set; }
        public float SlotProgressiveJackpot { get; set; }
        public float SlotMiniJackpot { get; set; }

        public override string ToString() =>
            $"**Current Jackpots**\n" +
            $" Mega: {SlotsMegaJackpot:N0}\n" +
            $" Minor: {SlotProgressiveJackpot:N0}\n" +
            $" Mini: {SlotMiniJackpot:N0}";
    }

    static readonly Random Rng = new();
    public static readonly ConcurrentDictionary<ulong, JackPots> JackPotsCache = new();

    /* ──────────────────────  CONTRIBUTION LOGIC  ──────────────────── */

    public static void AddToSlotsJackpots(ulong guildId, float wager) {
        double contrib = wager * CUT; // total going to progressive pool

        if ( !JackPotsCache.TryGetValue( guildId, out var jp ) ) {
            jp = JackPotsCache[guildId] = new JackPots {
                GuildId = guildId,
                SlotsMegaJackpot = Settings[JackpotType.Mega].ResetValue,
                SlotProgressiveJackpot = Settings[JackpotType.Minor].ResetValue,
                SlotMiniJackpot = Settings[JackpotType.Mini].ResetValue,
            };
        }

        jp.SlotsMegaJackpot += (float)(contrib * Settings[JackpotType.Mega].ContributionShare);
        jp.SlotProgressiveJackpot += (float)(contrib * Settings[JackpotType.Minor].ContributionShare);
        jp.SlotMiniJackpot += (float)(contrib * Settings[JackpotType.Mini].ContributionShare);
    }

    /* ────────────────────────  HIT DETERMINER  ─────────────────────── */

    public static bool GetJackpot(ulong guildId, float bet, out JackpotType type, out float amount) {
        amount = 0f;
        type = default;

        if ( !JackPotsCache.TryGetValue( guildId, out var jp ) || bet <= 0 )
            return false;

        // Priority order: Mega → Progressive → Mini
        foreach (var tier in new[] { JackpotType.Mega, JackpotType.Minor, JackpotType.Mini }) {
            var s = Settings[tier];

            // Linear bet-weighted odds capped at MaxMultiplier
            double multiplier = 1 + (s.MaxMultiplier - 1) * Math.Min( bet / s.ScalingBet, 1 );
            double p = s.BaseProb * multiplier;

            if ( Rng.NextDouble() < p ) {
                // -- jackpot hit! -------------------------------------
                type = tier;
                switch (tier) {
                    case JackpotType.Mega:
                        amount = jp.SlotsMegaJackpot;
                        jp.SlotsMegaJackpot = s.ResetValue;
                        break;
                    case JackpotType.Minor:
                        amount = jp.SlotProgressiveJackpot;
                        jp.SlotProgressiveJackpot = s.ResetValue;
                        break;
                    case JackpotType.Mini:
                        amount = jp.SlotMiniJackpot;
                        jp.SlotMiniJackpot = s.ResetValue;
                        break;
                }

                return true;
            }
        }

        return false;
    }

    /* ─────────────────────––  SAVE / LOAD (unchanged)  ───────────────────── */

    const string FILE_PATH = "jackpots.json";
    static string GetFilePath(ulong gid) => Path.Combine( "Data", gid.ToString(), FILE_PATH );

    public static async Task SaveAsync() {
        foreach ((ulong gid, var jp) in JackPotsCache) {
            string dir = Path.Combine( "Data", gid.ToString() );
            Directory.CreateDirectory( dir );

            string path = GetFilePath( gid );
            string json = Serialize( jp );
            await File.WriteAllTextAsync( path, json );
        }
        
        // clear the cache
        JackPotsCache.Clear();
    }

    public static async Task LoadAsync(IReadOnlyCollection<SocketGuild> guilds) {
        // clear the cache
        JackPotsCache.Clear();
        foreach (var g in guilds) {
            string path = GetFilePath( g.Id );
            if ( !File.Exists( path ) ) continue;

            string json = await File.ReadAllTextAsync( path );
            if ( Deserialize<JackPots>( json ) is { } loaded )
                JackPotsCache[g.Id] = loaded;
        }
    }

    static readonly JsonSerializerSettings WriteSettings = new() { Formatting = Formatting.Indented };
    static readonly JsonSerializerSettings ReadSettings = new() {
        MissingMemberHandling = MissingMemberHandling.Ignore,
        FloatParseHandling = FloatParseHandling.Double,
    };

    static string Serialize<T>(T value) => JsonConvert.SerializeObject( value, WriteSettings );
    static T? Deserialize<T>(string json) => JsonConvert.DeserializeObject<T>( json, ReadSettings );
}