using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace ArcTrading.Crypto
{
    /// <summary>
    /// Polling-based async helper for jslib bridge functions that return a
    /// request-id string. The JS side stores the underlying Promise resolution
    /// in a Map; C# polls via the matching <c>PollRequest</c> extern at ~30 ms
    /// cadence (yielding to the WebGL player loop via <see cref="Task.Delay"/>).
    ///
    /// JS-side envelope shape:
    ///   { "pending": true }                — still in flight
    ///   { "result": "<string>" }           — fulfilled; complex types pre-stringified
    ///   { "error": "<message>" }           — rejected
    /// </summary>
    public static class WebGLAsyncBridge
    {
        [Serializable] private class PollEnvelope
        {
            public bool pending;
            public string result;
            public string error;
        }

        public static async Task<string> AwaitAsync(
            string requestId,
            Func<string, string> pollFn,
            CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(requestId))
                throw new InvalidOperationException("WebGLAsyncBridge: bridge call returned empty request id");

            // Hard cap the wait to avoid leaking pending state if the JS side
            // gets stuck (e.g. MetaMask popup left open forever). 5 minutes is
            // long enough for user confirmation, short enough to surface bugs.
            var deadline = DateTime.UtcNow.AddMinutes(5);

            while (true)
            {
                ct.ThrowIfCancellationRequested();
                if (DateTime.UtcNow > deadline)
                    throw new TimeoutException($"WebGLAsyncBridge: request {requestId} did not resolve within 5 minutes");

                var json = pollFn(requestId);
                if (string.IsNullOrEmpty(json))
                    throw new InvalidOperationException($"WebGLAsyncBridge: PollRequest({requestId}) returned empty");

                var env = JsonUtility.FromJson<PollEnvelope>(json);
                if (env == null)
                    throw new InvalidOperationException($"WebGLAsyncBridge: PollRequest({requestId}) returned unparseable envelope: {json}");

                if (env.pending)
                {
                    await Task.Delay(30, ct).ConfigureAwait(true);
                    continue;
                }

                if (!string.IsNullOrEmpty(env.error))
                    throw new InvalidOperationException(env.error);

                return env.result ?? string.Empty;
            }
        }
    }
}
