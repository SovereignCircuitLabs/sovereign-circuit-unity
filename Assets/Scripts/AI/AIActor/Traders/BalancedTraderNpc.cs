using UnityEngine;

public class BalancedTraderNpc : TradingNpcActor
{
    private const float MintPriceUSDC = 0.1f;
    private const float MinProfitMultiplier = 1.3f;
    
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
        float sellPrice = portfolioState.bestSellPriceUSDC;
        float profitRatio = sellPrice / MintPriceUSDC;

        bool hasItem = portfolioState.nftInventoryCount > 0;
        bool canMint = portfolioState.walletUSDC >= MintPriceUSDC;

        if (hasItem && profitRatio >= MinProfitMultiplier)
        {
            return BuildDecision(
                TradeIntent.Withdraw,
                0.9f,
                $"Balanced arbitrage exit: selling NFT into favorable buyback price {sellPrice:F4} USDC."
            );
        }

        if (canMint && profitRatio >= MinProfitMultiplier)
        {
            return BuildDecision(
                TradeIntent.Deposit,
                1.0f,
                $"Balanced arbitrage entry: mint cost {MintPriceUSDC:F4}, sell price {sellPrice:F4}, ratio {profitRatio:F2}x."
            );
        }

        if (hasItem && profitRatio < 1.0f)
        {
            return TradeDecision.Hold(
                $"Balanced hold inventory: sell price below mint cost, ratio {profitRatio:F2}x."
            );
        }

        return TradeDecision.Hold(
            $"Balanced hold: market close to equilibrium, ratio {profitRatio:F2}x."
        );
    }
}
