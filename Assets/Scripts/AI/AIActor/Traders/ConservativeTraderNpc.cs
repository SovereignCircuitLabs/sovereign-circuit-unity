using UnityEngine;

public class ConservativeTraderNpc : TradingNpcActor
{
    private const float MintPriceUSDC = 0.1f;
    private const float MinProfitMultiplier = 2.0f;
    
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
        float sellPrice = portfolioState.bestSellPriceUSDC;
        float profitRatio = sellPrice / MintPriceUSDC;

        bool hasItem = portfolioState.nftInventoryCount > 0;
        bool canMint = portfolioState.walletUSDC >= MintPriceUSDC;

        if (hasItem && profitRatio >= MinProfitMultiplier)
        {
            return BuildDecision(
                TradeIntent.Withdraw,
                0.8f,
                $"Conservative arbitrage exit: sell price {sellPrice:F4} USDC, ratio {profitRatio:F2}x."
            );
        }

        if (canMint && profitRatio >= MinProfitMultiplier)
        {
            return BuildDecision(
                TradeIntent.Deposit,
                0.5f,
                $"Conservative arbitrage entry: buy at {MintPriceUSDC:F4}, expected sell {sellPrice:F4}."
            );
        }

        if (portfolioState.vaultUSDC > portfolioState.tradingBudgetUSDC * 1.25f)
        {
            return BuildDecision(
                TradeIntent.Withdraw,
                0.6f,
                "Conservative risk control: vault exposure exceeds target."
            );
        }

        return TradeDecision.Hold(
            $"Conservative hold: spread too small, sell price {sellPrice:F4} USDC."
        );
    }
}
