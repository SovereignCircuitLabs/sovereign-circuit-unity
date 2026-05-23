using System;
using System.Numerics;
using System.Threading.Tasks;
using Nethereum.Hex.HexTypes;
using Nethereum.Web3;
using Nethereum.Web3.Accounts;

// nanopayments - buyer/client
namespace ArcTrading.Nanopayment
{
    public partial class ArcNanopaymentClient
    {
        private const string gatewayContractAddress = "0x0077777d7EBA4688BDeF3E311b846F25870A19B9";

        private ArcTradingContractClient tradingContractClient;
        private string rpcUrl = "https://rpc.testnet.arc.network";
        private string privateKey;

        private Web3 gatewayReadOnlyWeb3;

        private const string GatewayWalletAbi = @"[
          {""inputs"":[{""internalType"":""address"",""name"":""token"",""type"":""address""},{""internalType"":""uint256"",""name"":""value"",""type"":""uint256""}],""name"":""deposit"",""outputs"":[],""stateMutability"":""nonpayable"",""type"":""function""},
          {""inputs"":[{""internalType"":""address"",""name"":""token"",""type"":""address""},{""internalType"":""address"",""name"":""depositor"",""type"":""address""},{""internalType"":""uint256"",""name"":""value"",""type"":""uint256""}],""name"":""depositFor"",""outputs"":[],""stateMutability"":""nonpayable"",""type"":""function""},
          {""inputs"":[{""internalType"":""address"",""name"":""token"",""type"":""address""},{""internalType"":""address"",""name"":""depositor"",""type"":""address""}],""name"":""totalBalance"",""outputs"":[{""internalType"":""uint256"",""name"":"""",""type"":""uint256""}],""stateMutability"":""view"",""type"":""function""},
          {""inputs"":[{""internalType"":""address"",""name"":""token"",""type"":""address""},{""internalType"":""address"",""name"":""depositor"",""type"":""address""}],""name"":""availableBalance"",""outputs"":[{""internalType"":""uint256"",""name"":"""",""type"":""uint256""}],""stateMutability"":""view"",""type"":""function""},
          {""inputs"":[{""internalType"":""address"",""name"":""token"",""type"":""address""},{""internalType"":""address"",""name"":""depositor"",""type"":""address""}],""name"":""withdrawingBalance"",""outputs"":[{""internalType"":""uint256"",""name"":"""",""type"":""uint256""}],""stateMutability"":""view"",""type"":""function""},
          {""inputs"":[{""internalType"":""address"",""name"":""token"",""type"":""address""},{""internalType"":""address"",""name"":""depositor"",""type"":""address""}],""name"":""withdrawableBalance"",""outputs"":[{""internalType"":""uint256"",""name"":"""",""type"":""uint256""}],""stateMutability"":""view"",""type"":""function""},
          {""inputs"":[],""name"":""withdrawalDelay"",""outputs"":[{""internalType"":""uint256"",""name"":"""",""type"":""uint256""}],""stateMutability"":""view"",""type"":""function""},
          {""inputs"":[{""internalType"":""address"",""name"":""token"",""type"":""address""},{""internalType"":""address"",""name"":""depositor"",""type"":""address""}],""name"":""withdrawalBlock"",""outputs"":[{""internalType"":""uint256"",""name"":"""",""type"":""uint256""}],""stateMutability"":""view"",""type"":""function""},
          {""inputs"":[{""internalType"":""address"",""name"":""token"",""type"":""address""},{""internalType"":""uint256"",""name"":""value"",""type"":""uint256""}],""name"":""initiateWithdrawal"",""outputs"":[],""stateMutability"":""nonpayable"",""type"":""function""},
          {""inputs"":[{""internalType"":""address"",""name"":""token"",""type"":""address""}],""name"":""withdraw"",""outputs"":[],""stateMutability"":""nonpayable"",""type"":""function""}
        ]";

        private void Start()
        {
            tradingContractClient = GetComponent<ArcTradingContractClient>();
            rpcUrl = tradingContractClient.RpcUrl;
            privateKey = tradingContractClient.PrivateKey;
            gatewayReadOnlyWeb3 = new Web3(rpcUrl);
        }

        // --------- Write (state-changing) calls ---------

        public async Task<string> ApproveIfNeededThenGatewayDepositAsync(decimal amountUsdc)
        {
            var amount = Erc20UsdcHelper.ParseUsdc(amountUsdc);
            var web3 = await CreateSignedGatewayWeb3Async();

            await Erc20UsdcHelper.EnsureApprovalAsync(web3, gatewayContractAddress, amount);

            return await GatewayDepositAsync(Erc20UsdcHelper.ArcUsdcAddress, amount);
        }

        public async Task<string> GatewayDepositAsync(string tokenAddress, BigInteger value)
        {
            var web3 = await CreateSignedGatewayWeb3Async();
            var contract = web3.Eth.GetContract(GatewayWalletAbi, gatewayContractAddress);
            var fn = contract.GetFunction("deposit");
            var gas = new HexBigInteger(200000);

            return await fn.SendTransactionAsync(
                web3.TransactionManager.Account.Address,
                gas,
                null,
                tokenAddress,
                value);
        }

        public async Task<string> GatewayDepositForAsync(string tokenAddress, string depositor, BigInteger value)
        {
            var web3 = await CreateSignedGatewayWeb3Async();
            var contract = web3.Eth.GetContract(GatewayWalletAbi, gatewayContractAddress);
            var fn = contract.GetFunction("depositFor");
            var gas = new HexBigInteger(200000);

            return await fn.SendTransactionAsync(
                web3.TransactionManager.Account.Address,
                gas,
                null,
                tokenAddress,
                depositor,
                value);
        }

        public async Task<string> GatewayInitiateWithdrawalAsync(string tokenAddress, BigInteger value)
        {
            var web3 = await CreateSignedGatewayWeb3Async();
            var contract = web3.Eth.GetContract(GatewayWalletAbi, gatewayContractAddress);
            var fn = contract.GetFunction("initiateWithdrawal");
            var gas = new HexBigInteger(200000);

            return await fn.SendTransactionAsync(
                web3.TransactionManager.Account.Address,
                gas,
                null,
                tokenAddress,
                value);
        }

        public async Task<string> GatewayWithdrawAsync(string tokenAddress)
        {
            var web3 = await CreateSignedGatewayWeb3Async();
            var contract = web3.Eth.GetContract(GatewayWalletAbi, gatewayContractAddress);
            var fn = contract.GetFunction("withdraw");
            var gas = new HexBigInteger(200000);

            return await fn.SendTransactionAsync(
                web3.TransactionManager.Account.Address,
                gas,
                null,
                tokenAddress);
        }

        // --------- Read (view) calls ---------

        public async Task<BigInteger> GatewayTotalBalanceAsync(string tokenAddress, string depositor)
        {
            var contract = gatewayReadOnlyWeb3.Eth.GetContract(GatewayWalletAbi, gatewayContractAddress);
            var fn = contract.GetFunction("totalBalance");
            return await fn.CallAsync<BigInteger>(tokenAddress, depositor);
        }

        public async Task<BigInteger> GatewayAvailableBalanceAsync(string tokenAddress, string depositor)
        {
            var contract = gatewayReadOnlyWeb3.Eth.GetContract(GatewayWalletAbi, gatewayContractAddress);
            var fn = contract.GetFunction("availableBalance");
            return await fn.CallAsync<BigInteger>(tokenAddress, depositor);
        }

        public async Task<BigInteger> GatewayWithdrawingBalanceAsync(string tokenAddress, string depositor)
        {
            var contract = gatewayReadOnlyWeb3.Eth.GetContract(GatewayWalletAbi, gatewayContractAddress);
            var fn = contract.GetFunction("withdrawingBalance");
            return await fn.CallAsync<BigInteger>(tokenAddress, depositor);
        }

        public async Task<BigInteger> GatewayWithdrawableBalanceAsync(string tokenAddress, string depositor)
        {
            var contract = gatewayReadOnlyWeb3.Eth.GetContract(GatewayWalletAbi, gatewayContractAddress);
            var fn = contract.GetFunction("withdrawableBalance");
            return await fn.CallAsync<BigInteger>(tokenAddress, depositor);
        }

        public async Task<BigInteger> GatewayWithdrawalDelayAsync()
        {
            var contract = gatewayReadOnlyWeb3.Eth.GetContract(GatewayWalletAbi, gatewayContractAddress);
            var fn = contract.GetFunction("withdrawalDelay");
            return await fn.CallAsync<BigInteger>();
        }

        public async Task<BigInteger> GatewayWithdrawalBlockAsync(string tokenAddress, string depositor)
        {
            var contract = gatewayReadOnlyWeb3.Eth.GetContract(GatewayWalletAbi, gatewayContractAddress);
            var fn = contract.GetFunction("withdrawalBlock");
            return await fn.CallAsync<BigInteger>(tokenAddress, depositor);
        }

        // --------- Helpers ---------

        private async Task<Web3> CreateSignedGatewayWeb3Async()
        {
            if (string.IsNullOrWhiteSpace(privateKey))
            {
                throw new InvalidOperationException($"{name} requires a private key for gateway wallet actions.");
            }

            var chainId = await gatewayReadOnlyWeb3.Eth.ChainId.SendRequestAsync();
            var account = new Account(privateKey.Trim(), chainId.Value);
            return new Web3(account, rpcUrl);
        }
    }
}