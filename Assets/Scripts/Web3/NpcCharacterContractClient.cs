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
#if UNITY_WEBGL && !UNITY_EDITOR
        ArcTrading.Crypto.EthRawTxSender.ConfigureRpc(rpcUrl);
#endif
    }

    public async Task<long> GetChainIdAsync()
    {
        if (CachedChainId.HasValue) return CachedChainId.Value;
#if UNITY_WEBGL && !UNITY_EDITOR
        var id = await ArcTrading.Crypto.WebGLPublicRpc.GetChainIdAsync();
        CachedChainId = id;
        ArcTrading.Crypto.EthRawTxSender.ConfigureChainId(id);
        return id;
#else
        var id = await Web3RpcRetry.RunAsync(async () =>
            (long)(await readOnlyWeb3.Eth.ChainId.SendRequestAsync()).Value,
            label: "NpcCharacter.chainId");
        CachedChainId = id;
        return id;
#endif
    }

    public async Task<(string wallet, ulong version)> GetPaymentBindingAsync(BigInteger tokenId)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        return await ArcTrading.WebGL.WebGLChainApi.GetPaymentBindingAsync(tokenId);
#else
        return await Web3RpcRetry.RunAsync(async () =>
        {
            var contract = readOnlyWeb3.Eth.GetContract(Abi, nftContractAddress);
            var fn = contract.GetFunction("getPaymentBinding");
            var dto = await fn.CallDeserializingToObjectAsync<PaymentBindingDTO>(tokenId);
            return (dto.Wallet, dto.Version);
        }, label: $"NpcCharacter.getPaymentBinding({tokenId})");
#endif
    }

    public async Task<string> OwnerOfAsync(BigInteger tokenId)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        return await ArcTrading.WebGL.WebGLChainApi.NpcOwnerOfAsync(tokenId);
#else
        return await Web3RpcRetry.RunAsync(() =>
        {
            var contract = readOnlyWeb3.Eth.GetContract(Abi, nftContractAddress);
            return contract.GetFunction("ownerOf").CallAsync<string>(tokenId);
        }, label: $"NpcCharacter.ownerOf({tokenId})");
#endif
    }

    public async Task<bool> ExistsAsync(BigInteger tokenId)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        return await ArcTrading.WebGL.WebGLChainApi.NpcExistsAsync(tokenId);
#else
        return await Web3RpcRetry.RunAsync(() =>
        {
            var contract = readOnlyWeb3.Eth.GetContract(Abi, nftContractAddress);
            return contract.GetFunction("exists").CallAsync<bool>(tokenId);
        }, label: $"NpcCharacter.exists({tokenId})");
#endif
    }

    public async Task<BigInteger> BalanceOfAsync(string owner)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        return await ArcTrading.WebGL.WebGLChainApi.NpcBalanceOfAsync(owner);
#else
        return await Web3RpcRetry.RunAsync(() =>
        {
            var contract = readOnlyWeb3.Eth.GetContract(Abi, nftContractAddress);
            return contract.GetFunction("balanceOf").CallAsync<BigInteger>(owner);
        }, label: $"NpcCharacter.balanceOf({owner})");
#endif
    }

    public async Task<BigInteger> GetNextTokenIdAsync()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        return await ArcTrading.WebGL.WebGLChainApi.NpcNextTokenIdAsync();
#else
        return await Web3RpcRetry.RunAsync(() =>
        {
            var contract = readOnlyWeb3.Eth.GetContract(Abi, nftContractAddress);
            return contract.GetFunction("nextTokenId").CallAsync<BigInteger>();
        }, label: "NpcCharacter.nextTokenId");
#endif
    }

    public async Task<NpcDataDTO> GetNpcAsync(BigInteger tokenId)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        var npc = await ArcTrading.WebGL.WebGLChainApi.GetNpcAsync(tokenId);
        return ConvertNpcData(npc);
#else
        return await Web3RpcRetry.RunAsync(async () =>
        {
            var contract = readOnlyWeb3.Eth.GetContract(Abi, nftContractAddress);
            var fn = contract.GetFunction("getNpc");
            var wrapped = await fn.CallDeserializingToObjectAsync<GetNpcOutputDTO>(tokenId);
            return wrapped?.Data;
        }, label: $"NpcCharacter.getNpc({tokenId})");
#endif
    }

#if UNITY_WEBGL && !UNITY_EDITOR
    private static NpcDataDTO ConvertNpcData(ArcTrading.WebGL.WebGLChainApi.NpcData src)
    {
        if (src == null) return null;
        var dto = new NpcDataDTO
        {
            NpcName = src.npcName,
            MetadataURI = src.metadataURI,
            Archetype = src.archetype,
            RiskLevel = src.riskLevel,
            Level = src.level,
            Reputation = src.reputation,
        };
        if (src.portfolio != null)
        {
            dto.Portfolio = new PortfolioConfigDTO
            {
                LivingNeedsWeightBps = src.portfolio.livingNeedsWeightBps,
                ReserveWeightBps = src.portfolio.reserveWeightBps,
                TradingWeightBps = src.portfolio.tradingWeightBps,
                MinimumLivingBudgetUSDC = ParseUlong(src.portfolio.minimumLivingBudgetUSDC),
                MinimumReserveBudgetUSDC = ParseUlong(src.portfolio.minimumReserveBudgetUSDC),
                RebalanceIntervalSeconds = src.portfolio.rebalanceIntervalSeconds,
                ChainActionCooldownSeconds = src.portfolio.chainActionCooldownSeconds,
                MinTradeUSDC = ParseUlong(src.portfolio.minTradeUSDC),
                MaxTradeUSDC = ParseUlong(src.portfolio.maxTradeUSDC),
            };
        }
        return dto;
    }

    private static ulong ParseUlong(string s)
        => string.IsNullOrEmpty(s) ? 0UL : ulong.Parse(s);
#endif

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

        for (BigInteger id = BigInteger.One; id < next; id += BigInteger.One)
        {
            ct.ThrowIfCancellationRequested();
            string holder;
            try
            {
                // OwnerOfAsync internally branches: Nethereum on Desktop/Editor,
                // /npc-character/:tokenId/owner on WebGL. Burned/nonexistent
                // tokens revert (Desktop) or 5xx (WebGL) — caught below.
                holder = await OwnerOfAsync(id);
            }
            catch (OperationCanceledException) { throw; }
            catch
            {
                continue;
            }

            if (!string.Equals(holder, owner, StringComparison.OrdinalIgnoreCase)) continue;

            NpcDataDTO data;
            try
            {
                data = await GetNpcAsync(id);
            }
            catch (OperationCanceledException) { throw; }
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
#if UNITY_WEBGL && !UNITY_EDITOR
        var data = HexToBytes(ArcTrading.Crypto.WebGLAbiBridge.EncodeFunctionData(
            Abi,
            "bindPaymentWallet",
            ArcTrading.Crypto.EthRawTxSender.JsonArgs(tokenId, walletAddress)));
#else
        var data = readOnlyWeb3.Eth.GetContract(Abi, nftContractAddress)
            .GetFunction("bindPaymentWallet")
            .GetData(tokenId, walletAddress)
            .HexToByteArray();
#endif
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
#if UNITY_WEBGL && !UNITY_EDITOR
        var data = HexToBytes(ArcTrading.Crypto.WebGLAbiBridge.EncodeFunctionData(
            Abi,
            "clearPaymentWallet",
            ArcTrading.Crypto.EthRawTxSender.JsonArgs(tokenId)));
#else
        var data = readOnlyWeb3.Eth.GetContract(Abi, nftContractAddress)
            .GetFunction("clearPaymentWallet")
            .GetData(tokenId)
            .HexToByteArray();
#endif
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

#if UNITY_WEBGL && !UNITY_EDITOR
        // Server's admin wallet pays; the "owner" identity is whoever the server
        // is configured to act as. Semantically different from Desktop where the
        // NFT owner's PK pays — call sites should still see funds land at toAddress.
        var data = HexToBytes(ArcTrading.Crypto.WebGLAbiBridge.EncodeFunctionData(
            Erc20TransferAbi,
            "transfer",
            ArcTrading.Crypto.EthRawTxSender.JsonArgs(toAddress, amount)));
#else
        var data = readOnlyWeb3.Eth.GetContract(Erc20TransferAbi, Erc20UsdcHelper.ArcUsdcAddress)
            .GetFunction("transfer")
            .GetData(toAddress, amount)
            .HexToByteArray();
#endif
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

#if UNITY_WEBGL && !UNITY_EDITOR
        // The server's /tba/execute route packs (target,value,data,operation) into
        // the TBA's execute() itself, so we forward the inner call rather than the
        // pre-encoded execute calldata.
        var executeCalldata = HexToBytes(ArcTrading.Crypto.WebGLAbiBridge.EncodeFunctionData(
            Erc6551AccountAbi,
            "execute",
            ArcTrading.Crypto.EthRawTxSender.JsonArgs(
                target,
                value,
                ArcTrading.Crypto.EthRawTxSender.BytesToHex(data),
                0)));
#else
        var executeCalldata = readOnlyWeb3.Eth.GetContract(Erc6551AccountAbi, tbaAddress)
            .GetFunction("execute")
            .GetData(target, value, data ?? Array.Empty<byte>(), (byte)0)
            .HexToByteArray();
#endif
        return await SendOwnerTxAsync(
            to: tbaAddress,
            value: BigInteger.Zero,
            data: executeCalldata,
            gas: gas,
            waitReceipt: waitForReceipt,
            label: $"TBA.execute → {Shorten(target)}");
    }

#if UNITY_WEBGL && !UNITY_EDITOR
    private static byte[] HexToBytes(string hex)
    {
        if (string.IsNullOrEmpty(hex) || hex == "0x") return Array.Empty<byte>();
        var clean = hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? hex.Substring(2) : hex;
        if ((clean.Length & 1) == 1) clean = "0" + clean;
        var bytes = new byte[clean.Length / 2];
        for (int i = 0; i < bytes.Length; i++)
            bytes[i] = Convert.ToByte(clean.Substring(i * 2, 2), 16);
        return bytes;
    }
#endif

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
        Debug.Log($"[NpcCharacterContractClient] SendOwnerTxAsync({label}): entering, awaiting chainId");
        var chainId = await GetChainIdAsync();
        Debug.Log($"[NpcCharacterContractClient] SendOwnerTxAsync({label}): chainId={chainId}, awaiting ownerTxGate");
        await ownerTxGate.WaitAsync();
        Debug.Log($"[NpcCharacterContractClient] SendOwnerTxAsync({label}): ownerTxGate acquired");
        try
        {
            string txHash;
            if (loginViaAuth)
            {
                Debug.Log($"[NpcCharacterContractClient] SendOwnerTxAsync({label}): SendViaBridgeAsync start");
                txHash = await SendViaBridgeAsync(chainId, to, value, data, gas, label);
                Debug.Log($"[NpcCharacterContractClient] SendOwnerTxAsync({label}): SendViaBridgeAsync returned tx={txHash}");
            }
            else
            {
                txHash = await SendViaLocalKeyAsync(chainId, to, value, data, gas);
            }
#if UNITY_WEBGL && !UNITY_EDITOR
            // Nethereum's RpcClient → Newtonsoft path trips IL2CPP stripping on
            // RpcParametersJsonConverter under WebGL, so we route the receipt
            // poll through the server's plain-JSON GET /tx/receipt/:hash. The
            // local-key path (EthRawTxSender → /tx/send-raw) already had viem
            // waitForTransactionReceipt on the server, so this loop typically
            // resolves on the first iteration; the bridge path (MetaMask
            // eth_sendTransaction) returns immediately on broadcast, so the
            // poll is what actually waits for confirmation there. Without this,
            // post-bind verifiers (e.g. NpcPaymentWalletService) read 0x0,
            // think the bind failed, and re-bind — abandoning the previous
            // operator wallet and any funds on it.
            if (waitReceipt)
            {
                await WaitReceiptWebGLAsync(txHash, label);
                Debug.Log($"[NpcCharacterContractClient] SendOwnerTxAsync({label}): WaitReceiptWebGLAsync returned");
            }
#else
            if (waitReceipt) await WaitReceiptAsync(readOnlyWeb3, txHash);
#endif
            Debug.Log($"[NpcCharacterContractClient] SendOwnerTxAsync({label}): returning txHash");
            return txHash;
        }
        finally
        {
            Debug.Log($"[NpcCharacterContractClient] SendOwnerTxAsync({label}): finally — releasing ownerTxGate");
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
#if UNITY_WEBGL && !UNITY_EDITOR
            throw new InvalidOperationException(
                $"{name}: WebGL owner-side writes must go through MetaMask. " +
                "Set loginViaAuth=true on this component; embedding nftOwnerPrivateKey " +
                "in a WebGL build would ship the private key inside the bundle.");
#else
            throw new InvalidOperationException(
                $"{name} requires nftOwnerPrivateKey for owner-side writes " +
                "(or enable loginViaAuth to route through MetaMask).");
#endif

#if UNITY_WEBGL && !UNITY_EDITOR
        ArcTrading.Crypto.EthRawTxSender.ConfigureChainId(chainId);
        var pk = nftOwnerPrivateKey.Trim();
        var from = ArcTrading.Crypto.EthCryptoBackend.Current.DeriveAddress(pk);
        return await ArcTrading.Crypto.EthRawTxSender.SendLegacyAsync(
            from,
            pk,
            to,
            value,
            ArcTrading.Crypto.EthRawTxSender.BytesToHex(data),
            gas.Value).ConfigureAwait(true);
#else
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
#endif
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
            await ArcTrading.Crypto.WebGLAsyncBridge.DelayMsAsync(800);
        }
    }

#if UNITY_WEBGL && !UNITY_EDITOR
    private static async Task WaitReceiptWebGLAsync(string txHash, string label)
    {
        const int pollIntervalMs = 800;
        // Hard cap so a permanently-dropped tx doesn't wedge a UI flow forever.
        // 5 minutes is well past any realistic confirmation latency on Arc testnet.
        const int maxAttempts = 5 * 60 * 1000 / pollIntervalMs;
        // Surface progress every ~8s so callers can see the poll is alive and the
        // tx hash they need to inspect on a block explorer.
        const int logEveryAttempts = 10;

        // Tolerate a short burst of Unknown right after broadcast — the node may
        // not have indexed the tx yet. Beyond that, Unknown means the node has
        // never heard of this hash (most likely dropped/replaced), and polling
        // longer is just wasting time. Pending (tx in mempool) still gets the
        // full maxAttempts budget.
        const int unknownGraceAttempts = 8; // ~6.4s

        Debug.Log($"[NpcCharacterContractClient] {label} broadcast (tx={txHash}); waiting for receipt…");
        int consecutiveUnknown = 0;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            ArcTrading.WebGL.WebGLChainApi.TxReceiptStatus status;
            try
            {
                (status, _) = await ArcTrading.WebGL.WebGLChainApi
                    .GetTxReceiptAsync(txHash).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[NpcCharacterContractClient] {label} receipt poll attempt {attempt} threw: {ex.Message} — retrying.");
                await ArcTrading.Crypto.WebGLAsyncBridge.DelayMsAsync(pollIntervalMs);
                continue;
            }

            if (status == ArcTrading.WebGL.WebGLChainApi.TxReceiptStatus.Success)
            {
                Debug.Log($"[NpcCharacterContractClient] {label} confirmed (tx={txHash}) after {attempt} poll(s).");
                return;
            }
            if (status == ArcTrading.WebGL.WebGLChainApi.TxReceiptStatus.Reverted)
                throw new InvalidOperationException($"{label} reverted (tx={txHash})");

            if (status == ArcTrading.WebGL.WebGLChainApi.TxReceiptStatus.Unknown)
            {
                consecutiveUnknown++;
                if (consecutiveUnknown >= unknownGraceAttempts)
                    throw new InvalidOperationException(
                        $"{label}: node has no record of tx {txHash} after {consecutiveUnknown * pollIntervalMs / 1000}s. " +
                        "It was likely dropped from the mempool or replaced. Check the wallet's tx history.");
            }
            else
            {
                consecutiveUnknown = 0;
            }

            if (attempt % logEveryAttempts == 0)
                Debug.Log($"[NpcCharacterContractClient] {label} still {status} after {attempt * pollIntervalMs / 1000}s (tx={txHash}).");

            await ArcTrading.Crypto.WebGLAsyncBridge.DelayMsAsync(pollIntervalMs);
        }
        throw new InvalidOperationException(
            $"{label} receipt did not arrive within {maxAttempts * pollIntervalMs / 1000}s (tx={txHash}). " +
            "Check the block explorer — the tx may have been dropped or replaced.");
    }
#endif

    private static string Shorten(string addr)
    {
        if (string.IsNullOrEmpty(addr) || addr.Length < 12) return addr ?? "";
        return addr.Substring(0, 6) + "…" + addr.Substring(addr.Length - 4);
    }
}
