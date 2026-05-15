using UnityEngine;

public class ConservativeTraderNpc : TradingNpcActor
{
    protected override void ConfigurePortfolio()
    {
        archetype = TradingNpcArchetype.ConservativeSaver;
        portfolioConfig.livingNeedsWeight = 0.45f;
        portfolioConfig.reserveWeight = 0.40f;
        portfolioConfig.tradingWeight = 0.15f;
        portfolioConfig.minTradeUSDC = 0.003f;
        portfolioConfig.maxTradeUSDC = 0.015f;
        portfolioConfig.chainActionCooldown = 15f;
    }

    protected override TradeDecision DecideTrade()
    {
        float roll = Random.value;
        if (portfolioState.vaultUSDC > portfolioState.tradingBudgetUSDC * 1.25f)
        {
            return BuildDecision(TradeIntent.Withdraw, 0.7f, "vault exposure above conservative target");
        }

        if (roll < 0.25f)
        {
            return BuildDecision(TradeIntent.Deposit, 0.5f, "small low-risk allocation");
        }

        return TradeDecision.Hold("waiting for safer entry");
    }
}
