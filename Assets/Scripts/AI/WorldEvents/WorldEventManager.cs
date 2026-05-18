using System;
using System.Collections.Generic;
using UnityEngine;

public class WorldEventManager : Singleton<WorldEventManager>
{
    [SerializeField] private List<WorldEventDefinition> eventDefinitions = new List<WorldEventDefinition>();

    private readonly HashSet<WorldEventType> activeEventTypes = new HashSet<WorldEventType>();
    private readonly Dictionary<WorldEventType, float> startedTimes = new Dictionary<WorldEventType, float>();

    public event Action<List<ActiveWorldEvent>> ActiveEventsChanged;

    public static WorldEventManager GetOrCreate()
    {
        WorldEventManager manager = Instance;
        if (manager != null)
        {
            return manager;
        }

        GameObject managerObject = new GameObject("World Event Manager");
        manager = managerObject.AddComponent<WorldEventManager>();
        return manager;
    }

    protected override void Awake()
    {
        base.Awake();
        EnsureDefaultDefinitions();
    }

    public IReadOnlyList<WorldEventDefinition> EventDefinitions
    {
        get { return eventDefinitions; }
    }

    public bool ActivateEvent(WorldEventType type)
    {
        if (!TryGetDefinition(type, out WorldEventDefinition definition))
        {
            Debug.LogWarning($"World event definition not found: {type}");
            return false;
        }

        if (!activeEventTypes.Add(type))
        {
            return false;
        }

        startedTimes[type] = Time.time;
        Debug.Log($"World event activated: {definition.displayName}");
        NotifyActiveEventsChanged();
        return true;
    }

    public bool DeactivateEvent(WorldEventType type)
    {
        if (!activeEventTypes.Remove(type))
        {
            return false;
        }

        startedTimes.Remove(type);
        Debug.Log($"World event deactivated: {type}");
        NotifyActiveEventsChanged();
        return true;
    }

    public void ToggleEvent(WorldEventType type)
    {
        if (IsEventActive(type))
        {
            DeactivateEvent(type);
        }
        else
        {
            ActivateEvent(type);
        }
    }

    public void ClearEvents()
    {
        if (activeEventTypes.Count == 0)
        {
            return;
        }

        activeEventTypes.Clear();
        startedTimes.Clear();
        NotifyActiveEventsChanged();
    }

    public bool IsEventActive(WorldEventType type)
    {
        return activeEventTypes.Contains(type);
    }

    public NpcPortfolioConfig BuildModifiedConfig(NpcPortfolioConfig baseConfig)
    {
        NpcPortfolioConfig modified = NpcPortfolioConfig.CopyOf(baseConfig);
        for (int i = 0; i < eventDefinitions.Count; i++)
        {
            WorldEventDefinition definition = eventDefinitions[i];
            if (definition == null || !activeEventTypes.Contains(definition.type) || definition.modifier == null)
            {
                continue;
            }

            definition.modifier.ApplyTo(modified);
        }

        return modified;
    }

    public List<ActiveWorldEvent> GetActiveEvents()
    {
        List<ActiveWorldEvent> events = new List<ActiveWorldEvent>();
        for (int i = 0; i < eventDefinitions.Count; i++)
        {
            WorldEventDefinition definition = eventDefinitions[i];
            if (definition == null || !activeEventTypes.Contains(definition.type))
            {
                continue;
            }

            float startedTime = startedTimes.ContainsKey(definition.type) ? startedTimes[definition.type] : 0f;
            events.Add(new ActiveWorldEvent
            {
                type = definition.type,
                displayName = definition.displayName,
                description = definition.description,
                startedGameTime = startedTime
            });
        }

        return events;
    }

    [ContextMenu("Activate Energy Shortage")]
    public void ActivateEnergyShortage()
    {
        ActivateEvent(WorldEventType.EnergyShortage);
    }

    [ContextMenu("Activate Inflation")]
    public void ActivateInflation()
    {
        ActivateEvent(WorldEventType.Inflation);
    }

    [ContextMenu("Activate Market Boom")]
    public void ActivateMarketBoom()
    {
        ActivateEvent(WorldEventType.MarketBoom);
    }

    [ContextMenu("Activate Liquidity Crunch")]
    public void ActivateLiquidityCrunch()
    {
        ActivateEvent(WorldEventType.LiquidityCrunch);
    }

    [ContextMenu("Clear World Events")]
    public void ClearWorldEvents()
    {
        ClearEvents();
    }

    private void NotifyActiveEventsChanged()
    {
        ActiveEventsChanged?.Invoke(GetActiveEvents());
    }

    private bool TryGetDefinition(WorldEventType type, out WorldEventDefinition definition)
    {
        EnsureDefaultDefinitions();
        for (int i = 0; i < eventDefinitions.Count; i++)
        {
            definition = eventDefinitions[i];
            if (definition != null && definition.type == type)
            {
                return true;
            }
        }

        definition = null;
        return false;
    }

    private void EnsureDefaultDefinitions()
    {
        if (eventDefinitions == null)
        {
            eventDefinitions = new List<WorldEventDefinition>();
        }

        AddDefaultDefinition(
            WorldEventType.EnergyShortage,
            "Energy Shortage",
            "Higher living cost and lower trading appetite.",
            1.25f, 1.10f, 0.75f,
            1.50f, 1.10f,
            1.15f, 1.15f,
            0.90f, 0.80f);

        AddDefaultDefinition(
            WorldEventType.Inflation,
            "Inflation",
            "Living costs rise and nominal trade sizes expand.",
            1.35f, 0.95f, 0.85f,
            1.60f, 1.15f,
            0.90f, 0.90f,
            1.20f, 1.20f);

        AddDefaultDefinition(
            WorldEventType.MarketBoom,
            "Market Boom",
            "NPCs allocate more budget to trading and act faster.",
            0.90f, 0.75f, 1.40f,
            0.90f, 0.85f,
            0.80f, 0.75f,
            1.10f, 1.25f);

        AddDefaultDefinition(
            WorldEventType.LiquidityCrunch,
            "Liquidity Crunch",
            "NPCs preserve reserves and reduce chain trade size.",
            1.05f, 1.40f, 0.70f,
            1.10f, 1.50f,
            1.30f, 1.40f,
            0.80f, 0.65f);
    }

    private void AddDefaultDefinition(
        WorldEventType type,
        string displayName,
        string description,
        float livingWeight,
        float reserveWeight,
        float tradingWeight,
        float minimumLiving,
        float minimumReserve,
        float rebalanceInterval,
        float chainCooldown,
        float minTrade,
        float maxTrade)
    {
        if (HasDefinition(type))
        {
            return;
        }

        eventDefinitions.Add(new WorldEventDefinition
        {
            type = type,
            displayName = displayName,
            description = description,
            modifier = new WorldEventConfigModifier
            {
                livingNeedsWeightMultiplier = livingWeight,
                reserveWeightMultiplier = reserveWeight,
                tradingWeightMultiplier = tradingWeight,
                minimumLivingBudgetMultiplier = minimumLiving,
                minimumReserveBudgetMultiplier = minimumReserve,
                rebalanceIntervalMultiplier = rebalanceInterval,
                chainActionCooldownMultiplier = chainCooldown,
                minTradeMultiplier = minTrade,
                maxTradeMultiplier = maxTrade
            }
        });
    }

    private bool HasDefinition(WorldEventType type)
    {
        for (int i = 0; i < eventDefinitions.Count; i++)
        {
            if (eventDefinitions[i] != null && eventDefinitions[i].type == type)
            {
                return true;
            }
        }

        return false;
    }
}
