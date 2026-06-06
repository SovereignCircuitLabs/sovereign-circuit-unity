using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using ArcTrading.Auth;
using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Hex.HexConvertors.Extensions;
using Nethereum.Hex.HexTypes;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.Web3;
using Nethereum.Web3.Accounts;
using UnityEngine;

[FunctionOutput]
public class PaymentBindingDTO : IFunctionOutputDTO
{
    [Parameter("address", "wallet", 1)] public string Wallet { get; set; }
    [Parameter("uint64",  "version", 2)] public ulong Version { get; set; }
}

// Mirrors NpcCharacter.PortfolioConfig (Solidity). Stored fields are 6-decimal USDC
// for amounts, bps (0..10000) for weights, and seconds for intervals.
public class PortfolioConfigDTO
{
    [Parameter("uint16", "livingNeedsWeightBps", 1)] public ushort LivingNeedsWeightBps { get; set; }
    [Parameter("uint16", "reserveWeightBps", 2)]    public ushort ReserveWeightBps { get; set; }
    [Parameter("uint16", "tradingWeightBps", 3)]    public ushort TradingWeightBps { get; set; }
    [Parameter("uint64", "minimumLivingBudgetUSDC", 4)]  public ulong MinimumLivingBudgetUSDC { get; set; }
    [Parameter("uint64", "minimumReserveBudgetUSDC", 5)] public ulong MinimumReserveBudgetUSDC { get; set; }
    [Parameter("uint32", "rebalanceIntervalSeconds", 6)] public uint RebalanceIntervalSeconds { get; set; }
    [Parameter("uint32", "chainActionCooldownSeconds", 7)] public uint ChainActionCooldownSeconds { get; set; }
    [Parameter("uint64", "minTradeUSDC", 8)] public ulong MinTradeUSDC { get; set; }
    [Parameter("uint64", "maxTradeUSDC", 9)] public ulong MaxTradeUSDC { get; set; }
}

// Mirrors NpcCharacter.NpcData (Solidity).
public class NpcDataDTO
{
    [Parameter("string", "npcName", 1)]      public string NpcName { get; set; }
    [Parameter("string", "metadataURI", 2)]  public string MetadataURI { get; set; }
    [Parameter("uint8",  "archetype", 3)]    public byte Archetype { get; set; }
    [Parameter("uint8",  "riskLevel", 4)]    public byte RiskLevel { get; set; }
    [Parameter("uint16", "level", 5)]        public ushort Level { get; set; }
    [Parameter("uint32", "reputation", 6)]   public uint Reputation { get; set; }
    [Parameter("tuple",  "portfolio", 7)]    public PortfolioConfigDTO Portfolio { get; set; }
}

// Wrapper because getNpc() returns a single tuple at the top-level outputs slot;
// CallDeserializingToObjectAsync needs a [FunctionOutput] DTO whose first parameter
// holds that tuple.
[FunctionOutput]
public class GetNpcOutputDTO : IFunctionOutputDTO
{
    [Parameter("tuple", "", 1)] public NpcDataDTO Data { get; set; }
}

public readonly struct OwnedNpc
{
    public readonly BigInteger TokenId;
    public readonly NpcDataDTO Data;

    public OwnedNpc(BigInteger tokenId, NpcDataDTO data)
    {
        TokenId = tokenId;
        Data = data;
    }
}

public class NpcCharacterContractClient : MonoBehaviour
{
    [SerializeField] private string rpcUrl = "https://rpc.testnet.arc.network";

    // Address of the deployed NpcCharacter NFT contract
    [SerializeField] private string nftContractAddress;

    // Private key of the EOA that currently owns the NPC NFTs we manage from this client.
    // NOT used for x402 signing — only for owner-side writes when loginViaAuth=false.
    [SerializeField] private string nftOwnerPrivateKey;

    // When true, owner-side writes go through the WalletLoginService bridge —
    // each tx triggers a MetaMask popup in the browser. When false, sign locally
    // with nftOwnerPrivateKey.
    [SerializeField] private bool loginViaAuth = false;

    [Header("Bridge login (only used when loginViaAuth=true)")]
    [SerializeField] private int bridgePreferredPort = 7777;
    [SerializeField] private string bridgeSiweStatement = "Sign in to ArcTrading";
    [SerializeField] private uint bridgeSessionTtlMinutes = 1440; // 24 h

    public string NftContractAddress => nftContractAddress;
    public string RpcUrl => rpcUrl;

    private const string Abi = @"[
      {""inputs"":[{""internalType"":""uint256"",""name"":""tokenId"",""type"":""uint256""}],
        ""name"":""getPaymentBinding"",
        ""outputs"":[
          {""internalType"":""address"",""name"":""wallet"",""type"":""address""},
          {""internalType"":""uint64"",""name"":""version"",""type"":""uint64""}],
        ""stateMutability"":""view"",""type"":""function""},
      {""inputs"":[
          {""internalType"":""uint256"",""name"":""tokenId"",""type"":""uint256""},
          {""internalType"":""address"",""name"":""wallet"",""type"":""address""}],
        ""name"":""bindPaymentWallet"",
        ""outputs"":[],""stateMutability"":""nonpayable"",""type"":""function""},
      {""inputs"":[{""internalType"":""uint256"",""name"":""tokenId"",""type"":""uint256""}],
        ""name"":""clearPaymentWallet"",
        ""outputs"":[],""stateMutability"":""nonpayable"",""type"":""function""},
      {""inputs"":[{""internalType"":""uint256"",""name"":""tokenId"",""type"":""uint256""}],
        ""name"":""ownerOf"",
        ""outputs"":[{""internalType"":""address"",""name"":"""",""type"":""address""}],
        ""stateMutability"":""view"",""type"":""function""},
      {""inputs"":[{""internalType"":""uint256"",""name"":""tokenId"",""type"":""uint256""}],
        ""name"":""exists"",
        ""outputs"":[{""internalType"":""bool"",""name"":"""",""type"":""bool""}],
        ""stateMutability"":""view"",""type"":""function""},
      {""inputs"":[{""internalType"":""address"",""name"":""owner"",""type"":""address""}],
        ""name"":""balanceOf"",
        ""outputs"":[{""internalType"":""uint256"",""name"":"""",""type"":""uint256""}],
        ""stateMutability"":""view"",""type"":""function""},
      {""inputs"":[],
        ""name"":""nextTokenId"",
        ""outputs"":[{""internalType"":""uint256"",""name"":"""",""type"":""uint256""}],
        ""stateMutability"":""view"",""type"":""function""},
      {""inputs"":[{""internalType"":""uint256"",""name"":""tokenId"",""type"":""uint256""}],
        ""name"":""getNpc"",
        ""outputs"":[{
          ""components"":[
            {""internalType"":""string"",""name"":""npcName"",""type"":""string""},
            {""internalType"":""string"",""name"":""metadataURI"",""type"":""string""},
            {""internalType"":""uint8"",""name"":""archetype"",""type"":""uint8""},
            {""internalType"":""uint8"",""name"":""riskLevel"",""type"":""uint8""},
            {""internalType"":""uint16"",""name"":""level"",""type"":""uint16""},
            {""internalType"":""uint32"",""name"":""reputation"",""type"":""uint32""},
            {""components"":[
              {""internalType"":""uint16"",""name"":""livingNeedsWeightBps"",""type"":""uint16""},
              {""internalType"":""uint16"",""name"":""reserveWeightBps"",""type"":""uint16""},
              {""internalType"":""uint16"",""name"":""tradingWeightBps"",""type"":""uint16""},
              {""internalType"":""uint64"",""name"":""minimumLivingBudgetUSDC"",""type"":""uint64""},
              {""internalType"":""uint64"",""name"":""minimumReserveBudgetUSDC"",""type"":""uint64""},
              {""internalType"":""uint32"",""name"":""rebalanceIntervalSeconds"",""type"":""uint32""},
              {""internalType"":""uint32"",""name"":""chainActionCooldownSeconds"",""type"":""uint32""},
              {""internalType"":""uint64"",""name"":""minTradeUSDC"",""type"":""uint64""},
              {""internalType"":""uint64"",""name"":""maxTradeUSDC"",""type"":""uint64""}],
            ""internalType"":""struct NpcCharacter.PortfolioConfig"",""name"":""portfolio"",""type"":""tuple""}],
          ""internalType"":""struct NpcCharacter.NpcData"",""name"":"""",""type"":""tuple""}],
        ""stateMutability"":""view"",""type"":""function""}
    ]";

    // Minimal ERC-20 transfer ABI — only used to build calldata for the bridge path.
    private const string Erc20TransferAbi = @"[
      {""constant"":false,""inputs"":[{""name"":""to"",""type"":""address""},{""name"":""amount"",""type"":""uint256""}],""name"":""transfer"",""outputs"":[{""name"":"""",""type"":""bool""}],""type"":""function""}
    ]";

    // ERC-6551 TBA accepts execute() only from the parent NFT's owner.
    private const string Erc6551AccountAbi = @"[
      {""inputs"":[{""internalType"":""address"",""name"":""to"",""type"":""address""},{""internalType"":""uint256"",""name"":""value"",""type"":""uint256""},{""internalType"":""bytes"",""name"":""data"",""type"":""bytes""},{""internalType"":""uint8"",""name"":""operation"",""type"":""uint8""}],""name"":""execute"",""outputs"":[{""internalType"":""bytes"",""name"":"""",""type"":""bytes""}],""stateMutability"":""payable"",""type"":""function""}
    ]";

    private Web3 readOnlyWeb3;

    // Serializes owner-signed tx submission so concurrent bind/clear calls from
    // multiple NPCs sharing this single client do not race on eth_getTransactionCount
    // (local path) or interleave MetaMask popups (bridge path).
    private static readonly SemaphoreSlim ownerTxGate = new SemaphoreSlim(1, 1);

    public long? CachedChainId { get; private set; }

    private void Awake()
    {
        readOnlyWeb3 = new Web3(rpcUrl);
    }

    public async Task<long> GetChainIdAsync()
    {
        if (CachedChainId.HasValue) return CachedChainId.Value;
        var id = (long)(await readOnlyWeb3.Eth.ChainId.SendRequestAsync()).Value;
        CachedChainId = id;
        return id;
    }

    public async Task<(string wallet, ulong version)> GetPaymentBindingAsync(BigInteger tokenId)
    {
        var contract = readOnlyWeb3.Eth.GetContract(Abi, nftContractAddress);
        var fn = contract.GetFunction("getPaymentBinding");
        var dto = await fn.CallDeserializingToObjectAsync<PaymentBindingDTO>(tokenId);
        return (dto.Wallet, dto.Version);
    }

    public async Task<string> OwnerOfAsync(BigInteger tokenId)
    {
        var contract = readOnlyWeb3.Eth.GetContract(Abi, nftContractAddress);
        return await contract.GetFunction("ownerOf").CallAsync<string>(tokenId);
    }

    public async Task<bool> ExistsAsync(BigInteger tokenId)
    {
        var contract = readOnlyWeb3.Eth.GetContract(Abi, nftContractAddress);
        return await contract.GetFunction("exists").CallAsync<bool>(tokenId);
    }

    public async Task<BigInteger> BalanceOfAsync(string owner)
    {
        var contract = readOnlyWeb3.Eth.GetContract(Abi, nftContractAddress);
        return await contract.GetFunction("balanceOf").CallAsync<BigInteger>(owner);
    }

    public async Task<BigInteger> GetNextTokenIdAsync()
    {
        var contract = readOnlyWeb3.Eth.GetContract(Abi, nftContractAddress);
        return await contract.GetFunction("nextTokenId").CallAsync<BigInteger>();
    }

    public async Task<NpcDataDTO> GetNpcAsync(BigInteger tokenId)
    {
        var contract = readOnlyWeb3.Eth.GetContract(Abi, nftContractAddress);
        var fn = contract.GetFunction("getNpc");
        var wrapped = await fn.CallDeserializingToObjectAsync<GetNpcOutputDTO>(tokenId);
        return wrapped?.Data;
    }
    
    public async Task<List<OwnedNpc>> EnumerateOwnedNpcsAsync(
        string owner, CancellationToken ct = default)
    {
        var result = new List<OwnedNpc>();
        if (string.IsNullOrWhiteSpace(owner)) return result;

        var next = await GetNextTokenIdAsync();
        if (next <= BigInteger.One) return result;

        // Skip the enumeration entirely if the owner holds nothing.
        var balance = await BalanceOfAsync(owner);
        if (balance == BigInteger.Zero) return result;

        var contract = readOnlyWeb3.Eth.GetContract(Abi, nftContractAddress);
        
        var ownerOfFn = contract.GetFunction("ownerOf");
        var getNpcFn  = contract.GetFunction("getNpc");

        for (BigInteger id = BigInteger.One; id < next; id += BigInteger.One)
        {
            ct.ThrowIfCancellationRequested();
            string holder;
            try
            {
                holder = await ownerOfFn.CallAsync<string>(id);
            }
            catch
            {
                // Burned / nonexistent tokenId reverts; skip.
                continue;
            }

            if (!string.Equals(holder, owner, StringComparison.OrdinalIgnoreCase)) continue;

            NpcDataDTO data;
            try
            {
                var wrapped = await getNpcFn.CallDeserializingToObjectAsync<GetNpcOutputDTO>(id);
                data = wrapped?.Data;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[NpcCharacterContractClient] getNpc({id}) failed: {ex.Message}");
                continue;
            }
            if (data == null) continue;

            result.Add(new OwnedNpc(id, data));
            if (result.Count >= (int)balance) break; // early exit
        }

        return result;
    }

    public async Task<string> BindPaymentWalletAsync(BigInteger tokenId, string walletAddress)
    {
        var data = readOnlyWeb3.Eth.GetContract(Abi, nftContractAddress)
            .GetFunction("bindPaymentWallet")
            .GetData(tokenId, walletAddress)
            .HexToByteArray();
        return await SendOwnerTxAsync(
            to: nftContractAddress,
            value: BigInteger.Zero,
            data: data,
            gas: new HexBigInteger(120000),
            waitReceipt: true,
            label: $"bindPaymentWallet(tokenId={tokenId})");
    }

    public async Task<string> ClearPaymentWalletAsync(BigInteger tokenId)
    {
        var data = readOnlyWeb3.Eth.GetContract(Abi, nftContractAddress)
            .GetFunction("clearPaymentWallet")
            .GetData(tokenId)
            .HexToByteArray();
        return await SendOwnerTxAsync(
            to: nftContractAddress,
            value: BigInteger.Zero,
            data: data,
            gas: new HexBigInteger(80000),
            waitReceipt: true,
            label: $"clearPaymentWallet(tokenId={tokenId})");
    }

    public async Task<string> TransferUsdcFromOwnerAsync(string toAddress, BigInteger amount)
    {
        if (string.IsNullOrWhiteSpace(toAddress))
            throw new ArgumentException("toAddress is required", nameof(toAddress));
        if (amount <= BigInteger.Zero)
            throw new ArgumentException("amount must be positive", nameof(amount));

        var data = readOnlyWeb3.Eth.GetContract(Erc20TransferAbi, Erc20UsdcHelper.ArcUsdcAddress)
            .GetFunction("transfer")
            .GetData(toAddress, amount)
            .HexToByteArray();
        return await SendOwnerTxAsync(
            to: Erc20UsdcHelper.ArcUsdcAddress,
            value: BigInteger.Zero,
            data: data,
            gas: new HexBigInteger(120000),
            waitReceipt: true,
            label: $"USDC transfer → {Shorten(toAddress)} ({amount})");
    }

    /// <summary>
    /// legacy
    /// </summary>
    public async Task<string> ExecuteTbaAsOwnerAsync(
        string tbaAddress,
        string target,
        BigInteger value,
        byte[] data,
        HexBigInteger gas,
        bool waitForReceipt)
    {
        if (string.IsNullOrWhiteSpace(tbaAddress))
            throw new ArgumentException("tbaAddress is required", nameof(tbaAddress));
        if (string.IsNullOrWhiteSpace(target))
            throw new ArgumentException("target is required", nameof(target));

        var executeCalldata = readOnlyWeb3.Eth.GetContract(Erc6551AccountAbi, tbaAddress)
            .GetFunction("execute")
            .GetData(target, value, data ?? Array.Empty<byte>(), (byte)0)
            .HexToByteArray();
        return await SendOwnerTxAsync(
            to: tbaAddress,
            value: BigInteger.Zero,
            data: executeCalldata,
            gas: gas,
            waitReceipt: waitForReceipt,
            label: $"TBA.execute → {Shorten(target)}");
    }

    // ---------------- shared owner-tx submission ----------------

    /// <summary>
    /// Single owner-side write path. Picks between local-signing (Inspector PK) and
    /// the WalletLoginService bridge (MetaMask popup) based on <see cref="loginViaAuth"/>.
    ///
    /// Receipt polling always uses readOnlyWeb3 so it works the same for both paths.
    /// </summary>
    private async Task<string> SendOwnerTxAsync(
        string to, BigInteger value, byte[] data, HexBigInteger gas,
        bool waitReceipt, string label)
    {
        var chainId = await GetChainIdAsync();
        await ownerTxGate.WaitAsync();
        try
        {
            string txHash;
            if (loginViaAuth)
            {
                txHash = await SendViaBridgeAsync(chainId, to, value, data, gas, label);
            }
            else
            {
                txHash = await SendViaLocalKeyAsync(chainId, to, value, data, gas);
            }
            if (waitReceipt) await WaitReceiptAsync(readOnlyWeb3, txHash);
            return txHash;
        }
        finally
        {
            ownerTxGate.Release();
        }
    }

    private async Task<string> SendViaBridgeAsync(
        long chainId, string to, BigInteger value, byte[] data, HexBigInteger gas, string label)
    {
        var service = WalletLoginService.Instance;
        if (service == null)
            throw new InvalidOperationException(
                $"{name}: loginViaAuth=true but WalletLoginService Singleton is missing. " +
                "Add a GameObject with WalletLoginService to the scene.");

        service.ConfigureChainId(chainId);
        var session = await service.EnsureLoggedInAsync(
            bridgeSiweStatement,
            bridgePreferredPort,
            TimeSpan.FromMinutes(Math.Max(1, bridgeSessionTtlMinutes)),
            CancellationToken.None,
            persistentBridge: true).ConfigureAwait(true);

        var req = new WalletTxRequest
        {
            from = session.wallet,
            to = to,
            value = "0x" + value.ToString("x"),
            data = data == null || data.Length == 0 ? "0x" : "0x" + data.ToHex(),
            gas = "0x" + gas.Value.ToString("x"),
            chainId = chainId,
            label = label,
        };
        return await service.SendOwnerTransactionAsync(req).ConfigureAwait(true);
    }

    private async Task<string> SendViaLocalKeyAsync(
        long chainId, string to, BigInteger value, byte[] data, HexBigInteger gas)
    {
        if (string.IsNullOrWhiteSpace(nftOwnerPrivateKey))
            throw new InvalidOperationException(
                $"{name} requires nftOwnerPrivateKey for owner-side writes " +
                "(or enable loginViaAuth to route through MetaMask).");

        var account = new Account(nftOwnerPrivateKey.Trim(), chainId);
        var web3 = new Web3(account, rpcUrl);
        var txInput = new TransactionInput
        {
            From = account.Address,
            To = to,
            Value = new HexBigInteger(value),
            Data = data == null || data.Length == 0 ? "0x" : "0x" + data.ToHex(),
            Gas = gas,
        };
        return await web3.Eth.TransactionManager.SendTransactionAsync(txInput).ConfigureAwait(true);
    }

    private static async Task WaitReceiptAsync(Web3 web3, string txHash)
    {
        while (true)
        {
            var receipt = await web3.Eth.Transactions.GetTransactionReceipt.SendRequestAsync(txHash);
            if (receipt != null)
            {
                if (receipt.Status == null || receipt.Status.Value == BigInteger.Zero)
                    throw new InvalidOperationException($"tx {txHash} reverted");
                return;
            }
            await Task.Delay(800);
        }
    }

    private static string Shorten(string addr)
    {
        if (string.IsNullOrEmpty(addr) || addr.Length < 12) return addr ?? "";
        return addr.Substring(0, 6) + "…" + addr.Substring(addr.Length - 4);
    }
}
