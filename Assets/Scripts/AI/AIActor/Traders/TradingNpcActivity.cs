using System;
using System.Collections.Generic;
using UnityEngine;

public enum TradingNpcActivityType
{
    Initialized,
    MoveToTarget,
    BuyLivingSupplies,
    Rebalance,
    TradeHold,
    TradeDeposit,
    TradeDepositNanopayment,
    TradeWithdraw,
    WorldEventConfigChanged,
    ChainActionFailed
}

[Serializable]
public class TradingNpcActivityRecord
{
    public TradingNpcActivityType type;
    public string title;
    public string details;
    public string txHash;
    public float amountUSDC;
    public float gameTime;
    public string utcTime;
    public Vector3 worldPosition;
}

[Serializable]
public class NpcFieldDisplayInfo
{
    public string group;
    public string fieldName;
    public string displayName;
    public string value;
    public string comment;
}

[Serializable]
public class TradingNpcSnapshot
{
    public string npcId;
    public string displayName;
    public TradingNpcArchetype archetype;
    public string walletAddress;
    public string tbaAddress;
    public Vector3 worldPosition;
    public string currentActivity;
    public bool isRunningChainAction;
    public NpcPortfolioConfig portfolioConfig;
    public NpcPortfolioState portfolioState;
    public List<NpcFieldDisplayInfo> fields = new List<NpcFieldDisplayInfo>();
    public List<TradingNpcActivityRecord> recentActivities = new List<TradingNpcActivityRecord>();
}
