using System;
using UnityEngine;

public enum WorldEventType
{
    EnergyShortage,
    Inflation,
    MarketBoom,
    LiquidityCrunch
}

[Serializable]
public class WorldEventConfigModifier
{
    [Header("Budget Weight Multipliers")]
    public float livingNeedsWeightMultiplier = 1f;
    public float reserveWeightMultiplier = 1f;
    public float tradingWeightMultiplier = 1f;

    [Header("Threshold Multipliers")]
    public float minimumLivingBudgetMultiplier = 1f;
    public float minimumReserveBudgetMultiplier = 1f;

    [Header("Timing Multipliers")]
    public float rebalanceIntervalMultiplier = 1f;
    public float chainActionCooldownMultiplier = 1f;

    [Header("Trade Size Multipliers")]
    public float minTradeMultiplier = 1f;
    public float maxTradeMultiplier = 1f;

    [Header("Trader Expectation Adjustments")]
    public float globalRiskMultiplier = 1f;
    public float eventBonus = 0f;

    public void ApplyTo(NpcPortfolioConfig config)
    {
        if (config == null)
        {
            return;
        }

        config.livingNeedsWeight = Mathf.Max(0f, config.livingNeedsWeight * livingNeedsWeightMultiplier);
        config.reserveWeight = Mathf.Max(0f, config.reserveWeight * reserveWeightMultiplier);
        config.tradingWeight = Mathf.Max(0f, config.tradingWeight * tradingWeightMultiplier);
        config.minimumLivingBudgetUSDC = Mathf.Max(0f, config.minimumLivingBudgetUSDC * minimumLivingBudgetMultiplier);
        config.minimumReserveBudgetUSDC = Mathf.Max(0f, config.minimumReserveBudgetUSDC * minimumReserveBudgetMultiplier);
        config.rebalanceInterval = Mathf.Max(0.1f, config.rebalanceInterval * rebalanceIntervalMultiplier);
        config.chainActionCooldown = Mathf.Max(0.1f, config.chainActionCooldown * chainActionCooldownMultiplier);
        config.minTradeUSDC = Mathf.Max(0f, config.minTradeUSDC * minTradeMultiplier);
        config.maxTradeUSDC = Mathf.Max(config.minTradeUSDC, config.maxTradeUSDC * maxTradeMultiplier);
    }
}

[Serializable]
public class WorldEventDefinition
{
    public WorldEventType type;
    public string displayName;
    [TextArea] public string description;
    public WorldEventConfigModifier modifier = new WorldEventConfigModifier();
}

[Serializable]
public class ActiveWorldEvent
{
    public WorldEventType type;
    public string displayName;
    public string description;
    public float startedGameTime;
}
