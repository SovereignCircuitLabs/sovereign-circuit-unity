using UnityEngine;

public class ConservativeTraderNpc : TradingNpcActor
{
    private const float MinProfitMultiplier = 1.6f;

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
        float mintPrice = portfolioState.avgBuyPriceUSDC;
        float sellPrice = portfolioState.bestSellPriceUSDC;
        float profitRatio = mintPrice > 0f ? sellPrice / mintPrice : 0f;

        bool hasItem = portfolioState.nftInventoryCount > 0;
        bool canMint = mintPrice > 0f && portfolioState.walletUSDC >= mintPrice;

        if (hasItem && profitRatio >= MinProfitMultiplier)
        {
            return BuildDecision(
                TradeIntent.SellNFT,
                $"Conservative arbitrage exit: sell price {sellPrice:F4} USDC, ratio {profitRatio:F2}x."
            );
        }

        if (canMint && profitRatio >= MinProfitMultiplier)
        {
            return BuildDecision(
                TradeIntent.BuyNFT,
                $"Conservative arbitrage entry: buy at avg {mintPrice:F4}, expected sell {sellPrice:F4}, ratio {profitRatio:F2}x."
            );
        }

        if (portfolioState.vaultUSDC > portfolioState.tradingBudgetUSDC * 1.25f)
        {
            return BuildDecision(
                TradeIntent.SellNFT,
                "Conservative risk control: vault exposure exceeds target."
            );
        }

        return TradeDecision.Hold(
            $"Conservative hold: spread too small, sell price {sellPrice:F4} USDC, ratio {profitRatio:F2}x."
        );
    }
}
