using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using ArcTrading.Auth;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Buy flow:
///   1. Reuse / wait for the SIWE wallet session (WalletLoginService).
///   2. Re-quote the price on click (the on-chain quote moves with TBA value
///      and listed supply).
///   3. Ensure USDC allowance ≥ maxPriceCap. If short, send approve via the
///      MetaMask bridge.
///   4. Submit NpcMarketplace.buyNpc(tokenId, maxPriceCap) via the bridge.
///      The contract also checks the seller's NFT approval; that was set at
///      list time, so the buyer doesn't approve anything NFT-side.
/// </summary>
public class NpcMarketplaceMenu : MonoBehaviour
{
    [Header("Contracts")]
    [SerializeField] private NpcMarketplaceClient marketplaceClient;
    [SerializeField] private NpcNFTPricingClient pricingClient;
    [SerializeField] private NpcCharacterContractClient npcCharacter;

    [Header("Panel")]
    [Tooltip("Root GameObject of the buy-NPC panel. Toggled by Open/Close.")]
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private Button openPanelButton;
    [SerializeField] private Button closePanelButton;
    [SerializeField] private Button refreshButton;

    [Header("Listings list")]
    [Tooltip("Parent transform of the row prefab instances. " +
             "Should have a VerticalLayoutGroup + ContentSizeFitter on the scroll-view content.")]
    [SerializeField] private RectTransform rowContainer;
    [SerializeField] private NpcMarketplaceRow rowPrefab;

    [Header("Status / feedback")]
    [SerializeField] private Text statusText;
    [SerializeField] private Text walletLabel;
    [SerializeField] private GameObject loadingIndicator;
    //[SerializeField] private GameObject emptyStateRoot;

    [Header("Buy guardrails")]
    [Tooltip("Extra room above the quoted price the player tolerates between " +
             "quote-time and execution-time. 500 bps = 5%.")]
    [SerializeField, Range(0, 5000)] private int buySlippageBps = 500;
    [Tooltip("Auto-refresh listings every N seconds while the panel is open. 0 disables auto-refresh.")]
    [SerializeField, Min(0f)] private float autoRefreshInterval = 0f;

    private readonly List<NpcMarketplaceRow> spawnedRows = new List<NpcMarketplaceRow>();
    private CancellationTokenSource lifetimeCts;
    private bool refreshing;
    private bool buying;
    private float nextAutoRefreshTime;

    private void Awake()
    {
        lifetimeCts = new CancellationTokenSource();

        if (openPanelButton != null)
        {
            openPanelButton.onClick.RemoveAllListeners();
            openPanelButton.onClick.AddListener(OpenPanel);
        }

        if (closePanelButton != null)
        {
            closePanelButton.onClick.RemoveAllListeners();
            closePanelButton.onClick.AddListener(ClosePanel);
        }

        if (refreshButton != null)
        {
            refreshButton.onClick.RemoveAllListeners();
            refreshButton.onClick.AddListener(() => _ = RefreshAsync());
        }

        if (panelRoot != null) panelRoot.SetActive(false);
        SetLoading(false);
        UpdateWalletLabel();
    }

    private void OnEnable()
    {
        if (WalletLoginService.Instance != null)
            WalletLoginService.Instance.OnLoginSucceeded += OnLoginSucceeded;
    }

    private void OnDisable()
    {
        if (WalletLoginService.Instance != null)
            WalletLoginService.Instance.OnLoginSucceeded -= OnLoginSucceeded;
    }

    private void OnDestroy()
    {
        try { lifetimeCts?.Cancel(); } catch { /* ignored */ }
        lifetimeCts?.Dispose();
        lifetimeCts = null;
    }

    private void Update()
    {
        if (autoRefreshInterval <= 0f) return;
        if (panelRoot == null || !panelRoot.activeSelf) return;
        if (Time.unscaledTime < nextAutoRefreshTime) return;

        nextAutoRefreshTime = Time.unscaledTime + autoRefreshInterval;
        if (!refreshing && !buying) _ = RefreshAsync();
    }

    public void OpenPanel()
    {
        if (panelRoot != null) panelRoot.SetActive(true);
        UpdateWalletLabel();
        _ = RefreshAsync();
    }

    public void ClosePanel()
    {
        if (panelRoot != null) panelRoot.SetActive(false);
    }

    private void OnLoginSucceeded(WalletSession _)
    {
        UpdateWalletLabel();
        if (panelRoot != null && panelRoot.activeSelf) RefreshAsync();
    }

    private void UpdateWalletLabel()
    {
        if (walletLabel == null) return;
        var login = WalletLoginService.Instance;
        if (login != null && login.HasSession && login.Current != null)
            walletLabel.text = $"Wallet: {ShortAddress(login.Current.wallet)}";
        else
            walletLabel.text = "Wallet: (not signed in)";
    }

    private async Task RefreshAsync()
    {
        if (refreshing) return;
        if (marketplaceClient == null)
        {
            SetStatus("marketplaceClient reference missing.", isError: true);
            return;
        }

        refreshing = true;
        SetLoading(true);
        SetStatus("Loading listings…", isError: false);

        try
        {
            var listings = await marketplaceClient.EnumerateActiveListingsAsync(lifetimeCts.Token);
            RenderListings(listings);
            SetStatus(listings.Count == 0 ? "No active listings." : $"{listings.Count} listing(s).", isError: false);
            //if (emptyStateRoot != null) emptyStateRoot.SetActive(listings.Count == 0);
        }
        catch (OperationCanceledException)
        {
            /* destroyed mid-refresh */
        }
        catch (Exception ex)
        {
            Debug.LogError($"[NpcMarketplaceMenu] refresh failed: {ex}");
            SetStatus($"Refresh failed: {ex.Message}", isError: true);
        }
        finally
        {
            refreshing = false;
            SetLoading(false);
        }
    }

    private void RenderListings(List<NpcListingInfo> listings)
    {
        if (rowContainer == null || rowPrefab == null)
        {
            Debug.LogError("[NpcMarketplaceMenu] rowContainer / rowPrefab not assigned.");
            return;
        }

        for (int i = 0; i < spawnedRows.Count; i++)
        {
            if (spawnedRows[i] != null) Destroy(spawnedRows[i].gameObject);
        }
        spawnedRows.Clear();

        for (int i = 0; i < listings.Count; i++)
        {
            var row = Instantiate(rowPrefab, rowContainer);
            row.gameObject.SetActive(true);
            row.Bind(listings[i], OnBuyClicked);
            spawnedRows.Add(row);
        }
    }

    private async void OnBuyClicked(NpcListingInfo listing)
    {
        if (buying)
        {
            SetStatus("A purchase is already in progress.", isError: true);
            return;
        }
        if (listing == null) return;

        buying = true;
        SetRowsInteractable(false);
        try
        {
            await BuyAsync(listing);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[NpcMarketplaceMenu] buy failed: {ex}");
            SetStatus($"Buy failed: {ex.Message}", isError: true);
        }
        finally
        {
            buying = false;
            SetRowsInteractable(true);
        }
    }

    private async Task BuyAsync(NpcListingInfo listing)
    {
        SetStatus($"Quoting price for token #{listing.TokenId}…", isError: false);

        // Re-quote at click time — the AMM price moves with TBA value + listed supply.
        BigInteger livePrice;
        if (pricingClient != null)
        {
            var quote = await pricingClient.QuoteNpcPriceAsync(listing.TokenId);
            livePrice = quote.Price;
        }
        else
        {
            livePrice = listing.QuotedPrice;
        }

        // Cap = livePrice * (1 + slippage). Buyer pays at most this.
        BigInteger cap = livePrice + livePrice * buySlippageBps / 10_000;
        if (cap < livePrice) cap = livePrice;

        SetStatus(
            $"Submitting buyNpc(#{listing.TokenId}) — live {ToUsdc(livePrice):0.######} USDC, cap {ToUsdc(cap):0.######}. " +
            "Approve in MetaMask…",
            isError: false);

        var txHash = await marketplaceClient.BuyNpcAsync(listing.TokenId, cap, lifetimeCts.Token);
        SetStatus($"Bought NPC #{listing.TokenId}. tx: {txHash}", isError: false);
        Debug.Log($"[NpcMarketplaceMenu] buyNpc({listing.TokenId}) tx={txHash}");

        await RefreshAsync();
    }

    private void SetRowsInteractable(bool interactable)
    {
        for (int i = 0; i < spawnedRows.Count; i++)
        {
            if (spawnedRows[i] != null) spawnedRows[i].SetInteractable(interactable);
        }
        if (refreshButton != null) refreshButton.interactable = interactable;
    }

    private void SetLoading(bool loading)
    {
        if (loadingIndicator != null) loadingIndicator.SetActive(loading);
        if (refreshButton != null) refreshButton.interactable = !loading;
    }

    private void SetStatus(string message, bool isError)
    {
        if (statusText == null) return;
        statusText.text = message;
        statusText.color = isError ? new Color(1f, 0.4f, 0.4f, 1f) : new Color(0.9f, 0.95f, 0.95f, 1f);
    }

    private static decimal ToUsdc(BigInteger raw)
    {
        return (decimal)raw / 1_000_000m;
    }

    private static string ShortAddress(string addr)
    {
        if (string.IsNullOrEmpty(addr) || addr.Length < 12) return addr ?? "";
        return addr.Substring(0, 6) + "…" + addr.Substring(addr.Length - 4);
    }
}
