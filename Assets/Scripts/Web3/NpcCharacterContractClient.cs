using System;
using System.Numerics;
using System.Threading.Tasks;
using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;
using Nethereum.Hex.HexTypes;
using Nethereum.Web3;
using Nethereum.Web3.Accounts;
using UnityEngine;

[FunctionOutput]
public class PaymentBindingDTO : IFunctionOutputDTO
{
    [Parameter("address", "wallet", 1)] public string Wallet { get; set; }
    [Parameter("uint64",  "version", 2)] public ulong Version { get; set; }
}

public class NpcCharacterContractClient : MonoBehaviour
{
    [SerializeField] private string rpcUrl = "https://rpc.testnet.arc.network";

    // Address of the deployed NpcCharacter NFT contract
    [SerializeField] private string contractAddress;

    // Private key of the EOA that currently owns the NPC NFTs we manage from this client.
    // NOT used for x402 signing!
    [SerializeField] private string nftOwnerPrivateKey;

    public string ContractAddress => contractAddress;
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
        ""stateMutability"":""view"",""type"":""function""}
    ]";

    private Web3 readOnlyWeb3;
    
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
        var contract = readOnlyWeb3.Eth.GetContract(Abi, contractAddress);
        var fn = contract.GetFunction("getPaymentBinding");
        var dto = await fn.CallDeserializingToObjectAsync<PaymentBindingDTO>(tokenId);
        return (dto.Wallet, dto.Version);
    }

    public async Task<string> OwnerOfAsync(BigInteger tokenId)
    {
        var contract = readOnlyWeb3.Eth.GetContract(Abi, contractAddress);
        return await contract.GetFunction("ownerOf").CallAsync<string>(tokenId);
    }

    public async Task<bool> ExistsAsync(BigInteger tokenId)
    {
        var contract = readOnlyWeb3.Eth.GetContract(Abi, contractAddress);
        return await contract.GetFunction("exists").CallAsync<bool>(tokenId);
    }
    
    public async Task<string> BindPaymentWalletAsync(BigInteger tokenId, string walletAddress)
    {
        var web3 = await CreateSignedWeb3Async();
        var contract = web3.Eth.GetContract(Abi, contractAddress);
        var bind = contract.GetFunction("bindPaymentWallet");
        var gas = new HexBigInteger(120000);
        var txHash = await bind.SendTransactionAsync(
            web3.TransactionManager.Account.Address, gas, null, tokenId, walletAddress);
        await WaitReceiptAsync(web3, txHash);
        return txHash;
    }
    
    public async Task<string> ClearPaymentWalletAsync(BigInteger tokenId)
    {
        var web3 = await CreateSignedWeb3Async();
        var contract = web3.Eth.GetContract(Abi, contractAddress);
        var clearFn = contract.GetFunction("clearPaymentWallet");
        var gas = new HexBigInteger(80000);
        var txHash = await clearFn.SendTransactionAsync(
            web3.TransactionManager.Account.Address, gas, null, tokenId);
        await WaitReceiptAsync(web3, txHash);
        return txHash;
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

    private async Task<Web3> CreateSignedWeb3Async()
    {
        if (string.IsNullOrWhiteSpace(nftOwnerPrivateKey))
            throw new InvalidOperationException(
                $"{name} requires nftOwnerPrivateKey for bind/clear calls.");
        var chainId = await GetChainIdAsync();
        var account = new Account(nftOwnerPrivateKey.Trim(), chainId);
        return new Web3(account, rpcUrl);
    }
}
