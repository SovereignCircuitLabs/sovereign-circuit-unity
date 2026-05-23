using System;
using System.Numerics;
using System.Threading.Tasks;
using Nethereum.Hex.HexTypes;
using Nethereum.Web3;
using Nethereum.Web3.Accounts;
using UnityEngine;

public class ArcTradingContractClient : MonoBehaviour
{
    [SerializeField] private string rpcUrl = "https://rpc.testnet.arc.network";
    public string RpcUrl => rpcUrl;
    [SerializeField] private string contractAddress = "0x211796A3AE23F223F0c6536875595Cbbcb18E973";
    public string ContractAddress => contractAddress;
    [SerializeField] private string privateKey;
    public string PrivateKey => privateKey;

    private const string Abi = @"[
      {""inputs"":[{""internalType"":""address"",""name"":""_usdc"",""type"":""address""}],""stateMutability"":""nonpayable"",""type"":""constructor""},
      {""inputs"":[],""name"":""usdc"",""outputs"":[{""internalType"":""contract IERC20"",""name"":"""",""type"":""address""}],""stateMutability"":""view"",""type"":""function""},
      {""inputs"":[],""name"":""owner"",""outputs"":[{""internalType"":""address"",""name"":"""",""type"":""address""}],""stateMutability"":""view"",""type"":""function""},
      {""inputs"":[{""internalType"":""address"",""name"":"""",""type"":""address""}],""name"":""balances"",""outputs"":[{""internalType"":""uint256"",""name"":"""",""type"":""uint256""}],""stateMutability"":""view"",""type"":""function""},
      {""inputs"":[{""internalType"":""address"",""name"":""newOwner"",""type"":""address""}],""name"":""transferOwnership"",""outputs"":[],""stateMutability"":""nonpayable"",""type"":""function""},
      {""inputs"":[{""internalType"":""address"",""name"":""to"",""type"":""address""},{""internalType"":""uint256"",""name"":""amount"",""type"":""uint256""},{""internalType"":""string"",""name"":""reason"",""type"":""string""}],""name"":""payPlayer"",""outputs"":[],""stateMutability"":""nonpayable"",""type"":""function""},
      {""inputs"":[{""internalType"":""uint256"",""name"":""amount"",""type"":""uint256""}],""name"":""deposit"",""outputs"":[],""stateMutability"":""nonpayable"",""type"":""function""},
      {""inputs"":[{""internalType"":""uint256"",""name"":""amount"",""type"":""uint256""}],""name"":""withdraw"",""outputs"":[],""stateMutability"":""nonpayable"",""type"":""function""},
      {""inputs"":[{""internalType"":""address"",""name"":""account"",""type"":""address""}],""name"":""getBalance"",""outputs"":[{""internalType"":""uint256"",""name"":"""",""type"":""uint256""}],""stateMutability"":""view"",""type"":""function""},
      {""inputs"":[],""name"":""getContractBalance"",""outputs"":[{""internalType"":""uint256"",""name"":"""",""type"":""uint256""}],""stateMutability"":""view"",""type"":""function""}
    ]";

    private Web3 readOnlyWeb3;

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

    public async Task<string> DepositAsync(decimal amountUSDC)
    {
        var web3 = await CreateSignedWeb3Async();
        var amount = Erc20UsdcHelper.ParseUsdc(amountUSDC);

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
        var contract = web3.Eth.GetContract(Abi, contractAddress);
        var withdraw = contract.GetFunction("withdraw");
        var amount = Erc20UsdcHelper.ParseUsdc(amountUSDC);
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

    private async Task<Web3> CreateSignedWeb3Async()
    {
        if (string.IsNullOrWhiteSpace(privateKey))
        {
            throw new InvalidOperationException($"{name} requires a private key for NPC chain actions.");
        }

        var chainId = await readOnlyWeb3.Eth.ChainId.SendRequestAsync();
        var account = new Account(privateKey.Trim(), chainId.Value);
        return new Web3(account, rpcUrl);
    }

    private static decimal FromUsdc(BigInteger amount)
    {
        return (decimal)amount / 1_000_000m;
    }
}
