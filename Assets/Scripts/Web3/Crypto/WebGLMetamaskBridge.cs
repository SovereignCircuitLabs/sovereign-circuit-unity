using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace ArcTrading.Crypto
{
    /// <summary>
    /// Player-side MetaMask interactions via the jslib bridge. Used in WebGL
    /// for SIWE login (personal_sign) and owner-side transactions (e.g.
    /// bindPaymentWallet via eth_sendTransaction). MetaMask popups confirm
    /// each action; the wallet identity is whoever the player connected.
    ///
    /// Distinct from <see cref="IEthCryptoBackend"/>, which holds the NPC's
    /// LOCAL paymentWallet (no popup, generated in-browser, persisted in
    /// IndexedDB via Unity's persistentDataPath).
    /// </summary>
    public static class WebGLMetamaskBridge
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern string ArcMm_RequestAccounts();
        [DllImport("__Internal")] private static extern string ArcMm_PersonalSign(string message, string address);
        [DllImport("__Internal")] private static extern string ArcMm_SendTransaction(string txJson);
        [DllImport("__Internal")] private static extern string ArcMm_ChainId();
        [DllImport("__Internal")] private static extern string ArcMm_PollRequest(string id);
#else
        private static string ArcMm_RequestAccounts() => throw new PlatformNotSupportedException("WebGL-only");
        private static string ArcMm_PersonalSign(string _, string __) => throw new PlatformNotSupportedException("WebGL-only");
        private static string ArcMm_SendTransaction(string _) => throw new PlatformNotSupportedException("WebGL-only");
        private static string ArcMm_ChainId() => throw new PlatformNotSupportedException("WebGL-only");
        private static string ArcMm_PollRequest(string _) => throw new PlatformNotSupportedException("WebGL-only");
#endif

        public static async Task<string> RequestAccountsAsync(CancellationToken ct = default)
        {
            var id = ArcMm_RequestAccounts();
            var raw = await WebGLAsyncBridge.AwaitAsync(id, ArcMm_PollRequest, ct).ConfigureAwait(true);
            // raw is JSON-stringified array: ["0xabc...", "0xdef..."]
            return ExtractFirstAddress(raw);
        }

        public static async Task<string> PersonalSignAsync(string message, string address, CancellationToken ct = default)
        {
            var id = ArcMm_PersonalSign(message, address);
            return await WebGLAsyncBridge.AwaitAsync(id, ArcMm_PollRequest, ct).ConfigureAwait(true);
        }

        /// <summary>
        /// txJson follows the eth_sendTransaction JSON-RPC params shape:
        ///   { "from", "to", "value" (hex), "data" (hex), "gas" (hex), "gasPrice" (hex)? }
        /// MetaMask both signs AND broadcasts; the returned promise resolves
        /// with the tx hash.
        /// </summary>
        public static async Task<string> SendTransactionAsync(string txJson, CancellationToken ct = default)
        {
            var id = ArcMm_SendTransaction(txJson);
            return await WebGLAsyncBridge.AwaitAsync(id, ArcMm_PollRequest, ct).ConfigureAwait(true);
        }

        /// <summary>Returns the chain id as a hex string (e.g. "0x4297" for Arc testnet).</summary>
        public static async Task<string> ChainIdHexAsync(CancellationToken ct = default)
        {
            var id = ArcMm_ChainId();
            return await WebGLAsyncBridge.AwaitAsync(id, ArcMm_PollRequest, ct).ConfigureAwait(true);
        }

        public static async Task<long> ChainIdAsync(CancellationToken ct = default)
        {
            var hex = await ChainIdHexAsync(ct).ConfigureAwait(true);
            if (string.IsNullOrEmpty(hex)) return 0;
            var clean = hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? hex.Substring(2) : hex;
            return Convert.ToInt64(clean, 16);
        }

        private static string ExtractFirstAddress(string jsonArray)
        {
            // Stringified array: ["0x...", ...]. Minimal hand-parse to avoid the
            // overhead of a JSON lib for this single use case.
            if (string.IsNullOrEmpty(jsonArray))
                throw new InvalidOperationException("MetaMask returned empty accounts list");

            int start = jsonArray.IndexOf('"');
            int end = start >= 0 ? jsonArray.IndexOf('"', start + 1) : -1;
            if (start < 0 || end < 0 || end <= start + 1)
                throw new InvalidOperationException($"MetaMask returned no accounts: {jsonArray}");
            return jsonArray.Substring(start + 1, end - start - 1);
        }
    }
}
