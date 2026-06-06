using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using ArcTrading.Auth;
using UnityEngine;
using Vector3 = UnityEngine.Vector3;
using Quaternion = UnityEngine.Quaternion;

/// <summary>
/// On successful wallet login, queries the player's NPC NFTs from
/// NpcCharacter and instantiates one trader prefab per owned token,
/// mapped by on-chain Archetype, seeded with the on-chain PortfolioConfig.
///
/// Spawn flow:
/// 1. Pick prefab by Archetype enum.
/// 2. Instantiate under an inactive holder so Awake/OnEnable don't fire yet.
/// 3. Wire runtime overrides: tokenId, payment-wallet service ref, on-chain
///    portfolio config, world navigation targets.
/// 4. Reparent to scene root, position with jitter around the spawn anchor,
///    then drop the holder so Start() runs with everything in place.
/// </summary>
public class OwnedNpcSpawner : MonoBehaviour
{
    [Header("Chain")] [SerializeField] private NpcCharacterContractClient npcContract;
    [SerializeField] private NpcPaymentWalletService npcPaymentWalletService;

    [Header("Prefabs by archetype")] [SerializeField]
    private GameObject conservativeSaverPrefab;

    [SerializeField] private GameObject balancedTraderPrefab;
    [SerializeField] private GameObject aggressiveSpeculatorPrefab;

    [Header("Spawn placement")]
    [Tooltip("Center point for spawned NPCs. Falls back to this GameObject's " +
             "transform if null.")]
    [SerializeField]
    private Transform spawnAnchor;

    [SerializeField, Min(0f)] private float spawnRadius = 2f;

    [Header("Navigation targets (copied onto spawned NPCs)")] [SerializeField]
    private Transform marketPoint;

    [SerializeField] private Transform shopPoint;
    [SerializeField] private Transform aggressiveHomePoint;
    [SerializeField] private Transform balancedHomePoint;
    [SerializeField] private Transform conservativeHomePoint;

    [Header("Misc")] [SerializeField] private bool logVerbose = true;

    private readonly HashSet<ulong> spawnedTokenIds = new HashSet<ulong>();
    private CancellationTokenSource lifetimeCts;
    private bool spawning;

    private void Awake()
    {
        lifetimeCts = new CancellationTokenSource();
    }

    private void Start()
    {
        var login = WalletLoginService.Instance;
        if (login == null)
        {
            Debug.LogError("[OwnedNpcSpawner] WalletLoginService not found in scene.");
            return;
        }

        login.OnLoginSucceeded += HandleLoginSucceeded;

        // Cover the case where login already completed before Start ran.
        if (login.HasSession)
        {
            HandleLoginSucceeded(login.Current);
        }
    }

    private void OnDestroy()
    {
        try
        {
            lifetimeCts?.Cancel();
        }
        catch
        {
            /* ignored */
        }

        lifetimeCts?.Dispose();
        lifetimeCts = null;
        if (WalletLoginService.Instance != null)
            WalletLoginService.Instance.OnLoginSucceeded -= HandleLoginSucceeded;
    }

    private async void HandleLoginSucceeded(WalletSession session)
    {
        if (session == null || string.IsNullOrWhiteSpace(session.wallet)) return;
        if (spawning) return;
        spawning = true;
        try
        {
            await SpawnOwnedAsync(session.wallet, lifetimeCts.Token);
        }
        catch (OperationCanceledException)
        {
            /* component destroyed mid-spawn */
        }
        catch (Exception ex)
        {
            Debug.LogError($"[OwnedNpcSpawner] spawn flow failed: {ex}");
        }
        finally
        {
            spawning = false;
        }
    }

    private async Task SpawnOwnedAsync(string playerWallet, CancellationToken ct)
    {
        if (npcContract == null)
        {
            Debug.LogError("[OwnedNpcSpawner] npcContract reference is missing.");
            return;
        }

        if (logVerbose)
            Debug.Log($"[OwnedNpcSpawner] scanning NPC NFTs owned by {playerWallet}…");

        var owned = await npcContract.EnumerateOwnedNpcsAsync(playerWallet, ct);

        if (logVerbose)
            Debug.Log($"[OwnedNpcSpawner] found {owned.Count} NPC NFT(s) owned by {playerWallet}.");

        foreach (var entry in owned)
        {
            ct.ThrowIfCancellationRequested();
            ulong tokenIdUlong = (ulong)entry.TokenId;
            if (!spawnedTokenIds.Add(tokenIdUlong))
            {
                if (logVerbose)
                    Debug.Log($"[OwnedNpcSpawner] tokenId={tokenIdUlong} already spawned, skipping.");
                continue;
            }

            try
            {
                SpawnOne(entry.TokenId, entry.Data);
            }
            catch (Exception ex)
            {
                Debug.LogError(
                    $"[OwnedNpcSpawner] failed to spawn tokenId={tokenIdUlong}: {ex}");
                spawnedTokenIds.Remove(tokenIdUlong);
            }
        }
    }

    private void SpawnOne(BigInteger tokenId, NpcDataDTO data)
    {
        var archetype = (TradingNpcArchetype)data.Archetype;
        var prefab = PickPrefab(archetype);
        if (prefab == null)
        {
            Debug.LogError(
                $"[OwnedNpcSpawner] no prefab assigned for archetype {archetype} (tokenId={tokenId}).");
            return;
        }

        // Holder is created inactive so Instantiate-under-it produces an
        // activeInHierarchy=false instance — Awake/OnEnable are deferred until
        // we reparent out.
        var holder = new GameObject($"__OwnedNpcSpawnHolder_{tokenId}");
        holder.SetActive(false);

        GameObject instance = null;
        try
        {
            instance = Instantiate(prefab, holder.transform);

            var arc = instance.GetComponent<ArcTradingContractClient>();
            if (arc != null)
            {
                arc.SetNftTokenIdForRuntime((ulong)tokenId);
                if (npcPaymentWalletService != null)
                    arc.SetNpcPaymentWalletServiceForRuntime(npcPaymentWalletService);
            }
            else
            {
                Debug.LogWarning(
                    $"[OwnedNpcSpawner] prefab {prefab.name} has no ArcTradingContractClient — " +
                    "tokenId injection skipped.");
            }

            var actor = instance.GetComponent<TradingNpcActor>();
            if (actor != null)
            {
                actor.SetRuntimePortfolioOverride(MapChainPortfolio(data.Portfolio));

                switch ((TradingNpcArchetype)data.Archetype)
                {
                    case TradingNpcArchetype.AggressiveSpeculator:
                        actor.SetRuntimeWorldPoints(marketPoint, shopPoint, aggressiveHomePoint);
                        break;
                    case TradingNpcArchetype.BalancedTrader:
                        actor.SetRuntimeWorldPoints(marketPoint, shopPoint, balancedHomePoint);
                        break;
                    case TradingNpcArchetype.ConservativeSaver:
                        actor.SetRuntimeWorldPoints(marketPoint, shopPoint, conservativeHomePoint);
                        break;
                    default:
                        Debug.LogWarning(
                            $"[OwnedNpcSpawner] unrecognized archetype {archetype} for tokenId={tokenId} — " +
                            "world-point overrides skipped.");
                        break;
                }
            }
            else
            {
                Debug.LogWarning(
                    $"[OwnedNpcSpawner] prefab {prefab.name} has no TradingNpcActor — " +
                    "portfolio/world-point overrides skipped.");
            }

            instance.name = string.IsNullOrWhiteSpace(data.NpcName)
                ? $"NPC #{tokenId}"
                : $"{data.NpcName} #{tokenId}";

            // Move to scene root and place at anchor + jitter.
            Vector3 origin = spawnAnchor != null ? spawnAnchor.position : transform.position;
            UnityEngine.Vector2 jitter2D = spawnRadius > 0f
                ? UnityEngine.Random.insideUnitCircle * spawnRadius
                : UnityEngine.Vector2.zero;
            Vector3 worldPos = origin + new Vector3(jitter2D.x, 0f, jitter2D.y);

            instance.transform.SetParent(null, worldPositionStays: false);
            instance.transform.position = worldPos;
            instance.transform.rotation = Quaternion.identity;

            if (logVerbose)
            {
                Debug.Log(
                    $"[OwnedNpcSpawner] spawned tokenId={tokenId} '{data.NpcName}' " +
                    $"archetype={archetype} risk={data.RiskLevel} level={data.Level} " +
                    $"@ {worldPos}");
            }
        }
        finally
        {
            // Reparenting alone activates the instance (parent now null = root, active).
            // If something failed mid-setup the instance is still under the inactive
            // holder; destroying the holder cleans both up.
            if (instance != null && instance.transform.parent == holder.transform)
            {
                Destroy(instance);
            }

            Destroy(holder);
        }
    }

    private GameObject PickPrefab(TradingNpcArchetype archetype)
    {
        switch (archetype)
        {
            case TradingNpcArchetype.ConservativeSaver: return conservativeSaverPrefab;
            case TradingNpcArchetype.BalancedTrader: return balancedTraderPrefab;
            case TradingNpcArchetype.AggressiveSpeculator: return aggressiveSpeculatorPrefab;
            default: return null;
        }
    }

    // BPS (0..10000) → weight 0..1
    // USDC 6-decimal uint64 → float USDC
    // Seconds → float seconds
    private static NpcPortfolioConfig MapChainPortfolio(PortfolioConfigDTO p)
    {
        var c = new NpcPortfolioConfig();
        if (p == null) return c;

        const float BpsDenominator = 10000f;
        const float UsdcDecimals = 1_000_000f;

        c.livingNeedsWeight = p.LivingNeedsWeightBps / BpsDenominator;
        c.reserveWeight = p.ReserveWeightBps / BpsDenominator;
        c.tradingWeight = p.TradingWeightBps / BpsDenominator;

        c.minimumLivingBudgetUSDC = p.MinimumLivingBudgetUSDC / UsdcDecimals;
        c.minimumReserveBudgetUSDC = p.MinimumReserveBudgetUSDC / UsdcDecimals;
        c.rebalanceInterval = p.RebalanceIntervalSeconds;
        c.chainActionCooldown = p.ChainActionCooldownSeconds;
        c.minTradeUSDC = p.MinTradeUSDC / UsdcDecimals;
        c.maxTradeUSDC = p.MaxTradeUSDC / UsdcDecimals;
        return c;
    }
}