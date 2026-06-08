using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using ArcTrading.Auth;
using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Hex.HexConvertors.Extensions;
using Nethereum.Web3;
using UnityEngine;

[FunctionOutput]
public class MarketplaceListingOutputDTO : IFunctionOutputDTO
{
    [Parameter("address", "seller", 1)] public string Seller { get; set; }
    [Parameter("uint256", "minPrice", 2)] public BigInteger MinPrice { get; set; }
    [Parameter("bool", "active", 3)] public bool Active { get; set; }
}

public class NpcListingInfo
{
    public BigInteger TokenId;
    public string Seller;
    public BigInteger MinPrice;
    public bool Active;

    public BigInteger QuotedPrice;
    public BigInteger TbaTotalValue;
    public BigInteger ScarcityMultiplierBps;
    public BigInteger ClassId;

    public NpcDataDTO NpcData;
}

/// <summary>
/// Wrapper around the NpcMarketplace contract.
///
/// Read paths (listings, getListing) use a local read-only Web3.
/// Write paths (listNpc / cancelListing / buyNpc, plus required USDC / ERC-721
/// approvals) go through the WalletLoginService bridge so they are signed by
/// the player's MetaMask wallet — the same one returned by SIWE login.
/// </summary>
public class NpcMarketplaceClient : MonoBehaviour
{
    [SerializeField] private string rpcUrl = "https://rpc.testnet.arc.network";
    [SerializeField] private string marketplaceContractAddress;
    [SerializeField] private string usdcAddress = Erc20UsdcHelper.ArcUsdcAddress;

    [Header("Refs")]
    [SerializeField] private NpcCharacterContractClient npcCharacter;
    [SerializeField] private NpcNFTPricingClient pricing;

    [Header("Tx defaults")]
    [SerializeField] private ulong approveUsdcGas = 100_000;
    [SerializeField] private ulong approveNftGas = 120_000;
    [SerializeField] private ulong listGas = 250_000;
    [SerializeField] private ulong cancelGas = 200_000;
    [SerializeField] private ulong buyGas = 400_000;

    public string MarketplaceContractAddress => marketplaceContractAddress;
    public string UsdcAddress => usdcAddress;
    public NpcCharacterContractClient NpcCharacter => npcCharacter;
    public NpcNFTPricingClient Pricing => pricing;

    private const string Abi = @"[
      {""inputs"":[{""internalType"":""uint256"",""name"":""tokenId"",""type"":""uint256""},{""internalType"":""uint256"",""name"":""minPrice"",""type"":""uint256""}],""name"":""listNpc"",""outputs"":[],""stateMutability"":""nonpayable"",""type"":""function""},
      {""inputs"":[{""internalType"":""uint256"",""name"":""tokenId"",""type"":""uint256""}],""name"":""cancelListing"",""outputs"":[],""stateMutability"":""nonpayable"",""type"":""function""},
      {""inputs"":[{""internalType"":""uint256"",""name"":""tokenId"",""type"":""uint256""}],""name"":""clearStaleListing"",""outputs"":[],""stateMutability"":""nonpayable"",""type"":""function""},
      {""inputs"":[{""internalType"":""uint256"",""name"":""tokenId"",""type"":""uint256""},{""internalType"":""uint256"",""name"":""maxPrice"",""type"":""uint256""}],""name"":""buyNpc"",""outputs"":[],""stateMutability"":""nonpayable"",""type"":""function""},
      {""inputs"":[{""internalType"":""uint256"",""name"":""tokenId"",""type"":""uint256""}],""name"":""getListing"",""outputs"":[{""internalType"":""address"",""name"":""seller"",""type"":""address""},{""internalType"":""uint256"",""name"":""minPrice"",""type"":""uint256""},{""internalType"":""bool"",""name"":""active"",""type"":""bool""}],""stateMutability"":""view"",""type"":""function""},
      {""inputs"":[{""internalType"":""uint256"",""name"":"""",""type"":""uint256""}],""name"":""listings"",""outputs"":[{""internalType"":""address"",""name"":""seller"",""type"":""address""},{""internalType"":""uint256"",""name"":""minPrice"",""type"":""uint256""},{""internalType"":""bool"",""name"":""active"",""type"":""bool""}],""stateMutability"":""view"",""type"":""function""},
      {""inputs"":[],""name"":""npcCharacter"",""outputs"":[{""internalType"":""address"",""name"":"""",""type"":""address""}],""stateMutability"":""view"",""type"":""function""},
      {""inputs"":[],""name"":""pricing"",""outputs"":[{""internalType"":""address"",""name"":"""",""type"":""address""}],""stateMutability"":""view"",""type"":""function""},
      {""inputs"":[],""name"":""usdc"",""outputs"":[{""internalType"":""address"",""name"":"""",""type"":""address""}],""stateMutability"":""view"",""type"":""function""}
    ]";

    private const string Erc20Abi = @"[
      {""constant"":true,""inputs"":[{""name"":""owner"",""type"":""address""},{""name"":""spender"",""type"":""address""}],""name"":""allowance"",""outputs"":[{""name"":"""",""type"":""uint256""}],""type"":""function""},
      {""constant"":false,""inputs"":[{""name"":""spender"",""type"":""address""},{""name"":""amount"",""type"":""uint256""}],""name"":""approve"",""outputs"":[{""name"":"""",""type"":""bool""}],""type"":""function""}
    ]";

    private const string Erc721Abi = @"[
      {""inputs"":[{""internalType"":""address"",""name"":""owner"",""type"":""address""},{""internalType"":""address"",""name"":""operator"",""type"":""address""}],""name"":""isApprovedForAll"",""outputs"":[{""internalType"":""bool"",""name"":"""",""type"":""bool""}],""stateMutability"":""view"",""type"":""function""},
      {""inputs"":[{""internalType"":""uint256"",""name"":""tokenId"",""type"":""uint256""}],""name"":""getApproved"",""outputs"":[{""internalType"":""address"",""name"":"""",""type"":""address""}],""stateMutability"":""view"",""type"":""function""},
      {""inputs"":[{""internalType"":""address"",""name"":""operator"",""type"":""address""},{""internalType"":""bool"",""name"":""approved"",""type"":""bool""}],""name"":""setApprovalForAll"",""outputs"":[],""stateMutability"":""nonpayable"",""type"":""function""},
      {""inputs"":[{""internalType"":""address"",""name"":""to"",""type"":""address""},{""internalType"":""uint256"",""name"":""tokenId"",""type"":""uint256""}],""name"":""approve"",""outputs"":[],""stateMutability"":""nonpayable"",""type"":""function""}
    ]";

    private Web3 readOnlyWeb3;

    private void Awake()
    {
        readOnlyWeb3 = new Web3(rpcUrl);
    }

    // ---------------- Read ----------------

    public async Task<MarketplaceListingOutputDTO> GetListingAsync(BigInteger tokenId)
    {
        var contract = readOnlyWeb3.Eth.GetContract(Abi, marketplaceContractAddress);
        return await contract.GetFunction("getListing")
            .CallDeserializingToObjectAsync<MarketplaceListingOutputDTO>(tokenId);
    }

    /// <summary>
    /// Enumerate every active listing by iterating tokenId = 1 .. nextTokenId-1
    /// and calling getListing(tokenId). The marketplace has no batch-listing view,
    /// so this is O(N) RPC calls — fine for the menu refresh use case.
    ///
    /// For each active listing we also fetch the on-chain price quote and the NPC
    /// data (archetype, name) so the UI can render the whole row in one shot.
    /// </summary>
    public async Task<List<NpcListingInfo>> EnumerateActiveListingsAsync(CancellationToken ct = default)
    {
        var result = new List<NpcListingInfo>();
        if (npcCharacter == null)
        {
            Debug.LogError("[NpcMarketplaceClient] npcCharacter ref is missing — cannot enumerate.");
            return result;
        }

        var next = await npcCharacter.GetNextTokenIdAsync();
        if (next <= BigInteger.One) return result;

        for (BigInteger id = BigInteger.One; id < next; id += BigInteger.One)
        {
            ct.ThrowIfCancellationRequested();

            MarketplaceListingOutputDTO listing;
            try
            {
                listing = await GetListingAsync(id);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[NpcMarketplaceClient] getListing({id}) failed: {ex.Message}");
                continue;
            }

            if (listing == null || !listing.Active) continue;

            var info = new NpcListingInfo
            {
                TokenId = id,
                Seller = listing.Seller,
                MinPrice = listing.MinPrice,
                Active = true,
            };

            if (pricing != null)
            {
                try
                {
                    var quote = await pricing.QuoteNpcPriceAsync(id);
                    info.QuotedPrice = quote.Price;
                    info.TbaTotalValue = quote.TbaTotalValue;
                    info.ScarcityMultiplierBps = quote.ScarcityMultiplierBps;
                    info.ClassId = await pricing.GetNpcClassIdAsync(id);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[NpcMarketplaceClient] quoteNpcPrice({id}) failed: {ex.Message}");
                }
            }

            try
            {
                info.NpcData = await npcCharacter.GetNpcAsync(id);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[NpcMarketplaceClient] getNpc({id}) failed: {ex.Message}");
            }

            result.Add(info);
        }

        return result;
    }

    private async Task<BigInteger> GetUsdcAllowanceAsync(string owner)
    {
        var usdc = readOnlyWeb3.Eth.GetContract(Erc20Abi, usdcAddress);
        return await usdc.GetFunction("allowance")
            .CallAsync<BigInteger>(owner, marketplaceContractAddress);
    }

    public async Task<bool> IsMarketplaceNftApprovedAsync(string owner, BigInteger tokenId)
    {
        if (npcCharacter == null) return false;
        var nft = readOnlyWeb3.Eth.GetContract(Erc721Abi, npcCharacter.NftContractAddress);
        var allApproved = await nft.GetFunction("isApprovedForAll")
            .CallAsync<bool>(owner, marketplaceContractAddress);
        if (allApproved) return true;

        var single = await nft.GetFunction("getApproved").CallAsync<string>(tokenId);
        return !string.IsNullOrWhiteSpace(single)
               && string.Equals(single, marketplaceContractAddress, StringComparison.OrdinalIgnoreCase);
    }

    // ---------------- Write (player wallet, via bridge) ----------------

    /// <summary>
    /// Buy a listed NPC. Pulls the live price quote, ensures buyer USDC allowance
    /// covers the cap, then sends buyNpc via the MetaMask bridge. The seller-side
    /// NFT approval is enforced by the contract at list time, so we don't re-prompt
    /// it here.
    /// </summary>
    public async Task<string> BuyNpcAsync(
        BigInteger tokenId,
        BigInteger maxPriceCap,
        CancellationToken ct = default)
    {
        var session = RequireLoggedInSession();
        var buyer = session.wallet;
        var chainId = session.chainId;

        await EnsureUsdcAllowanceAsync(buyer, maxPriceCap, chainId, ct);

        var data = readOnlyWeb3.Eth.GetContract(Abi, marketplaceContractAddress)
            .GetFunction("buyNpc")
            .GetData(tokenId, maxPriceCap)
            .HexToByteArray();

        var txHash = await SendBridgeTxAsync(
            buyer, marketplaceContractAddress, BigInteger.Zero, data, buyGas, chainId,
            label: $"buyNpc(tokenId={tokenId}, maxPrice={maxPriceCap})");
        await WaitReceiptAsync(txHash);
        return txHash;
    }

    /// <summary>
    /// List an NPC owned by the player. Ensures the marketplace is approved as an
    /// operator on the NpcCharacter contract (setApprovalForAll), then submits
    /// listNpc via the bridge.
    /// </summary>
    public async Task<string> ListNpcAsync(
        BigInteger tokenId,
        BigInteger minPrice,
        CancellationToken ct = default)
    {
        var session = RequireLoggedInSession();
        var seller = session.wallet;
        var chainId = session.chainId;

        await EnsureNftApprovalForAllAsync(seller, chainId, ct);

        var data = readOnlyWeb3.Eth.GetContract(Abi, marketplaceContractAddress)
            .GetFunction("listNpc")
            .GetData(tokenId, minPrice)
            .HexToByteArray();

        var txHash = await SendBridgeTxAsync(
            seller, marketplaceContractAddress, BigInteger.Zero, data, listGas, chainId,
            label: $"listNpc(tokenId={tokenId}, minPrice={minPrice})");
        await WaitReceiptAsync(txHash);
        return txHash;
    }

    public async Task<string> CancelListingAsync(BigInteger tokenId, CancellationToken ct = default)
    {
        var session = RequireLoggedInSession();
        var data = readOnlyWeb3.Eth.GetContract(Abi, marketplaceContractAddress)
            .GetFunction("cancelListing")
            .GetData(tokenId)
            .HexToByteArray();
        var txHash = await SendBridgeTxAsync(
            session.wallet, marketplaceContractAddress, BigInteger.Zero, data, cancelGas, session.chainId,
            label: $"cancelListing(tokenId={tokenId})");
        await WaitReceiptAsync(txHash);
        return txHash;
    }

    // ---------------- Approvals ----------------

    private async Task EnsureUsdcAllowanceAsync(
        string owner, BigInteger amount, long chainId, CancellationToken ct)
    {
        var allowance = await GetUsdcAllowanceAsync(owner);
        if (allowance >= amount) return;

        Debug.Log($"[NpcMarketplaceClient] USDC allowance {allowance} < {amount}, approving marketplace…");

        var data = readOnlyWeb3.Eth.GetContract(Erc20Abi, usdcAddress)
            .GetFunction("approve")
            .GetData(marketplaceContractAddress, amount)
            .HexToByteArray();

        var txHash = await SendBridgeTxAsync(
            owner, usdcAddress, BigInteger.Zero, data, approveUsdcGas, chainId,
            label: $"USDC.approve(marketplace, {amount})");
        await WaitReceiptAsync(txHash);
    }

    private async Task EnsureNftApprovalForAllAsync(string owner, long chainId, CancellationToken ct)
    {
        if (npcCharacter == null)
            throw new InvalidOperationException(
                "[NpcMarketplaceClient] npcCharacter ref is missing — cannot approve NFT.");

        var nft = readOnlyWeb3.Eth.GetContract(Erc721Abi, npcCharacter.NftContractAddress);
        var approved = await nft.GetFunction("isApprovedForAll")
            .CallAsync<bool>(owner, marketplaceContractAddress);
        if (approved) return;

        Debug.Log($"[NpcMarketplaceClient] NPC NFT not approved for marketplace, calling setApprovalForAll…");

        var data = nft.GetFunction("setApprovalForAll")
            .GetData(marketplaceContractAddress, true)
            .HexToByteArray();

        var txHash = await SendBridgeTxAsync(
            owner, npcCharacter.NftContractAddress, BigInteger.Zero, data, approveNftGas, chainId,
            label: $"NpcCharacter.setApprovalForAll(marketplace, true)");
        await WaitReceiptAsync(txHash);
    }

    // ---------------- Bridge plumbing ----------------

    private static WalletSession RequireLoggedInSession()
    {
        var login = WalletLoginService.Instance;
        if (login == null)
            throw new InvalidOperationException(
                "[NpcMarketplaceClient] WalletLoginService singleton missing — add it to the scene.");
        if (!login.HasSession || login.Current == null || string.IsNullOrWhiteSpace(login.Current.wallet))
            throw new InvalidOperationException(
                "[NpcMarketplaceClient] No active wallet session — sign in first.");
        if (!login.BridgeReady)
            throw new InvalidOperationException(
                "[NpcMarketplaceClient] Bridge not ready — reopen the login tab in the browser.");
        return login.Current;
    }

    private static async Task<string> SendBridgeTxAsync(
        string from, string to, BigInteger value, byte[] data, ulong gas, long chainId, string label)
    {
        var req = new WalletTxRequest
        {
            from = from,
            to = to,
            value = "0x" + value.ToString("x"),
            data = data == null || data.Length == 0 ? "0x" : "0x" + data.ToHex(),
            gas = "0x" + gas.ToString("x"),
            chainId = chainId,
            label = label,
        };
        return await WalletLoginService.Instance.SendOwnerTransactionAsync(req).ConfigureAwait(true);
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
}
