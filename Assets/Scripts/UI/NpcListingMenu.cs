using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using ArcTrading.Auth;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// "List my NPCs for sale" menu — the seller-side counterpart of
/// NpcMarketplaceMenu.
///
/// List flow:
///   1. Reuse / wait for the SIWE wallet session (WalletLoginService).
///   2. Enumerate NPCs owned by the signed-in wallet via
///      NpcMarketplaceClient.EnumerateOwnedForSaleAsync — each row carries
///      the on-chain quote, TBA value, and current listing state.
///   3. On "List for Sell" click: read minPrice from the row's input field
///      (USDC decimal → 6-decimal raw), submit listNpc via the bridge.
///      NpcMarketplaceClient.ListNpcAsync handles the one-time
///      setApprovalForAll on NpcCharacter.
///   4. On "Cancel Listing" click (rows already listed): submit
///      cancelListing via the bridge.
/// </summary>
public class NpcListingMenu : MonoBehaviour
{
    [Header("Contracts")]
    [SerializeField] private NpcMarketplaceClient marketplaceClient;
    [SerializeField] private NpcCharacterContractClient npcCharacter;

    [Header("Panel")]
    [Tooltip("Root GameObject of the list-my-NPCs panel. Toggled by Open/Close.")]
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private GameObject buyPanelRoot;
    [SerializeField] private Button openPanelButton;
    [SerializeField] private Button closePanelButton;
    [SerializeField] private Button openBuyPanelButton;
    [SerializeField] private Button refreshButton;

    [Header("Owned list")]
    [Tooltip("Parent transform of the row prefab instances. " +
             "Should have a VerticalLayoutGroup + ContentSizeFitter on the scroll-view content.")]
    [SerializeField] private RectTransform rowContainer;
    [SerializeField] private NpcListingRow rowPrefab;

    [Header("Status / feedback")]
    [SerializeField] private Text statusText;
    [SerializeField] private Text walletLabel;
    [SerializeField] private GameObject loadingIndicator;

    [Header("Auto-refresh")]
    [Tooltip("Auto-refresh owned NPCs every N seconds while the panel is open. 0 disables auto-refresh.")]
    [SerializeField, Min(0f)] private float autoRefreshInterval = 0f;

    private readonly List<NpcListingRow> spawnedRows = new List<NpcListingRow>();
    private CancellationTokenSource lifetimeCts;
    private bool refreshing;
    private bool submitting;
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

        if (openBuyPanelButton != null)
        {
            openBuyPanelButton.onClick.RemoveAllListeners();
            openBuyPanelButton.onClick.AddListener(OpenBuyPanel);
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
        if (!refreshing && !submitting) _ = RefreshAsync();
    }

    public void OpenPanel()
    {
        if (panelRoot != null) panelRoot.SetActive(true);
        if(buyPanelRoot != null) buyPanelRoot.SetActive(false);
        UpdateWalletLabel();
        _ = RefreshAsync();
    }

    public void ClosePanel()
    {
        if (panelRoot != null) panelRoot.SetActive(false);
        if(buyPanelRoot != null) buyPanelRoot.SetActive(false);
    }

    public void OpenBuyPanel()
    {
        if(panelRoot != null) panelRoot.SetActive(false);
        GetComponent<NpcMarketplaceMenu>()?.OpenPanel();
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

        var login = WalletLoginService.Instance;
        if (login == null || !login.HasSession || login.Current == null
            || string.IsNullOrWhiteSpace(login.Current.wallet))
        {
            ClearRows();
            SetStatus("Sign in to view your NPCs.", isError: true);
            return;
        }

        refreshing = true;
        SetLoading(true);
        SetStatus("Loading your NPCs…", isError: false);

        try
        {
            var owned = await marketplaceClient.EnumerateOwnedForSaleAsync(
                login.Current.wallet, lifetimeCts.Token);
            RenderRows(owned);
            SetStatus(
                owned.Count == 0 ? "You don't own any NPCs yet." : $"{owned.Count} NPC(s) owned.",
                isError: false);
        }
        catch (OperationCanceledException)
        {
            /* destroyed mid-refresh */
        }
        catch (Exception ex)
        {
            Debug.LogError($"[NpcListingMenu] refresh failed: {ex}");
            SetStatus($"Refresh failed: {ex.Message}", isError: true);
        }
        finally
        {
            refreshing = false;
            SetLoading(false);
        }
    }

    private void RenderRows(List<OwnedNpcSellableInfo> owned)
    {
        if (rowContainer == null || rowPrefab == null)
        {
            Debug.LogError("[NpcListingMenu] rowContainer / rowPrefab not assigned.");
            return;
        }

        ClearRows();

        for (int i = 0; i < owned.Count; i++)
        {
            var row = Instantiate(rowPrefab, rowContainer);
            row.gameObject.SetActive(true);
            row.Bind(owned[i], OnListClicked, OnCancelClicked);
            spawnedRows.Add(row);
        }
    }

    private void ClearRows()
    {
        for (int i = 0; i < spawnedRows.Count; i++)
        {
            if (spawnedRows[i] != null) Destroy(spawnedRows[i].gameObject);
        }
        spawnedRows.Clear();
    }

    private async void OnListClicked(OwnedNpcSellableInfo info, BigInteger minPriceRaw)
    {
        if (submitting)
        {
            SetStatus("A listing action is already in progress.", isError: true);
            return;
        }
        if (info == null) return;

        submitting = true;
        SetRowsInteractable(false);
        try
        {
            await ListAsync(info, minPriceRaw);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[NpcListingMenu] list failed: {ex}");
            SetStatus($"List failed: {ex.Message}", isError: true);
        }
        finally
        {
            submitting = false;
            SetRowsInteractable(true);
        }
    }

    private async void OnCancelClicked(OwnedNpcSellableInfo info)
    {
        if (submitting)
        {
            SetStatus("A listing action is already in progress.", isError: true);
            return;
        }
        if (info == null) return;

        submitting = true;
        SetRowsInteractable(false);
        try
        {
            await CancelAsync(info);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[NpcListingMenu] cancel failed: {ex}");
            SetStatus($"Cancel failed: {ex.Message}", isError: true);
        }
        finally
        {
            submitting = false;
            SetRowsInteractable(true);
        }
    }

    private async Task ListAsync(OwnedNpcSellableInfo info, BigInteger minPriceRaw)
    {
        SetStatus(
            $"Submitting listNpc(#{info.TokenId}) — minPrice {ToUsdc(minPriceRaw):0.######} USDC. " +
            "Approve in MetaMask (first listing also asks for NFT approval)…",
            isError: false);

        var txHash = await marketplaceClient.ListNpcAsync(
            info.TokenId, minPriceRaw, lifetimeCts.Token);
        SetStatus($"Listed NPC #{info.TokenId}. tx: {txHash}", isError: false);
        Debug.Log($"[NpcListingMenu] listNpc({info.TokenId}, {minPriceRaw}) tx={txHash}");

        await RefreshAsync();
    }

    private async Task CancelAsync(OwnedNpcSellableInfo info)
    {
        SetStatus($"Submitting cancelListing(#{info.TokenId}) — approve in MetaMask…", isError: false);

        var txHash = await marketplaceClient.CancelListingAsync(info.TokenId, lifetimeCts.Token);
        SetStatus($"Cancelled listing for NPC #{info.TokenId}. tx: {txHash}", isError: false);
        Debug.Log($"[NpcListingMenu] cancelListing({info.TokenId}) tx={txHash}");

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
