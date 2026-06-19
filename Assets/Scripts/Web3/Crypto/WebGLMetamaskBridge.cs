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
        [DllImport("__Internal")] private static extern string ArcMm_GetAccounts();
        [DllImport("__Internal")] private static extern string ArcMm_PersonalSign(string message, string address);
        [DllImport("__Internal")] private static extern string ArcMm_SendTransaction(string txJson);
        [DllImport("__Internal")] private static extern string ArcMm_ChainId();
        [DllImport("__Internal")] private static extern string ArcMm_GetCachedSession();
        [DllImport("__Internal")] private static extern void ArcMm_ClearCachedSession();
        [DllImport("__Internal")] private static extern string ArcMm_PollRequest(string id);
#else
        private static string ArcMm_RequestAccounts() => throw new PlatformNotSupportedException("WebGL-only");
        private static string ArcMm_GetAccounts() => throw new PlatformNotSupportedException("WebGL-only");
        private static string ArcMm_PersonalSign(string _, string __) => throw new PlatformNotSupportedException("WebGL-only");
        private static string ArcMm_SendTransaction(string _) => throw new PlatformNotSupportedException("WebGL-only");
        private static string ArcMm_ChainId() => throw new PlatformNotSupportedException("WebGL-only");
        private static string ArcMm_GetCachedSession() => throw new PlatformNotSupportedException("WebGL-only");
        private static void ArcMm_ClearCachedSession() => throw new PlatformNotSupportedException("WebGL-only");
        private static string ArcMm_PollRequest(string _) => throw new PlatformNotSupportedException("WebGL-only");
#endif

        public static async Task<string> RequestAccountsAsync(CancellationToken ct = default)
        {
            var id = ArcMm_RequestAccounts();
            var raw = await WebGLAsyncBridge.AwaitAsync(id, ArcMm_PollRequest, ct).ConfigureAwait(true);
            // raw is JSON-stringified array: ["0xabc...", "0xdef..."]
            return ExtractFirstAddress(raw);
        }

        /// <summary>
        /// Silent read of currently-permitted accounts via eth_accounts.
        /// Returns lowercase 0x-prefixed addresses. Empty array means MetaMask
        /// has no active permission for this site (or it was revoked).
        /// </summary>
        public static async Task<string[]> GetAccountsAsync(CancellationToken ct = default)
        {
            var id = ArcMm_GetAccounts();
            var raw = await WebGLAsyncBridge.AwaitAsync(id, ArcMm_PollRequest, ct).ConfigureAwait(true);
            return ExtractAllAddresses(raw);
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

        public static string GetCachedSessionJson()
        {
            return ArcMm_GetCachedSession();
        }

        public static void ClearCachedSession()
        {
            ArcMm_ClearCachedSession();
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

        private static readonly string[] EmptyAddresses = new string[0];

        private static string[] ExtractAllAddresses(string jsonArray)
        {
            // Stringified array: ["0xabc","0xdef"] or []. Empty list is a legal
            // "no permission" answer for eth_accounts — caller treats that as
            // a session-cleared signal rather than an error.
            if (string.IsNullOrEmpty(jsonArray)) return EmptyAddresses;
            var addrs = new System.Collections.Generic.List<string>();
            int i = 0;
            while (i < jsonArray.Length)
            {
                int start = jsonArray.IndexOf('"', i);
                if (start < 0) break;
                int end = jsonArray.IndexOf('"', start + 1);
                if (end < 0 || end <= start + 1) break;
                addrs.Add(jsonArray.Substring(start + 1, end - start - 1).ToLowerInvariant());
                i = end + 1;
            }
            return addrs.ToArray();
        }
    }
}
