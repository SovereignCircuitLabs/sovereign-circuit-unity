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
    BuyNFT,
    SellNFT
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
    public float minTradeUSDC = 0.05f;
    public float maxTradeUSDC = 5f;

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
    /// <summary>NPC 钱包中的 USDC 余额（包含 payment wallet 和 TBA）</summary>
    public float walletUSDC;

    /// <summary>NPC 买到的 NFT 的价值</summary>
    public float vaultUSDC;

    /// <summary>NPC 在 Circle Gateway Wallet 中可用的 USDC 余额，用于 x402 nanopayment 即时结算</summary>
    public float gatewayUSDC;

    /// <summary>预留给生活需求的 USDC 预算，低于阈值时 NPC 会优先从钱包补充</summary>
    public float livingBudgetUSDC;

    /// <summary>安全储备预算，用来限制交易行为，避免把钱包资金全部投入 Vault</summary>
    public float reserveBudgetUSDC;

    /// <summary>可用于交易或策略投入的 USDC 预算，决定 NPC 是否有足够资金执行交易</summary>
    public float tradingBudgetUSDC;

    /// <summary>NPC 在 GameItems 上持有的全部 NFT 总件数（5 种类型求和），用于判断"是否有库存可卖"。</summary>
    public int nftInventoryCount;

    /// <summary>GamePayment 当前 5 种 NFT 回收价里的最高值（市场信号）。NPC 用它对比 MintPrice 决定是否套利。</summary>
    public float bestSellPriceUSDC;

    /// <summary>GamePayment 当前 5 种 NFT buyPrice 的平均值（bonding curve 动态）。代表 on-chain mintRandom 的期望成本，
    /// 用作 DecideTrade 套利判断的动态基准 —— 取代旧版本里硬编码的 0.10 USDC MintPriceUSDC 常量。</summary>
    public float avgBuyPriceUSDC;

    public float TotalUSDC
    {
        get { return walletUSDC + vaultUSDC + gatewayUSDC; }
    }
}

public struct TradeDecision
{
    public TradeIntent intent;
    public string reason;

    public static TradeDecision Hold(string reason)
    {
        return new TradeDecision { intent = TradeIntent.Hold, reason = reason };
    }
}
