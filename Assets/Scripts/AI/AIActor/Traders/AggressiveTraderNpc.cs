using UnityEngine;

public class AggressiveTraderNpc : TradingNpcActor
{
    protected override void ConfigurePortfolio()
    {
        archetype = TradingNpcArchetype.AggressiveSpeculator;
        portfolioConfig.livingNeedsWeight = 0.20f;
        portfolioConfig.reserveWeight = 0.15f;
        portfolioConfig.tradingWeight = 0.65f;
        portfolioConfig.minTradeUSDC = 0.01f;
        portfolioConfig.maxTradeUSDC = 0.08f;
        portfolioConfig.chainActionCooldown = 5f;
    }

    protected override TradeDecision DecideTrade()
    {
        float roll = Random.value;
        if (roll < 0.65f)
        {
            return BuildDecision(TradeIntent.Deposit, 1.5f, "aggressive momentum entry");
        }

        if (roll < 0.85f)
        {
            return BuildDecision(TradeIntent.Withdraw, 1.1f, "fast risk reduction");
        }

        return TradeDecision.Hold("waiting for volatility");
    }
}
