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
        public float totalGatewayUSDC;
        public float totalUSDC;
        public float averageWalletUSDC;
        public float averageVaultUSDC;
        public float averageGatewayUSDC;
        public float walletStdDev;
        public float vaultStdDev;
        public float gatewayStdDev;
        public float depositToWithdrawRatio;
        public int recentDepositCount;
        public int recentWithdrawCount;
        public int recentHoldCount;
        public float volatilityIndex;

        // ----- Arbitrage / NFT-loot signals -----
        /// <summary>Sum of NFT items held across all NPCs (vault inventory at their TBAs).</summary>
        public int totalNftInventory;
        public float averageNftInventory;
        /// <summary>Number of NPCs currently holding ≥1 NFT (i.e. able to call sellItem).</summary>
        public int npcsWithInventory;
        /// <summary>Highest buyback price observed across NPCs' last refresh — same global signal queried at slightly different ticks.</summary>
        public float marketBestSellPriceUSDC;
        /// <summary>marketBestSellPriceUSDC ÷ MINT_PRICE. >1.0 means buying then immediately selling is profitable on at least one NFT type.</summary>
        public float marketArbitrageRatio;
        /// <summary>Count of NPCs whose own bestSellPriceUSDC > MINT_PRICE — i.e. the per-NPC view of "arbitrage exists".</summary>
        public int profitableNpcCount;
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
        public float gatewayUSDC;
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

        // ----- Arbitrage / NFT-loot signals -----
        /// <summary>How many GameItems NFTs this NPC currently holds (vault inventory).</summary>
        public int nftInventoryCount;
        /// <summary>Best buyback price observed on this NPC's last refresh — drives the arbitrage decision in DecideTrade.</summary>
        public float bestSellPriceUSDC;
        /// <summary>bestSellPriceUSDC ÷ MINT_PRICE. >1.0 = mint-then-sell is profitable per this NPC's last read.</summary>
        public float arbitrageRatio;
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
