using System;
using System.Collections.Generic;

namespace ArcTrading.MacroAgent
{
    [Serializable]
    public class EconomySnapshot
    {
        public string capturedAtUtc;
        public float gameTimeSeconds;

        public MarketSummary market = new MarketSummary();
        public ChainStatus chain = new ChainStatus();
        public List<NpcSnapshotEntry> npcs = new List<NpcSnapshotEntry>();
        public List<WorldEventSnapshot> activeWorldEvents = new List<WorldEventSnapshot>();
        public CurrentPolicySnapshot currentPolicy = new CurrentPolicySnapshot();
    }

    [Serializable]
    public class MarketSummary
    {
        public int npcCount;
        public float totalWalletUSDC;
        public float totalVaultUSDC;
        public float totalUSDC;
        public float averageWalletUSDC;
        public float averageVaultUSDC;
        public float walletStdDev;
        public float vaultStdDev;
        public float depositToWithdrawRatio;
        public int recentDepositCount;
        public int recentWithdrawCount;
        public int recentHoldCount;
        public float volatilityIndex;
    }

    [Serializable]
    public class ChainStatus
    {
        public int runningChainActions;
        public int recentChainFailures;
        public float averageCooldownRemaining;
        public int npcsBlockedByCooldown;
    }

    [Serializable]
    public class NpcSnapshotEntry
    {
        public string npcId;
        public string displayName;
        public string archetype;
        public string currentActivity;
        public bool isRunningChainAction;

        public float walletUSDC;
        public float vaultUSDC;
        public float totalUSDC;
        public float livingBudgetUSDC;
        public float reserveBudgetUSDC;
        public float tradingBudgetUSDC;

        public float livingNeedsWeight;
        public float reserveWeight;
        public float tradingWeight;
        public float minTradeUSDC;
        public float maxTradeUSDC;
        public float rebalanceInterval;
        public float chainActionCooldown;

        public int recentDeposits;
        public int recentWithdraws;
        public int recentHolds;
        public int recentFailures;
    }

    [Serializable]
    public class WorldEventSnapshot
    {
        public string type;
        public string displayName;
        public float elapsedSeconds;
    }

    [Serializable]
    public class CurrentPolicySnapshot
    {
        public string lastTargetEvent;
        public string lastReasoning;
        public string lastAppliedUtc;
        public float ageSeconds;
    }
}
