using System;
using UnityEngine;
using UnityEngine.Serialization;

public enum TradingNpcArchetype
{
    ConservativeSaver,
    BalancedTrader,
    AggressiveSpeculator
}

public enum TradeIntent
{
    Hold,
    Deposit,
    Withdraw
}

[Serializable]
public class NpcPortfolioConfig
{
    [Header("Budgets")]
    [Range(0f, 1f)] public float livingNeedsWeight = 0.35f;
    [Range(0f, 1f)] public float reserveWeight = 0.35f;
    [Range(0f, 1f)] public float tradingWeight = 0.30f;

    [Header("Thresholds")]
    public float minimumLivingBudgetUSDC = 0.05f;
    public float minimumReserveBudgetUSDC = 0.05f;
    public float rebalanceInterval = 20f;
    public float chainActionCooldown = 8f;

    [Header("Trade Size")]
    public float minTradeUSDC = 0.005f;
    public float maxTradeUSDC = 0.05f;
}

[Serializable]
public class NpcPortfolioState
{
    public float walletUSDC;
    public float vaultUSDC;
    public float livingBudgetUSDC;
    public float reserveBudgetUSDC;
    public float tradingBudgetUSDC;

    public float TotalUSDC
    {
        get { return walletUSDC + vaultUSDC; }
    }
}

public struct TradeDecision
{
    public TradeIntent intent;
    public float amountUSDC;
    public string reason;

    public static TradeDecision Hold(string reason)
    {
        return new TradeDecision { intent = TradeIntent.Hold, amountUSDC = 0f, reason = reason };
    }
}
