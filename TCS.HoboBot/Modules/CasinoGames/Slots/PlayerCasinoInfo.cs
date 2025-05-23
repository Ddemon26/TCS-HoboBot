using System.Collections.Concurrent;
namespace TCS.HoboBot.Modules.CasinoGames.Slots;

public class PlayerCasinoInfo {
    public static ConcurrentDictionary<ulong, Dictionary<ulong, PlayerCasinoInfo>> Cache = new();
    //public static readonly ConcurrentDictionary<ulong, PlayerCasinoInfo> PlayerCasinoInfo = new();
    public ulong GuildId { get; set; }
    public string UserName { get; set; } = string.Empty;

    public float TotalBetAmount { get; set; }
    public float TotalWinAmount { get; set; }
    public float TotalLostAmount { get; set; }

    public int SpinCount { get; set; }
    public int TotalSpinWins { get; set; }
    public int TotalSpinLosses { get; set; }
    public int HighestSpinWinStreak { get; set; }
}