using UnityEngine;

public class BalancedTraderNpc : TradingNpcActor
{
    private const float MinProfitMultiplier = 1.3f;

    protected override void ConfigurePortfolio()
    {
        archetype = TradingNpcArchetype.BalancedTrader;
        portfolioConfig.livingNeedsWeight = 0.30f;
        portfolioConfig.reserveWeight = 0.30f;
        portfolioConfig.tradingWeight = 0.40f;
        portfolioConfig.minTradeUSDC = 0.03f;
        portfolioConfig.maxTradeUSDC = 5.5f;
        portfolioConfig.chainActionCooldown = 8f;
    }

    protected override TradeDecision DecideTrade()
    {
        float mintPrice = portfolioState.avgBuyPriceUSDC;
        float sellPrice = portfolioState.bestSellPriceUSDC;
        // float profitRatio = mintPrice > 0f ? sellPrice / mintPrice : 0f;
        
        WorldEventManager eventManager = WorldEventManager.GetOrCreate();
        float globalRiskMultiplier = eventManager.GetCurrentEventsGlobalRiskMultiplier();
        float eventBonus = eventManager.GetCurrentEventsBonus();
        float expectedAssetValue = sellPrice * globalRiskMultiplier + eventBonus;
        float profitRatio = mintPrice > 0f ? expectedAssetValue / mintPrice : 0f;

        bool hasItem = portfolioState.nftInventoryCount > 0;
        bool canMint = mintPrice > 0f && portfolioState.walletUSDC >= mintPrice;

        if (hasItem && profitRatio >= MinProfitMultiplier)
        {
            return BuildDecision(
                TradeIntent.SellNFT,
                $"Balanced arbitrage exit: selling NFT into favorable buyback price {sellPrice:F4} USDC, ratio {profitRatio:F2}x."
            );
        }

        if (canMint && profitRatio >= MinProfitMultiplier)
        {
            return BuildDecision(
                TradeIntent.BuyNFT,
                $"Balanced arbitrage entry: avg mint cost {mintPrice:F4}, sell price {sellPrice:F4}, ratio {profitRatio:F2}x."
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
