using System;
using System.Numerics;
using System.Threading.Tasks;
using Nethereum.Hex.HexTypes;
using Nethereum.Web3;

public static class Erc20UsdcHelper
{
    public const string ArcUsdcAddress = "0x3600000000000000000000000000000000000000";

    public static BigInteger ParseUsdc(decimal amount)
    {
        return new BigInteger(amount * 1_000_000m);
    }

    private const string Erc20Abi = @"[
      {""constant"":true,""inputs"":[{""name"":""owner"",""type"":""address""}],""name"":""balanceOf"",""outputs"":[{""name"":"""",""type"":""uint256""}],""type"":""function""},
      {""constant"":true,""inputs"":[{""name"":""owner"",""type"":""address""},{""name"":""spender"",""type"":""address""}],""name"":""allowance"",""outputs"":[{""name"":"""",""type"":""uint256""}],""type"":""function""},
      {""constant"":false,""inputs"":[{""name"":""spender"",""type"":""address""},{""name"":""amount"",""type"":""uint256""}],""name"":""approve"",""outputs"":[{""name"":"""",""type"":""bool""}],""type"":""function""}
    ]";

    public static async Task<BigInteger> GetBalanceAsync(Web3 web3, string owner)
    {
        var usdc = web3.Eth.GetContract(Erc20Abi, ArcUsdcAddress);
        return await usdc.GetFunction("balanceOf").CallAsync<BigInteger>(owner);
    }

    public static async Task<BigInteger> GetAllowanceAsync(Web3 web3, string owner, string spender)
    {
        var usdc = web3.Eth.GetContract(Erc20Abi, ArcUsdcAddress);
        return await usdc.GetFunction("allowance").CallAsync<BigInteger>(owner, spender);
    }

    public static async Task<string> ApproveAsync(Web3 web3, string owner, string spender, BigInteger amount)
    {
        var usdc = web3.Eth.GetContract(Erc20Abi, ArcUsdcAddress);
        var approveFn = usdc.GetFunction("approve");
        return await approveFn.SendTransactionAsync(
            owner,
            new HexBigInteger(100000),
            null,
            spender,
            amount);
    }

    // Make sure `spender` is allowed to pull at least `amount` USDC from the signer's wallet.
    // Throws if the wallet does not have enough USDC.
    // Awaits the approve receipt so the next tx sent by the caller sees the new allowance.
    public static async Task EnsureApprovalAsync(Web3 web3, string spender, BigInteger amount)
    {
        var owner = web3.TransactionManager.Account.Address;

        var balance = await GetBalanceAsync(web3, owner);
        if (balance < amount)
            throw new InvalidOperationException($"Insufficient USDC balance. Have={balance}, Need={amount}");

        var allowance = await GetAllowanceAsync(web3, owner, spender);
        if (allowance >= amount) return;

        var approveTx = await ApproveAsync(web3, owner, spender, amount);
        await web3.Eth.Transactions.GetTransactionReceipt.SendRequestAsync(approveTx);
    }
}
