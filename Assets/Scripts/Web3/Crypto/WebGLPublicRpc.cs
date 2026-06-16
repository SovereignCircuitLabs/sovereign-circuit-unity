using System;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace ArcTrading.Crypto
{
    /// <summary>
    /// Read-only RPC helpers (nonce / gasPrice / chainId) exposed via the
    /// jslib bridge to viem's <c>publicClient</c>. Used by Step 4 (NPC
    /// paymentWallet writes) — building a signed legacy tx needs nonce,
    /// gasPrice and chainId. Going through the page's viem client keeps
    /// Unity out of raw JSON-RPC.
    ///
    /// Configure once at boot with <see cref="ConfigureRpc"/> (idempotent;
    /// re-config swaps the underlying client).
    /// </summary>
    public static class WebGLPublicRpc
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern void ArcPubRpc_Configure(string rpcUrl);
        [DllImport("__Internal")] private static extern string ArcPubRpc_GetTransactionCount(string address);
        [DllImport("__Internal")] private static extern string ArcPubRpc_GetGasPrice();
        [DllImport("__Internal")] private static extern string ArcPubRpc_GetChainId();
        [DllImport("__Internal")] private static extern string ArcPubRpc_PollRequest(string id);
#else
        private static void ArcPubRpc_Configure(string _) => throw new PlatformNotSupportedException("WebGL-only");
        private static string ArcPubRpc_GetTransactionCount(string _) => throw new PlatformNotSupportedException("WebGL-only");
        private static string ArcPubRpc_GetGasPrice() => throw new PlatformNotSupportedException("WebGL-only");
        private static string ArcPubRpc_GetChainId() => throw new PlatformNotSupportedException("WebGL-only");
        private static string ArcPubRpc_PollRequest(string _) => throw new PlatformNotSupportedException("WebGL-only");
#endif

        public static void ConfigureRpc(string rpcUrl)
        {
            if (string.IsNullOrWhiteSpace(rpcUrl))
                throw new ArgumentException("rpcUrl is required", nameof(rpcUrl));
            ArcPubRpc_Configure(rpcUrl);
        }

        public static async Task<ulong> GetTransactionCountAsync(string address, CancellationToken ct = default)
        {
            var id = ArcPubRpc_GetTransactionCount(address);
            var raw = await WebGLAsyncBridge.AwaitAsync(id, ArcPubRpc_PollRequest, ct).ConfigureAwait(true);
            return ulong.Parse(raw);
        }

        public static async Task<BigInteger> GetGasPriceAsync(CancellationToken ct = default)
        {
            var id = ArcPubRpc_GetGasPrice();
            var raw = await WebGLAsyncBridge.AwaitAsync(id, ArcPubRpc_PollRequest, ct).ConfigureAwait(true);
            return BigInteger.Parse(raw);
        }

        public static async Task<long> GetChainIdAsync(CancellationToken ct = default)
        {
            var id = ArcPubRpc_GetChainId();
            var raw = await WebGLAsyncBridge.AwaitAsync(id, ArcPubRpc_PollRequest, ct).ConfigureAwait(true);
            return long.Parse(raw);
        }
    }
}
