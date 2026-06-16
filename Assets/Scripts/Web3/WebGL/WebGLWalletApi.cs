using System;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace ArcTrading.WebGL
{
    /// <summary>
    /// Write-path entry points (Phase 2-4). Every method here POSTs to the
    /// TypeScript server's contract routes; the server holds the executor key
    /// and signs every transaction. The player never signs anything on-chain
    /// in WebGL mode.
    ///
    /// Authentication: call <see cref="ArcTradingWebGLConfig.SetAdminToken"/>
    /// once at boot (e.g. from a Bootstrap MonoBehaviour or after SIWE login).
    /// Every method here calls <see cref="RequireToken"/> first so a missing
    /// token surfaces immediately instead of as a 401 across the wire.
    /// </summary>
    public static class WebGLWalletApi
    {
        // ============================================================
        //  request / response DTOs
        // ============================================================

        // Server response field naming is not fully fixed across services;
        // accept the common variants and pick whichever is present.
        [Serializable] public class TxResponse
        {
            public string txHash;
            public string hash;
            public string transactionHash;
            public string id;
            public string price;
            public string status;
        }

        [Serializable] private class BuyItemX402Body
        {
            public string to;
            public string paidAmount;
            public string maxPriceAllowed;
        }
        [Serializable] private class MintRandomBody { public string maxPriceAllowed; }
        [Serializable] private class MintRandomX402Body { public string to; }

        [Serializable] private class UsdcApproveBody { public string spender; public string amount; }
        [Serializable] private class UsdcTransferBody { public string to; public string amount; }

        [Serializable] private class BindPaymentWalletBody { public string tokenId; public string wallet; }
        [Serializable] private class ClearPaymentWalletBody { public string tokenId; }

        [Serializable] private class GatewayDepositBody { public string token; public string value; }
        [Serializable] private class GatewayDepositForBody { public string token; public string depositor; public string value; }
        [Serializable] private class GatewayInitiateWithdrawalBody { public string token; public string value; }
        [Serializable] private class GatewayWithdrawBody { public string token; }

        [Serializable] private class TbaExecuteBody
        {
            public string account;
            public string to;
            public string value;
            public string data;
            public int operation;
        }

        // ============================================================
        //  helpers
        // ============================================================

        public static string ResolveTxHash(TxResponse r)
        {
            if (r == null) return null;
            if (!string.IsNullOrEmpty(r.txHash)) return r.txHash;
            if (!string.IsNullOrEmpty(r.hash)) return r.hash;
            if (!string.IsNullOrEmpty(r.transactionHash)) return r.transactionHash;
            return null;
        }

        private static void RequireToken()
        {
            if (string.IsNullOrEmpty(ArcTradingWebGLConfig.AdminToken))
                throw new InvalidOperationException(
                    "ArcTradingWebGLConfig.AdminToken is not set. Call SetAdminToken(...) " +
                    "before any write-path WebGL API call.");
        }

        private static async Task<string> PostAndResolveAsync(string path, string body, string opLabel, CancellationToken ct)
        {
            var raw = await ArcTradingApiClient.PostJsonAsync(path, body, ct).ConfigureAwait(true);
            if (ArcTradingWebGLConfig.VerboseLogging) Debug.Log($"[WebGLWalletApi] {opLabel} -> {raw}");
            var dto = string.IsNullOrEmpty(raw) ? null : JsonUtility.FromJson<TxResponse>(raw);
            return ResolveTxHash(dto) ?? raw;
        }

        // ============================================================
        //  Phase 2: buy / sell / mint items
        // ============================================================

        public static Task<string> SellItemAsync(BigInteger itemId, CancellationToken ct = default)
        {
            RequireToken();
            return PostAndResolveAsync($"/game/item/{itemId}/sell", null, $"sellItem({itemId})", ct);
        }

        public static Task<string> BuyItemX402Async(
            string to, BigInteger itemId,
            BigInteger paidAmount, BigInteger maxPriceAllowed,
            CancellationToken ct = default)
        {
            RequireToken();
            if (string.IsNullOrWhiteSpace(to)) throw new ArgumentException("to address required", nameof(to));
            var body = JsonUtility.ToJson(new BuyItemX402Body
            {
                to = to,
                paidAmount = paidAmount.ToString(),
                maxPriceAllowed = maxPriceAllowed.ToString(),
            });
            return PostAndResolveAsync($"/game/item/{itemId}/buy-x402", body, $"buyItemX402({itemId})", ct);
        }

        public static Task<string> MintRandomAsync(BigInteger maxPriceAllowed, CancellationToken ct = default)
        {
            RequireToken();
            var body = JsonUtility.ToJson(new MintRandomBody { maxPriceAllowed = maxPriceAllowed.ToString() });
            return PostAndResolveAsync("/game/mint-random", body, "mintRandom", ct);
        }

        public static Task<string> MintRandomX402Async(string to, CancellationToken ct = default)
        {
            RequireToken();
            if (string.IsNullOrWhiteSpace(to)) throw new ArgumentException("to address required", nameof(to));
            var body = JsonUtility.ToJson(new MintRandomX402Body { to = to });
            return PostAndResolveAsync("/game/mint-random-x402", body, "mintRandomX402", ct);
        }

        // ============================================================
        //  Phase 3: USDC + payment-binding (x402 setup paths)
        // ============================================================

        public static Task<string> UsdcApproveAsync(string spender, BigInteger amount, CancellationToken ct = default)
        {
            RequireToken();
            if (string.IsNullOrWhiteSpace(spender)) throw new ArgumentException("spender required", nameof(spender));
            var body = JsonUtility.ToJson(new UsdcApproveBody { spender = spender, amount = amount.ToString() });
            return PostAndResolveAsync("/usdc/approve", body, $"usdcApprove({spender})", ct);
        }

        public static Task<string> UsdcTransferAsync(string to, BigInteger amount, CancellationToken ct = default)
        {
            RequireToken();
            if (string.IsNullOrWhiteSpace(to)) throw new ArgumentException("to required", nameof(to));
            var body = JsonUtility.ToJson(new UsdcTransferBody { to = to, amount = amount.ToString() });
            return PostAndResolveAsync("/usdc/transfer", body, $"usdcTransfer({to})", ct);
        }

        public static Task<string> BindPaymentWalletAsync(BigInteger tokenId, string wallet, CancellationToken ct = default)
        {
            RequireToken();
            if (string.IsNullOrWhiteSpace(wallet)) throw new ArgumentException("wallet required", nameof(wallet));
            var body = JsonUtility.ToJson(new BindPaymentWalletBody { tokenId = tokenId.ToString(), wallet = wallet });
            return PostAndResolveAsync("/npc-character/payment-binding", body, $"bindPaymentWallet({tokenId})", ct);
        }

        public static Task<string> ClearPaymentWalletAsync(BigInteger tokenId, CancellationToken ct = default)
        {
            RequireToken();
            var body = JsonUtility.ToJson(new ClearPaymentWalletBody { tokenId = tokenId.ToString() });
            return PostAndResolveAsync("/npc-character/payment-binding/clear", body, $"clearPaymentWallet({tokenId})", ct);
        }

        // ============================================================
        //  Phase 4: Gateway deposit / withdraw + TBA execute
        // ============================================================

        public static Task<string> GatewayDepositAsync(string token, BigInteger value, CancellationToken ct = default)
        {
            RequireToken();
            if (string.IsNullOrWhiteSpace(token)) throw new ArgumentException("token required", nameof(token));
            var body = JsonUtility.ToJson(new GatewayDepositBody { token = token, value = value.ToString() });
            return PostAndResolveAsync("/gateway/deposit", body, $"gatewayDeposit({token})", ct);
        }

        public static Task<string> GatewayDepositForAsync(string token, string depositor, BigInteger value, CancellationToken ct = default)
        {
            RequireToken();
            if (string.IsNullOrWhiteSpace(token)) throw new ArgumentException("token required", nameof(token));
            if (string.IsNullOrWhiteSpace(depositor)) throw new ArgumentException("depositor required", nameof(depositor));
            var body = JsonUtility.ToJson(new GatewayDepositForBody
            {
                token = token, depositor = depositor, value = value.ToString(),
            });
            return PostAndResolveAsync("/gateway/deposit-for", body, $"gatewayDepositFor({depositor})", ct);
        }

        public static Task<string> GatewayInitiateWithdrawalAsync(string token, BigInteger value, CancellationToken ct = default)
        {
            RequireToken();
            if (string.IsNullOrWhiteSpace(token)) throw new ArgumentException("token required", nameof(token));
            var body = JsonUtility.ToJson(new GatewayInitiateWithdrawalBody { token = token, value = value.ToString() });
            return PostAndResolveAsync("/gateway/initiate-withdrawal", body, $"gatewayInitiateWithdrawal({token})", ct);
        }

        public static Task<string> GatewayWithdrawAsync(string token, CancellationToken ct = default)
        {
            RequireToken();
            if (string.IsNullOrWhiteSpace(token)) throw new ArgumentException("token required", nameof(token));
            var body = JsonUtility.ToJson(new GatewayWithdrawBody { token = token });
            return PostAndResolveAsync("/gateway/withdraw", body, $"gatewayWithdraw({token})", ct);
        }

        /// <summary>
        /// Calls ERC-6551 TBA's execute(). The server validates the target is in an
        /// allowlist (currently extended with the items contract). <paramref name="dataHex"/>
        /// MUST be a 0x-prefixed lowercase hex string.
        /// </summary>
        public static Task<string> TbaExecuteAsync(
            string account, string to,
            BigInteger value, string dataHex, int operation,
            CancellationToken ct = default)
        {
            RequireToken();
            if (string.IsNullOrWhiteSpace(account)) throw new ArgumentException("account required", nameof(account));
            if (string.IsNullOrWhiteSpace(to)) throw new ArgumentException("to required", nameof(to));
            if (string.IsNullOrEmpty(dataHex)) dataHex = "0x";
            else if (!dataHex.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) dataHex = "0x" + dataHex;
            var body = JsonUtility.ToJson(new TbaExecuteBody
            {
                account = account, to = to, value = value.ToString(),
                data = dataHex, operation = operation,
            });
            return PostAndResolveAsync("/tba/execute", body, $"tbaExecute({account})", ct);
        }
    }
}
