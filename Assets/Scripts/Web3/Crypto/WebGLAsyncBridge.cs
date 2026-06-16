using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace ArcTrading.Crypto
{
    /// <summary>
    /// Polling-based async helper for jslib bridge functions that return a
    /// request-id string. The JS side stores the underlying Promise resolution
    /// in a Map; C# polls via the matching <c>PollRequest</c> extern at ~30 ms
    /// cadence, yielding through <see cref="DelayMsAsync"/>.
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
            Debug.Log($"[WebGLAsyncBridge] AwaitAsync({requestId}): entered");
            if (string.IsNullOrEmpty(requestId))
                throw new InvalidOperationException("WebGLAsyncBridge: bridge call returned empty request id");

            // Hard cap the wait to avoid leaking pending state if the JS side
            // gets stuck (e.g. MetaMask popup left open forever). 5 minutes is
            // long enough for user confirmation, short enough to surface bugs.
            var deadline = DateTime.UtcNow.AddMinutes(5);
            int polls = 0;

            while (true)
            {
                ct.ThrowIfCancellationRequested();
                if (DateTime.UtcNow > deadline)
                    throw new TimeoutException($"WebGLAsyncBridge: request {requestId} did not resolve within 5 minutes");

                polls++;
                var json = pollFn(requestId);
                if (polls <= 3 || polls % 50 == 0)
                    Debug.Log($"[WebGLAsyncBridge] AwaitAsync({requestId}) poll #{polls}: raw='{json}'");

                if (string.IsNullOrEmpty(json))
                    throw new InvalidOperationException($"WebGLAsyncBridge: PollRequest({requestId}) returned empty");

                var env = JsonUtility.FromJson<PollEnvelope>(json);
                if (env == null)
                    throw new InvalidOperationException($"WebGLAsyncBridge: PollRequest({requestId}) returned unparseable envelope: {json}");

                if (env.pending)
                {
                    if (polls <= 3) Debug.Log($"[WebGLAsyncBridge] AwaitAsync({requestId}) poll #{polls}: about to delay 30ms");
                    await DelayMsAsync(30, ct).ConfigureAwait(true);
                    if (polls <= 3) Debug.Log($"[WebGLAsyncBridge] AwaitAsync({requestId}) poll #{polls}: delay returned");
                    continue;
                }

                if (!string.IsNullOrEmpty(env.error))
                    throw new InvalidOperationException(env.error);

                Debug.Log($"[WebGLAsyncBridge] AwaitAsync({requestId}): returning result='{env.result}' after {polls} polls");
                return env.result ?? string.Empty;
            }
        }

        // -------------------------------------------------------------
        //  WebGL-safe delay
        // -------------------------------------------------------------
        // Task.Delay in Unity WebGL builds doesn't always wake the await
        // continuation (the SynchronizationContext / player-loop integration
        // has an edge case that strands any poll/backoff loop). Route through
        // a coroutine on a hidden MonoBehaviour instead — coroutines are
        // pumped by Unity's player loop every frame, which is guaranteed to
        // run. Desktop / Editor still uses Task.Delay (threadpool timer).
        //
        // Exposed publicly so every backoff / retry / receipt-poll site in
        // the codebase can share a single WebGL-safe delay implementation.
        public static Task DelayMsAsync(int ms, CancellationToken ct = default)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var helper = WebGLYieldHelper.EnsureInstance();
            CancellationTokenRegistration ctReg = default;
            if (ct.CanBeCanceled)
                ctReg = ct.Register(() => tcs.TrySetCanceled(ct));
            helper.StartCoroutine(WebGLYieldHelper.DelayRoutine(Mathf.Max(0f, ms / 1000f), () =>
            {
                ctReg.Dispose();
                tcs.TrySetResult(true);
            }));
            return tcs.Task;
#else
            return ct.CanBeCanceled ? Task.Delay(ms, ct) : Task.Delay(ms);
#endif
        }

        public static Task DelayAsync(TimeSpan delay, CancellationToken ct = default)
            => DelayMsAsync((int)Math.Max(0, Math.Round(delay.TotalMilliseconds)), ct);
    }

#if UNITY_WEBGL && !UNITY_EDITOR
    /// <summary>
    /// Hidden MonoBehaviour host so we can drive WaitForSecondsRealtime from
    /// async/await callsites in WebGL. Spawned lazily on first delay.
    /// </summary>
    internal class WebGLYieldHelper : MonoBehaviour
    {
        private static WebGLYieldHelper instance;

        public static WebGLYieldHelper EnsureInstance()
        {
            if (instance == null)
            {
                var go = new GameObject("[WebGLYieldHelper]");
                go.hideFlags = HideFlags.HideAndDontSave;
                DontDestroyOnLoad(go);
                instance = go.AddComponent<WebGLYieldHelper>();
            }
            return instance;
        }

        public static IEnumerator DelayRoutine(float seconds, Action callback)
        {
            if (seconds > 0f) yield return new WaitForSecondsRealtime(seconds);
            else yield return null;
            callback();
        }
    }
#endif
}
