using System;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace ArcTrading.Crypto
{
    /// <summary>
    /// WebGL implementation of <see cref="IEthCryptoBackend"/>. Delegates every
    /// crypto primitive to <c>Assets/Plugins/WebGL/ArcTradingCryptoBridge.jslib</c>,
    /// which in turn calls into <c>window.ArcTradingCryptoBridge</c> — a thin
    /// wrapper around viem that the WebGL HTML template loads.
    ///
    /// All viem operations we need (generatePrivateKey, privateKeyToAccount.address,
    /// signTransaction, signTypedData, signMessage) are synchronous, so the jslib
    /// extern functions return marshaled strings directly. The <see cref="Task"/>
    /// shape on the interface is for API symmetry — these methods don't actually
    /// yield to the player loop.
    ///
    /// IMPORTANT: every method here throws <see cref="MissingMethodException"/>
    /// until the .jslib bridge is implemented and viem is loaded into the page.
    /// Build the jslib (<c>ArcTradingCryptoBridge.jslib</c>) and wire viem into
    /// the WebGL template's <c>&lt;head&gt;</c> before Phase 5 Sell/Mint paths
    /// can be exercised in a real WebGL build. See the file header comments in
    /// the jslib for the JS contract.
    /// </summary>
    public class WebGLCryptoBackend : IEthCryptoBackend
    {
        // ------------------ jslib externs ------------------
        // The jslib file declares these as JS functions; emscripten links them
        // at build time. They return char* into the WebAssembly heap; Unity's
        // string marshaller copies them on the way back in. JS-side allocates
        // via _malloc / writeStringToHeap8 (see the jslib comments).
        //
        // Returning a JSON-encoded error envelope (e.g. {"error":"..."}) is
        // the convention we'll use to surface JS-side failures because emscripten
        // can't throw a typed C# exception across the boundary cheaply.

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern string ArcCrypto_GenerateKey();
        [DllImport("__Internal")] private static extern string ArcCrypto_DeriveAddress(string privateKey);
        [DllImport("__Internal")] private static extern string ArcCrypto_SignLegacyTx(string txJson, string privateKey);
        [DllImport("__Internal")] private static extern string ArcCrypto_SignEip1559Tx(string txJson, string privateKey);
        [DllImport("__Internal")] private static extern string ArcCrypto_SignTypedDataV4(string typedDataJson, string privateKey);
        [DllImport("__Internal")] private static extern string ArcCrypto_SignPersonalMessage(string message, string privateKey);
#else
        // Editor / Standalone stubs so the file compiles on every target. These
        // never run because the facade only selects WebGLCryptoBackend under
        // UNITY_WEBGL && !UNITY_EDITOR.
        private static string ArcCrypto_GenerateKey() => throw new NotSupportedException();
        private static string ArcCrypto_DeriveAddress(string _) => throw new NotSupportedException();
        private static string ArcCrypto_SignLegacyTx(string _, string __) => throw new NotSupportedException();
        private static string ArcCrypto_SignEip1559Tx(string _, string __) => throw new NotSupportedException();
        private static string ArcCrypto_SignTypedDataV4(string _, string __) => throw new NotSupportedException();
        private static string ArcCrypto_SignPersonalMessage(string _, string __) => throw new NotSupportedException();
#endif

        // ------------------ key management ------------------

        public Task<GeneratedKey> GenerateKeyAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var raw = ArcCrypto_GenerateKey();
            var parsed = JsonUtility.FromJson<KeyBridgeResponse>(raw ?? "");
            ThrowIfError(parsed, "GenerateKey");
            return Task.FromResult(new GeneratedKey
            {
                Address = parsed.address,
                PrivateKey = parsed.privateKey,
            });
        }

        public string DeriveAddress(string privateKey)
        {
            var raw = ArcCrypto_DeriveAddress(privateKey);
            var parsed = JsonUtility.FromJson<AddressBridgeResponse>(raw ?? "");
            ThrowIfError(parsed, "DeriveAddress");
            return parsed.address;
        }

        // ------------------ tx signing ------------------

        public Task<string> SignLegacyTxAsync(LegacyTxParams tx, string privateKey, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var txJson = JsonUtility.ToJson(new LegacyTxBridgeBody
            {
                to = tx.To ?? string.Empty,
                nonce = tx.Nonce.ToString(),
                gasPrice = tx.GasPrice.ToString(),
                gasLimit = tx.GasLimit.ToString(),
                value = tx.Value.ToString(),
                data = string.IsNullOrEmpty(tx.Data) ? "0x" : tx.Data,
                chainId = tx.ChainId,
            });
            var raw = ArcCrypto_SignLegacyTx(txJson, privateKey);
            var parsed = JsonUtility.FromJson<SignBridgeResponse>(raw ?? "");
            ThrowIfError(parsed, "SignLegacyTx");
            return Task.FromResult(parsed.signed);
        }

        public Task<string> SignEip1559TxAsync(Eip1559TxParams tx, string privateKey, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var txJson = JsonUtility.ToJson(new Eip1559TxBridgeBody
            {
                to = tx.To ?? string.Empty,
                nonce = tx.Nonce.ToString(),
                maxFeePerGas = tx.MaxFeePerGas.ToString(),
                maxPriorityFeePerGas = tx.MaxPriorityFeePerGas.ToString(),
                gasLimit = tx.GasLimit.ToString(),
                value = tx.Value.ToString(),
                data = string.IsNullOrEmpty(tx.Data) ? "0x" : tx.Data,
                chainId = tx.ChainId,
            });
            var raw = ArcCrypto_SignEip1559Tx(txJson, privateKey);
            var parsed = JsonUtility.FromJson<SignBridgeResponse>(raw ?? "");
            ThrowIfError(parsed, "SignEip1559Tx");
            return Task.FromResult(parsed.signed);
        }

        public Task<string> SignTypedDataV4Async(string typedDataJson, string privateKey, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var raw = ArcCrypto_SignTypedDataV4(typedDataJson, privateKey);
            var parsed = JsonUtility.FromJson<SignBridgeResponse>(raw ?? "");
            ThrowIfError(parsed, "SignTypedDataV4");
            return Task.FromResult(parsed.signature);
        }

        public Task<string> SignPersonalMessageAsync(string message, string privateKey, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var raw = ArcCrypto_SignPersonalMessage(message, privateKey);
            var parsed = JsonUtility.FromJson<SignBridgeResponse>(raw ?? "");
            ThrowIfError(parsed, "SignPersonalMessage");
            return Task.FromResult(parsed.signature);
        }

        // ------------------ bridge DTOs ------------------

        [Serializable] private class BridgeError { public string error; }
        [Serializable] private class KeyBridgeResponse { public string error; public string address; public string privateKey; }
        [Serializable] private class AddressBridgeResponse { public string error; public string address; }
        [Serializable] private class SignBridgeResponse { public string error; public string signed; public string signature; }

        [Serializable] private class LegacyTxBridgeBody
        {
            public string to;
            public string nonce;
            public string gasPrice;
            public string gasLimit;
            public string value;
            public string data;
            public long chainId;
        }
        [Serializable] private class Eip1559TxBridgeBody
        {
            public string to;
            public string nonce;
            public string maxFeePerGas;
            public string maxPriorityFeePerGas;
            public string gasLimit;
            public string value;
            public string data;
            public long chainId;
        }

        private static void ThrowIfError(KeyBridgeResponse r, string op)
        {
            if (r != null && !string.IsNullOrEmpty(r.error))
                throw new InvalidOperationException($"[WebGLCryptoBackend.{op}] bridge error: {r.error}");
            if (r == null || string.IsNullOrEmpty(r.privateKey))
                throw new InvalidOperationException($"[WebGLCryptoBackend.{op}] bridge returned empty payload");
        }
        private static void ThrowIfError(AddressBridgeResponse r, string op)
        {
            if (r != null && !string.IsNullOrEmpty(r.error))
                throw new InvalidOperationException($"[WebGLCryptoBackend.{op}] bridge error: {r.error}");
            if (r == null || string.IsNullOrEmpty(r.address))
                throw new InvalidOperationException($"[WebGLCryptoBackend.{op}] bridge returned empty payload");
        }
        private static void ThrowIfError(SignBridgeResponse r, string op)
        {
            if (r != null && !string.IsNullOrEmpty(r.error))
                throw new InvalidOperationException($"[WebGLCryptoBackend.{op}] bridge error: {r.error}");
            if (r == null || (string.IsNullOrEmpty(r.signed) && string.IsNullOrEmpty(r.signature)))
                throw new InvalidOperationException($"[WebGLCryptoBackend.{op}] bridge returned empty payload");
        }
    }
}
