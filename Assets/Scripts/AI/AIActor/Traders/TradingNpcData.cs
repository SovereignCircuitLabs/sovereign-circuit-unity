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

    public static NpcPortfolioConfig CopyOf(NpcPortfolioConfig source)
    {
        NpcPortfolioConfig copy = new NpcPortfolioConfig();
        if (source == null)
        {
            return copy;
        }

        copy.livingNeedsWeight = source.livingNeedsWeight;
        copy.reserveWeight = source.reserveWeight;
        copy.tradingWeight = source.tradingWeight;
        copy.minimumLivingBudgetUSDC = source.minimumLivingBudgetUSDC;
        copy.minimumReserveBudgetUSDC = source.minimumReserveBudgetUSDC;
        copy.rebalanceInterval = source.rebalanceInterval;
        copy.chainActionCooldown = source.chainActionCooldown;
        copy.minTradeUSDC = source.minTradeUSDC;
        copy.maxTradeUSDC = source.maxTradeUSDC;
        return copy;
    }

    public void CopyFrom(NpcPortfolioConfig source)
    {
        if (source == null)
        {
            return;
        }

        livingNeedsWeight = source.livingNeedsWeight;
        reserveWeight = source.reserveWeight;
        tradingWeight = source.tradingWeight;
        minimumLivingBudgetUSDC = source.minimumLivingBudgetUSDC;
        minimumReserveBudgetUSDC = source.minimumReserveBudgetUSDC;
        rebalanceInterval = source.rebalanceInterval;
        chainActionCooldown = source.chainActionCooldown;
        minTradeUSDC = source.minTradeUSDC;
        maxTradeUSDC = source.maxTradeUSDC;
    }
}

[Serializable]
public class NpcPortfolioState
{
    /// <summary>NPC 钱包中可直接使用的 USDC 余额，用于支付生活预算或执行入金操作，也可以存入Vault（即智能合约账户）</summary>
    public float walletUSDC;

    /// <summary>NPC 已存入 Vault 的 USDC 余额，代表资金正在参与 Vault 策略、投资以及收益逻辑，且不在钱包内，只能通过 withdraw 取出</summary>
    public float vaultUSDC;

    /// <summary>预留给生活需求的 USDC 预算，低于阈值时 NPC 会优先从钱包补充</summary>
    public float livingBudgetUSDC;

    /// <summary>安全储备预算，用来限制交易行为，避免把钱包资金全部投入 Vault</summary>
    public float reserveBudgetUSDC;

    /// <summary>可用于交易或策略投入的 USDC 预算，决定 NPC 是否有足够资金执行交易</summary>
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
