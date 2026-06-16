using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using ArcTrading.Nanopayment;
using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Hex.HexConvertors.Extensions;
using Nethereum.Hex.HexTypes;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.Web3;
using Nethereum.Web3.Accounts;
using UnityEngine;

public class ArcTradingContractClient : MonoBehaviour
{
    [SerializeField] private string rpcUrl = "https://rpc.testnet.arc.network";
    public string RpcUrl => rpcUrl;
    // GamePayment contract address
    [SerializeField] private string contractAddress = "0xc7C9BBCe60802c94AfB7e224e98928A4Ee0de158";
    public string ContractAddress => contractAddress;
    [SerializeField] private string privateKey;
    [SerializeField] private float initialUsdcCapital = 0.5f;
    public string PrivateKey => privateKey;
    
    [Header("NPC NFT Identity")]
    [SerializeField] private ulong nftTokenId;
    [SerializeField] private NpcPaymentWalletService npcPaymentWalletService;

    // Resolved from chain via GamePayment.npcTba(tokenId) — see EnsureTbaAddressAsync.
    // The x402 server mints loot NFTs to this address (validating it on-chain), so the NPC's
    // canonical TBA — not the operator wallet — holds the inventory.
    private string cachedTbaAddress;
    public string TbaAddress => cachedTbaAddress ?? string.Empty;

    [Tooltip("If true, ignore the privateKey field above and lazy-resolve the trader signing key " +
             "from the on-chain bound payment wallet. Demo-friendly (no manual key creation) but " +
             "expands operator-key leak blast radius and abandons funds at the old address on rebind/transfer.")]
    [SerializeField] private bool useBoundWalletAsTrader;

    public BigInteger NftTokenId => new BigInteger(nftTokenId);
    public NpcPaymentWalletService NpcPaymentWalletService => npcPaymentWalletService;

    private string cachedTraderPrivateKey;
    private ulong  cachedTraderKeyVersion;

    private const string Abi = @"[
      {""inputs"":[{""internalType"":""address"",""name"":""_usdc"",""type"":""address""},{""internalType"":""address"",""name"":""_items"",""type"":""address""},{""internalType"":""address"",""name"":""_gateway"",""type"":""address""},{""internalType"":""address"",""name"":""_manager"",""type"":""address""}],""stateMutability"":""nonpayable"",""type"":""constructor""},
      {""inputs"":[],""name"":""BASELINE_PRICE"",""outputs"":[{""internalType"":""uint256"",""name"":"""",""type"":""uint256""}],""stateMutability"":""view"",""type"":""function""},
      {""inputs"":[],""name"":""PRICE_SLOPE"",""outputs"":[{""internalType"":""uint256"",""name"":"""",""type"":""uint256""}],""stateMutability"":""view"",""type"":""function""},
      {""inputs"":[],""name"":""SELL_SPREAD_BPS"",""outputs"":[{""internalType"":""uint256"",""name"":"""",""type"":""uint256""}],""stateMutability"":""view"",""type"":""function""},
      {""inputs"":[],""name"":""BPS_DENOMINATOR"",""outputs"":[{""internalType"":""uint256"",""name"":"""",""type"":""uint256""}],""stateMutability"":""view"",""type"":""function""},
      {""inputs"":[],""name"":""NUM_TYPES"",""outputs"":[{""internalType"":""uint256"",""name"":"""",""type"":""uint256""}],""stateMutability"":""view"",""type"":""function""},
      {""inputs"":[],""name"":""usdc"",""outputs"":[{""internalType"":""contract IERC20"",""name"":"""",""type"":""address""}],""stateMutability"":""view"",""type"":""function""},
      {""inputs"":[],""name"":""items"",""outputs"":[{""internalType"":""address"",""name"":"""",""type"":""address""}],""stateMutability"":""view"",""type"":""function""},
      {""inputs"":[],""name"":""owner"",""outputs"":[{""internalType"":""address"",""name"":"""",""type"":""address""}],""stateMutability"":""view"",""type"":""function""},
      {""inputs"":[],""name"":""gateway"",""outputs"":[{""internalType"":""contract IGatewayWallet"",""name"":"""",""type"":""address""}],""stateMutability"":""view"",""type"":""function""},
      {""inputs"":[{""internalType"":""uint256"",""name"":""tokenId"",""type"":""uint256""}],""name"":""npcTba"",""outputs"":[{""internalType"":""address"",""name"":"""",""type"":""address""}],""stateMutability"":""view"",""type"":""function""},
      {""inputs"":[{""internalType"":""uint256"",""name"":"""",""type"":""uint256""}],""name"":""itemIds"",""outputs"":[{""internalType"":""uint256"",""name"":"""",""type"":""uint256""}],""stateMutability"":""view"",""type"":""function""},
      {""inputs"":[{""internalType"":""uint256"",""name"":"""",""type"":""uint256""}],""name"":""circulatingSupply"",""outputs"":[{""internalType"":""uint256"",""name"":"""",""type"":""uint256""}],""stateMutability"":""view"",""type"":""function""},
      {""inputs"":[],""name"":""activeTypeCount"",""outputs"":[{""internalType"":""uint256"",""name"":"""",""type"":""uint256""}],""stateMutability"":""view"",""type"":""function""},
      {""inputs"":[{""internalType"":""address"",""name"":""newOwner"",""type"":""address""}],""name"":""transferOwnership"",""outputs"":[],""stateMutability"":""nonpayable"",""type"":""function""},
      {""inputs"":[{""internalType"":""uint256"",""name"":""maxPriceAllowed"",""type"":""uint256""}],""name"":""mintRandom"",""outputs"":[{""internalType"":""uint256"",""name"":""id"",""type"":""uint256""}],""stateMutability"":""nonpayable"",""type"":""function""},
      {""inputs"":[{""internalType"":""address"",""name"":""to"",""type"":""address""}],""name"":""mintRandomX402"",""outputs"":[{""internalType"":""uint256"",""name"":""id"",""type"":""uint256""}],""stateMutability"":""nonpayable"",""type"":""function""},
      {""inputs"":[{""internalType"":""address"",""name"":""to"",""type"":""address""},{""internalType"":""uint256"",""name"":""id"",""type"":""uint256""},{""internalType"":""uint256"",""name"":""paidAmount"",""type"":""uint256""},{""internalType"":""uint256"",""name"":""maxPriceAllowed"",""type"":""uint256""}],""name"":""buyItemX402"",""outputs"":[{""internalType"":""uint256"",""name"":""price"",""type"":""uint256""}],""stateMutability"":""nonpayable"",""type"":""function""},
      {""inputs"":[{""internalType"":""uint256"",""name"":""id"",""type"":""uint256""}],""name"":""sellItem"",""outputs"":[{""internalType"":""uint256"",""name"":""price"",""type"":""uint256""}],""stateMutability"":""nonpayable"",""type"":""function""},
      {""inputs"":[{""internalType"":""uint256"",""name"":""id"",""type"":""uint256""}],""name"":""getBuyPrice"",""outputs"":[{""internalType"":""uint256"",""name"":"""",""type"":""uint256""}],""stateMutability"":""view"",""type"":""function""},
      {""inputs"":[{""internalType"":""uint256"",""name"":""id"",""type"":""uint256""}],""name"":""getSellPrice"",""outputs"":[{""internalType"":""uint256"",""name"":"""",""type"":""uint256""}],""stateMutability"":""view"",""type"":""function""},
      {""inputs"":[],""name"":""getContractBalance"",""outputs"":[{""internalType"":""uint256"",""name"":"""",""type"":""uint256""}],""stateMutability"":""view"",""type"":""function""},
      {""inputs"":[{""internalType"":""address"",""name"":""_gateway"",""type"":""address""}],""name"":""setGateway"",""outputs"":[],""stateMutability"":""nonpayable"",""type"":""function""},
      {""inputs"":[{""internalType"":""uint256"",""name"":""amount"",""type"":""uint256""}],""name"":""depositToGateway"",""outputs"":[],""stateMutability"":""nonpayable"",""type"":""function""},
      {""inputs"":[{""internalType"":""uint256"",""name"":""amount"",""type"":""uint256""}],""name"":""initiateGatewayWithdrawal"",""outputs"":[],""stateMutability"":""nonpayable"",""type"":""function""},
      {""inputs"":[],""name"":""completeGatewayWithdrawal"",""outputs"":[],""stateMutability"":""nonpayable"",""type"":""function""},
      {""inputs"":[{""internalType"":""address"",""name"":""delegate"",""type"":""address""}],""name"":""addGatewayDelegate"",""outputs"":[],""stateMutability"":""nonpayable"",""type"":""function""},
      {""inputs"":[{""internalType"":""address"",""name"":""delegate"",""type"":""address""}],""name"":""removeGatewayDelegate"",""outputs"":[],""stateMutability"":""nonpayable"",""type"":""function""},
      {""inputs"":[],""name"":""gatewayAvailableBalance"",""outputs"":[{""internalType"":""uint256"",""name"":"""",""type"":""uint256""}],""stateMutability"":""view"",""type"":""function""},
      {""inputs"":[],""name"":""gatewayWithdrawableBalance"",""outputs"":[{""internalType"":""uint256"",""name"":"""",""type"":""uint256""}],""stateMutability"":""view"",""type"":""function""},
      {""inputs"":[],""name"":""gatewayWithdrawingBalance"",""outputs"":[{""internalType"":""uint256"",""name"":"""",""type"":""uint256""}],""stateMutability"":""view"",""type"":""function""},
      {""inputs"":[],""name"":""gatewayTotalBalance"",""outputs"":[{""internalType"":""uint256"",""name"":"""",""type"":""uint256""}],""stateMutability"":""view"",""type"":""function""},
      {""inputs"":[],""name"":""gatewayWithdrawalBlock"",""outputs"":[{""internalType"":""uint256"",""name"":"""",""type"":""uint256""}],""stateMutability"":""view"",""type"":""function""},
      {""inputs"":[],""name"":""gatewayWithdrawalDelay"",""outputs"":[{""internalType"":""uint256"",""name"":"""",""type"":""uint256""}],""stateMutability"":""view"",""type"":""function""},
      {""inputs"":[{""internalType"":""address"",""name"":""addr"",""type"":""address""}],""name"":""isGatewayAuthorized"",""outputs"":[{""internalType"":""bool"",""name"":"""",""type"":""bool""}],""stateMutability"":""view"",""type"":""function""},
      {""inputs"":[],""name"":""isGatewayTokenSupported"",""outputs"":[{""internalType"":""bool"",""name"":"""",""type"":""bool""}],""stateMutability"":""view"",""type"":""function""},
      {""inputs"":[],""name"":""manager"",""outputs"":[{""internalType"":""contract Npc6551Manager"",""name"":"""",""type"":""address""}],""stateMutability"":""view"",""type"":""function""},
      {""inputs"":[{""internalType"":""address"",""name"":""_manager"",""type"":""address""}],""name"":""setManager"",""outputs"":[],""stateMutability"":""nonpayable"",""type"":""function""},
      {""inputs"":[],""name"":""getItemIds"",""outputs"":[{""internalType"":""uint256[5]"",""name"":"""",""type"":""uint256[5]""}],""stateMutability"":""view"",""type"":""function""},
      {""inputs"":[],""name"":""getAllBuyPrices"",""outputs"":[{""internalType"":""uint256[5]"",""name"":""prices"",""type"":""uint256[5]""}],""stateMutability"":""view"",""type"":""function""},
      {""inputs"":[],""name"":""getAllSellPrices"",""outputs"":[{""internalType"":""uint256[5]"",""name"":""prices"",""type"":""uint256[5]""}],""stateMutability"":""view"",""type"":""function""},
      {""inputs"":[{""internalType"":""address"",""name"":""tba"",""type"":""address""}],""name"":""getTbaItemBalances"",""outputs"":[{""internalType"":""uint256[5]"",""name"":""ids"",""type"":""uint256[5]""},{""internalType"":""uint256[5]"",""name"":""balances"",""type"":""uint256[5]""}],""stateMutability"":""view"",""type"":""function""},
      {""inputs"":[{""internalType"":""address"",""name"":""tba"",""type"":""address""}],""name"":""getTbaOwnedItems"",""outputs"":[{""internalType"":""uint256[]"",""name"":""ids"",""type"":""uint256[]""},{""internalType"":""uint256[]"",""name"":""balances"",""type"":""uint256[]""}],""stateMutability"":""view"",""type"":""function""},
      {""inputs"":[{""internalType"":""uint256"",""name"":""tokenId"",""type"":""uint256""}],""name"":""getNpcTbaItemBalances"",""outputs"":[{""internalType"":""address"",""name"":""tba"",""type"":""address""},{""internalType"":""uint256[5]"",""name"":""ids"",""type"":""uint256[5]""},{""internalType"":""uint256[5]"",""name"":""balances"",""type"":""uint256[5]""}],""stateMutability"":""view"",""type"":""function""},
      {""inputs"":[{""internalType"":""uint256"",""name"":""tokenId"",""type"":""uint256""}],""name"":""getNpcTbaOwnedItems"",""outputs"":[{""internalType"":""address"",""name"":""tba"",""type"":""address""},{""internalType"":""uint256[]"",""name"":""ids"",""type"":""uint256[]""},{""internalType"":""uint256[]"",""name"":""balances"",""type"":""uint256[]""}],""stateMutability"":""view"",""type"":""function""}
    ]";

    // Minimal ERC1155 ABI — only what we need to read NPC's GameItems inventory.
    private const string Erc1155Abi = @"[
      {""inputs"":[{""internalType"":""address"",""name"":""account"",""type"":""address""},{""internalType"":""uint256"",""name"":""id"",""type"":""uint256""}],""name"":""balanceOf"",""outputs"":[{""internalType"":""uint256"",""name"":"""",""type"":""uint256""}],""stateMutability"":""view"",""type"":""function""},
      {""inputs"":[{""internalType"":""address"",""name"":""operator"",""type"":""address""},{""internalType"":""bool"",""name"":""approved"",""type"":""bool""}],""name"":""setApprovalForAll"",""outputs"":[],""stateMutability"":""nonpayable"",""type"":""function""},
      {""inputs"":[{""internalType"":""address"",""name"":""account"",""type"":""address""},{""internalType"":""address"",""name"":""operator"",""type"":""address""}],""name"":""isApprovedForAll"",""outputs"":[{""internalType"":""bool"",""name"":"""",""type"":""bool""}],""stateMutability"":""view"",""type"":""function""}
    ]";

    private const string Erc20WriteAbi = @"[
      {""constant"":false,""inputs"":[{""name"":""spender"",""type"":""address""},{""name"":""amount"",""type"":""uint256""}],""name"":""approve"",""outputs"":[{""name"":"""",""type"":""bool""}],""type"":""function""},
      {""constant"":false,""inputs"":[{""name"":""to"",""type"":""address""},{""name"":""amount"",""type"":""uint256""}],""name"":""transfer"",""outputs"":[{""name"":"""",""type"":""bool""}],""type"":""function""}
    ]";

    // ERC-6551 TBA execute(). Operator can call this after NpcCharacter binding is
    // honored by the account implementation's _isValidSigner.
    private const string Erc6551AccountAbi = @"[
      {""inputs"":[{""internalType"":""address"",""name"":""to"",""type"":""address""},{""internalType"":""uint256"",""name"":""value"",""type"":""uint256""},{""internalType"":""bytes"",""name"":""data"",""type"":""bytes""},{""internalType"":""uint8"",""name"":""operation"",""type"":""uint8""}],""name"":""execute"",""outputs"":[{""internalType"":""bytes"",""name"":"""",""type"":""bytes""}],""stateMutability"":""payable"",""type"":""function""}
    ]";

    private const int ItemTypeCount = 5;

    private Web3 readOnlyWeb3;
    private string cachedItemsAddress;
    private BigInteger[] cachedItemIds;
    
    private decimal lastKnownOnchainGatewayUsdc;
    private decimal pendingX402OutflowUsdc;
    private bool gatewayBaselineInitialized;

    public string WalletAddress { get; private set; }

    private void Awake()
    {
        readOnlyWeb3 = new Web3(rpcUrl);
#if UNITY_WEBGL && !UNITY_EDITOR
        ArcTrading.Crypto.EthRawTxSender.ConfigureRpc(rpcUrl);
#endif
    }

    public async Task InitializeWalletAsync()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        var signer = await GetTraderSignerAsync();
        WalletAddress = signer.address;
#else
        var web3 = await CreateSignedWeb3Async();
        WalletAddress = web3.TransactionManager.Account.Address;
#endif
    }

    /// <summary>
    /// Set the on-chain NPC NFT tokenId at runtime (used by OwnedNpcSpawner when
    /// instantiating prefabs from chain-discovered NPCs). MUST be called before
    /// Start() / InitializeWalletAsync / EnsurePaymentWalletBoundAsync — i.e. while
    /// the instance is still under an inactive parent.
    /// </summary>
    public void SetNftTokenIdForRuntime(ulong tokenId)
    {
        nftTokenId = tokenId;
        cachedTbaAddress = null;
        cachedTraderPrivateKey = null;
        cachedTraderKeyVersion = 0;
    }

    /// <summary>
    /// Wire up the shared scene-level NpcPaymentWalletService onto this runtime
    /// instance (prefab's serialized reference is null because the dependency
    /// lives in the scene, not in the prefab asset).
    /// </summary>
    public void SetNpcPaymentWalletServiceForRuntime(NpcPaymentWalletService service)
    {
        npcPaymentWalletService = service;
    }
    
    public async Task<NpcPaymentSigner?> EnsurePaymentWalletBoundAsync()
    {
        if (npcPaymentWalletService == null || nftTokenId == 0) return null;
        var signer = await npcPaymentWalletService.EnsureBoundOrRebindAsync(NftTokenId);
        if (useBoundWalletAsTrader) CacheBoundSigner(signer);
        await EnsureTbaAddressAsync();
        return signer;
    }
    
    public async Task<string> EnsureTbaAddressAsync()
    {
        if (!string.IsNullOrEmpty(cachedTbaAddress)) return cachedTbaAddress;
        if (nftTokenId == 0)
            throw new InvalidOperationException(
                $"{name}: nftTokenId is 0 — cannot resolve TBA on chain.");

#if UNITY_WEBGL && !UNITY_EDITOR
        var tba = await ArcTrading.WebGL.WebGLChainApi.GetNpcTbaAsync(NftTokenId);
#else
        var contract = readOnlyWeb3.Eth.GetContract(Abi, contractAddress);
        var tba = await contract.GetFunction("npcTba").CallAsync<string>(NftTokenId);
#endif
        Debug.Log($"[{name}] Resolved TBA address {tba} for NPC tokenId {nftTokenId} via GamePayment.npcTba.");
        if (string.IsNullOrWhiteSpace(tba) || IsZeroAddress(tba))
            throw new InvalidOperationException(
                $"{name}: GamePayment.npcTba({nftTokenId}) returned 0x0 — the contract's " +
                "Npc6551Manager is not set or this tokenId has no deployed TBA.");
        cachedTbaAddress = tba;
        return cachedTbaAddress;
    }

    public void InvalidateBoundTraderCache()
    {
        cachedTraderPrivateKey = null;
        cachedTraderKeyVersion = 0;
    }

    private void CacheBoundSigner(NpcPaymentSigner signer)
    {
        var firstResolve = string.IsNullOrEmpty(cachedTraderPrivateKey);
        cachedTraderPrivateKey = signer.PrivateKey;
        privateKey = cachedTraderPrivateKey;
        cachedTraderKeyVersion = signer.Version;
        if (firstResolve)
        {
            Debug.Log($"[{name}] useBoundWalletAsTrader=true → trader wallet is {signer.Address}.");
        }
    }

    public async Task<decimal> GetWalletBalanceUSDCAsync()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        var signer = await GetTraderSignerAsync();
        var balanceWebgl = await ArcTrading.WebGL.WebGLChainApi.GetUsdcBalanceAsync(signer.address);
        return FromUsdc(balanceWebgl);
#else
        var web3 = await CreateSignedWeb3Async();
        var owner = web3.TransactionManager.Account.Address;
        var balance = await Erc20UsdcHelper.GetBalanceAsync(web3, owner);
        return FromUsdc(balance);
#endif
    }
    
    public async Task<decimal> GetWalletBalanceUSDCAsync(string account)
    {
        if (string.IsNullOrWhiteSpace(account)) return 0m;
#if UNITY_WEBGL && !UNITY_EDITOR
        var balance = await ArcTrading.WebGL.WebGLChainApi.GetUsdcBalanceAsync(account);
#else
        var balance = await Erc20UsdcHelper.GetBalanceAsync(readOnlyWeb3, account);
#endif
        return FromUsdc(balance);
    }

    /// <summary>
    /// Vault value = NPC's NFT inventory(TBA) marked to the contract's buyback price,
    /// i.e. Σ over the 5 managed item types of: balanceOf(tba, id) × getSellPrice(id).
    /// </summary>
    public async Task<decimal> GetVaultBalanceUSDCAsync(string account)
    {
        if (string.IsNullOrWhiteSpace(account)) return 0m;

#if UNITY_WEBGL && !UNITY_EDITOR
        var items = await ArcTrading.WebGL.WebGLChainApi.GetTbaItemBalancesAsync(account);
        var sellPricesArr = await ArcTrading.WebGL.WebGLChainApi.GetAllSellPricesRawAsync();
        BigInteger totalUnits = BigInteger.Zero;
        if (items?.balances != null)
        {
            int n = Math.Min(items.balances.Length, sellPricesArr.Length);
            for (int i = 0; i < n; i++)
            {
                if (string.IsNullOrEmpty(items.balances[i])) continue;
                var bal = BigInteger.Parse(items.balances[i]);
                if (bal == BigInteger.Zero) continue;
                totalUnits += bal * sellPricesArr[i];
            }
        }
        return FromUsdc(totalUnits);
#else
        var contract = readOnlyWeb3.Eth.GetContract(Abi, contractAddress);

        var balances = await contract.GetFunction("getTbaItemBalances")
            .CallDeserializingToObjectAsync<GetTbaItemBalancesOutputDTO>(account);
        var sellPrices = await contract.GetFunction("getAllSellPrices").CallAsync<List<BigInteger>>();

        BigInteger totalUnits = BigInteger.Zero;
        for (int i = 0; i < balances.Balances.Count; i++)
        {
            if (balances.Balances[i] == BigInteger.Zero) continue;
            totalUnits += balances.Balances[i] * sellPrices[i];
        }
        return FromUsdc(totalUnits);
#endif
    }

    // ---- Item / NFT queries ----

    public async Task<string> GetItemsAddressAsync()
    {
        if (!string.IsNullOrEmpty(cachedItemsAddress)) return cachedItemsAddress;
#if UNITY_WEBGL && !UNITY_EDITOR
        cachedItemsAddress = await ArcTrading.WebGL.WebGLChainApi.GetItemsAddressAsync();
#else
        var contract = readOnlyWeb3.Eth.GetContract(Abi, contractAddress);
        cachedItemsAddress = await contract.GetFunction("items").CallAsync<string>();
#endif
        return cachedItemsAddress;
    }

    public async Task<BigInteger[]> GetItemIdsAsync()
    {
        if (cachedItemIds != null) return cachedItemIds;
#if UNITY_WEBGL && !UNITY_EDITOR
        cachedItemIds = await ArcTrading.WebGL.WebGLChainApi.GetItemIdsAsync();
#else
        var contract = readOnlyWeb3.Eth.GetContract(Abi, contractAddress);
        var ids = await contract.GetFunction("getItemIds").CallAsync<List<BigInteger>>();
        cachedItemIds = ids.ToArray();
#endif
        return cachedItemIds;
    }

    public async Task<decimal> GetSellPriceUSDCAsync(BigInteger itemId)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        var price = await ArcTrading.WebGL.WebGLChainApi.GetSellPriceAsync(itemId);
#else
        var contract = readOnlyWeb3.Eth.GetContract(Abi, contractAddress);
        var price = await contract.GetFunction("getSellPrice").CallAsync<BigInteger>(itemId);
#endif
        return FromUsdc(price);
    }

    public async Task<decimal[]> GetAllSellPricesUSDCAsync()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        var raw = await ArcTrading.WebGL.WebGLChainApi.GetAllSellPricesRawAsync();
        var prices = new decimal[raw.Length];
        for (int i = 0; i < raw.Length; i++) prices[i] = FromUsdc(raw[i]);
        return prices;
#else
        var contract = readOnlyWeb3.Eth.GetContract(Abi, contractAddress);
        var raw = await contract.GetFunction("getAllSellPrices").CallAsync<List<BigInteger>>();
        var prices = new decimal[raw.Count];
        for (int i = 0; i < raw.Count; i++) prices[i] = FromUsdc(raw[i]);
        return prices;
#endif
    }

    public async Task<BigInteger[]> GetAllBuyPricesRawAsync()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        return await ArcTrading.WebGL.WebGLChainApi.GetAllBuyPricesRawAsync();
#else
        var contract = readOnlyWeb3.Eth.GetContract(Abi, contractAddress);
        var raw = await contract.GetFunction("getAllBuyPrices").CallAsync<List<BigInteger>>();
        return raw.ToArray();
#endif
    }

    public async Task<BigInteger[]> GetAllSellPricesRawAsync()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        return await ArcTrading.WebGL.WebGLChainApi.GetAllSellPricesRawAsync();
#else
        var contract = readOnlyWeb3.Eth.GetContract(Abi, contractAddress);
        var raw = await contract.GetFunction("getAllSellPrices").CallAsync<List<BigInteger>>();
        return raw.ToArray();
#endif
    }

    /// <summary>
    /// Highest buyback price across all 5 managed NFT types — used as a market arbitrage signal
    /// ("if anything is paying above MintPrice, mint or sell"). Independent of who owns what.
    /// </summary>
    public async Task<decimal> GetBestSellPriceUSDCAsync()
    {
        var prices = await GetAllSellPricesUSDCAsync();
        decimal max = 0m;
        for (int i = 0; i < prices.Length; i++)
            if (prices[i] > max) max = prices[i];
        return max;
    }

    /// <summary>
    /// Total NFTs (sum of balanceOf across the 5 types) held by `account`.
    /// Pass the NPC's TBA address — that's where x402 mints land in the new flow.
    /// </summary>
    public async Task<int> GetNftInventoryCountAsync(string account)
    {
        if (string.IsNullOrWhiteSpace(account)) return 0;
#if UNITY_WEBGL && !UNITY_EDITOR
        var items = await ArcTrading.WebGL.WebGLChainApi.GetTbaItemBalancesAsync(account);
        int total = 0;
        if (items?.balances != null)
        {
            for (int i = 0; i < items.balances.Length; i++)
            {
                if (string.IsNullOrEmpty(items.balances[i])) continue;
                total += (int)BigInteger.Parse(items.balances[i]);
            }
        }
        return total;
#else
        var contract = readOnlyWeb3.Eth.GetContract(Abi, contractAddress);
        var dto = await contract.GetFunction("getTbaItemBalances")
            .CallDeserializingToObjectAsync<GetTbaItemBalancesOutputDTO>(account);
        int total = 0;
        for (int i = 0; i < dto.Balances.Count; i++) total += (int)dto.Balances[i];
        return total;
#endif
    }

    public async Task<BigInteger> GetCirculatingSupplyAsync(BigInteger itemId)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        return await ArcTrading.WebGL.WebGLChainApi.GetCirculatingSupplyAsync(itemId);
#else
        var contract = readOnlyWeb3.Eth.GetContract(Abi, contractAddress);
        return await contract.GetFunction("circulatingSupply").CallAsync<BigInteger>(itemId);
#endif
    }

    public async Task<BigInteger> GetActiveTypeCountAsync()
    {
        var contract = readOnlyWeb3.Eth.GetContract(Abi, contractAddress);
        return await contract.GetFunction("activeTypeCount").CallAsync<BigInteger>();
    }
    
    public async Task<decimal> GetMintPriceUSDCAsync()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        var price = await ArcTrading.WebGL.WebGLChainApi.GetBaselinePriceAsync();
#else
        var contract = readOnlyWeb3.Eth.GetContract(Abi, contractAddress);
        var price = await contract.GetFunction("BASELINE_PRICE").CallAsync<BigInteger>();
#endif
        return FromUsdc(price);
    }

    public async Task<decimal> GetBuyPriceUSDCAsync(BigInteger itemId)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        var price = await ArcTrading.WebGL.WebGLChainApi.GetBuyPriceAsync(itemId);
#else
        var contract = readOnlyWeb3.Eth.GetContract(Abi, contractAddress);
        var price = await contract.GetFunction("getBuyPrice").CallAsync<BigInteger>(itemId);
#endif
        return FromUsdc(price);
    }

    public async Task<decimal[]> GetAllBuyPricesUSDCAsync()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        var raw = await ArcTrading.WebGL.WebGLChainApi.GetAllBuyPricesRawAsync();
        var prices = new decimal[raw.Length];
        for (int i = 0; i < raw.Length; i++) prices[i] = FromUsdc(raw[i]);
        return prices;
#else
        var contract = readOnlyWeb3.Eth.GetContract(Abi, contractAddress);
        var raw = await contract.GetFunction("getAllBuyPrices").CallAsync<List<BigInteger>>();
        var prices = new decimal[raw.Count];
        for (int i = 0; i < raw.Count; i++) prices[i] = FromUsdc(raw[i]);
        return prices;
#endif
    }

    /// <summary>
    /// Average buy price across the 5 NFT types — the expected cost of an on-chain
    /// mintRandom() call (which picks one id uniformly at random). NPC DecideTrade
    /// uses this as the dynamic mint-price anchor in the profitRatio formula.
    /// </summary>
    public async Task<decimal> GetAvgBuyPriceUSDCAsync()
    {
        var prices = await GetAllBuyPricesUSDCAsync();
        if (prices.Length == 0) return 0m;
        decimal sum = 0m;
        for (int i = 0; i < prices.Length; i++) sum += prices[i];
        return sum / prices.Length;
    }
    
    public async Task<BigInteger> GetMaxBuyPriceAsync()
    {
        var raw = await GetAllBuyPricesRawAsync();
        BigInteger max = BigInteger.Zero;
        for (int i = 0; i < raw.Length; i++)
            if (raw[i] > max) max = raw[i];
        return max;
    }

    public async Task<BigInteger> GetNftBalanceAsync(string account, BigInteger itemId)
    {
        var itemsAddr = await GetItemsAddressAsync();
#if UNITY_WEBGL && !UNITY_EDITOR
        return await ArcTrading.WebGL.WebGLChainApi.GetErc1155BalanceAsync(itemsAddr, account, itemId);
#else
        var items = readOnlyWeb3.Eth.GetContract(Erc1155Abi, itemsAddr);
        return await items.GetFunction("balanceOf").CallAsync<BigInteger>(account, itemId);
#endif
    }

    /// <summary>
    /// Returns the first item id where balanceOf(npc, id) > 0, or null if NPC owns nothing.
    /// Used by sellItem callers that don't care which type to sell.
    /// Uses GamePayment.getTbaOwnedItems — the contract already filters to balances > 0.
    /// </summary>
    public async Task<BigInteger?> FindFirstOwnedItemIdAsync(string account)
    {
        if (string.IsNullOrWhiteSpace(account)) return null;
#if UNITY_WEBGL && !UNITY_EDITOR
        var items = await ArcTrading.WebGL.WebGLChainApi.GetTbaOwnedItemsAsync(account);
        if (items?.ids == null || items.ids.Length == 0) return null;
        return BigInteger.Parse(items.ids[0]);
#else
        var contract = readOnlyWeb3.Eth.GetContract(Abi, contractAddress);
        var dto = await contract.GetFunction("getTbaOwnedItems")
            .CallDeserializingToObjectAsync<GetTbaOwnedItemsOutputDTO>(account);
        if (dto.Ids == null || dto.Ids.Count == 0) return null;
        return dto.Ids[0];
#endif
    }

    public async Task<decimal> GetContractTotalUsdcAsync()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        var g = await ArcTrading.WebGL.WebGLChainApi.GetGamePaymentGatewayAsync();
        return FromUsdc(string.IsNullOrEmpty(g?.contractBalance) ? BigInteger.Zero : BigInteger.Parse(g.contractBalance));
#else
        var contract = readOnlyWeb3.Eth.GetContract(Abi, contractAddress);
        var fn = contract.GetFunction("getContractBalance");
        var balance = await fn.CallAsync<BigInteger>();
        return FromUsdc(balance);
#endif
    }

    // ----- GamePayment contract's own Circle Gateway state (owner-managed pool) -----

    public async Task<string> GetConfiguredGatewayAddressAsync()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        // Server's /game/config exposes the gateway address via constants(); the route
        // accepts either casing depending on TS-side naming. Both fields are read.
        var cfg = await ArcTrading.WebGL.ArcTradingApiClient.GetJsonAsync<ArcTrading.WebGL.WebGLChainApi.GameConfigResponse>("/game/config");
        return !string.IsNullOrEmpty(cfg?.gatewayAddress) ? cfg.gatewayAddress : cfg?.gateway;
#else
        var contract = readOnlyWeb3.Eth.GetContract(Abi, contractAddress);
        return await contract.GetFunction("gateway").CallAsync<string>();
#endif
    }

    public async Task<decimal> GetContractGatewayAvailableUSDCAsync()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        var g = await ArcTrading.WebGL.WebGLChainApi.GetGamePaymentGatewayAsync();
        return FromUsdc(string.IsNullOrEmpty(g?.availableBalance) ? BigInteger.Zero : BigInteger.Parse(g.availableBalance));
#else
        var contract = readOnlyWeb3.Eth.GetContract(Abi, contractAddress);
        var balance = await contract.GetFunction("gatewayAvailableBalance").CallAsync<BigInteger>();
        return FromUsdc(balance);
#endif
    }

    public async Task<decimal> GetContractGatewayWithdrawableUSDCAsync()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        var g = await ArcTrading.WebGL.WebGLChainApi.GetGamePaymentGatewayAsync();
        return FromUsdc(string.IsNullOrEmpty(g?.withdrawableBalance) ? BigInteger.Zero : BigInteger.Parse(g.withdrawableBalance));
#else
        var contract = readOnlyWeb3.Eth.GetContract(Abi, contractAddress);
        var balance = await contract.GetFunction("gatewayWithdrawableBalance").CallAsync<BigInteger>();
        return FromUsdc(balance);
#endif
    }

    public async Task<decimal> GetContractGatewayWithdrawingUSDCAsync()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        var g = await ArcTrading.WebGL.WebGLChainApi.GetGamePaymentGatewayAsync();
        return FromUsdc(string.IsNullOrEmpty(g?.withdrawingBalance) ? BigInteger.Zero : BigInteger.Parse(g.withdrawingBalance));
#else
        var contract = readOnlyWeb3.Eth.GetContract(Abi, contractAddress);
        var balance = await contract.GetFunction("gatewayWithdrawingBalance").CallAsync<BigInteger>();
        return FromUsdc(balance);
#endif
    }

    public async Task<decimal> GetContractGatewayTotalUSDCAsync()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        var g = await ArcTrading.WebGL.WebGLChainApi.GetGamePaymentGatewayAsync();
        return FromUsdc(string.IsNullOrEmpty(g?.totalBalance) ? BigInteger.Zero : BigInteger.Parse(g.totalBalance));
#else
        var contract = readOnlyWeb3.Eth.GetContract(Abi, contractAddress);
        var balance = await contract.GetFunction("gatewayTotalBalance").CallAsync<BigInteger>();
        return FromUsdc(balance);
#endif
    }

    public async Task<BigInteger> GetContractGatewayWithdrawalBlockAsync()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        var g = await ArcTrading.WebGL.WebGLChainApi.GetGamePaymentGatewayAsync();
        return string.IsNullOrEmpty(g?.withdrawalBlock) ? BigInteger.Zero : BigInteger.Parse(g.withdrawalBlock);
#else
        var contract = readOnlyWeb3.Eth.GetContract(Abi, contractAddress);
        return await contract.GetFunction("gatewayWithdrawalBlock").CallAsync<BigInteger>();
#endif
    }

    public async Task<BigInteger> GetContractGatewayWithdrawalDelayAsync()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        var g = await ArcTrading.WebGL.WebGLChainApi.GetGamePaymentGatewayAsync();
        return string.IsNullOrEmpty(g?.withdrawalDelay) ? BigInteger.Zero : BigInteger.Parse(g.withdrawalDelay);
#else
        var contract = readOnlyWeb3.Eth.GetContract(Abi, contractAddress);
        return await contract.GetFunction("gatewayWithdrawalDelay").CallAsync<BigInteger>();
#endif
    }

    public async Task<bool> IsGatewayAuthorizedAsync(string addr)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        var g = await ArcTrading.WebGL.WebGLChainApi.GetGamePaymentGatewayAsync(addr);
        return g != null && g.authorized;
#else
        var contract = readOnlyWeb3.Eth.GetContract(Abi, contractAddress);
        return await contract.GetFunction("isGatewayAuthorized").CallAsync<bool>(addr);
#endif
    }

    public async Task<bool> IsGatewayTokenSupportedAsync()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        var g = await ArcTrading.WebGL.WebGLChainApi.GetGamePaymentGatewayAsync();
        return g != null && g.tokenSupported;
#else
        var contract = readOnlyWeb3.Eth.GetContract(Abi, contractAddress);
        return await contract.GetFunction("isGatewayTokenSupported").CallAsync<bool>();
#endif
    }

    // ----- GamePayment contract's Npc6551Manager admin -----

    public async Task<string> GetManagerAddressAsync()
    {
        var contract = readOnlyWeb3.Eth.GetContract(Abi, contractAddress);
        return await contract.GetFunction("manager").CallAsync<string>();
    }

    public async Task<string> SetManagerAsync(string managerAddress)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        var signer = await GetTraderSignerAsync();
        return await ArcTrading.Crypto.EthRawTxSender.SendFunctionAsync(
            signer.address, signer.privateKey, contractAddress, Abi, "setManager",
            ArcTrading.Crypto.EthRawTxSender.JsonArgs(managerAddress),
            new BigInteger(120000));
#else
        var web3 = await CreateSignedWeb3Async();
        var contract = web3.Eth.GetContract(Abi, contractAddress);
        var fn = contract.GetFunction("setManager");
        var gas = new HexBigInteger(120000);
        return await fn.SendTransactionAsync(
            web3.TransactionManager.Account.Address, gas, null, managerAddress);
#endif
    }

    // ----- GamePayment contract's owner-only gateway admin -----

    public async Task<string> SetGatewayAsync(string gatewayAddress)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        var signer = await GetTraderSignerAsync();
        return await ArcTrading.Crypto.EthRawTxSender.SendFunctionAsync(
            signer.address, signer.privateKey, contractAddress, Abi, "setGateway",
            ArcTrading.Crypto.EthRawTxSender.JsonArgs(gatewayAddress),
            new BigInteger(120000));
#else
        var web3 = await CreateSignedWeb3Async();
        var contract = web3.Eth.GetContract(Abi, contractAddress);
        var fn = contract.GetFunction("setGateway");
        var gas = new HexBigInteger(120000);
        return await fn.SendTransactionAsync(
            web3.TransactionManager.Account.Address, gas, null, gatewayAddress);
#endif
    }

    public async Task<string> DepositToGatewayAsync(decimal amountUSDC)
    {
        var amount = Erc20UsdcHelper.ParseUsdc(amountUSDC);
#if UNITY_WEBGL && !UNITY_EDITOR
        var signer = await GetTraderSignerAsync();
        return await ArcTrading.Crypto.EthRawTxSender.SendFunctionAsync(
            signer.address, signer.privateKey, contractAddress, Abi, "depositToGateway",
            ArcTrading.Crypto.EthRawTxSender.JsonArgs(amount),
            new BigInteger(250000));
#else
        var web3 = await CreateSignedWeb3Async();
        var contract = web3.Eth.GetContract(Abi, contractAddress);
        var fn = contract.GetFunction("depositToGateway");
        var gas = new HexBigInteger(250000);
        return await fn.SendTransactionAsync(
            web3.TransactionManager.Account.Address, gas, null, amount);
#endif
    }

    public async Task<string> InitiateGatewayWithdrawalAsync(decimal amountUSDC)
    {
        var amount = Erc20UsdcHelper.ParseUsdc(amountUSDC);
#if UNITY_WEBGL && !UNITY_EDITOR
        var signer = await GetTraderSignerAsync();
        return await ArcTrading.Crypto.EthRawTxSender.SendFunctionAsync(
            signer.address, signer.privateKey, contractAddress, Abi, "initiateGatewayWithdrawal",
            ArcTrading.Crypto.EthRawTxSender.JsonArgs(amount),
            new BigInteger(200000));
#else
        var web3 = await CreateSignedWeb3Async();
        var contract = web3.Eth.GetContract(Abi, contractAddress);
        var fn = contract.GetFunction("initiateGatewayWithdrawal");
        var gas = new HexBigInteger(200000);
        return await fn.SendTransactionAsync(
            web3.TransactionManager.Account.Address, gas, null, amount);
#endif
    }

    public async Task<string> CompleteGatewayWithdrawalAsync()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        var signer = await GetTraderSignerAsync();
        return await ArcTrading.Crypto.EthRawTxSender.SendFunctionAsync(
            signer.address, signer.privateKey, contractAddress, Abi, "completeGatewayWithdrawal",
            ArcTrading.Crypto.EthRawTxSender.JsonArgs(),
            new BigInteger(200000));
#else
        var web3 = await CreateSignedWeb3Async();
        var contract = web3.Eth.GetContract(Abi, contractAddress);
        var fn = contract.GetFunction("completeGatewayWithdrawal");
        var gas = new HexBigInteger(200000);
        return await fn.SendTransactionAsync(
            web3.TransactionManager.Account.Address, gas, null);
#endif
    }

    public async Task<string> AddGatewayDelegateAsync(string delegateAddress)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        var signer = await GetTraderSignerAsync();
        return await ArcTrading.Crypto.EthRawTxSender.SendFunctionAsync(
            signer.address, signer.privateKey, contractAddress, Abi, "addGatewayDelegate",
            ArcTrading.Crypto.EthRawTxSender.JsonArgs(delegateAddress),
            new BigInteger(150000));
#else
        var web3 = await CreateSignedWeb3Async();
        var contract = web3.Eth.GetContract(Abi, contractAddress);
        var fn = contract.GetFunction("addGatewayDelegate");
        var gas = new HexBigInteger(150000);
        return await fn.SendTransactionAsync(
            web3.TransactionManager.Account.Address, gas, null, delegateAddress);
#endif
    }

    public async Task<string> RemoveGatewayDelegateAsync(string delegateAddress)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        var signer = await GetTraderSignerAsync();
        return await ArcTrading.Crypto.EthRawTxSender.SendFunctionAsync(
            signer.address, signer.privateKey, contractAddress, Abi, "removeGatewayDelegate",
            ArcTrading.Crypto.EthRawTxSender.JsonArgs(delegateAddress),
            new BigInteger(150000));
#else
        var web3 = await CreateSignedWeb3Async();
        var contract = web3.Eth.GetContract(Abi, contractAddress);
        var fn = contract.GetFunction("removeGatewayDelegate");
        var gas = new HexBigInteger(150000);
        return await fn.SendTransactionAsync(
            web3.TransactionManager.Account.Address, gas, null, delegateAddress);
#endif
    }

    public async Task<decimal> GetGatewayAvailableBalanceUSDCAsync()
    {
        var arcNanopayment = GetComponent<ArcNanopaymentClient>();
        if (arcNanopayment == null) return 0m;

#if UNITY_WEBGL && !UNITY_EDITOR
        var signer = await GetTraderSignerAsync();
        var balance = await arcNanopayment.GatewayAvailableBalanceAsync(
            Erc20UsdcHelper.ArcUsdcAddress,
            signer.address);
#else
        var web3 = await CreateSignedWeb3Async();
        var balance = await arcNanopayment.GatewayAvailableBalanceAsync(
            Erc20UsdcHelper.ArcUsdcAddress,
            web3.TransactionManager.Account.Address);
#endif
        var onchain = FromUsdc(balance);
        
        if (gatewayBaselineInitialized && onchain < lastKnownOnchainGatewayUsdc)
        {
            var settled = lastKnownOnchainGatewayUsdc - onchain;
            pendingX402OutflowUsdc = pendingX402OutflowUsdc > settled
                ? pendingX402OutflowUsdc - settled
                : 0m;
        }

        lastKnownOnchainGatewayUsdc = onchain;
        gatewayBaselineInitialized = true;

        var effective = onchain - pendingX402OutflowUsdc;
        return effective < 0m ? 0m : effective;
    }

    private void RecordX402Outflow(BigInteger paidSmallestUnits)
    {
        if (paidSmallestUnits <= BigInteger.Zero) return;
        pendingX402OutflowUsdc += FromUsdc(paidSmallestUnits);
    }

    public async Task<decimal> GetGatewayAvailableBalanceUSDCAsync(string address)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        if (string.IsNullOrWhiteSpace(address)) return 0m;
        var balance = await ArcTrading.WebGL.WebGLChainApi.GetGatewayAvailableBalanceAsync(
            Erc20UsdcHelper.ArcUsdcAddress, address);
        return FromUsdc(balance);
#else
        var arcNanopayment = GetComponent<ArcNanopaymentClient>();
        if (arcNanopayment == null) return 0m;

        var web3 = await CreateSignedWeb3Async();
        var balance = await arcNanopayment.GatewayAvailableBalanceAsync(
            Erc20UsdcHelper.ArcUsdcAddress,
            address);
        return FromUsdc(balance);
#endif
    }
    
    public async Task<string> MintRandomAsync(BigInteger itemIdToBeMinted, bool nanopayment = false)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        // nanopayment=true is the ONLY WebGL path that works without local signing:
        // /game/mint-random-x402 takes `to=tba` as a contract arg, so the server's
        // serverAccount can call it on the player's behalf without forging identity
        // (server is paying its own x402 dispatcher cost). The minted NFT lands at
        // the player's TBA. This is fine.
        //
        // nanopayment=false (plain mintRandom from TBA's USDC) requires the NPC's
        // paymentWallet to sign — TODO Phase 5 via IEthCryptoBackend + /tx/send-raw.
        if (nanopayment)
        {
            if (nftTokenId == 0)
                throw new InvalidOperationException(
                    $"{name}: nftTokenId is 0 — set it to the deployed NPC NFT tokenId before enabling nanopayment.");
            var tbaWebgl = await EnsureTbaAddressAsync();
            return await ArcTrading.WebGL.WebGLWalletApi.MintRandomX402Async(tbaWebgl);
        }

        var maxBuyPriceWebgl = await GetMaxBuyPriceAsync();
        var trader = await GetTraderSignerAsync();
        await EnsureWebGLUsdcApprovalAsync(
            trader.address,
            trader.privateKey,
            contractAddress,
            maxBuyPriceWebgl);

        return await ArcTrading.Crypto.EthRawTxSender.SendFunctionAsync(
            trader.address,
            trader.privateKey,
            contractAddress,
            Abi,
            "mintRandom",
            ArcTrading.Crypto.EthRawTxSender.JsonArgs(maxBuyPriceWebgl),
            new BigInteger(300000));
#else
        if (nanopayment)
        {
            if (npcPaymentWalletService == null)
                throw new InvalidOperationException(
                    $"{name}: npcPaymentWalletService is not wired — nanopayment path cannot resolve the NPC operator key.");
            if (nftTokenId == 0)
                throw new InvalidOperationException(
                    $"{name}: nftTokenId is 0 — set it to the deployed NPC NFT tokenId before enabling nanopayment.");

            var arcNanopayment = GetComponent<ArcNanopaymentClient>();
            var capUsdc = (decimal)arcNanopayment.maxNanopaymentUsdc;
            var nanopaymentCap = Erc20UsdcHelper.ParseUsdc(capUsdc);

            var effectiveAvailableUsdc = await GetGatewayAvailableBalanceUSDCAsync();
            if (effectiveAvailableUsdc < capUsdc)
                await arcNanopayment.ApproveIfNeededThenGatewayDepositAsync((decimal)arcNanopayment.maxNanopaymentUsdc);

            var tba = await EnsureTbaAddressAsync();

            var content = await arcNanopayment.FetchPaywalledResourceAsync(
                arcNanopayment.x402ServerBaseUrl + itemIdToBeMinted,
                NftTokenId,
                npcPaymentWalletService,
                nanopaymentCap,
                tba);
            RecordX402Outflow(arcNanopayment.LastPaidAmountSmallestUnits);
            return content;
        }

        var web3 = await CreateSignedWeb3Async();

        var maxBuyPrice = await GetMaxBuyPriceAsync();
        await Erc20UsdcHelper.EnsureApprovalAsync(web3, contractAddress, maxBuyPrice);

        var contract = web3.Eth.GetContract(Abi, contractAddress);
        var fn = contract.GetFunction("mintRandom");
        var gas = new HexBigInteger(300000);

        return await fn.SendTransactionAsync(
            web3.TransactionManager.Account.Address, gas, null, maxBuyPrice);
#endif
    }
    
    /// <summary>
    /// Sell one NFT held by the NPC's ERC-6551 TBA.
    /// Desktop: signed by the local paymentWallet, routed through the TBA execute() call.
    /// WebGL: TODO Phase 5 — must locally sign TBA.execute(sellItem(itemId)) using the
    /// in-browser paymentWallet (generated via jslib + viem), then broadcast through
    /// the server's POST /tx/send-raw relay. Routing it through a server-held wallet
    /// would turn the operational paymentWallet into a custodial key, which violates
    /// the binding architecture (see [[npc-payment-binding-architecture]]).
    /// </summary>
    public async Task<string> SellItemAsync(string tbaAddress, BigInteger itemId)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        if (npcPaymentWalletService == null)
            throw new InvalidOperationException(
                $"{name}: SellItemAsync needs NpcPaymentWalletService to resolve the operator key.");

        var signer = await npcPaymentWalletService.EnsureBoundOrRebindAsync(NftTokenId);
        var itemsAddr = await GetItemsAddressAsync();

        var approved = await ArcTrading.WebGL.WebGLChainApi.GetErc1155IsApprovedForAllAsync(
            itemsAddr, tbaAddress, contractAddress);
        if (!approved)
        {
            var setApprovalData = ArcTrading.Crypto.WebGLAbiBridge.EncodeFunctionData(
                Erc1155Abi,
                "setApprovalForAll",
                ArcTrading.Crypto.EthRawTxSender.JsonArgs(contractAddress, true));
            await ArcTrading.Crypto.EthRawTxSender.SendTbaExecuteAsync(
                signer.Address,
                signer.PrivateKey,
                tbaAddress,
                itemsAddr,
                BigInteger.Zero,
                setApprovalData,
                new BigInteger(200000));
        }

        var sellItemData = ArcTrading.Crypto.WebGLAbiBridge.EncodeFunctionData(
            Abi,
            "sellItem",
            ArcTrading.Crypto.EthRawTxSender.JsonArgs(itemId));
        return await ArcTrading.Crypto.EthRawTxSender.SendTbaExecuteAsync(
            signer.Address,
            signer.PrivateKey,
            tbaAddress,
            contractAddress,
            BigInteger.Zero,
            sellItemData,
            new BigInteger(400000));
#else
        if (npcPaymentWalletService == null)
            throw new InvalidOperationException(
                $"{name}: SellItemAsync needs NpcPaymentWalletService to resolve the operator key.");

        var operatorWeb3 = await CreateOperatorWeb3Async();
        var itemsAddr = await GetItemsAddressAsync();

        var itemsReadonly = readOnlyWeb3.Eth.GetContract(Erc1155Abi, itemsAddr);
        var approved = await itemsReadonly.GetFunction("isApprovedForAll")
            .CallAsync<bool>(tbaAddress, contractAddress);
        if (!approved)
        {
            var setApprovalData = itemsReadonly.GetFunction("setApprovalForAll")
                .GetData(contractAddress, true)
                .HexToByteArray();
            await SendTbaExecuteAsync(
                operatorWeb3, tbaAddress, itemsAddr, setApprovalData,
                new HexBigInteger(200000), waitForReceipt: true);
        }

        var sellItemData = readOnlyWeb3.Eth.GetContract(Abi, contractAddress)
            .GetFunction("sellItem")
            .GetData(itemId)
            .HexToByteArray();
        return await SendTbaExecuteAsync(
            operatorWeb3, tbaAddress, contractAddress, sellItemData,
            new HexBigInteger(400000), waitForReceipt: false);
#endif
    }

    private async Task<Web3> CreateOperatorWeb3Async()
    {
        var signer = await npcPaymentWalletService.EnsureBoundOrRebindAsync(NftTokenId);
        var chainId = await readOnlyWeb3.Eth.ChainId.SendRequestAsync();
        var account = new Account(signer.PrivateKey, chainId.Value);
        return new Web3(account, rpcUrl);
    }

#if UNITY_WEBGL && !UNITY_EDITOR
    private async Task EnsureWebGLUsdcApprovalAsync(
        string owner,
        string privateKeyForOwner,
        string spender,
        BigInteger amount)
    {
        var balance = await ArcTrading.WebGL.WebGLChainApi.GetUsdcBalanceAsync(owner);
        if (balance < amount)
            throw new InvalidOperationException($"Insufficient USDC balance. Have={balance}, Need={amount}");

        var allowance = await ArcTrading.WebGL.WebGLChainApi.GetUsdcAllowanceAsync(owner, spender);
        if (allowance >= amount) return;

        await ArcTrading.Crypto.EthRawTxSender.SendFunctionAsync(
            owner,
            privateKeyForOwner,
            Erc20UsdcHelper.ArcUsdcAddress,
            Erc20WriteAbi,
            "approve",
            ArcTrading.Crypto.EthRawTxSender.JsonArgs(spender, amount),
            new BigInteger(100000));
    }
#endif

    private async Task<string> SendTbaExecuteAsync(
        Web3 operatorWeb3, string tbaAddress, string target,
        byte[] data, HexBigInteger gas, bool waitForReceipt)
    {
        var executeData = readOnlyWeb3.Eth.GetContract(Erc6551AccountAbi, tbaAddress)
            .GetFunction("execute")
            .GetData(target, BigInteger.Zero, data ?? Array.Empty<byte>(), (byte)0);
        var txInput = new TransactionInput
        {
            From = operatorWeb3.TransactionManager.Account.Address,
            To = tbaAddress,
            Value = new HexBigInteger(BigInteger.Zero),
            Data = executeData,
            Gas = gas,
        };
        var txHash = await operatorWeb3.Eth.TransactionManager
            .SendTransactionAsync(txInput).ConfigureAwait(true);
        if (waitForReceipt) await WaitReceiptAsync(txHash);
        return txHash;
    }

    private async Task WaitReceiptAsync(string txHash)
    {
        while (true)
        {
            var receipt = await readOnlyWeb3.Eth.Transactions.GetTransactionReceipt
                .SendRequestAsync(txHash);
            if (receipt != null)
            {
                if (receipt.Status == null || receipt.Status.Value == BigInteger.Zero)
                    throw new InvalidOperationException($"tx {txHash} reverted");
                return;
            }
            await Task.Delay(800);
        }
    }

    public async Task<(string address, string privateKey)> GetTraderSignerAsync()
    {
        var pk = await ResolveTraderPrivateKeyAsync();
        var address = ArcTrading.Crypto.EthCryptoBackend.Current.DeriveAddress(pk);
        return (address, pk);
    }

    private async Task<Web3> CreateSignedWeb3Async()
    {
        var pk = await ResolveTraderPrivateKeyAsync();
        var chainId = await readOnlyWeb3.Eth.ChainId.SendRequestAsync();
        var account = new Account(pk, chainId.Value);
        return new Web3(account, rpcUrl);
    }

    private async Task<string> ResolveTraderPrivateKeyAsync()
    {
        if (useBoundWalletAsTrader)
        {
            if (npcPaymentWalletService == null)
                throw new InvalidOperationException(
                    $"{name}: useBoundWalletAsTrader is enabled but npcPaymentWalletService is not wired.");
            if (nftTokenId == 0)
                throw new InvalidOperationException(
                    $"{name}: useBoundWalletAsTrader is enabled but nftTokenId is 0.");

            if (!string.IsNullOrEmpty(cachedTraderPrivateKey))
                return cachedTraderPrivateKey;

            var signer = await npcPaymentWalletService.EnsureBoundOrRebindAsync(NftTokenId);
            CacheBoundSigner(signer);
            return cachedTraderPrivateKey;
        }

        if (string.IsNullOrWhiteSpace(privateKey))
            throw new InvalidOperationException(
                $"{name} requires a private key for NPC chain actions (or enable useBoundWalletAsTrader).");
        return privateKey.Trim();
    }
    
    public async Task EnsureNpcHasInitialCapitalAsync()
    {
        if (initialUsdcCapital <= 0f) return;

        var target = (decimal)initialUsdcCapital;
        var currentNpcBalance = await GetWalletBalanceUSDCAsync();
        if (currentNpcBalance >= target)
        {
            Debug.Log($"[{name}] initial capital OK: wallet has {currentNpcBalance} USDC (target {target}).");
            return;
        }

        if (npcPaymentWalletService == null || npcPaymentWalletService.NpcContract == null)
            throw new InvalidOperationException(
                $"{name}: cannot top up trader wallet — npcPaymentWalletService / NpcContract not wired.");

        var web3 = await CreateSignedWeb3Async();
        var traderAddr = web3.TransactionManager.Account.Address;
        var deficit = target - currentNpcBalance;
        var deficitUnits = Erc20UsdcHelper.ParseUsdc(deficit);

        Debug.Log($"[{name}] trader wallet {traderAddr} short {deficit} USDC " +
                  $"(have {currentNpcBalance}, target {target}); topping up from NFT owner.");

        var txHash = await npcPaymentWalletService.NpcContract
            .TransferUsdcFromOwnerAsync(traderAddr, deficitUnits);
        Debug.Log($"[{name}] capital top-up tx: {txHash}");
    }
    
    private static decimal FromUsdc(BigInteger amount)
    {
        return (decimal)amount / 1_000_000m;
    }

    private static bool IsZeroAddress(string addr)
    {
        if (string.IsNullOrEmpty(addr)) return true;
        var hex = addr.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? addr.Substring(2) : addr;
        foreach (var c in hex)
            if (c != '0') return false;
        return true;
    }
}

// Output DTOs for GamePayment's batched view functions. These are tuple-returning
// view fns; Nethereum can't decode them into a single primitive, so we route them
// through [FunctionOutput] DTOs with positional [Parameter] tags.

[FunctionOutput]
public class GetTbaItemBalancesOutputDTO : IFunctionOutputDTO
{
    [Parameter("uint256[5]", "ids",      1)] public List<BigInteger> Ids      { get; set; }
    [Parameter("uint256[5]", "balances", 2)] public List<BigInteger> Balances { get; set; }
}

[FunctionOutput]
public class GetTbaOwnedItemsOutputDTO : IFunctionOutputDTO
{
    [Parameter("uint256[]", "ids",      1)] public List<BigInteger> Ids      { get; set; }
    [Parameter("uint256[]", "balances", 2)] public List<BigInteger> Balances { get; set; }
}

[FunctionOutput]
public class GetNpcTbaItemBalancesOutputDTO : IFunctionOutputDTO
{
    [Parameter("address",    "tba",      1)] public string            Tba      { get; set; }
    [Parameter("uint256[5]", "ids",      2)] public List<BigInteger>  Ids      { get; set; }
    [Parameter("uint256[5]", "balances", 3)] public List<BigInteger>  Balances { get; set; }
}

[FunctionOutput]
public class GetNpcTbaOwnedItemsOutputDTO : IFunctionOutputDTO
{
    [Parameter("address",   "tba",      1)] public string           Tba      { get; set; }
    [Parameter("uint256[]", "ids",      2)] public List<BigInteger> Ids      { get; set; }
    [Parameter("uint256[]", "balances", 3)] public List<BigInteger> Balances { get; set; }
}
