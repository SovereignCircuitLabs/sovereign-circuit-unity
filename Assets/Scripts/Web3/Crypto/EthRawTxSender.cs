using System;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ArcTrading.WebGL;
using UnityEngine;

namespace ArcTrading.Crypto
{
    /// <summary>
    /// WebGL helper that wraps the full "build context → sign → broadcast"
    /// pipeline for a single transaction. Desktop callers don't need this —
    /// Nethereum's <c>ContractHandler.SendTransactionAsync</c> already covers
    /// the equivalent flow. The WebGL flow is:
    ///
    ///   1. Read nonce (via <see cref="WebGLPublicRpc"/> against the page's viem client)
    ///   2. Read gasPrice (same)
    ///   3. Cache chainId (set up at boot from the server's /game/config)
    ///   4. Sign legacy tx via <see cref="IEthCryptoBackend.SignLegacyTxAsync"/>
    ///   5. POST to the server's /tx/send-raw to broadcast
    ///
    /// All operations are async on the player loop — no blocking, no threads.
    /// </summary>
    public static class EthRawTxSender
    {
        private static long cachedChainId;

        /// <summary>
        /// Configure the chain id once at boot (typically from the
        /// authenticated WalletLoginService.Current.chainId). The sender uses
        /// this directly instead of round-tripping via WebGLPublicRpc on every
        /// call.
        /// </summary>
        public static void ConfigureChainId(long chainId) => cachedChainId = chainId;

        public static void ConfigureRpc(string rpcUrl) => WebGLPublicRpc.ConfigureRpc(rpcUrl);

        [Serializable] private class SendRawBody { public string raw; }
        [Serializable] private class SendRawResponse
        {
            public string txHash; public string status;
            public string blockNumber; public string gasUsed; public string error;
        }

        public static async Task<string> SendLegacyAsync(
            string fromAddress,
            string privateKey,
            string toAddress,
            BigInteger value,
            string dataHex,
            BigInteger gasLimit,
            CancellationToken ct = default)
        {
            if (cachedChainId == 0)
                cachedChainId = await WebGLPublicRpc.GetChainIdAsync(ct).ConfigureAwait(true);
            if (string.IsNullOrEmpty(fromAddress))
                throw new ArgumentException("fromAddress required");

            // Build context
            var nonceTask = WebGLPublicRpc.GetTransactionCountAsync(fromAddress, ct);
            var gasPriceTask = WebGLPublicRpc.GetGasPriceAsync(ct);
            await Task.WhenAll(nonceTask, gasPriceTask).ConfigureAwait(true);
            var nonce = nonceTask.Result;
            var gasPrice = gasPriceTask.Result;

            // Sign
            var tx = new LegacyTxParams
            {
                To = toAddress,
                Nonce = new BigInteger(nonce),
                GasPrice = gasPrice,
                GasLimit = gasLimit,
                Value = value,
                Data = string.IsNullOrEmpty(dataHex) ? "0x" : dataHex,
                ChainId = cachedChainId,
            };
            var signed = await EthCryptoBackend.Current.SignLegacyTxAsync(tx, privateKey, ct).ConfigureAwait(true);

            // Broadcast
            var bodyJson = JsonUtility.ToJson(new SendRawBody { raw = signed });
            var raw = await ArcTradingApiClient.PostJsonAsync("/tx/send-raw", bodyJson, ct).ConfigureAwait(true);
            if (string.IsNullOrEmpty(raw))
                throw new InvalidOperationException("EthRawTxSender: empty response from /tx/send-raw");
            var resp = JsonUtility.FromJson<SendRawResponse>(raw);
            if (resp == null)
                throw new InvalidOperationException($"EthRawTxSender: unparseable response: {raw}");
            if (!string.IsNullOrEmpty(resp.error))
                throw new InvalidOperationException($"EthRawTxSender: broadcast failed: {resp.error}");
            if (string.IsNullOrEmpty(resp.txHash))
                throw new InvalidOperationException($"EthRawTxSender: no txHash in response: {raw}");
            // viem's waitForTransactionReceipt returns status: "success" | "reverted".
            // /tx/send-raw also returns "pending" when the server gave up on the receipt
            // — we surface that to the caller as the txHash plus no exception, so they
            // can decide whether to poll. Only "reverted" is a hard fail here.
            if (string.Equals(resp.status, "reverted", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"EthRawTxSender: tx {resp.txHash} reverted on-chain");
            return resp.txHash;
        }

        public static Task<string> SendFunctionAsync(
            string fromAddress,
            string privateKey,
            string contractAddress,
            string abiJson,
            string functionName,
            string argsJson,
            BigInteger gasLimit,
            CancellationToken ct = default)
        {
            var dataHex = WebGLAbiBridge.EncodeFunctionData(abiJson, functionName, argsJson);
            return SendLegacyAsync(
                fromAddress,
                privateKey,
                contractAddress,
                BigInteger.Zero,
                dataHex,
                gasLimit,
                ct);
        }

        public static Task<string> SendTbaExecuteAsync(
            string signerAddress,
            string privateKey,
            string tbaAddress,
            string targetAddress,
            BigInteger value,
            string innerDataHex,
            BigInteger gasLimit,
            CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(innerDataHex)) innerDataHex = "0x";
            var executeData = WebGLAbiBridge.EncodeFunctionData(
                Erc6551AccountAbi,
                "execute",
                JsonArgs(targetAddress, value, innerDataHex, 0));
            return SendLegacyAsync(
                signerAddress,
                privateKey,
                tbaAddress,
                BigInteger.Zero,
                executeData,
                gasLimit,
                ct);
        }

        public static string JsonArgs(params object[] args)
        {
            var sb = new StringBuilder();
            sb.Append('[');
            for (int i = 0; i < args.Length; i++)
            {
                if (i > 0) sb.Append(',');
                AppendJsonValue(sb, args[i]);
            }
            sb.Append(']');
            return sb.ToString();
        }

        public static string BytesToHex(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return "0x";
            var sb = new StringBuilder(2 + bytes.Length * 2);
            sb.Append("0x");
            for (int i = 0; i < bytes.Length; i++)
                sb.Append(bytes[i].ToString("x2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        private static void AppendJsonValue(StringBuilder sb, object value)
        {
            if (value == null)
            {
                sb.Append("null");
                return;
            }

            switch (value)
            {
                case string s:
                    sb.Append('"').Append(EscapeJson(s)).Append('"');
                    return;
                case bool b:
                    sb.Append(b ? "true" : "false");
                    return;
                case BigInteger bi:
                    sb.Append('"').Append(bi.ToString(CultureInfo.InvariantCulture)).Append('"');
                    return;
                case byte b:
                    sb.Append(b.ToString(CultureInfo.InvariantCulture));
                    return;
                case int n:
                    sb.Append(n.ToString(CultureInfo.InvariantCulture));
                    return;
                case long n:
                    sb.Append(n.ToString(CultureInfo.InvariantCulture));
                    return;
                case uint n:
                    sb.Append(n.ToString(CultureInfo.InvariantCulture));
                    return;
                case ulong n:
                    sb.Append(n.ToString(CultureInfo.InvariantCulture));
                    return;
                default:
                    sb.Append('"').Append(EscapeJson(Convert.ToString(value, CultureInfo.InvariantCulture))).Append('"');
                    return;
            }
        }

        private static string EscapeJson(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private const string Erc6551AccountAbi = @"[
          {""inputs"":[{""internalType"":""address"",""name"":""to"",""type"":""address""},{""internalType"":""uint256"",""name"":""value"",""type"":""uint256""},{""internalType"":""bytes"",""name"":""data"",""type"":""bytes""},{""internalType"":""uint8"",""name"":""operation"",""type"":""uint8""}],""name"":""execute"",""outputs"":[{""internalType"":""bytes"",""name"":"""",""type"":""bytes""}],""stateMutability"":""payable"",""type"":""function""}
        ]";
    }
}
