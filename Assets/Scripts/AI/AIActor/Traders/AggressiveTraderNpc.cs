using UnityEngine;

public class AggressiveTraderNpc : TradingNpcActor
{
    private const float MinProfitMultiplier = 1.1f;

    protected override void ConfigurePortfolio()
    {
        archetype = TradingNpcArchetype.AggressiveSpeculator;
        portfolioConfig.livingNeedsWeight = 0.20f;
        portfolioConfig.reserveWeight = 0.15f;
        portfolioConfig.tradingWeight = 0.65f;
        portfolioConfig.minTradeUSDC = 0.05f;
        portfolioConfig.maxTradeUSDC = 6.5f;
        portfolioConfig.chainActionCooldown = 5f;
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
                $"Aggressive fast exit: capturing spread at {sellPrice:F4} USDC, ratio {profitRatio:F2}x."
            );
        }

        if (canMint && profitRatio >= MinProfitMultiplier)
        {
            return BuildDecision(
                TradeIntent.BuyNFT,
                $"Aggressive arbitrage entry: avg mint {mintPrice:F4} vs best sell {sellPrice:F4}, ratio {profitRatio:F2}x."
            );
        }

        if (canMint && Random.value < 0.10f)
        {
            return BuildDecision(
                TradeIntent.BuyNFT,
                $"Aggressive exploration: small speculative mint at avg {mintPrice:F4} despite weak spread."
            );
        }

        return TradeDecision.Hold(
            $"Aggressive hold: no profitable spread, ratio {profitRatio:F2}x."
        );
    }
}
