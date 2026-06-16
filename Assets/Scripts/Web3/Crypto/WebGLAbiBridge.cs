using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace ArcTrading.Crypto
{
    /// <summary>
    /// ABI encoder bridge so the WebGL build doesn't need a hand-rolled C#
    /// encoder. Delegates to <c>window.ArcTradingCryptoBridge.encodeFunctionData</c>
    /// which wraps viem's <c>encodeFunctionData</c>. Sync — no polling.
    ///
    /// Args convention: pass uint256 as decimal strings (the JS side coerces
    /// any /^\d+$/ string back to BigInt for viem). Addresses are 0x strings,
    /// booleans are bool, bytes are 0x-hex strings.
    /// </summary>
    public static class WebGLAbiBridge
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern string ArcCrypto_EncodeFunctionData(
            string abiJson, string functionName, string argsJson);
#else
        private static string ArcCrypto_EncodeFunctionData(string _, string __, string ___)
            => throw new PlatformNotSupportedException("WebGL-only");
#endif

        [Serializable] private class EncodeResponse { public string error; public string data; }

        public static string EncodeFunctionData(string abiJson, string functionName, string argsJson)
        {
            if (string.IsNullOrEmpty(abiJson)) throw new ArgumentException("abiJson required");
            if (string.IsNullOrEmpty(functionName)) throw new ArgumentException("functionName required");
            if (string.IsNullOrEmpty(argsJson)) argsJson = "[]";

            var raw = ArcCrypto_EncodeFunctionData(abiJson, functionName, argsJson);
            if (string.IsNullOrEmpty(raw))
                throw new InvalidOperationException($"encodeFunctionData({functionName}) returned empty");

            var parsed = JsonUtility.FromJson<EncodeResponse>(raw);
            if (parsed != null && !string.IsNullOrEmpty(parsed.error))
                throw new InvalidOperationException($"encodeFunctionData({functionName}) error: {parsed.error}");
            if (parsed == null || string.IsNullOrEmpty(parsed.data))
                throw new InvalidOperationException($"encodeFunctionData({functionName}) returned no data");
            return parsed.data;
        }
    }
}
