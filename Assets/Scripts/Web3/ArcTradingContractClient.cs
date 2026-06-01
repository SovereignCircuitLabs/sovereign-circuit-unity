using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using ArcTrading.Nanopayment;
using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Hex.HexConvertors.Extensions;
using Nethereum.Hex.HexTypes;
using Nethereum.Web3;
using Nethereum.Web3.Accounts;
using UnityEngine;

public class ArcTradingContractClient : MonoBehaviour
{
    [SerializeField] private string rpcUrl = "https://rpc.testnet.arc.network";
    public string RpcUrl => rpcUrl;
    // GamePayment contract address
    [SerializeField] private string contractAddress = "0x8274AEC72E8c7Ac0eB411A23fCde8058Ad41E8ab";
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
    }

    public async Task InitializeWalletAsync()
    {
        var web3 = await CreateSignedWeb3Async();
        WalletAddress = web3.TransactionManager.Account.Address;
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

        var contract = readOnlyWeb3.Eth.GetContract(Abi, contractAddress);
        var tba = await contract.GetFunction("npcTba").CallAsync<string>(NftTokenId);
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
        var web3 = await CreateSignedWeb3Async();
        var owner = web3.TransactionManager.Account.Address;
        var balance = await Erc20UsdcHelper.GetBalanceAsync(web3, owner);
        return FromUsdc(balance);
    }
    
    public async Task<decimal> GetWalletBalanceUSDCAsync(string account)
    {
        if (string.IsNullOrWhiteSpace(account)) return 0m;
        var balance = await Erc20UsdcHelper.GetBalanceAsync(readOnlyWeb3, account);
        return FromUsdc(balance);
    }

    /// <summary>
    /// Vault value = NPC's NFT inventory(TBA) marked to the contract's buyback price,
    /// i.e. Σ over the 5 managed item types of: balanceOf(tba, id) × getSellPrice(id).
    /// </summary>
    public async Task<decimal> GetVaultBalanceUSDCAsync(string account)
    {
        if (string.IsNullOrWhiteSpace(account)) return 0m;

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
    }

    // ---- Item / NFT queries ----

    public async Task<string> GetItemsAddressAsync()
    {
        if (!string.IsNullOrEmpty(cachedItemsAddress)) return cachedItemsAddress;
        var contract = readOnlyWeb3.Eth.GetContract(Abi, contractAddress);
        cachedItemsAddress = await contract.GetFunction("items").CallAsync<string>();
        return cachedItemsAddress;
    }

    public async Task<BigInteger[]> GetItemIdsAsync()
    {
        if (cachedItemIds != null) return cachedItemIds;
        var contract = readOnlyWeb3.Eth.GetContract(Abi, contractAddress);
        var ids = await contract.GetFunction("getItemIds").CallAsync<List<BigInteger>>();
        cachedItemIds = ids.ToArray();
        return cachedItemIds;
    }

    public async Task<decimal> GetSellPriceUSDCAsync(BigInteger itemId)
    {
        var contract = readOnlyWeb3.Eth.GetContract(Abi, contractAddress);
        var price = await contract.GetFunction("getSellPrice").CallAsync<BigInteger>(itemId);
        return FromUsdc(price);
    }

    public async Task<decimal[]> GetAllSellPricesUSDCAsync()
    {
        var contract = readOnlyWeb3.Eth.GetContract(Abi, contractAddress);
        var raw = await contract.GetFunction("getAllSellPrices").CallAsync<List<BigInteger>>();
        var prices = new decimal[raw.Count];
        for (int i = 0; i < raw.Count; i++) prices[i] = FromUsdc(raw[i]);
        return prices;
    }

    public async Task<BigInteger[]> GetAllBuyPricesRawAsync()
    {
        var contract = readOnlyWeb3.Eth.GetContract(Abi, contractAddress);
        var raw = await contract.GetFunction("getAllBuyPrices").CallAsync<List<BigInteger>>();
        return raw.ToArray();
    }

    public async Task<BigInteger[]> GetAllSellPricesRawAsync()
    {
        var contract = readOnlyWeb3.Eth.GetContract(Abi, contractAddress);
        var raw = await contract.GetFunction("getAllSellPrices").CallAsync<List<BigInteger>>();
        return raw.ToArray();
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
        var contract = readOnlyWeb3.Eth.GetContract(Abi, contractAddress);
        var dto = await contract.GetFunction("getTbaItemBalances")
            .CallDeserializingToObjectAsync<GetTbaItemBalancesOutputDTO>(account);
        int total = 0;
        for (int i = 0; i < dto.Balances.Count; i++) total += (int)dto.Balances[i];
        return total;
    }

    public async Task<BigInteger> GetCirculatingSupplyAsync(BigInteger itemId)
    {
        var contract = readOnlyWeb3.Eth.GetContract(Abi, contractAddress);
        return await contract.GetFunction("circulatingSupply").CallAsync<BigInteger>(itemId);
    }

    public async Task<BigInteger> GetActiveTypeCountAsync()
    {
        var contract = readOnlyWeb3.Eth.GetContract(Abi, contractAddress);
        return await contract.GetFunction("activeTypeCount").CallAsync<BigInteger>();
    }
    
    public async Task<decimal> GetMintPriceUSDCAsync()
    {
        var contract = readOnlyWeb3.Eth.GetContract(Abi, contractAddress);
        var price = await contract.GetFunction("BASELINE_PRICE").CallAsync<BigInteger>();
        return FromUsdc(price);
    }

    public async Task<decimal> GetBuyPriceUSDCAsync(BigInteger itemId)
    {
        var contract = readOnlyWeb3.Eth.GetContract(Abi, contractAddress);
        var price = await contract.GetFunction("getBuyPrice").CallAsync<BigInteger>(itemId);
        return FromUsdc(price);
    }

    public async Task<decimal[]> GetAllBuyPricesUSDCAsync()
    {
        var contract = readOnlyWeb3.Eth.GetContract(Abi, contractAddress);
        var raw = await contract.GetFunction("getAllBuyPrices").CallAsync<List<BigInteger>>();
        var prices = new decimal[raw.Count];
        for (int i = 0; i < raw.Count; i++) prices[i] = FromUsdc(raw[i]);
        return prices;
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
        var items = readOnlyWeb3.Eth.GetContract(Erc1155Abi, itemsAddr);
        return await items.GetFunction("balanceOf").CallAsync<BigInteger>(account, itemId);
    }

    /// <summary>
    /// Returns the first item id where balanceOf(npc, id) > 0, or null if NPC owns nothing.
    /// Used by sellItem callers that don't care which type to sell.
    /// Uses GamePayment.getTbaOwnedItems — the contract already filters to balances > 0.
    /// </summary>
    public async Task<BigInteger?> FindFirstOwnedItemIdAsync(string account)
    {
        if (string.IsNullOrWhiteSpace(account)) return null;
        var contract = readOnlyWeb3.Eth.GetContract(Abi, contractAddress);
        var dto = await contract.GetFunction("getTbaOwnedItems")
            .CallDeserializingToObjectAsync<GetTbaOwnedItemsOutputDTO>(account);
        if (dto.Ids == null || dto.Ids.Count == 0) return null;
        return dto.Ids[0];
    }

    public async Task<decimal> GetContractTotalUsdcAsync()
    {
        var contract = readOnlyWeb3.Eth.GetContract(Abi, contractAddress);
        var fn = contract.GetFunction("getContractBalance");
        var balance = await fn.CallAsync<BigInteger>();
        return FromUsdc(balance);
    }

    // ----- GamePayment contract's own Circle Gateway state (owner-managed pool) -----

    public async Task<string> GetConfiguredGatewayAddressAsync()
    {
        var contract = readOnlyWeb3.Eth.GetContract(Abi, contractAddress);
        return await contract.GetFunction("gateway").CallAsync<string>();
    }

    public async Task<decimal> GetContractGatewayAvailableUSDCAsync()
    {
        var contract = readOnlyWeb3.Eth.GetContract(Abi, contractAddress);
        var balance = await contract.GetFunction("gatewayAvailableBalance").CallAsync<BigInteger>();
        return FromUsdc(balance);
    }

    public async Task<decimal> GetContractGatewayWithdrawableUSDCAsync()
    {
        var contract = readOnlyWeb3.Eth.GetContract(Abi, contractAddress);
        var balance = await contract.GetFunction("gatewayWithdrawableBalance").CallAsync<BigInteger>();
        return FromUsdc(balance);
    }

    public async Task<decimal> GetContractGatewayWithdrawingUSDCAsync()
    {
        var contract = readOnlyWeb3.Eth.GetContract(Abi, contractAddress);
        var balance = await contract.GetFunction("gatewayWithdrawingBalance").CallAsync<BigInteger>();
        return FromUsdc(balance);
    }

    public async Task<decimal> GetContractGatewayTotalUSDCAsync()
    {
        var contract = readOnlyWeb3.Eth.GetContract(Abi, contractAddress);
        var balance = await contract.GetFunction("gatewayTotalBalance").CallAsync<BigInteger>();
        return FromUsdc(balance);
    }

    public async Task<BigInteger> GetContractGatewayWithdrawalBlockAsync()
    {
        var contract = readOnlyWeb3.Eth.GetContract(Abi, contractAddress);
        return await contract.GetFunction("gatewayWithdrawalBlock").CallAsync<BigInteger>();
    }

    public async Task<BigInteger> GetContractGatewayWithdrawalDelayAsync()
    {
        var contract = readOnlyWeb3.Eth.GetContract(Abi, contractAddress);
        return await contract.GetFunction("gatewayWithdrawalDelay").CallAsync<BigInteger>();
    }

    public async Task<bool> IsGatewayAuthorizedAsync(string addr)
    {
        var contract = readOnlyWeb3.Eth.GetContract(Abi, contractAddress);
        return await contract.GetFunction("isGatewayAuthorized").CallAsync<bool>(addr);
    }

    public async Task<bool> IsGatewayTokenSupportedAsync()
    {
        var contract = readOnlyWeb3.Eth.GetContract(Abi, contractAddress);
        return await contract.GetFunction("isGatewayTokenSupported").CallAsync<bool>();
    }

    // ----- GamePayment contract's Npc6551Manager admin -----

    public async Task<string> GetManagerAddressAsync()
    {
        var contract = readOnlyWeb3.Eth.GetContract(Abi, contractAddress);
        return await contract.GetFunction("manager").CallAsync<string>();
    }

    public async Task<string> SetManagerAsync(string managerAddress)
    {
        var web3 = await CreateSignedWeb3Async();
        var contract = web3.Eth.GetContract(Abi, contractAddress);
        var fn = contract.GetFunction("setManager");
        var gas = new HexBigInteger(120000);
        return await fn.SendTransactionAsync(
            web3.TransactionManager.Account.Address, gas, null, managerAddress);
    }

    // ----- GamePayment contract's owner-only gateway admin -----

    public async Task<string> SetGatewayAsync(string gatewayAddress)
    {
        var web3 = await CreateSignedWeb3Async();
        var contract = web3.Eth.GetContract(Abi, contractAddress);
        var fn = contract.GetFunction("setGateway");
        var gas = new HexBigInteger(120000);
        return await fn.SendTransactionAsync(
            web3.TransactionManager.Account.Address, gas, null, gatewayAddress);
    }

    public async Task<string> DepositToGatewayAsync(decimal amountUSDC)
    {
        var web3 = await CreateSignedWeb3Async();
        var amount = Erc20UsdcHelper.ParseUsdc(amountUSDC);
        var contract = web3.Eth.GetContract(Abi, contractAddress);
        var fn = contract.GetFunction("depositToGateway");
        var gas = new HexBigInteger(250000);
        return await fn.SendTransactionAsync(
            web3.TransactionManager.Account.Address, gas, null, amount);
    }

    public async Task<string> InitiateGatewayWithdrawalAsync(decimal amountUSDC)
    {
        var web3 = await CreateSignedWeb3Async();
        var amount = Erc20UsdcHelper.ParseUsdc(amountUSDC);
        var contract = web3.Eth.GetContract(Abi, contractAddress);
        var fn = contract.GetFunction("initiateGatewayWithdrawal");
        var gas = new HexBigInteger(200000);
        return await fn.SendTransactionAsync(
            web3.TransactionManager.Account.Address, gas, null, amount);
    }

    public async Task<string> CompleteGatewayWithdrawalAsync()
    {
        var web3 = await CreateSignedWeb3Async();
        var contract = web3.Eth.GetContract(Abi, contractAddress);
        var fn = contract.GetFunction("completeGatewayWithdrawal");
        var gas = new HexBigInteger(200000);
        return await fn.SendTransactionAsync(
            web3.TransactionManager.Account.Address, gas, null);
    }

    public async Task<string> AddGatewayDelegateAsync(string delegateAddress)
    {
        var web3 = await CreateSignedWeb3Async();
        var contract = web3.Eth.GetContract(Abi, contractAddress);
        var fn = contract.GetFunction("addGatewayDelegate");
        var gas = new HexBigInteger(150000);
        return await fn.SendTransactionAsync(
            web3.TransactionManager.Account.Address, gas, null, delegateAddress);
    }

    public async Task<string> RemoveGatewayDelegateAsync(string delegateAddress)
    {
        var web3 = await CreateSignedWeb3Async();
        var contract = web3.Eth.GetContract(Abi, contractAddress);
        var fn = contract.GetFunction("removeGatewayDelegate");
        var gas = new HexBigInteger(150000);
        return await fn.SendTransactionAsync(
            web3.TransactionManager.Account.Address, gas, null, delegateAddress);
    }

    public async Task<decimal> GetGatewayAvailableBalanceUSDCAsync()
    {
        var arcNanopayment = GetComponent<ArcNanopaymentClient>();
        if (arcNanopayment == null) return 0m;

        var web3 = await CreateSignedWeb3Async();
        var balance = await arcNanopayment.GatewayAvailableBalanceAsync(
            Erc20UsdcHelper.ArcUsdcAddress,
            web3.TransactionManager.Account.Address);
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
        var arcNanopayment = GetComponent<ArcNanopaymentClient>();
        if (arcNanopayment == null) return 0m;

        var web3 = await CreateSignedWeb3Async();
        var balance = await arcNanopayment.GatewayAvailableBalanceAsync(
            Erc20UsdcHelper.ArcUsdcAddress,
            address);
        return FromUsdc(balance);
    }
    
    public async Task<string> MintRandomAsync(BigInteger itemIdToBeMinted, bool nanopayment = false)
    {
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
    }
    
    /// <summary>
    /// Sell one NFT held by the NPC's ERC-6551 TBA.
    /// </summary>
    public async Task<string> SellItemAsync(string tbaAddress, BigInteger itemId)
    {
        if (npcPaymentWalletService == null || npcPaymentWalletService.NpcContract == null)
            throw new InvalidOperationException(
                $"{name}: SellItemAsync needs NpcCharacterContractClient (NFT owner key) " +
                "to sign ERC-6551 execute — wire npcPaymentWalletService in the Inspector.");

        var itemsAddr = await GetItemsAddressAsync();
        var itemsReadonly = readOnlyWeb3.Eth.GetContract(Erc1155Abi, itemsAddr);

        // 检查 TBA 是否已经授权 GamePayment 操作它的 ERC1155 NFT，没授权的话先授权
        var approved = await itemsReadonly.GetFunction("isApprovedForAll")
            .CallAsync<bool>(tbaAddress, contractAddress);
        if (!approved)
        {
            var setApprovalData = itemsReadonly.GetFunction("setApprovalForAll")
                .GetData(contractAddress, true)
                .HexToByteArray();
            await npcPaymentWalletService.NpcContract.ExecuteTbaAsOwnerAsync(
                tbaAddress, itemsAddr, BigInteger.Zero, setApprovalData,
                new HexBigInteger(200000), waitForReceipt: true);
        }

        // 让 TBA 调用 GamePayment.sellItem(itemId)
        var sellItemData = readOnlyWeb3.Eth.GetContract(Abi, contractAddress)
            .GetFunction("sellItem")
            .GetData(itemId)
            .HexToByteArray();
        return await npcPaymentWalletService.NpcContract.ExecuteTbaAsOwnerAsync(
            tbaAddress, contractAddress, BigInteger.Zero, sellItemData,
            new HexBigInteger(400000), waitForReceipt: false);
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
