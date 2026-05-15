using System;
using System.Threading.Tasks;
using CleverCrow.Fluid.BTs.Trees;
using UnityEngine;
using BtTaskStatus = CleverCrow.Fluid.BTs.Tasks.TaskStatus;

[RequireComponent(typeof(ArcTradingContractClient))]
public abstract class TradingNpcActor : AIActor
{
    [Header("Trader NPC")]
    public TradingNpcArchetype archetype = TradingNpcArchetype.BalancedTrader;
    public NpcPortfolioConfig portfolioConfig = new NpcPortfolioConfig();
    public NpcPortfolioState portfolioState = new NpcPortfolioState();

    [Header("World Targets")]
    [SerializeField] private Transform marketPoint;
    [SerializeField] private Transform shopPoint;
    [SerializeField] private Transform homePoint;
    [SerializeField] private float arriveHeight = 0.5f;

    private ArcTradingContractClient contractClient;
    private float lastRebalanceTime = -999f;
    private float lastChainActionTime = -999f;
    private float lastLivingSpendTime = -999f;
    private bool asyncActionRunning;
    private bool asyncActionFinished;
    private bool asyncActionSucceeded;
    private string asyncActionName;
    private bool brainReady;

    protected override async void Start()
    {
        base.Start();
        brain = null;
        brainReady = false;
        contractClient = GetComponent<ArcTradingContractClient>();
        ConfigurePortfolio();

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
        }
    }

    protected override void FixedUpdate()
    {
        base.FixedUpdate();

        if (brainReady && brain != null)
        {
            brain.Tick();
        }
    }

    protected abstract void ConfigurePortfolio();
    protected abstract TradeDecision DecideTrade();

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
        targetPosition.y = arriveHeight;

        Vector3 acceleration = steeringBehaviors.Arrive(targetPosition);
        acceleration = AvoidCollision(acceleration);

        steeringBehaviors.Steer(acceleration);
        steeringBehaviors.LookMoveDirection();

        return steeringBehaviors.IsArrived(targetPosition) ? BtTaskStatus.Success : BtTaskStatus.Continue;
    }

    private BtTaskStatus Wander()
    {
        if (wanderBehaviors == null || steeringBehaviors == null)
        {
            return BtTaskStatus.Success;
        }

        wanderBehaviors.targetHeight = arriveHeight;
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
        float spend = Mathf.Min(portfolioState.walletUSDC, Mathf.Max(0.005f, portfolioConfig.minimumLivingBudgetUSDC * 0.25f));
        portfolioState.walletUSDC -= spend;
        portfolioState.livingBudgetUSDC += spend;
        lastLivingSpendTime = Time.time;
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
            Debug.Log($"{LogPrefix} rebalanced portfolio: living={portfolioState.livingBudgetUSDC:0.####}, reserve={portfolioState.reserveBudgetUSDC:0.####}, trading={portfolioState.tradingBudgetUSDC:0.####}");
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
            float amount = Mathf.Clamp(decision.amountUSDC, portfolioConfig.minTradeUSDC, portfolioConfig.maxTradeUSDC);
            amount = Mathf.Min(amount, portfolioState.tradingBudgetUSDC);

            if (decision.intent == TradeIntent.Hold || amount < portfolioConfig.minTradeUSDC)
            {
                Debug.Log($"{LogPrefix} holds: {decision.reason}");
                return;
            }

            string txHash = null;
            if (decision.intent == TradeIntent.Deposit)
            {
                amount = Mathf.Min(amount, portfolioState.walletUSDC - portfolioState.reserveBudgetUSDC);
                if (amount < portfolioConfig.minTradeUSDC)
                {
                    return;
                }

                txHash = await contractClient.DepositAsync((decimal)amount);
                portfolioState.walletUSDC -= amount;
                portfolioState.vaultUSDC += amount;
            }
            else if (decision.intent == TradeIntent.Withdraw)
            {
                amount = Mathf.Min(amount, portfolioState.vaultUSDC);
                if (amount < portfolioConfig.minTradeUSDC)
                {
                    return;
                }

                txHash = await contractClient.WithdrawAsync((decimal)amount);
                portfolioState.walletUSDC += amount;
                portfolioState.vaultUSDC -= amount;
            }

            lastChainActionTime = Time.time;
            Debug.Log($"{LogPrefix} {decision.intent} {amount:0.####} USDC, reason={decision.reason}, tx={txHash}");
        });
    }

    protected TradeDecision BuildDecision(TradeIntent intent, float riskMultiplier, string reason)
    {
        float baseAmount = UnityEngine.Random.Range(portfolioConfig.minTradeUSDC, portfolioConfig.maxTradeUSDC);
        return new TradeDecision
        {
            intent = intent,
            amountUSDC = baseAmount * Mathf.Max(0.1f, riskMultiplier),
            reason = reason
        };
    }

    private async Task InitializePortfolioAsync()
    {
        await contractClient.InitializeWalletAsync();
        await RefreshBalancesAsync();
        AllocateBudgetsFromBalances();
        lastRebalanceTime = Time.time;

        Debug.Log($"{LogPrefix} initialized USDC portfolio for {contractClient.WalletAddress}: wallet={portfolioState.walletUSDC:0.####}, vault={portfolioState.vaultUSDC:0.####}, living={portfolioState.livingBudgetUSDC:0.####}, reserve={portfolioState.reserveBudgetUSDC:0.####}, trading={portfolioState.tradingBudgetUSDC:0.####}");
    }

    private async Task RefreshBalancesAsync()
    {
        portfolioState.walletUSDC = (float)await contractClient.GetWalletBalanceUSDCAsync();
        portfolioState.vaultUSDC = (float)await contractClient.GetVaultBalanceUSDCAsync();
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
}
