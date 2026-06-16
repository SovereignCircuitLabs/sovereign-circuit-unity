using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace ArcTrading.WebGL
{
    /// <summary>
    /// Thin async/await wrapper around <see cref="UnityWebRequest"/>. Designed for the
    /// Unity WebGL build path: never spawns threads, never blocks the player loop, and
    /// surfaces HTTP errors as a typed exception so callers can branch on status code.
    ///
    /// Compiles on every platform (Editor included) so it can be unit-tested or used
    /// from Standalone for debugging. The decision of WHEN to call HTTP vs. Nethereum
    /// is made by the per-method <c>#if UNITY_WEBGL &amp;&amp; !UNITY_EDITOR</c> branch in
    /// the contract client files.
    /// </summary>
    public static class ArcTradingApiClient
    {
        public sealed class ApiException : Exception
        {
            public long StatusCode { get; }
            public string ResponseBody { get; }

            public ApiException(string message, long statusCode, string body) : base(message)
            {
                StatusCode = statusCode;
                ResponseBody = body;
            }
        }

        public static Task<string> GetAsync(string path, CancellationToken ct = default)
            => SendWithRetryAsync(UnityWebRequest.kHttpVerbGET, path, null, ct);

        public static Task<string> PostJsonAsync(string path, string jsonBody, CancellationToken ct = default)
            => SendWithRetryAsync(UnityWebRequest.kHttpVerbPOST, path, jsonBody, ct);

        public static async Task<T> GetJsonAsync<T>(string path, CancellationToken ct = default) where T : class
        {
            var text = await GetAsync(path, ct).ConfigureAwait(true);
            if (string.IsNullOrEmpty(text)) return null;
            return JsonUtility.FromJson<T>(text);
        }

        public static async Task<T> PostJsonAsync<T>(string path, string jsonBody, CancellationToken ct = default) where T : class
        {
            var text = await PostJsonAsync(path, jsonBody, ct).ConfigureAwait(true);
            if (string.IsNullOrEmpty(text)) return null;
            return JsonUtility.FromJson<T>(text);
        }

        private static async Task<string> SendWithRetryAsync(string method, string path, string body, CancellationToken ct)
        {
            var baseUrl = ArcTradingWebGLConfig.ApiBaseUrl;
            if (string.IsNullOrWhiteSpace(baseUrl))
                throw new InvalidOperationException("ArcTradingWebGLConfig.ApiBaseUrl is not set.");

            var fullUrl = baseUrl + (path.StartsWith("/") ? path : "/" + path);
            int retries = Math.Max(0, ArcTradingWebGLConfig.MaxRetries);
            int attempt = 0;

            while (true)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    return await SendOnceAsync(method, fullUrl, body, ct).ConfigureAwait(true);
                }
                catch (ApiException ex) when (ShouldRetry(ex) && attempt < retries)
                {
                    attempt++;
                    await DelayAsync(BackoffMs(attempt), ct).ConfigureAwait(true);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception) when (attempt < retries)
                {
                    attempt++;
                    await DelayAsync(BackoffMs(attempt), ct).ConfigureAwait(true);
                }
            }
        }

        // Quadratic-ish backoff capped at 1.5s. Keeps total wait under ~3s for the
        // default MaxRetries=2 budget so UI callers don't stall too long.
        private static int BackoffMs(int attempt) => Math.Min(1500, 250 * attempt * attempt);

        private static bool ShouldRetry(ApiException ex)
            => ex.StatusCode == 0 || ex.StatusCode >= 500;

        // Task.Delay strands the await continuation in some WebGL builds (the
        // SynchronizationContext / player-loop integration has an edge case
        // that leaves the retry loop wedged forever). Delegate to the shared
        // coroutine-backed delay, which falls back to Task.Delay off-WebGL.
        private static Task DelayAsync(int ms, CancellationToken ct)
            => ArcTrading.Crypto.WebGLAsyncBridge.DelayMsAsync(ms, ct);

        private static Task<string> SendOnceAsync(string method, string url, string body, CancellationToken ct)
        {
            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            UnityWebRequest req = null;
            try
            {
                req = new UnityWebRequest(url, method);
                req.downloadHandler = new DownloadHandlerBuffer();
                if (!string.IsNullOrEmpty(body))
                {
                    req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
                    req.SetRequestHeader("Content-Type", "application/json");
                }
                var token = ArcTradingWebGLConfig.AdminToken;
                if (!string.IsNullOrEmpty(token))
                    req.SetRequestHeader("Authorization", "Bearer " + token);
                req.timeout = ArcTradingWebGLConfig.RequestTimeoutSeconds;

                if (ArcTradingWebGLConfig.VerboseLogging)
                    Debug.Log($"[ArcTradingApiClient] {method} {url}");

                var op = req.SendWebRequest();

                CancellationTokenRegistration ctReg = default;
                if (ct.CanBeCanceled)
                {
                    var captured = req;
                    ctReg = ct.Register(() =>
                    {
                        try { if (captured != null && !captured.isDone) captured.Abort(); }
                        catch { /* swallow - request lifecycle is owned by completed callback */ }
                    });
                }

                var captured2 = req;
                var ctRegFinal = ctReg;
                op.completed += _ =>
                {
                    var r = captured2;
                    try
                    {
                        if (ct.IsCancellationRequested)
                        {
                            tcs.TrySetCanceled(ct);
                            return;
                        }

                        var responseBody = r.downloadHandler != null ? r.downloadHandler.text : null;

                        if (ArcTradingWebGLConfig.VerboseLogging)
                            Debug.Log($"[ArcTradingApiClient] {method} {url} -> {(long)r.responseCode}: {responseBody}");

#if UNITY_2020_2_OR_NEWER
                        var success = r.result == UnityWebRequest.Result.Success;
#else
                        var success = !r.isHttpError && !r.isNetworkError;
#endif
                        if (!success)
                        {
                            tcs.TrySetException(new ApiException(
                                $"HTTP {(long)r.responseCode} {method} {url}: {r.error}",
                                (long)r.responseCode,
                                responseBody));
                        }
                        else
                        {
                            tcs.TrySetResult(responseBody);
                        }
                    }
                    finally
                    {
                        try { ctRegFinal.Dispose(); } catch { }
                        try { r.Dispose(); } catch { }
                    }
                };
            }
            catch (Exception ex)
            {
                try { req?.Dispose(); } catch { }
                tcs.TrySetException(ex);
            }
            return tcs.Task;
        }
    }
}
