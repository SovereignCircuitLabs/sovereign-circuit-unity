using UnityEngine;

public class AggressiveTraderNpc : TradingNpcActor
{
    private const float MintPriceUSDC = 0.1f;
    private const float MinProfitMultiplier = 1.1f;
    
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
        float sellPrice = portfolioState.bestSellPriceUSDC;
        float profitRatio = sellPrice / MintPriceUSDC;

        bool hasItem = portfolioState.nftInventoryCount > 0;
        bool canMint = portfolioState.walletUSDC >= MintPriceUSDC;

        if (hasItem && profitRatio >= MinProfitMultiplier)
        {
            return BuildDecision(
                TradeIntent.Withdraw,
                1.2f,
                $"Aggressive fast exit: capturing spread at {sellPrice:F4} USDC, ratio {profitRatio:F2}x."
            );
        }

        if (canMint && profitRatio >= MinProfitMultiplier)
        {
            return BuildDecision(
                TradeIntent.Deposit,
                1.5f,
                $"Aggressive arbitrage entry: minting against profitable buyback spread, ratio {profitRatio:F2}x."
            );
        }

        if (canMint && Random.value < 0.10f)
        {
            return BuildDecision(
                TradeIntent.Deposit,
                0.6f,
                "Aggressive exploration: small speculative mint despite weak spread."
            );
        }

        return TradeDecision.Hold(
            $"Aggressive hold: no profitable spread, ratio {profitRatio:F2}x."
        );
    }
}
