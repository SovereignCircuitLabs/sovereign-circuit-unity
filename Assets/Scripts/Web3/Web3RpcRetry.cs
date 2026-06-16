using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Nethereum.JsonRpc.Client;
using UnityEngine;

/// <summary>
/// Retry wrapper for Nethereum (Desktop / Editor) RPC reads. Mono's HTTP +
/// TLS stack occasionally drops connections mid-handshake against public
/// HTTPS endpoints, surfacing as <see cref="RpcClientUnknownException"/>
/// wrapping IOException / WebException / HttpRequestException. Those are
/// transient — one retry almost always succeeds.
///
/// Reverts come back as <see cref="RpcResponseException"/>, which is NOT
/// considered transient and bubbles up immediately so callers still see
/// real contract errors.
///
/// WebGL paths don't use Nethereum directly; the equivalent retry already
/// lives in <c>ArcTradingApiClient.SendWithRetryAsync</c>.
/// </summary>
public static class Web3RpcRetry
{
    private const int DefaultMaxRetries = 3;

    public static async Task<T> RunAsync<T>(
        Func<Task<T>> op,
        string label = null,
        int maxRetries = DefaultMaxRetries,
        CancellationToken ct = default)
    {
        int attempt = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                return await op().ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (attempt < maxRetries && IsTransient(ex))
            {
                attempt++;
                var delayMs = BackoffMs(attempt);
                Debug.LogWarning(
                    $"[Web3RpcRetry] {label ?? "op"} transient failure " +
                    $"(attempt {attempt}/{maxRetries}, retry in {delayMs}ms): {RootMessage(ex)}");
                await DelayAsync(delayMs, ct).ConfigureAwait(true);
            }
        }
    }

    public static Task RunAsync(
        Func<Task> op,
        string label = null,
        int maxRetries = DefaultMaxRetries,
        CancellationToken ct = default)
    {
        return RunAsync(async () =>
        {
            await op().ConfigureAwait(true);
            return 0;
        }, label, maxRetries, ct);
    }

    // Quadratic-ish backoff, capped at 1.5s. With default maxRetries=3 the
    // worst-case total wait is ~250 + 1000 + 1500 = 2.75s before giving up.
    private static int BackoffMs(int attempt) => Math.Min(1500, 250 * attempt * attempt);

    // Routed through the WebGL-safe coroutine delay so the rare case where this
    // helper does get called from a WebGL build (e.g. shared helper paths) does
    // not strand on a non-firing await continuation. Desktop / Editor still
    // hits Task.Delay underneath.
    private static Task DelayAsync(int ms, CancellationToken ct)
        => ArcTrading.Crypto.WebGLAsyncBridge.DelayMsAsync(ms, ct);

    private static bool IsTransient(Exception ex)
    {
        for (var e = ex; e != null; e = e.InnerException)
        {
            switch (e)
            {
                case RpcClientUnknownException _:    // Nethereum's transport-layer wrapper
                case IOException _:                  // socket / stream errors
                case WebException _:                 // legacy mono webclient
                case HttpRequestException _:         // HttpClient errors
                case TimeoutException _:
                    return true;
            }
        }
        return false;
    }

    private static string RootMessage(Exception ex)
    {
        var e = ex;
        while (e.InnerException != null) e = e.InnerException;
        return e.Message;
    }
}
