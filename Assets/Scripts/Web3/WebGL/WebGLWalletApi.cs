using System;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace ArcTrading.WebGL
{
    /// <summary>
    /// Legacy server write-path entry points. Phase 2-4 writes now sign locally
    /// through IEthCryptoBackend and broadcast through POST /tx/send-raw; most
    /// methods here intentionally reject calls so WebGL cannot silently fall
    /// back to the server-admin signer. The x402 mint route is the exception:
    /// the server owns that payment flow and receives an explicit `to` address.
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

            // NPC-perspective aggregate routes (/npc/:tokenId/sell-item/:itemId,
            // /npc/:tokenId/mint-random) return this when a lazy approval was
            // also submitted as a prerequisite tx. Empty when not needed.
            public string approvalTxHash;

            // Echoed back by the NPC-perspective routes for caller logging.
            public string tokenId;
            public string itemId;
            public string tba;
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

        private static NotSupportedException SignedRawTxRequired(string operation)
            => new NotSupportedException(
                $"{operation} requires local signing via IEthCryptoBackend and POST /tx/send-raw. " +
                "Do not route this WebGL write through the server-admin contract route.");

        // ============================================================
        //  Phase 2: buy / sell / mint items
        // ============================================================

        public static Task<string> SellItemAsync(BigInteger itemId, CancellationToken ct = default)
        {
            throw SignedRawTxRequired($"sellItem({itemId})");
        }

        public static Task<string> BuyItemX402Async(
            string to, BigInteger itemId,
            BigInteger paidAmount, BigInteger maxPriceAllowed,
            CancellationToken ct = default)
        {
            throw SignedRawTxRequired($"buyItemX402({itemId})");
        }

        public static Task<string> MintRandomAsync(BigInteger maxPriceAllowed, CancellationToken ct = default)
        {
            throw SignedRawTxRequired("mintRandom");
        }

        public static Task<string> MintRandomX402Async(string to, CancellationToken ct = default)
        {
            RequireToken();
            if (string.IsNullOrWhiteSpace(to)) throw new ArgumentException("to address required", nameof(to));
            var body = JsonUtility.ToJson(new MintRandomX402Body { to = to });
            return PostAndResolveAsync("/game/mint-random-x402", body, "mintRandomX402", ct);
        }

        // ============================================================
        //  Phase 2.5: NPC-perspective aggregate writes
        // ============================================================
        // These route the call through the NPC's ERC-6551 TBA via the server's
        // /tba/execute primitive, so msg.sender to GamePayment is the TBA (the
        // entity that actually holds the items / pays the USDC). The server's
        // serverAccount must be bound as paymentWallet for the NPC NFT — call
        // BindPaymentWalletAsync(tokenId, serverAccountAddress) at boot once
        // per NPC, or the server returns 409 with "npc payment wallet not
        // bound to server account".

        public static Task<string> NpcSellItemAsync(BigInteger tokenId, BigInteger itemId, CancellationToken ct = default)
        {
            throw SignedRawTxRequired($"npcSellItem(tokenId={tokenId}, itemId={itemId})");
        }

        public static Task<string> NpcMintRandomAsync(BigInteger tokenId, BigInteger maxPriceAllowed, CancellationToken ct = default)
        {
            throw SignedRawTxRequired($"npcMintRandom(tokenId={tokenId})");
        }

        // ============================================================
        //  Phase 3: USDC + payment-binding (x402 setup paths)
        // ============================================================

        public static Task<string> UsdcApproveAsync(string spender, BigInteger amount, CancellationToken ct = default)
        {
            throw SignedRawTxRequired($"USDC.approve({spender})");
        }

        public static Task<string> UsdcTransferAsync(string to, BigInteger amount, CancellationToken ct = default)
        {
            throw SignedRawTxRequired($"USDC.transfer({to})");
        }

        public static Task<string> BindPaymentWalletAsync(BigInteger tokenId, string wallet, CancellationToken ct = default)
        {
            throw SignedRawTxRequired($"bindPaymentWallet({tokenId})");
        }

        public static Task<string> ClearPaymentWalletAsync(BigInteger tokenId, CancellationToken ct = default)
        {
            throw SignedRawTxRequired($"clearPaymentWallet({tokenId})");
        }

        // ============================================================
        //  Phase 4: Gateway deposit / withdraw + TBA execute
        // ============================================================

        public static Task<string> GatewayDepositAsync(string token, BigInteger value, CancellationToken ct = default)
        {
            throw SignedRawTxRequired($"gatewayDeposit({token})");
        }

        public static Task<string> GatewayDepositForAsync(string token, string depositor, BigInteger value, CancellationToken ct = default)
        {
            throw SignedRawTxRequired($"gatewayDepositFor({depositor})");
        }

        public static Task<string> GatewayInitiateWithdrawalAsync(string token, BigInteger value, CancellationToken ct = default)
        {
            throw SignedRawTxRequired($"gatewayInitiateWithdrawal({token})");
        }

        public static Task<string> GatewayWithdrawAsync(string token, CancellationToken ct = default)
        {
            throw SignedRawTxRequired($"gatewayWithdraw({token})");
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
            throw SignedRawTxRequired($"tbaExecute({account})");
        }
    }
}
