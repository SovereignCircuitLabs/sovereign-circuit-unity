using System;
using System.Globalization;
using System.Numerics;
using System.Threading.Tasks;
using Nethereum.Hex.HexTypes;
using Nethereum.Web3;
using Nethereum.Web3.Accounts;
using UnityEngine;

public class ArcTradingContractClient : MonoBehaviour
{
    [SerializeField] private string rpcUrl = "https://rpc.testnet.arc.network";
    [SerializeField] private string contractAddress = "0x5BE62566a1748de167165fB91C67cFB2BD37bFf0";
    [SerializeField] private string privateKey;

    private const string Abi = @"[
      {""inputs"":[{""internalType"":""address"",""name"":"""",""type"":""address""}],""name"":""balances"",""outputs"":[{""internalType"":""uint256"",""name"":"""",""type"":""uint256""}],""stateMutability"":""view"",""type"":""function""},
      {""inputs"":[],""name"":""deposit"",""outputs"":[],""stateMutability"":""payable"",""type"":""function""},
      {""stateMutability"":""payable"",""type"":""fallback"",""inputs"":[{""internalType"":""bytes"",""name"":""input"",""type"":""bytes""}],""outputs"":[{""internalType"":""bytes"",""name"":""output"",""type"":""bytes""}]},
      {""inputs"":[{""internalType"":""address"",""name"":""account"",""type"":""address""}],""name"":""getBalance"",""outputs"":[{""internalType"":""uint256"",""name"":"""",""type"":""uint256""}],""stateMutability"":""view"",""type"":""function""},
      {""inputs"":[],""name"":""getContractBalance"",""outputs"":[{""internalType"":""uint256"",""name"":"""",""type"":""uint256""}],""stateMutability"":""view"",""type"":""function""},
      {""inputs"":[{""internalType"":""address"",""name"":""to"",""type"":""address""},{""internalType"":""uint256"",""name"":""amount"",""type"":""uint256""},{""internalType"":""string"",""name"":""reason"",""type"":""string""}],""name"":""payPlayer"",""outputs"":[],""stateMutability"":""nonpayable"",""type"":""function""},
      {""inputs"":[{""internalType"":""uint256"",""name"":""amount"",""type"":""uint256""}],""name"":""withdraw"",""outputs"":[],""stateMutability"":""nonpayable"",""type"":""function""}
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
        var balanceWei = await web3.Eth.GetBalance.SendRequestAsync(web3.TransactionManager.Account.Address);
        return Web3.Convert.FromWei(balanceWei.Value);
    }

    public async Task<decimal> GetVaultBalanceUSDCAsync()
    {
        var web3 = await CreateSignedWeb3Async();
        var contract = readOnlyWeb3.Eth.GetContract(Abi, contractAddress);
        var getBalance = contract.GetFunction("getBalance");
        var balanceWei = await getBalance.CallAsync<BigInteger>(web3.TransactionManager.Account.Address);
        return Web3.Convert.FromWei(balanceWei);
    }

    public async Task<string> DepositAsync(decimal amountUSDC)
    {
        var web3 = await CreateSignedWeb3Async();
        var contract = web3.Eth.GetContract(Abi, contractAddress);
        var deposit = contract.GetFunction("deposit");
        var valueWei = new HexBigInteger(ToWei(amountUSDC));
        var gas = new HexBigInteger(100000);

        return await deposit.SendTransactionAsync(web3.TransactionManager.Account.Address, gas, valueWei);
    }

    public async Task<string> WithdrawAsync(decimal amountUSDC)
    {
        var web3 = await CreateSignedWeb3Async();
        var contract = web3.Eth.GetContract(Abi, contractAddress);
        var withdraw = contract.GetFunction("withdraw");
        var amountWei = ToWei(amountUSDC);
        var gas = new HexBigInteger(100000);

        return await withdraw.SendTransactionAsync(web3.TransactionManager.Account.Address, gas, null, amountWei);
    }

    public async Task<string> PayPlayerAsync(string playerAddress, decimal amountUSDC, string reason)
    {
        var web3 = await CreateSignedWeb3Async();
        var contract = web3.Eth.GetContract(Abi, contractAddress);
        var payPlayer = contract.GetFunction("payPlayer");
        var amountWei = ToWei(amountUSDC);
        var gas = new HexBigInteger(150000);

        return await payPlayer.SendTransactionAsync(
            web3.TransactionManager.Account.Address,
            gas,
            null,
            playerAddress,
            amountWei,
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

    private static BigInteger ToWei(decimal usdcAmount)
    {
        var normalized = usdcAmount.ToString(CultureInfo.InvariantCulture);
        return Web3.Convert.ToWei(decimal.Parse(normalized, CultureInfo.InvariantCulture));
    }
}
