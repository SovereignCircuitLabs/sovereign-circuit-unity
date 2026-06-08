using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace ArcTrading.Auth
{
    /// <summary>
    /// HttpListener bound to 127.0.0.1.
    ///
    /// Two roles in one process:
    /// 1. **Login**: serves the SIWE login page from StreamingAssets/walletlogin/ and
    ///    accepts the signed POST /auth callback.
    /// 2. **Tx bridge** (after login): keeps the same browser tab open and acts as a
    ///    relay between Unity and MetaMask. Unity enqueues unsigned txs via
    ///    EnqueueOwnerTxAsync; the page long-polls GET /tx-next, calls
    ///    eth_sendTransaction, then POSTs the resulting tx hash back to /tx-result.
    ///
    /// The bridge is required because SIWE only proves wallet ownership — it
    /// doesn't hand Unity the private key, so any owner-side write has to go
    /// through MetaMask in the browser.
    /// </summary>
    public sealed class WalletLoginServer : IDisposable
    {
        private readonly string statement;
        private readonly long chainId;
        private readonly TimeSpan sessionTtl;
        private readonly TimeSpan challengeTtl = TimeSpan.FromMinutes(10);
        private readonly bool persistentBridge;

        // Surfaced to the login page so it can render the public leaderboard /
        // price-chart panels without needing its own RPC config. All four are
        // strings (addresses + URL) and may be empty — the page hides the
        // dependent panel if any of them are missing.
        private readonly string rpcUrl;
        private readonly string gamePaymentAddress;
        private readonly string npcCharacterAddress;
        private readonly string usdcAddress;
        private readonly string gatewayAddress;

        private HttpListener listener;
        private string boundOrigin;          // e.g. "http://127.0.0.1:7777"
        private int boundPort;
        private string nonce;
        private DateTimeOffset challengeExpiresAt;
        private string cachedHtml;
        private CancellationTokenSource cts;
        private readonly TaskCompletionSource<WalletSession> tcs =
            new TaskCompletionSource<WalletSession>(TaskCreationOptions.RunContinuationsAsynchronously);
        private int handled;
        private volatile WalletSession authenticatedSession;

        // Tx bridge state ----
        private readonly ConcurrentQueue<WalletTxRequest> pendingTxs = new ConcurrentQueue<WalletTxRequest>();
        private readonly ConcurrentDictionary<string, TaskCompletionSource<string>> pendingTxResults =
            new ConcurrentDictionary<string, TaskCompletionSource<string>>();
        private readonly SemaphoreSlim txAvailable = new SemaphoreSlim(0, int.MaxValue);
        private DateTimeOffset lastBridgePoll;

        public string LoginUrl => boundOrigin + "/login";
        public int BoundPort => boundPort;
        public bool IsListening => listener != null && listener.IsListening;
        public WalletSession AuthenticatedSession => authenticatedSession;

        /// <summary>
        /// Approximate seconds since the browser last hit /tx-next. ∞ if never.
        /// Use to detect a stale/closed bridge tab.
        /// </summary>
        public double SecondsSinceLastBridgePoll =>
            lastBridgePoll == default
                ? double.PositiveInfinity
                : (DateTimeOffset.UtcNow - lastBridgePoll).TotalSeconds;

        public WalletLoginServer(string statement, long chainId, TimeSpan sessionTtl, bool persistentBridge,
            string rpcUrl = "", string gamePaymentAddress = "",
            string npcCharacterAddress = "", string usdcAddress = "",
            string gatewayAddress = "")
        {
            this.statement = string.IsNullOrEmpty(statement) ? "Sign in to ArcTrading" : statement;
            this.chainId = chainId;
            this.sessionTtl = sessionTtl;
            this.persistentBridge = persistentBridge;
            this.rpcUrl = rpcUrl ?? string.Empty;
            this.gamePaymentAddress = gamePaymentAddress ?? string.Empty;
            this.npcCharacterAddress = npcCharacterAddress ?? string.Empty;
            this.usdcAddress = usdcAddress ?? string.Empty;
            this.gatewayAddress = gatewayAddress ?? string.Empty;
        }

        public void Start(int preferredPort)
        {
            HttpListener candidate = null;
            int chosen = 0;
            for (int p = preferredPort; p < preferredPort + 10; p++)
            {
                candidate = new HttpListener();
                // 127.0.0.1 (not "+" / "*") = no admin-only URL ACL on Windows, loopback-only.
                candidate.Prefixes.Add($"http://127.0.0.1:{p}/");
                try
                {
                    candidate.Start();
                    chosen = p;
                    break;
                }
                catch (HttpListenerException)
                {
                    candidate.Close();
                    candidate = null;
                }
                catch (Exception ex)
                {
                    candidate?.Close();
                    throw new WalletLoginException($"failed to start listener on port {p}: {ex.Message}", ex);
                }
            }
            if (candidate == null)
                throw new WalletLoginException(
                    $"no free port in [{preferredPort}..{preferredPort + 9}]; close any process using these ports and retry.");

            listener = candidate;
            boundPort = chosen;
            boundOrigin = $"http://127.0.0.1:{boundPort}";
            nonce = GenerateNonceHex(16);
            challengeExpiresAt = DateTimeOffset.UtcNow + challengeTtl;
            cts = new CancellationTokenSource();

            Debug.Log($"[WalletLoginServer] listening on {boundOrigin} (login at {LoginUrl}, persistentBridge={persistentBridge})");
            _ = Task.Run(() => ListenLoop(cts.Token));
        }

        public Task<WalletSession> AwaitLoginAsync(CancellationToken externalCancel)
        {
            externalCancel.Register(() =>
            {
                tcs.TrySetCanceled(externalCancel);
                if (!persistentBridge) Stop();
            });
            return tcs.Task;
        }

        /// <summary>
        /// Enqueue an unsigned tx for the browser-side MetaMask to sign+broadcast.
        /// Resolves with the tx hash returned by eth_sendTransaction, or throws if
        /// the browser reports an error / disconnects.
        /// </summary>
        public Task<string> EnqueueOwnerTxAsync(WalletTxRequest req, CancellationToken ct = default)
        {
            if (!IsListening)
                throw new InvalidOperationException("[WalletLoginServer] not listening; call Start first.");
            if (authenticatedSession == null)
                throw new InvalidOperationException("[WalletLoginServer] bridge requires a successful /auth first.");
            if (req == null) throw new ArgumentNullException(nameof(req));
            if (string.IsNullOrEmpty(req.id)) req.id = Guid.NewGuid().ToString("N");
            if (string.IsNullOrEmpty(req.from)) req.from = authenticatedSession.wallet;

            var resultTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!pendingTxResults.TryAdd(req.id, resultTcs))
                throw new InvalidOperationException($"duplicate tx id {req.id}");

            ct.Register(() =>
            {
                if (pendingTxResults.TryRemove(req.id, out var pending))
                    pending.TrySetCanceled(ct);
            });

            pendingTxs.Enqueue(req);
            txAvailable.Release();
            return resultTcs.Task;
        }

        public void Stop()
        {
            try { cts?.Cancel(); } catch { /* ignored */ }
            try { listener?.Stop(); } catch { /* ignored */ }
            try { listener?.Close(); } catch { /* ignored */ }
            listener = null;

            // Drain pending bridge requests so callers don't hang forever.
            foreach (var kv in pendingTxResults)
                kv.Value.TrySetException(new InvalidOperationException("WalletLoginServer stopped"));
            pendingTxResults.Clear();
            while (pendingTxs.TryDequeue(out _)) { }
        }

        public void Dispose() => Stop();

        // ---------------- request loop ----------------

        private async Task ListenLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && listener != null && listener.IsListening)
            {
                HttpListenerContext ctx;
                try
                {
                    ctx = await listener.GetContextAsync().ConfigureAwait(false);
                }
                catch (ObjectDisposedException) { return; }
                catch (HttpListenerException) { return; }

                _ = Task.Run(() => HandleRequest(ctx), ct);
            }
        }

        private async Task HandleRequest(HttpListenerContext ctx)
        {
            try
            {
                var path = ctx.Request.Url.AbsolutePath ?? "/";
                var method = ctx.Request.HttpMethod;

                if (method == "GET" && (path == "/" || path == "/index.html"))
                {
                    Redirect(ctx, "/login");
                    return;
                }
                if (method == "GET" && path == "/login")
                {
                    await ServeLoginPage(ctx).ConfigureAwait(false);
                    return;
                }
                if (method == "POST" && path == "/auth")
                {
                    await HandleAuth(ctx).ConfigureAwait(false);
                    return;
                }
                if (method == "GET" && path == "/tx-next")
                {
                    await HandleTxNext(ctx).ConfigureAwait(false);
                    return;
                }
                if (method == "POST" && path == "/tx-result")
                {
                    await HandleTxResult(ctx).ConfigureAwait(false);
                    return;
                }
                if (method == "GET" && path == "/favicon.ico")
                {
                    WriteStatus(ctx, 204);
                    return;
                }
                WriteStatus(ctx, 404, "not found");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[WalletLoginServer] request handler error: {ex}");
                try { WriteStatus(ctx, 500, "internal error"); } catch { /* connection probably dead */ }
            }
        }

        private async Task ServeLoginPage(HttpListenerContext ctx)
        {
            var html = LoadHtmlOnce();
            if (html == null)
            {
                WriteStatus(ctx, 500, "login page asset missing");
                return;
            }

            // Inject per-session config so the browser never has to fetch a separate /nonce.
            var configJson =
                $"{{\"nonce\":\"{nonce}\"," +
                $"\"chainId\":{chainId}," +
                $"\"domain\":\"127.0.0.1:{boundPort}\"," +
                $"\"uri\":\"{boundOrigin}\"," +
                $"\"statement\":{JsonEncodeString(statement)}," +
                $"\"expiresAt\":\"{challengeExpiresAt.UtcDateTime:yyyy-MM-ddTHH:mm:ss.fffZ}\"," +
                $"\"authEndpoint\":\"/auth\"," +
                $"\"bridgeMode\":{(persistentBridge ? "true" : "false")}," +
                $"\"txNextEndpoint\":\"/tx-next\"," +
                $"\"txResultEndpoint\":\"/tx-result\"," +
                $"\"rpcUrl\":{JsonEncodeString(rpcUrl)}," +
                $"\"gamePaymentAddress\":{JsonEncodeString(gamePaymentAddress)}," +
                $"\"npcCharacterAddress\":{JsonEncodeString(npcCharacterAddress)}," +
                $"\"usdcAddress\":{JsonEncodeString(usdcAddress)}," +
                $"\"gatewayAddress\":{JsonEncodeString(gatewayAddress)}}}";
            var rendered = html.Replace("__LOGIN_CONFIG__", HtmlAttrEncode(configJson));

            await WriteBytes(ctx, 200, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(rendered)).ConfigureAwait(false);
        }

        private async Task HandleAuth(HttpListenerContext ctx)
        {
            if (Interlocked.CompareExchange(ref handled, 1, 0) != 0)
            {
                WriteJson(ctx, 409, "{\"error\":\"already handled\"}");
                return;
            }

            var origin = ctx.Request.Headers["Origin"];
            if (!string.IsNullOrEmpty(origin) && origin != boundOrigin && origin != "null")
            {
                Volatile.Write(ref handled, 0);
                WriteJson(ctx, 403, "{\"error\":\"bad origin\"}");
                return;
            }
            var contentType = ctx.Request.ContentType ?? string.Empty;
            if (!contentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase))
            {
                Volatile.Write(ref handled, 0);
                WriteJson(ctx, 415, "{\"error\":\"need application/json\"}");
                return;
            }

            string body;
            using (var reader = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding ?? Encoding.UTF8))
                body = await reader.ReadToEndAsync().ConfigureAwait(false);

            string message = null, signature = null;
            try
            {
                ExtractAuthFields(body, out message, out signature);
            }
            catch (Exception ex)
            {
                Volatile.Write(ref handled, 0);
                WriteJson(ctx, 400, $"{{\"error\":\"bad request body: {JsonEscape(ex.Message)}\"}}");
                return;
            }

            var verify = SiweMessage.Verify(message, signature, nonce, chainId);
            if (!verify.Ok)
            {
                Volatile.Write(ref handled, 0);
                WriteJson(ctx, 401, $"{{\"error\":{JsonEncodeString(verify.Reason)}}}");
                return;
            }

            var nowUtc = DateTimeOffset.UtcNow;
            var session = new WalletSession
            {
                wallet = verify.WalletLower,
                chainId = chainId,
                issuedAt = nowUtc.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                expiresAt = (nowUtc + sessionTtl).UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                message = message,
                signature = signature
            };

            authenticatedSession = session;
            tcs.TrySetResult(session);

            WriteJson(ctx, 200, $"{{\"ok\":true,\"wallet\":\"{verify.WalletLower}\",\"bridgeMode\":{(persistentBridge ? "true" : "false")}}}");

            // In non-bridge mode the listener tears down after a brief delay so the
            // browser has time to render its "complete" UI. In bridge mode we stay up.
            if (!persistentBridge)
            {
                _ = Task.Run(async () => { await Task.Delay(500).ConfigureAwait(false); Stop(); });
            }
        }

        private async Task HandleTxNext(HttpListenerContext ctx)
        {
            if (!persistentBridge)
            {
                WriteJson(ctx, 404, "{\"error\":\"bridge disabled\"}");
                return;
            }
            if (authenticatedSession == null)
            {
                WriteJson(ctx, 401, "{\"error\":\"not authenticated\"}");
                return;
            }

            lastBridgePoll = DateTimeOffset.UtcNow;

            // Long-poll up to 25s waiting for a queued tx. Browser will reconnect on 204.
            bool got;
            try
            {
                got = await txAvailable.WaitAsync(TimeSpan.FromSeconds(25), cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                WriteJson(ctx, 503, "{\"error\":\"shutting down\"}");
                return;
            }

            if (!got || !pendingTxs.TryDequeue(out var req))
            {
                WriteStatus(ctx, 204);
                return;
            }

            var json = SerializeTxRequest(req);
            WriteJson(ctx, 200, json);
            await Task.CompletedTask;
        }

        private async Task HandleTxResult(HttpListenerContext ctx)
        {
            if (!persistentBridge)
            {
                WriteJson(ctx, 404, "{\"error\":\"bridge disabled\"}");
                return;
            }
            var origin = ctx.Request.Headers["Origin"];
            if (!string.IsNullOrEmpty(origin) && origin != boundOrigin && origin != "null")
            {
                WriteJson(ctx, 403, "{\"error\":\"bad origin\"}");
                return;
            }

            string body;
            using (var reader = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding ?? Encoding.UTF8))
                body = await reader.ReadToEndAsync().ConfigureAwait(false);

            var id = ReadJsonString(body, "id");
            var txHash = ReadJsonString(body, "txHash");
            var error = ReadJsonString(body, "error");
            if (string.IsNullOrEmpty(id))
            {
                WriteJson(ctx, 400, "{\"error\":\"missing id\"}");
                return;
            }
            if (!pendingTxResults.TryRemove(id, out var resultTcs))
            {
                WriteJson(ctx, 404, "{\"error\":\"unknown tx id\"}");
                return;
            }

            if (!string.IsNullOrEmpty(error))
                resultTcs.TrySetException(new WalletLoginException($"browser-side tx failed: {error}"));
            else if (!string.IsNullOrEmpty(txHash))
                resultTcs.TrySetResult(txHash);
            else
                resultTcs.TrySetException(new WalletLoginException("browser-side tx result had neither txHash nor error"));

            WriteJson(ctx, 200, "{\"ok\":true}");
        }

        // ---------------- tx serialization ----------------

        private static string SerializeTxRequest(WalletTxRequest req)
        {
            var sb = new StringBuilder(256);
            sb.Append('{');
            sb.Append("\"id\":").Append(JsonEncodeString(req.id)).Append(',');
            sb.Append("\"from\":").Append(JsonEncodeString(req.from)).Append(',');
            sb.Append("\"to\":").Append(JsonEncodeString(req.to)).Append(',');
            sb.Append("\"value\":").Append(JsonEncodeString(req.value ?? "0x0")).Append(',');
            sb.Append("\"data\":").Append(JsonEncodeString(req.data ?? "0x")).Append(',');
            sb.Append("\"gas\":").Append(JsonEncodeString(req.gas ?? "")).Append(',');
            sb.Append("\"chainId\":").Append(req.chainId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            sb.Append('}');
            return sb.ToString();
        }

        private string LoadHtmlOnce()
        {
            if (cachedHtml != null) return cachedHtml;
            var path = Path.Combine(Application.streamingAssetsPath, "walletlogin", "index.html");
            if (!File.Exists(path))
            {
                Debug.LogError($"[WalletLoginServer] login HTML missing at {path}");
                return null;
            }
            cachedHtml = File.ReadAllText(path);
            return cachedHtml;
        }

        // ---------------- helpers ----------------

        private static void ExtractAuthFields(string json, out string message, out string signature)
        {
            message = ReadJsonString(json, "message")
                      ?? throw new FormatException("missing 'message'");
            signature = ReadJsonString(json, "signature")
                        ?? throw new FormatException("missing 'signature'");
        }

        private static string ReadJsonString(string json, string field)
        {
            var pattern = "\"" + field + "\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"";
            var m = System.Text.RegularExpressions.Regex.Match(json, pattern);
            if (!m.Success) return null;
            return UnescapeJsonString(m.Groups[1].Value);
        }

        private static string UnescapeJsonString(string s)
        {
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                var c = s[i];
                if (c != '\\') { sb.Append(c); continue; }
                if (i + 1 >= s.Length) throw new FormatException("trailing backslash");
                i++;
                var esc = s[i];
                switch (esc)
                {
                    case '"':  sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/':  sb.Append('/'); break;
                    case 'b':  sb.Append('\b'); break;
                    case 'f':  sb.Append('\f'); break;
                    case 'n':  sb.Append('\n'); break;
                    case 'r':  sb.Append('\r'); break;
                    case 't':  sb.Append('\t'); break;
                    case 'u':
                        if (i + 4 >= s.Length) throw new FormatException("bad \\u escape");
                        sb.Append((char)Convert.ToUInt16(s.Substring(i + 1, 4), 16));
                        i += 4;
                        break;
                    default: throw new FormatException($"bad escape \\{esc}");
                }
            }
            return sb.ToString();
        }

        private static string JsonEncodeString(string s)
        {
            if (s == null) return "null";
            var sb = new StringBuilder(s.Length + 2);
            sb.Append('"');
            foreach (var c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        private static string JsonEscape(string s)
        {
            if (s == null) return string.Empty;
            var quoted = JsonEncodeString(s);
            return quoted.Substring(1, quoted.Length - 2);
        }

        private static string HtmlAttrEncode(string s)
        {
            return s.Replace("&", "&amp;").Replace("\"", "&quot;").Replace("<", "&lt;").Replace(">", "&gt;");
        }

        private static string GenerateNonceHex(int bytes)
        {
            var buf = new byte[bytes];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(buf);
            var sb = new StringBuilder(bytes * 2);
            foreach (var b in buf) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        private static void Redirect(HttpListenerContext ctx, string to)
        {
            ctx.Response.StatusCode = 302;
            ctx.Response.RedirectLocation = to;
            ctx.Response.Close();
        }

        private static void WriteStatus(HttpListenerContext ctx, int code, string text = null)
        {
            ctx.Response.StatusCode = code;
            ctx.Response.ContentType = "text/plain; charset=utf-8";
            if (!string.IsNullOrEmpty(text))
            {
                var bytes = Encoding.UTF8.GetBytes(text);
                ctx.Response.ContentLength64 = bytes.Length;
                ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
            }
            ctx.Response.Close();
        }

        private static void WriteJson(HttpListenerContext ctx, int code, string body)
        {
            ctx.Response.StatusCode = code;
            ctx.Response.ContentType = "application/json; charset=utf-8";
            var bytes = Encoding.UTF8.GetBytes(body);
            ctx.Response.ContentLength64 = bytes.Length;
            ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
            ctx.Response.Close();
        }

        private static async Task WriteBytes(HttpListenerContext ctx, int code, string contentType, byte[] bytes)
        {
            ctx.Response.StatusCode = code;
            ctx.Response.ContentType = contentType;
            ctx.Response.ContentLength64 = bytes.Length;
            await ctx.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
            ctx.Response.Close();
        }
    }

    public class WalletLoginException : Exception
    {
        public WalletLoginException(string message) : base(message) { }
        public WalletLoginException(string message, Exception inner) : base(message, inner) { }
    }
}
