using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace ArcTrading.Crypto
{
    /// <summary>
    /// WebGL-resilient crypto backend. It prefers the browser JS bridge, but
    /// falls back to the pure C# Nethereum implementation when the page bridge
    /// is not installed. This keeps NPC paymentWallet bootstrap/signing from
    /// depending on external module/CDN loading in the WebGL template.
    /// </summary>
    public sealed class FallbackEthCryptoBackend : IEthCryptoBackend
    {
        private readonly IEthCryptoBackend primary;
        private readonly IEthCryptoBackend fallback;

        public FallbackEthCryptoBackend(IEthCryptoBackend primary, IEthCryptoBackend fallback)
        {
            this.primary = primary ?? throw new ArgumentNullException(nameof(primary));
            this.fallback = fallback ?? throw new ArgumentNullException(nameof(fallback));
        }

        public Task<GeneratedKey> GenerateKeyAsync(CancellationToken ct = default)
            => TryAsync(() => primary.GenerateKeyAsync(ct), () => fallback.GenerateKeyAsync(ct), "GenerateKey");

        public string DeriveAddress(string privateKey)
            => Try(() => primary.DeriveAddress(privateKey), () => fallback.DeriveAddress(privateKey), "DeriveAddress");

        public Task<string> SignLegacyTxAsync(LegacyTxParams tx, string privateKey, CancellationToken ct = default)
            => TryAsync(() => primary.SignLegacyTxAsync(tx, privateKey, ct), () => fallback.SignLegacyTxAsync(tx, privateKey, ct), "SignLegacyTx");

        public Task<string> SignEip1559TxAsync(Eip1559TxParams tx, string privateKey, CancellationToken ct = default)
            => TryAsync(() => primary.SignEip1559TxAsync(tx, privateKey, ct), () => fallback.SignEip1559TxAsync(tx, privateKey, ct), "SignEip1559Tx");

        public Task<string> SignTypedDataV4Async(string typedDataJson, string privateKey, CancellationToken ct = default)
            => TryAsync(() => primary.SignTypedDataV4Async(typedDataJson, privateKey, ct), () => fallback.SignTypedDataV4Async(typedDataJson, privateKey, ct), "SignTypedDataV4");

        public Task<string> SignPersonalMessageAsync(string message, string privateKey, CancellationToken ct = default)
            => TryAsync(() => primary.SignPersonalMessageAsync(message, privateKey, ct), () => fallback.SignPersonalMessageAsync(message, privateKey, ct), "SignPersonalMessage");

        private static T Try<T>(Func<T> primaryCall, Func<T> fallbackCall, string op)
        {
            try
            {
                return primaryCall();
            }
            catch (Exception ex) when (IsBridgeUnavailable(ex))
            {
                Debug.LogWarning($"[FallbackEthCryptoBackend] {op}: JS bridge unavailable; using Nethereum fallback. {ex.Message}");
                return fallbackCall();
            }
        }

        private static async Task<T> TryAsync<T>(Func<Task<T>> primaryCall, Func<Task<T>> fallbackCall, string op)
        {
            try
            {
                return await primaryCall().ConfigureAwait(true);
            }
            catch (Exception ex) when (IsBridgeUnavailable(ex))
            {
                Debug.LogWarning($"[FallbackEthCryptoBackend] {op}: JS bridge unavailable; using Nethereum fallback. {ex.Message}");
                return await fallbackCall().ConfigureAwait(true);
            }
        }

        private static bool IsBridgeUnavailable(Exception ex)
        {
            var msg = ex.Message ?? string.Empty;
            return msg.IndexOf("not installed", StringComparison.OrdinalIgnoreCase) >= 0
                   || msg.IndexOf("bridge returned empty payload", StringComparison.OrdinalIgnoreCase) >= 0
                   || msg.IndexOf("ArcTradingCryptoBridge", StringComparison.OrdinalIgnoreCase) >= 0
                   || msg.IndexOf("WebGLCryptoBackend", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
