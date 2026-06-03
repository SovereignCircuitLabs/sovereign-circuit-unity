using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CleverCrow.Fluid.BTs.Trees;
using Newtonsoft.Json.Linq;
using UnityEngine;
using BtTaskStatus = CleverCrow.Fluid.BTs.Tasks.TaskStatus;

[RequireComponent(typeof(ArcTradingContractClient))]
[RequireComponent(typeof(NavMeshNavigator))]
public abstract class TradingNpcActor : AIActor
{
    [Header("Trader NPC")] public TradingNpcArchetype archetype = TradingNpcArchetype.BalancedTrader;
    public NpcPortfolioConfig portfolioConfig = new NpcPortfolioConfig();
    public NpcPortfolioState portfolioState = new NpcPortfolioState();
    [SerializeField] private bool useNanopayment = false;

    [Header("World Targets")] [SerializeField]
    private Transform marketPoint;

    [SerializeField] private Transform shopPoint;
    [SerializeField] private Transform homePoint;
    //[SerializeField] private float arriveHeight = 0.5f;

    private ArcTradingContractClient contractClient;
    private NavMeshNavigator navMeshNavigator;
    private float lastRebalanceTime = -999f;
    private float lastChainActionTime = -999f;
    private float lastLivingSpendTime = -999f;
    private bool asyncActionRunning;
    private bool asyncActionFinished;
    private bool asyncActionSucceeded;
    private string asyncActionName;
    private bool brainReady;
    private readonly List<TradingNpcActivityRecord> activityLog = new List<TradingNpcActivityRecord>();
    private const int MaxActivityLogCount = 100;
    private string currentActivity = "Initializing";
    private string currentMoveTargetName;
    private NpcPortfolioConfig basePortfolioConfig;
    private WorldEventManager subscribedWorldEventManager;

    public string WalletAddress
    {
        get { return contractClient != null ? contractClient.WalletAddress : null; }
    }

    public string CurrentActivity
    {
        get { return currentActivity; }
    }

    public bool IsRunningChainAction
    {
        get { return asyncActionRunning; }
    }

    protected override async void Start()
    {
        base.Start();
        brain = null;
        brainReady = false;
        contractClient = GetComponent<ArcTradingContractClient>();
        navMeshNavigator = GetComponent<NavMeshNavigator>();
        ConfigurePortfolio();
        CaptureBasePortfolioConfig();
        SubscribeToWorldEvents();
        ApplyWorldEventConfig();

        try
        {
            await InitializePortfolioAsync();
            if (this == null)
            {
                return;
            }

            InitAI();
            brainReady = true;
        }
        catch (Exception ex)
        {
            if (this == null)
            {
                return;
            }

            Debug.LogError($"{LogPrefix} failed to initialize trader portfolio: {ex}");
            AddActivity(TradingNpcActivityType.ChainActionFailed, "Initialize failed", ex.Message);
        }
    }

    protected override void Update()
    {
        base.Update();

        if (Input.GetKeyDown(KeyCode.Escape))
            Application.Quit();
    }

    protected override void FixedUpdate()
    {
        base.FixedUpdate();

        if (brainReady && brain != null)
        {
            brain.Tick();
        }
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        SubscribeToWorldEvents();
        ApplyWorldEventConfig();
    }

    protected override void OnDisable()
    {
        UnsubscribeFromWorldEvents();
        base.OnDisable();
    }

    protected abstract void ConfigurePortfolio();
    protected abstract TradeDecision DecideTrade();

    private void CaptureBasePortfolioConfig()
    {
        basePortfolioConfig = NpcPortfolioConfig.CopyOf(portfolioConfig);
    }

    private void SubscribeToWorldEvents()
    {
        WorldEventManager manager = WorldEventManager.GetOrCreate();
        if (manager == null || subscribedWorldEventManager == manager)
        {
            return;
        }

        UnsubscribeFromWorldEvents();
        subscribedWorldEventManager = manager;
        subscribedWorldEventManager.ActiveEventsChanged += OnWorldEventsChanged;
    }

    private void UnsubscribeFromWorldEvents()
    {
        if (subscribedWorldEventManager == null)
        {
            return;
        }

        subscribedWorldEventManager.ActiveEventsChanged -= OnWorldEventsChanged;
        subscribedWorldEventManager = null;
    }

    private void OnWorldEventsChanged(List<ActiveWorldEvent> activeEvents)
    {
        ApplyWorldEventConfig();
        AllocateBudgetsFromBalances();
        AddActivity(
            TradingNpcActivityType.WorldEventConfigChanged,
            "World event config changed",
            BuildActiveWorldEventSummary(activeEvents));
    }

    private void ApplyWorldEventConfig()
    {
        if (basePortfolioConfig == null)
        {
            return;
        }

        WorldEventManager manager = WorldEventManager.Instance;
        NpcPortfolioConfig nextConfig = manager != null
            ? manager.BuildModifiedConfig(basePortfolioConfig)
            : NpcPortfolioConfig.CopyOf(basePortfolioConfig);
        portfolioConfig.CopyFrom(nextConfig);
    }

    private string BuildActiveWorldEventSummary(List<ActiveWorldEvent> activeEvents)
    {
        if (activeEvents == null || activeEvents.Count == 0)
        {
            return "No active world events; config restored to base values.";
        }

        string summary = "Active events: ";
        for (int i = 0; i < activeEvents.Count; i++)
        {
            if (i > 0)
            {
                summary += ", ";
            }

            summary += activeEvents[i].displayName;
        }

        return summary;
    }

    private void InitAI()
    {
        brain = new BehaviorTreeBuilder(gameObject)
            .Selector()
            .Sequence("Buy Living Needs")
            .Condition("Needs Supplies?", NeedsLivingSupplies)
            .Do("Go To Shop", () => MoveTo(shopPoint))
            .Do("Buy Supplies", BuyLivingSupplies)
            .End()
            .Sequence("Rebalance Portfolio")
            .Condition("Needs Rebalance?", NeedsPortfolioRebalance)
            .Do("Go Home", () => MoveTo(homePoint))
            .Do("Rebalance", RebalancePortfolio)
            .End()
            .Sequence("Trade On Chain")
            .Condition("Can Trade?", CanTrade)
            .Do("Go To Market", () => MoveTo(marketPoint))
            .Do("Execute Trade", ExecuteTrade)
            .End()
            .Do("Wander", Wander)
            .End()
            .Build();
    }

    private BtTaskStatus MoveTo(Transform target)
    {
        if (target == null || steeringBehaviors == null)
        {
            return BtTaskStatus.Success;
        }

        Vector3 targetPosition = target.position;
        //targetPosition.y = arriveHeight;

        bool targetChanged = currentMoveTargetName != target.name;
        if (targetChanged)
        {
            currentMoveTargetName = target.name;
            currentActivity = $"Moving to {currentMoveTargetName}";
            AddActivity(TradingNpcActivityType.MoveToTarget, "Move target changed", currentActivity);
        }
        
        if (navMeshNavigator != null)
        {
            navMeshNavigator.SetDestination(targetPosition);
        }
        
        Vector3 steerTarget = targetPosition;
        if (navMeshNavigator != null && navMeshNavigator.HasPath)
        {
            steerTarget = navMeshNavigator.currentCornerPosition;
            //steerTarget.y = arriveHeight;
        }

        Vector3 acceleration = steeringBehaviors.Arrive(steerTarget);
        acceleration = AvoidCollision(acceleration);

        steeringBehaviors.Steer(acceleration);
        steeringBehaviors.LookMoveDirection();

        bool pathConsumed = navMeshNavigator == null || !navMeshNavigator.HasPath;
        if (pathConsumed && steeringBehaviors.IsArrived(targetPosition))
        {
            currentActivity = $"Arrived at {currentMoveTargetName}";
            currentMoveTargetName = null;
            if (navMeshNavigator != null)
            {
                navMeshNavigator.ClearPath();
            }
            return BtTaskStatus.Success;
        }

        return BtTaskStatus.Continue;
    }

    private BtTaskStatus Wander()
    {
        if (wanderBehaviors == null || steeringBehaviors == null)
        {
            return BtTaskStatus.Success;
        }

        //wanderBehaviors.targetHeight = arriveHeight;
        Vector3 acceleration = AvoidCollision(wanderBehaviors.GetSteering());
        steeringBehaviors.Steer(acceleration);
        steeringBehaviors.LookMoveDirection();
        return BtTaskStatus.Success;
    }

    private bool NeedsLivingSupplies()
    {
        bool budgetLow = portfolioState.livingBudgetUSDC < portfolioConfig.minimumLivingBudgetUSDC;
        bool dailySpendDue = Time.time - lastLivingSpendTime > 30f;
        return budgetLow || dailySpendDue;
    }

    private BtTaskStatus BuyLivingSupplies()
    {
        float spend = Mathf.Min(portfolioState.walletUSDC,
            Mathf.Max(0.005f, portfolioConfig.minimumLivingBudgetUSDC * 0.25f));
        portfolioState.walletUSDC -= spend;
        portfolioState.livingBudgetUSDC += spend;
        lastLivingSpendTime = Time.time;
        currentActivity = $"Bought living supplies: {spend:0.####} USDC";
        AddActivity(TradingNpcActivityType.BuyLivingSupplies, "Bought living supplies", currentActivity, null, spend);
        Debug.Log($"{LogPrefix} bought living supplies for {spend:0.####} USDC");
        return BtTaskStatus.Success;
    }

    private bool NeedsPortfolioRebalance()
    {
        return Time.time - lastRebalanceTime >= portfolioConfig.rebalanceInterval;
    }

    private BtTaskStatus RebalancePortfolio()
    {
        return RunAsyncAction("Rebalance", async () =>
        {
            await RefreshBalancesAsync();
            AllocateBudgetsFromBalances();

            lastRebalanceTime = Time.time;
            currentActivity = "Portfolio rebalanced";
            AddActivity(
                TradingNpcActivityType.Rebalance,
                "Portfolio rebalanced",
                $"living={portfolioState.livingBudgetUSDC:0.####}, reserve={portfolioState.reserveBudgetUSDC:0.####}, trading={portfolioState.tradingBudgetUSDC:0.####}");
            Debug.Log(
                $"{LogPrefix} rebalanced portfolio: living={portfolioState.livingBudgetUSDC:0.####}, reserve={portfolioState.reserveBudgetUSDC:0.####}, trading={portfolioState.tradingBudgetUSDC:0.####}");
        });
    }

    private bool CanTrade()
    {
        if (asyncActionRunning)
        {
            return true;
        }

        if (Time.time - lastChainActionTime < portfolioConfig.chainActionCooldown)
        {
            return false;
        }

        return portfolioState.tradingBudgetUSDC >= portfolioConfig.minTradeUSDC;
    }

    private BtTaskStatus ExecuteTrade()
    {
        return RunAsyncAction("Trade", async () =>
        {
            await RefreshBalancesAsync();

            TradeDecision decision = DecideTrade();
            float amount = 0.0f;

            if (decision.intent == TradeIntent.Hold)
            {
                currentActivity = $"Holding: {decision.reason}";
                AddActivity(TradingNpcActivityType.TradeHold, "Trade hold", decision.reason);
                Debug.Log($"{LogPrefix} holds: {decision.reason}");
                return;
            }

            string txResult = null;
            string txHash = null;
            TradingNpcActivityType activityType = TradingNpcActivityType.TradeHold;
            if (decision.intent == TradeIntent.BuyNFT)
            {
                // Deposit intent → mintRandom: trade USDC (or x402 authorization) for one random NFT
                int itemIdToBeMinted = UnityEngine.Random.Range(1, 6);
                var mintPrice = (float)await contractClient.GetBuyPriceUSDCAsync(itemIdToBeMinted);
                if (portfolioState.walletUSDC - portfolioState.reserveBudgetUSDC < mintPrice)
                {
                    return;
                }

                amount = mintPrice;
                txResult = await contractClient.MintRandomAsync(itemIdToBeMinted, useNanopayment);
                txHash = useNanopayment ? ExtractX402Tx(txResult) : txResult;

                activityType = useNanopayment
                    ? TradingNpcActivityType.TradeDepositNanopayment
                    : TradingNpcActivityType.TradeDeposit;
            }
            else if (decision.intent == TradeIntent.SellNFT)
            {
                // Withdraw intent → sellItem: sell one NFT the NPC owns back to the contract
                var inventoryAddress = !string.IsNullOrWhiteSpace(contractClient.TbaAddress)
                    ? contractClient.TbaAddress
                    : contractClient.WalletAddress;
                var ownedId = await contractClient.FindFirstOwnedItemIdAsync(inventoryAddress);
                if (!ownedId.HasValue)
                {
                    return;
                }

                amount = (float)await contractClient.GetSellPriceUSDCAsync(ownedId.Value);
                if (amount < portfolioConfig.minTradeUSDC)
                {
                    return;
                }

                txResult = await contractClient.SellItemAsync(inventoryAddress, ownedId.Value);
                txHash = txResult;

                activityType = TradingNpcActivityType.TradeWithdraw;
            }

            lastChainActionTime = Time.time;
            string modeTag = useNanopayment ? " (nanopayment)" : "";
            currentActivity = $"{decision.intent}{modeTag} {amount:0.####} USDC";
            AddActivity(
                activityType,
                currentActivity,
                decision.reason,
                txHash,
                amount);

            await RefreshBalancesAsync();
            Debug.Log(
                $"{LogPrefix} {decision.intent}{modeTag} {amount:0.####} USDC, reason={decision.reason}, tx={txHash}");
        });
    }

    protected TradeDecision BuildDecision(TradeIntent intent, string reason)
    {
        return new TradeDecision
        {
            intent = intent,
            reason = reason
        };
    }

    private async Task InitializePortfolioAsync()
    {
        await contractClient.InitializeWalletAsync();

        // generate or load x402 signature payment wallet and bind it to the NPC(NFT)
        try
        {
            var signer = await contractClient.EnsurePaymentWalletBoundAsync();
            if (signer.HasValue)
            {
                AddActivity(
                    TradingNpcActivityType.Initialized,
                    "Payment wallet bound",
                    $"tokenId={signer.Value.TokenId}, paymentWallet={signer.Value.Address}, version={signer.Value.Version}");
                Debug.Log(
                    $"{LogPrefix} payment wallet bound: tokenId={signer.Value.TokenId}, addr={signer.Value.Address}, version={signer.Value.Version}");
            }

            await contractClient.EnsureNpcHasInitialCapitalAsync();
            await RefreshBalancesAsync();
            AllocateBudgetsFromBalances();
            lastRebalanceTime = Time.time;
            currentActivity = "Initialized";
            AddActivity(
                TradingNpcActivityType.Initialized,
                "Portfolio initialized",
                $"wallet={portfolioState.walletUSDC:0.####}, vault={portfolioState.vaultUSDC:0.####}, address={contractClient.WalletAddress}");

            Debug.Log($"{LogPrefix} initialized USDC portfolio for {contractClient.WalletAddress}: " +
                      $"wallet={portfolioState.walletUSDC:0.####}, vault={portfolioState.vaultUSDC:0.####}, " +
                      $"living={portfolioState.livingBudgetUSDC:0.####}, " +
                      $"reserve={portfolioState.reserveBudgetUSDC:0.####}, " +
                      $"trading={portfolioState.tradingBudgetUSDC:0.####}");
        }
        catch (NpcSignerNotOwned ex)
        {
            Debug.LogWarning($"{LogPrefix} {ex.Message}");
            AddActivity(TradingNpcActivityType.ChainActionFailed, "Payment wallet not owned by this device",
                ex.Message);
        }
        catch (Exception ex)
        {
            Debug.LogError($"{LogPrefix} failed to bind payment wallet: {ex}");
            AddActivity(TradingNpcActivityType.ChainActionFailed, "Bind payment wallet failed", ex.Message);
        }
    }

    private async Task RefreshBalancesAsync()
    {
        var inventoryAddress = !string.IsNullOrWhiteSpace(contractClient.TbaAddress)
            ? contractClient.TbaAddress
            : contractClient.WalletAddress;

        float traderUsdc = (float)await contractClient.GetWalletBalanceUSDCAsync();
        float tbaUsdc = !string.Equals(inventoryAddress, contractClient.WalletAddress,
            StringComparison.OrdinalIgnoreCase)
            ? (float)await contractClient.GetWalletBalanceUSDCAsync(inventoryAddress)
            : 0f;
        portfolioState.walletUSDC = traderUsdc + tbaUsdc;
        
        portfolioState.vaultUSDC = (float)await contractClient.GetVaultBalanceUSDCAsync(inventoryAddress);
        portfolioState.gatewayUSDC = (float)await contractClient.GetGatewayAvailableBalanceUSDCAsync();
        portfolioState.nftInventoryCount = await contractClient.GetNftInventoryCountAsync(inventoryAddress);
        portfolioState.bestSellPriceUSDC = (float)await contractClient.GetBestSellPriceUSDCAsync();
        portfolioState.avgBuyPriceUSDC = (float)await contractClient.GetAvgBuyPriceUSDCAsync();
    }

    private void AllocateBudgetsFromBalances()
    {
        float total = Mathf.Max(0f, portfolioState.TotalUSDC);
        float weightSum = Mathf.Max(0.0001f,
            portfolioConfig.livingNeedsWeight + portfolioConfig.reserveWeight + portfolioConfig.tradingWeight);

        portfolioState.livingBudgetUSDC = total * portfolioConfig.livingNeedsWeight / weightSum;
        portfolioState.reserveBudgetUSDC = total * portfolioConfig.reserveWeight / weightSum;
        portfolioState.tradingBudgetUSDC = total * portfolioConfig.tradingWeight / weightSum;
    }

    private BtTaskStatus RunAsyncAction(string actionName, Func<Task> action)
    {
        if (asyncActionRunning)
        {
            if (asyncActionName != actionName)
            {
                return BtTaskStatus.Continue;
            }

            return BtTaskStatus.Continue;
        }

        if (asyncActionFinished)
        {
            asyncActionFinished = false;
            return asyncActionSucceeded ? BtTaskStatus.Success : BtTaskStatus.Failure;
        }

        asyncActionRunning = true;
        asyncActionName = actionName;
        asyncActionSucceeded = false;

        _ = RunActionAndCaptureResult(action);
        return BtTaskStatus.Continue;
    }

    private async Task RunActionAndCaptureResult(Func<Task> action)
    {
        try
        {
            await action();
            asyncActionSucceeded = true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"{LogPrefix} async action failed: {ex}");
            currentActivity = $"{asyncActionName} failed";
            AddActivity(TradingNpcActivityType.ChainActionFailed, currentActivity, ex.Message);
            asyncActionSucceeded = false;
        }
        finally
        {
            asyncActionRunning = false;
            asyncActionFinished = true;
            asyncActionName = null;
        }
    }

    private Vector3 AvoidCollision(Vector3 acceleration)
    {
        if (collisionSensor == null || acceleration.sqrMagnitude <= 0.0001f)
        {
            return acceleration;
        }

        Vector3 accelerationDir = acceleration.normalized;
        collisionSensor.GetCollisionFreeDirection(accelerationDir, out accelerationDir);
        return accelerationDir * acceleration.magnitude;
    }

    private string LogPrefix
    {
        get
        {
            if (this == null)
            {
                return "[TraderNpc] DestroyedTraderNpc";
            }

            return $"[{archetype}] {name}";
        }
    }

    public TradingNpcSnapshot CreateSnapshot(int maxActivities = 20)
    {
        TradingNpcSnapshot snapshot = new TradingNpcSnapshot
        {
            npcId = GetInstanceID().ToString(),
            displayName = name,
            archetype = archetype,
            walletAddress = WalletAddress,
            tbaAddress = String.IsNullOrEmpty(contractClient.TbaAddress) ? "" : contractClient.TbaAddress,
            worldPosition = transform.position,
            currentActivity = currentActivity,
            isRunningChainAction = asyncActionRunning,
            portfolioConfig = portfolioConfig,
            portfolioState = portfolioState,
            fields = BuildFieldDisplayInfo()
        };

        int start = Mathf.Max(0, activityLog.Count - Mathf.Max(0, maxActivities));
        for (int i = activityLog.Count - 1; i >= start; i--)
        {
            snapshot.recentActivities.Add(activityLog[i]);
        }

        return snapshot;
    }

    public List<TradingNpcActivityRecord> GetRecentActivities(int maxCount = 20)
    {
        List<TradingNpcActivityRecord> result = new List<TradingNpcActivityRecord>();
        int start = Mathf.Max(0, activityLog.Count - Mathf.Max(0, maxCount));
        for (int i = activityLog.Count - 1; i >= start; i--)
        {
            result.Add(activityLog[i]);
        }

        return result;
    }

    public void RecordExternalActivity(
        TradingNpcActivityType type,
        string title,
        string details,
        string txHash = null,
        float amountUSDC = 0f)
    {
        currentActivity = title;
        AddActivity(type, title, details, txHash, amountUSDC);
    }

    private void AddActivity(
        TradingNpcActivityType type,
        string title,
        string details,
        string txHash = null,
        float amountUSDC = 0f)
    {
        activityLog.Add(new TradingNpcActivityRecord
        {
            type = type,
            title = title,
            details = details,
            txHash = txHash,
            amountUSDC = amountUSDC,
            gameTime = Time.time,
            utcTime = DateTime.UtcNow.ToString("o"),
            worldPosition = transform.position
        });

        if (activityLog.Count > MaxActivityLogCount)
        {
            activityLog.RemoveAt(0);
        }
    }

    private List<NpcFieldDisplayInfo> BuildFieldDisplayInfo()
    {
        return new List<NpcFieldDisplayInfo>
        {
            Field("Config", "livingNeedsWeight", "Living Needs Weight", portfolioConfig.livingNeedsWeight,
                "Share of total funds reserved for living needs."),
            Field("Config", "reserveWeight", "Reserve Weight", portfolioConfig.reserveWeight,
                "Share of total funds kept as safety reserve."),
            Field("Config", "tradingWeight", "Trading Weight", portfolioConfig.tradingWeight,
                "Share of total funds allowed for strategy/trading decisions."),
            Field("Config", "minimumLivingBudgetUSDC", "Minimum Living Budget", portfolioConfig.minimumLivingBudgetUSDC,
                "When living budget falls below this value, NPC buys supplies."),
            Field("Config", "minimumReserveBudgetUSDC", "Minimum Reserve Budget",
                portfolioConfig.minimumReserveBudgetUSDC, "Human-readable lower reserve target for UI and tuning."),
            Field("Config", "rebalanceInterval", "Rebalance Interval", portfolioConfig.rebalanceInterval,
                "Seconds between portfolio rebalances."),
            Field("Config", "chainActionCooldown", "Chain Action Cooldown", portfolioConfig.chainActionCooldown,
                "Minimum seconds between deposit/withdraw transactions."),
            Field("Config", "minTradeUSDC", "Minimum Trade Size", portfolioConfig.minTradeUSDC,
                "Smallest on-chain trade amount this NPC will attempt."),
            Field("Config", "maxTradeUSDC", "Maximum Trade Size", portfolioConfig.maxTradeUSDC,
                "Largest on-chain trade amount this NPC will attempt."),
            Field("State", "walletUSDC", "Wallet USDC", portfolioState.walletUSDC,
                "USDC available in the NPC wallet (including payment wallet and TBA)."),
            Field("State", "vaultUSDC", "Vault USDC", portfolioState.vaultUSDC,
                "USDC value of NFTs currently held in the vault (TBA)."),
            Field("State", "gatewayUSDC", "Gateway USDC", portfolioState.gatewayUSDC,
                "USDC available in the Circle Gateway Wallet for x402 nanopayments."),
            Field("State", "livingBudgetUSDC", "Living Budget", portfolioState.livingBudgetUSDC,
                "Budget currently allocated to living needs."),
            Field("State", "reserveBudgetUSDC", "Reserve Budget", portfolioState.reserveBudgetUSDC,
                "Budget currently allocated as safety reserve."),
            Field("State", "tradingBudgetUSDC", "Trading Budget", portfolioState.tradingBudgetUSDC,
                "Budget currently available for trade decisions."),
            Field("State", "TotalUSDC", "Total USDC", portfolioState.TotalUSDC,
                "Wallet plus vault plus gateway balance.")
        };
    }

    private static string ExtractX402Tx(string json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            return JObject.Parse(json).Value<string>("x402_tx");
        }
        catch (Exception)
        {
            return json;
        }
    }

    private static string ExtractX402Amount(string json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            return JObject.Parse(json).Value<string>("amount");
        }
        catch (Exception)
        {
            return json;
        }
    }

    private static NpcFieldDisplayInfo Field(string group, string fieldName, string displayName, float value,
        string comment)
    {
        return new NpcFieldDisplayInfo
        {
            group = group,
            fieldName = fieldName,
            displayName = displayName,
            value = value.ToString("0.####"),
            comment = comment
        };
    }
}