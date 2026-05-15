using System;
using System.Globalization;
using System.Numerics;
using System.Threading.Tasks;
using Nethereum.Hex.HexTypes;
using Nethereum.Web3;
using Nethereum.Web3.Accounts;
using UnityEngine;

public class ArcContractTest : MonoBehaviour
{
    private const string RpcUrl = "https://rpc.testnet.arc.network";
    private const string ContractAddress = "0x5BE62566a1748de167165fB91C67cFB2BD37bFf0";

    private const string Abi = @"[
      {""inputs"":[{""internalType"":""address"",""name"":"""",""type"":""address""}],""name"":""balances"",""outputs"":[{""internalType"":""uint256"",""name"":"""",""type"":""uint256""}],""stateMutability"":""view"",""type"":""function""},
      {""inputs"":[],""name"":""deposit"",""outputs"":[],""stateMutability"":""payable"",""type"":""function""},
      {""stateMutability"":""payable"",""type"":""fallback"",""inputs"":[{""internalType"":""bytes"",""name"":""input"",""type"":""bytes""}],""outputs"":[{""internalType"":""bytes"",""name"":""output"",""type"":""bytes""}]},
      {""inputs"":[{""internalType"":""address"",""name"":""account"",""type"":""address""}],""name"":""getBalance"",""outputs"":[{""internalType"":""uint256"",""name"":"""",""type"":""uint256""}],""stateMutability"":""view"",""type"":""function""},
      {""inputs"":[],""name"":""getContractBalance"",""outputs"":[{""internalType"":""uint256"",""name"":"""",""type"":""uint256""}],""stateMutability"":""view"",""type"":""function""},
      {""inputs"":[{""internalType"":""address"",""name"":""to"",""type"":""address""},{""internalType"":""uint256"",""name"":""amount"",""type"":""uint256""},{""internalType"":""string"",""name"":""reason"",""type"":""string""}],""name"":""payPlayer"",""outputs"":[],""stateMutability"":""nonpayable"",""type"":""function""},
      {""inputs"":[{""internalType"":""uint256"",""name"":""amount"",""type"":""uint256""}],""name"":""withdraw"",""outputs"":[],""stateMutability"":""nonpayable"",""type"":""function""}
    ]";

    [Header("Read")] [SerializeField] private string queryAddress = "0x0000000000000000000000000000000000000000";

    [Header("Write")] [SerializeField] private string privateKey;
    [SerializeField] private string depositAmountUSDC = "0.001";
    [SerializeField] private string withdrawAmountUSDC = "0.001";
    [SerializeField] private string payPlayerTo = "0x0000000000000000000000000000000000000000";
    [SerializeField] private string payPlayerAmountEth = "0.001";
    [SerializeField] private string payPlayerReason = "Unity test payment";

    private Web3 readOnlyWeb3;

    private void Awake()
    {
        readOnlyWeb3 = new Web3(RpcUrl);
    }

    [ContextMenu("Query Balances")]
    public async void QueryBalancesFromMenu()
    {
        await RunAndLogErrors(QueryBalancesAsync);
    }

    [ContextMenu("Deposit")]
    public async void DepositFromMenu()
    {
        await RunAndLogErrors(DepositAsync);
    }

    [ContextMenu("Withdraw")]
    public async void WithdrawFromMenu()
    {
        await RunAndLogErrors(WithdrawAsync);
    }

    [ContextMenu("Pay Player")]
    public async void PayPlayerFromMenu()
    {
        await RunAndLogErrors(PayPlayerAsync);
    }

    private async Task QueryBalancesAsync()
    {
        var contract = readOnlyWeb3.Eth.GetContract(Abi, ContractAddress);

        var getBalance = contract.GetFunction("getBalance");
        var balances = contract.GetFunction("balances");
        var getContractBalance = contract.GetFunction("getContractBalance");

        var accountBalanceWei = await getBalance.CallAsync<BigInteger>(queryAddress);
        var mappingBalanceWei = await balances.CallAsync<BigInteger>(queryAddress);
        var contractBalanceWei = await getContractBalance.CallAsync<BigInteger>();

        Debug.Log($"getBalance({queryAddress}) = {Web3.Convert.FromWei(accountBalanceWei)}");
        Debug.Log($"balances({queryAddress}) = {Web3.Convert.FromWei(mappingBalanceWei)}");
        Debug.Log($"getContractBalance() = {Web3.Convert.FromWei(contractBalanceWei)}");
    }

    private async Task DepositAsync()
    {
        var web3 = await CreateSignedWeb3Async();
        var contract = web3.Eth.GetContract(Abi, ContractAddress);
        var deposit = contract.GetFunction("deposit");
        var valueWei = new HexBigInteger(ToWei(depositAmountUSDC));
        var gas = new HexBigInteger(100000);

        var txHash = await deposit.SendTransactionAsync(web3.TransactionManager.Account.Address, gas, valueWei);
        Debug.Log($"deposit tx: {txHash}");
    }

    private async Task WithdrawAsync()
    {
        var web3 = await CreateSignedWeb3Async();
        var contract = web3.Eth.GetContract(Abi, ContractAddress);
        var withdraw = contract.GetFunction("withdraw");
        var amountWei = ToWei(withdrawAmountUSDC);
        var gas = new HexBigInteger(100000);

        var txHash = await withdraw.SendTransactionAsync(web3.TransactionManager.Account.Address, gas, null, amountWei);
        Debug.Log($"withdraw tx: {txHash}");
    }

    private async Task PayPlayerAsync()
    {
        var web3 = await CreateSignedWeb3Async();
        var contract = web3.Eth.GetContract(Abi, ContractAddress);
        var payPlayer = contract.GetFunction("payPlayer");
        var amountWei = ToWei(payPlayerAmountEth);
        var gas = new HexBigInteger(150000);

        var txHash = await payPlayer.SendTransactionAsync(
            web3.TransactionManager.Account.Address,
            gas,
            null,
            payPlayerTo,
            amountWei,
            payPlayerReason);

        Debug.Log($"payPlayer tx: {txHash}");
    }

    private async Task<Web3> CreateSignedWeb3Async()
    {
        if (string.IsNullOrWhiteSpace(privateKey))
        {
            throw new InvalidOperationException("Private key is required for write transactions.");
        }

        var chainId = await readOnlyWeb3.Eth.ChainId.SendRequestAsync();
        var account = new Account(privateKey.Trim(), chainId.Value);
        return new Web3(account, RpcUrl);
    }

    private static BigInteger ToWei(string ethAmount)
    {
        var amount = decimal.Parse(ethAmount, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
        return Web3.Convert.ToWei(amount);
    }

    private static async Task RunAndLogErrors(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            Debug.LogError(ex);
        }
    }
}