using System;

namespace ArcTrading.MacroAgent
{
    public enum MacroTargetEvent
    {
        Normal,
        EnergyShortage,
        Inflation,
        MarketBoom,
        LiquidityCrunch
    }

    [Serializable]
    public class MacroPolicyModifiers
    {
        public float livingNeedsWeightMultiplier = 1f;
        public float reserveWeightMultiplier = 1f;
        public float tradingWeightMultiplier = 1f;
        public float minimumLivingBudgetMultiplier = 1f;
        public float minimumReserveBudgetMultiplier = 1f;
        public float rebalanceIntervalMultiplier = 1f;
        public float chainActionCooldownMultiplier = 1f;
        public float minTradeMultiplier = 1f;
        public float maxTradeMultiplier = 1f;

        public WorldEventConfigModifier ToWorldEventModifier()
        {
            return new WorldEventConfigModifier
            {
                livingNeedsWeightMultiplier = livingNeedsWeightMultiplier,
                reserveWeightMultiplier = reserveWeightMultiplier,
                tradingWeightMultiplier = tradingWeightMultiplier,
                minimumLivingBudgetMultiplier = minimumLivingBudgetMultiplier,
                minimumReserveBudgetMultiplier = minimumReserveBudgetMultiplier,
                rebalanceIntervalMultiplier = rebalanceIntervalMultiplier,
                chainActionCooldownMultiplier = chainActionCooldownMultiplier,
                minTradeMultiplier = minTradeMultiplier,
                maxTradeMultiplier = maxTradeMultiplier
            };
        }
    }

    [Serializable]
    public class MacroPolicy
    {
        public string reasoning;
        public MacroTargetEvent target_event = MacroTargetEvent.Normal;
        public MacroPolicyModifiers modifiers = new MacroPolicyModifiers();
        public string appliedUtc;
        public string rawJson;
    }

    [Serializable]
    public class MacroPolicyValidationResult
    {
        public bool isValid;
        public string error;
        public MacroPolicy policy;

        public static MacroPolicyValidationResult Fail(string message)
        {
            return new MacroPolicyValidationResult { isValid = false, error = message };
        }

        public static MacroPolicyValidationResult Ok(MacroPolicy policy)
        {
            return new MacroPolicyValidationResult { isValid = true, policy = policy };
        }
    }
}
