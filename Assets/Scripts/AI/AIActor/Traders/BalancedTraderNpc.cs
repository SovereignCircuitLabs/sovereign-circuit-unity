using UnityEngine;

public class BalancedTraderNpc : TradingNpcActor
{
    protected override void ConfigurePortfolio()
    {
        archetype = TradingNpcArchetype.BalancedTrader;
        portfolioConfig.livingNeedsWeight = 0.30f;
        portfolioConfig.reserveWeight = 0.30f;
        portfolioConfig.tradingWeight = 0.40f;
        portfolioConfig.minTradeUSDC = 0.005f;
        portfolioConfig.maxTradeUSDC = 0.04f;
        portfolioConfig.chainActionCooldown = 8f;
    }

    protected override TradeDecision DecideTrade()
    {
        float roll = Random.value;
        if (roll < 0.45f)
        {
            return BuildDecision(TradeIntent.Deposit, 1f, "balanced buy signal");
        }

        if (roll < 0.70f)
        {
            return BuildDecision(TradeIntent.Withdraw, 0.8f, "taking partial profit");
        }

        return TradeDecision.Hold("portfolio is close to target");
    }
}
