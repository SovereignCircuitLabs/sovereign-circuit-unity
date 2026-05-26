using System;
using System.Numerics;
using System.Threading.Tasks;
using ArcTrading.Nanopayment;
using Nethereum.Hex.HexTypes;
using Nethereum.Web3;
using Nethereum.Web3.Accounts;
using UnityEngine;

public class ArcTradingContractClient : MonoBehaviour
{
    [SerializeField] private string rpcUrl = "https://rpc.testnet.arc.network";
    public string RpcUrl => rpcUrl;
    // GamePayment contract address
    [SerializeField] private string contractAddress = "0x3eCc01Be94D34f76aab3C1d5Ba23001d577Cd996";
    public string ContractAddress => contractAddress;
    [SerializeField] private string privateKey;
    [SerializeField] private float initialUsdcCapital = 0.5f;
    public string PrivateKey => privateKey;
    
    [Header("NPC NFT Identity")]
    [SerializeField] private ulong nftTokenId;
    [SerializeField] private NpcPaymentWalletService npcPaymentWalletService;

    [Tooltip("If true, ignore the privateKey field above and lazy-resolve the trader signing key " +
             "from the on-chain bound payment wallet. Demo-friendly (no manual key creation) but " +
             "expands operator-key leak blast radius and abandons funds at the old address on rebind/transfer.")]
    [SerializeField] private bool useBoundWalletAsTrader;

    public BigInteger NftTokenId => new BigInteger(nftTokenId);
    public NpcPaymentWalletService NpcPaymentWalletService => npcPaymentWalletService;

    private string cachedTraderPrivateKey;
    private ulong  cachedTraderKeyVersion;

    private const string Abi = @"[
      {""inputs"":[{""internalType"":""address"",""name"":""_usdc"",""type"":""address""}],""stateMutability"":""nonpayable"",""type"":""constructor""},
      {""inputs"":[],""name"":""usdc"",""outputs"":[{""internalType"":""contract IERC20"",""name"":"""",""type"":""address""}],""stateMutability"":""view"",""type"":""function""},
      {""inputs"":[],""name"":""owner"",""outputs"":[{""internalType"":""address"",""name"":"""",""type"":""address""}],""stateMutability"":""view"",""type"":""function""},
      {""inputs"":[{""internalType"":""address"",""name"":"""",""type"":""address""}],""name"":""balances"",""outputs"":[{""internalType"":""uint256"",""name"":"""",""type"":""uint256""}],""stateMutability"":""view"",""type"":""function""},
      {""inputs"":[{""internalType"":""address"",""name"":""newOwner"",""type"":""address""}],""name"":""transferOwnership"",""outputs"":[],""stateMutability"":""nonpayable"",""type"":""function""},
      {""inputs"":[{""internalType"":""address"",""name"":""to"",""type"":""address""},{""internalType"":""uint256"",""name"":""amount"",""type"":""uint256""},{""internalType"":""string"",""name"":""reason"",""type"":""string""}],""name"":""payPlayer"",""outputs"":[],""stateMutability"":""nonpayable"",""type"":""function""},
      {""inputs"":[{""internalType"":""address"",""name"":""from"",""type"":""address""},{""internalType"":""address"",""name"":""to"",""type"":""address""},{""internalType"":""uint256"",""name"":""amount"",""type"":""uint256""},{""internalType"":""string"",""name"":""reason"",""type"":""string""}],""name"":""transferFrom"",""outputs"":[],""stateMutability"":""nonpayable"",""type"":""function""},
      {""inputs"":[{""internalType"":""uint256"",""name"":""amount"",""type"":""uint256""}],""name"":""deposit"",""outputs"":[],""stateMutability"":""nonpayable"",""type"":""function""},
      {""inputs"":[{""internalType"":""uint256"",""name"":""amount"",""type"":""uint256""}],""name"":""withdraw"",""outputs"":[],""stateMutability"":""nonpayable"",""type"":""function""},
      {""inputs"":[{""internalType"":""address"",""name"":""account"",""type"":""address""}],""name"":""getBalance"",""outputs"":[{""internalType"":""uint256"",""name"":"""",""type"":""uint256""}],""stateMutability"":""view"",""type"":""function""},
      {""inputs"":[],""name"":""getContractBalance"",""outputs"":[{""internalType"":""uint256"",""name"":"""",""type"":""uint256""}],""stateMutability"":""view"",""type"":""function""}
    ]";

    private Web3 readOnlyWeb3;
    
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
        var signer = await npcPaymentWalletService.EnsureBoundAsync(NftTokenId);
        if (useBoundWalletAsTrader) CacheBoundSigner(signer);
        return signer;
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
            Debug.Log($"[{name}] useBoundWalletAsTrader=true → trader wallet is {signer.Address}. " +
                      "Fund this address with ARC (gas) + USDC before chain actions can succeed.");
        }
    }

    public async Task<decimal> GetWalletBalanceUSDCAsync()
    {
        var web3 = await CreateSignedWeb3Async();
        var owner = web3.TransactionManager.Account.Address;
        var balance = await Erc20UsdcHelper.GetBalanceAsync(web3, owner);
        return FromUsdc(balance);
    }

    public async Task<decimal> GetVaultBalanceUSDCAsync()
    {
        var web3 = await CreateSignedWeb3Async();
        var contract = readOnlyWeb3.Eth.GetContract(Abi, contractAddress);
        var getBalance = contract.GetFunction("getBalance");
        var balance = await getBalance.CallAsync<BigInteger>(web3.TransactionManager.Account.Address);
        return FromUsdc(balance);
    }

    public async Task<decimal> GetContractTotalUsdcAsync()
    {
        var contract = readOnlyWeb3.Eth.GetContract(Abi, contractAddress);
        var fn = contract.GetFunction("getContractBalance");
        var balance = await fn.CallAsync<BigInteger>();
        return FromUsdc(balance);
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

    public async Task<string> DepositAsync(decimal amountUSDC, bool nanopayment = false)
    {
        var web3 = await CreateSignedWeb3Async();
        var amount = Erc20UsdcHelper.ParseUsdc(amountUSDC);

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
                await arcNanopayment.ApproveIfNeededThenGatewayDepositAsync(0.5m);

            var content = await arcNanopayment.FetchPaywalledResourceAsync(
                arcNanopayment.x402ServerUrl,
                NftTokenId,
                npcPaymentWalletService,
                nanopaymentCap);
            RecordX402Outflow(arcNanopayment.LastPaidAmountSmallestUnits);
            return content;
        }

        // GamePayment.deposit pulls USDC via safeTransferFrom, so it needs allowance first.
        await Erc20UsdcHelper.EnsureApprovalAsync(web3, contractAddress, amount);

        var contract = web3.Eth.GetContract(Abi, contractAddress);
        var deposit = contract.GetFunction("deposit");
        var gas = new HexBigInteger(200000);

        return await deposit.SendTransactionAsync(
            web3.TransactionManager.Account.Address, gas, null, amount);
    }

    public async Task<string> WithdrawAsync(decimal amountUSDC)
    {
        var web3 = await CreateSignedWeb3Async();
        var amount = Erc20UsdcHelper.ParseUsdc(amountUSDC);

        var contract = web3.Eth.GetContract(Abi, contractAddress);
        var withdraw = contract.GetFunction("withdraw");
        var gas = new HexBigInteger(150000);

        return await withdraw.SendTransactionAsync(
            web3.TransactionManager.Account.Address, gas, null, amount);
    }

    public async Task<string> PayPlayerAsync(string playerAddress, decimal amountUSDC, string reason)
    {
        var web3 = await CreateSignedWeb3Async();
        var contract = web3.Eth.GetContract(Abi, contractAddress);
        var payPlayer = contract.GetFunction("payPlayer");
        var amount = Erc20UsdcHelper.ParseUsdc(amountUSDC);
        var gas = new HexBigInteger(200000);

        return await payPlayer.SendTransactionAsync(
            web3.TransactionManager.Account.Address,
            gas,
            null,
            playerAddress,
            amount,
            reason);
    }

    public async Task<string> TransferFromAsync(
        string fromAddress, string toAddress, decimal amountUSDC, string reason)
    {
        var web3 = await CreateSignedWeb3Async();
        var amount = Erc20UsdcHelper.ParseUsdc(amountUSDC);
        var signerAddr = web3.TransactionManager.Account.Address;

        var isSelfFrom = !string.IsNullOrEmpty(fromAddress)
            && string.Equals(signerAddr, fromAddress, StringComparison.OrdinalIgnoreCase);
        if (isSelfFrom)
            await Erc20UsdcHelper.EnsureApprovalAsync(web3, contractAddress, amount);

        var contract = web3.Eth.GetContract(Abi, contractAddress);
        var fn = contract.GetFunction("transferFrom");
        var gas = new HexBigInteger(200000);

        return await fn.SendTransactionAsync(
            signerAddr, gas, null,
            fromAddress, toAddress, amount, reason);
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

            var signer = await npcPaymentWalletService.EnsureBoundAsync(NftTokenId);
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
}
