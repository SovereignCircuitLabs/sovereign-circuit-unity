using System.Collections.Generic;
using UnityEngine;

namespace ArcTrading.MacroAgent
{
    /// <summary>
    /// Translates a validated macro policy into world-event state.
    /// The applier NEVER touches NPC private keys, contract calls, or asset balances directly —
    /// it only flips event activation and rewrites the multipliers consumed by the deterministic
    /// C# logic in TradingNpcActor/WorldEventManager.
    /// </summary>
    public class MacroPolicyApplier
    {
        private readonly Dictionary<WorldEventType, WorldEventConfigModifier> baselineModifiers
            = new Dictionary<WorldEventType, WorldEventConfigModifier>();

        private bool baselineCaptured;

        public MacroPolicy LastApplied { get; private set; }

        public void Apply(MacroPolicy policy)
        {
            if (policy == null)
            {
                return;
            }

            WorldEventManager manager = WorldEventManager.GetOrCreate();
            if (manager == null)
            {
                Debug.LogWarning("[MacroAgent] WorldEventManager unavailable; policy not applied.");
                return;
            }

            CaptureBaselineIfNeeded(manager);

            if (policy.target_event == MacroTargetEvent.Normal)
            {
                RestoreBaseline(manager);
                manager.ClearEvents();
                LastApplied = policy;
                Debug.Log("[MacroAgent] Applied policy → Normal (events cleared).");
                return;
            }

            WorldEventType target = ToWorldEventType(policy.target_event);

            // Reset every event modifier to the captured baseline, then overwrite the target.
            RestoreBaseline(manager);

            WorldEventDefinition def = FindDefinition(manager, target);
            if (def == null)
            {
                Debug.LogWarning("[MacroAgent] World event definition not found: " + target);
                return;
            }

            ApplyModifierValues(def.modifier, policy.modifiers);

            // Deactivate every other event so target_event is the sole active macro state.
            IReadOnlyList<WorldEventDefinition> defs = manager.EventDefinitions;
            for (int i = 0; i < defs.Count; i++)
            {
                if (defs[i] == null || defs[i].type == target)
                {
                    continue;
                }

                if (manager.IsEventActive(defs[i].type))
                {
                    manager.DeactivateEvent(defs[i].type);
                }
            }

            // Activating fires ActiveEventsChanged → NPCs reapply portfolio config from base + modifier.
            if (manager.IsEventActive(target))
            {
                // Force a refresh: deactivate then reactivate so subscribers see the new modifier values.
                manager.DeactivateEvent(target);
            }

            manager.ActivateEvent(target);
            LastApplied = policy;
            Debug.Log($"[MacroAgent] Applied policy → {target}: {policy.reasoning}");
        }

        private static WorldEventType ToWorldEventType(MacroTargetEvent ev)
        {
            switch (ev)
            {
                case MacroTargetEvent.EnergyShortage: return WorldEventType.EnergyShortage;
                case MacroTargetEvent.Inflation: return WorldEventType.Inflation;
                case MacroTargetEvent.MarketBoom: return WorldEventType.MarketBoom;
                case MacroTargetEvent.LiquidityCrunch: return WorldEventType.LiquidityCrunch;
                default: return WorldEventType.EnergyShortage;
            }
        }

        private void CaptureBaselineIfNeeded(WorldEventManager manager)
        {
            if (baselineCaptured)
            {
                return;
            }

            IReadOnlyList<WorldEventDefinition> defs = manager.EventDefinitions;
            for (int i = 0; i < defs.Count; i++)
            {
                WorldEventDefinition def = defs[i];
                if (def == null || def.modifier == null)
                {
                    continue;
                }

                baselineModifiers[def.type] = CopyModifier(def.modifier);
            }

            baselineCaptured = true;
        }

        private void RestoreBaseline(WorldEventManager manager)
        {
            if (!baselineCaptured)
            {
                return;
            }

            IReadOnlyList<WorldEventDefinition> defs = manager.EventDefinitions;
            for (int i = 0; i < defs.Count; i++)
            {
                WorldEventDefinition def = defs[i];
                if (def == null || def.modifier == null)
                {
                    continue;
                }

                if (baselineModifiers.TryGetValue(def.type, out WorldEventConfigModifier baseline))
                {
                    CopyModifierInto(baseline, def.modifier);
                }
            }
        }

        private static WorldEventDefinition FindDefinition(WorldEventManager manager, WorldEventType type)
        {
            IReadOnlyList<WorldEventDefinition> defs = manager.EventDefinitions;
            for (int i = 0; i < defs.Count; i++)
            {
                if (defs[i] != null && defs[i].type == type)
                {
                    return defs[i];
                }
            }

            return null;
        }

        private static void ApplyModifierValues(WorldEventConfigModifier target, MacroPolicyModifiers src)
        {
            target.livingNeedsWeightMultiplier = src.livingNeedsWeightMultiplier;
            target.reserveWeightMultiplier = src.reserveWeightMultiplier;
            target.tradingWeightMultiplier = src.tradingWeightMultiplier;
            target.minimumLivingBudgetMultiplier = src.minimumLivingBudgetMultiplier;
            target.minimumReserveBudgetMultiplier = src.minimumReserveBudgetMultiplier;
            target.rebalanceIntervalMultiplier = src.rebalanceIntervalMultiplier;
            target.chainActionCooldownMultiplier = src.chainActionCooldownMultiplier;
            target.minTradeMultiplier = src.minTradeMultiplier;
            target.maxTradeMultiplier = src.maxTradeMultiplier;
        }

        private static WorldEventConfigModifier CopyModifier(WorldEventConfigModifier src)
        {
            WorldEventConfigModifier copy = new WorldEventConfigModifier();
            CopyModifierInto(src, copy);
            return copy;
        }

        private static void CopyModifierInto(WorldEventConfigModifier src, WorldEventConfigModifier dst)
        {
            dst.livingNeedsWeightMultiplier = src.livingNeedsWeightMultiplier;
            dst.reserveWeightMultiplier = src.reserveWeightMultiplier;
            dst.tradingWeightMultiplier = src.tradingWeightMultiplier;
            dst.minimumLivingBudgetMultiplier = src.minimumLivingBudgetMultiplier;
            dst.minimumReserveBudgetMultiplier = src.minimumReserveBudgetMultiplier;
            dst.rebalanceIntervalMultiplier = src.rebalanceIntervalMultiplier;
            dst.chainActionCooldownMultiplier = src.chainActionCooldownMultiplier;
            dst.minTradeMultiplier = src.minTradeMultiplier;
            dst.maxTradeMultiplier = src.maxTradeMultiplier;
        }
    }
}
