using System.Collections.Concurrent;
namespace TCS.HoboBot.Modules.CasinoGames.Slots;

public class CasinoStats {
    // CASINO STATS -----------------------------------
    public float TotalBetAmount { get; set; }
    public float TotalWinAmount { get; set; }
    public float TotalLostAmount { get; set; }
    
    // BLACKJACK STATS ---------------------------
    public int TotalBlackjackGames { get; set; }
    public int TotalBlackjackWins { get; set; }
    public int TotalBlackjackLosses { get; set; }
    public decimal BlackJackAmountWon { get; set; }
    public decimal BlackJackAmountLost { get; set; }
    
    // SLOTS STATS -----------------------------------
    public int SpinCount { get; set; }
    public int TotalSpinWins { get; set; }
    public int TotalSpinLosses { get; set; }
    public decimal TotalSpinAmountWon { get; set; }
    public decimal TotalSpinAmountLost { get; set; }
    
    // ROULETTE STATS ---------------------------
    public int TotalRouletteGames { get; set; }
    public int TotalRouletteWins { get; set; }
    public int TotalRouletteLosses { get; set; }
    public decimal RouletteAmountWon { get; set; }
    public decimal RouletteAmountLost { get; set; }
    
    // BACCARAT STATS ---------------------------
    public int TotalBaccaratGames { get; set; }
    public int TotalBaccaratWins { get; set; }
    public int TotalBaccaratLosses { get; set; }
    public decimal BaccaratAmountWon { get; set; }
    public decimal BaccaratAmountLost { get; set; }
}

public class DealerInfo {
    // public enum DrugType { Weed, Shrooms, Cocaine, Heroin, Crack, Meth, Lsd, Ecstasy, Dmt }
    // DRUG DEALER STATS ---------------------------
    public int DealerTier { get; set; }
    public decimal TotalCashEarned { get; set; }
    public decimal AmountUntilNextTier { get; set; }
    public int TotalDrugSold { get; set; }
    public int TotalWeedSold { get; set; }
    public int TotalShroomsSold { get; set; }
    public int TotalCocaineSold { get; set; }
    public int TotalHeroinSold { get; set; }
    public int TotalCrackSold { get; set; }
    public int TotalMethSold { get; set; }
    public int TotalLsdSold { get; set; }
    public int TotalEcstasySold { get; set; }
    public int TotalDmtSold { get; set; }
}

public class PlayerInfo {
    public ulong GuildId { get; set; }
    public string UserName { get; set; } = string.Empty;
    
    readonly CasinoStats m_casinoStats = new();
    readonly DealerInfo m_dealerInfo = new DealerInfo();
    

    // WEAPON STATS ---------------------------
    public int HighestWeaponTier { get; set; }
    public string WeaponName { get; set; } = string.Empty;
}