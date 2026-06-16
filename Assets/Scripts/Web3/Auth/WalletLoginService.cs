using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ArcTrading.Crypto;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ArcTrading.Auth
{
    /// <summary>
    /// Singleton entry point for the desktop wallet login + tx bridge flow.
    ///
    /// Two usage modes:
    /// 1. **Identity-only** (default): EnsureLoggedInAsync without persistentBridge.
    ///    The local HTTP server tears down after /auth; you only get a verified
    ///    wallet address from Current.wallet. Suitable for read-only flows or for
    ///    callers that already have a local private key to sign with.
    /// 2. **Owner-signing**: EnsureLoggedInAsync(persistentBridge: true). The server
    ///    stays alive; the browser tab stays open and acts as a MetaMask relay.
    ///    Callers then use SendOwnerTransactionAsync to submit transactions on
    ///    behalf of the logged-in wallet — each one triggers a MetaMask popup.
    /// </summary>
    public class WalletLoginService : Singleton<WalletLoginService>
    {
        [Header("Login page on-chain data")]
        [Tooltip("GamePayment contract — drives the GameItems price chart and NPC vault valuation on the login page.")]
        [SerializeField] private string gamePaymentAddress = "0xc7C9BBCe60802c94AfB7e224e98928A4Ee0de158";
        [Tooltip("ARC USDC token — used to read each NPC TBA's USDC balance for the leaderboard.")]
        [SerializeField] private string usdcAddress = "0x3600000000000000000000000000000000000000";
        [Tooltip("Circle Gateway Wallet — read totalBalance(usdc, paymentWallet) per NPC for the leaderboard's 'Gateway' column.")]
        [SerializeField] private string gatewayAddress = "0x0077777d7EBA4688BDeF3E311b846F25870A19B9";
        
        [SerializeField] private NpcCharacterContractClient npcContract;

        public WalletSession Current { get; private set; }
        public bool HasSession => Current != null && Current.IsValidNow();

        /// <summary>True if a persistent tx-bridge server is up and connected to a browser.</summary>
        public bool BridgeReady =>
            activeServer != null && activeServer.IsListening
                                 && activeServer.AuthenticatedSession != null;

        public event Action<WalletSession> OnLoginSucceeded;

        private long cachedChainId;
        private bool chainIdConfigured = false;
        private WalletLoginServer activeServer;
        private TaskCompletionSource<WalletSession> inflight;

        protected override void Awake()
        {
            base.Awake();
            DontDestroyOnLoad(gameObject);
        }

        protected async void Start()
        {
            try
            {
                if (npcContract == null)
                {
                    npcContract = FindObjectOfType<NpcCharacterContractClient>();
                }

                if (!chainIdConfigured)
                {
                    var chainId = await npcContract.GetChainIdAsync();
                    ConfigureChainId(chainId);
                }

                await EnsureLoggedInAsync(
                    "Sign in to ArcTrading",
                    7777,
                    TimeSpan.FromMinutes(1440),
                    CancellationToken.None,
                    persistentBridge: true).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                Debug.Log("[WalletLoginService] startup login cancelled.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[WalletLoginService] startup login failed: {ex}");
            }
        }

        public void ConfigureChainId(long chainId)
        {
            cachedChainId = chainId;
            chainIdConfigured = true;
        }

        /// <summary>
        /// Returns a valid WalletSession, prompting the user via the OS browser when
        /// no usable session is on disk. When <paramref name="persistentBridge"/> is
        /// true the server is kept alive after /auth so the browser tab can relay
        /// future MetaMask signatures back to Unity (see SendOwnerTransactionAsync).
        ///
        /// Concurrent callers share the same in-flight Task.
        /// </summary>
        public async Task<WalletSession> EnsureLoggedInAsync(
            string siweStatement,
            int preferredPort,
            TimeSpan sessionTtl,
            CancellationToken ct = default,
            bool persistentBridge = false)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            // WebGL: there is no HttpListener, no separate browser tab to relay
            // through. The player's MetaMask extension lives in the SAME tab as
            // the Unity WebGL build, so SIWE is just a direct
            // window.ethereum.request({method: "personal_sign"}) via the jslib
            // bridge. persistentBridge has no meaning here — every send-tx call
            // pops MetaMask just like the Desktop bridge mode does.
            if (HasSession) return Current;

            // Restored session reuse still applies (same WalletSession schema).
            var restoredWebgl = WalletSession.LoadOrNull();
            if (restoredWebgl != null && restoredWebgl.IsValidNow())
            {
                Current = restoredWebgl;
                Debug.Log($"[WalletLoginService] restored session for {restoredWebgl.wallet}");
                OnLoginSucceeded?.Invoke(restoredWebgl);
                return restoredWebgl;
            }

            if (inflight != null) return await inflight.Task.ConfigureAwait(true);
            inflight = new TaskCompletionSource<WalletSession>(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                var wallet = await WebGLMetamaskBridge.RequestAccountsAsync(ct).ConfigureAwait(true);
                if (string.IsNullOrEmpty(wallet))
                    throw new InvalidOperationException("MetaMask returned no wallet");

                // Use the cached chainId if the caller already configured one;
                // otherwise ask MetaMask. SIWE messages with mismatched chainIds
                // are rejected by Verify.
                long chainIdLocal = chainIdConfigured
                    ? cachedChainId
                    : await WebGLMetamaskBridge.ChainIdAsync(ct).ConfigureAwait(true);
                ConfigureChainId(chainIdLocal);

                var origin = Application.absoluteURL ?? string.Empty;
                string domain = "webgl", uri = origin;
                try
                {
                    if (!string.IsNullOrEmpty(origin))
                    {
                        var u = new Uri(origin);
                        domain = u.Authority;
                        uri = u.GetLeftPart(UriPartial.Authority);
                    }
                }
                catch { /* fall back to defaults */ }

                var now = DateTimeOffset.UtcNow;
                var nonce = GenerateNonceHex(16);
                var siwe = SiweMessage.Build(new SiweMessage.BuildArgs
                {
                    Domain = domain,
                    Address = wallet,
                    Statement = string.IsNullOrEmpty(siweStatement) ? "Sign in to ArcTrading" : siweStatement,
                    Uri = uri,
                    ChainId = chainIdLocal,
                    Nonce = nonce,
                    IssuedAt = now,
                    ExpirationTime = now + sessionTtl,
                });

                var signature = await WebGLMetamaskBridge.PersonalSignAsync(siwe, wallet, ct).ConfigureAwait(true);

                var session = new WalletSession
                {
                    wallet = wallet.ToLowerInvariant(),
                    chainId = chainIdLocal,
                    issuedAt = now.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                    expiresAt = (now + sessionTtl).UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                    message = siwe,
                    signature = signature,
                };
                session.Save();
                Current = session;
                Debug.Log($"[WalletLoginService] webgl SIWE ok: {session.wallet}");
                OnLoginSucceeded?.Invoke(session);
                inflight.TrySetResult(session);
                return session;
            }
            catch (OperationCanceledException)
            {
                inflight?.TrySetCanceled(ct);
                throw;
            }
            catch (Exception ex)
            {
                inflight?.TrySetException(ex);
                throw;
            }
            finally
            {
                inflight = null;
            }
#else
            // Bridge callers can't reuse a PlayerPrefs-restored session as-is: they
            // need a live browser tab to sign txs. So we only short-circuit on
            // restored session when persistentBridge=false.
            if (HasSession && (!persistentBridge || BridgeReady))
                return Current;

            if (!persistentBridge)
            {
                var restored = WalletSession.LoadOrNull();
                if (restored != null && restored.IsValidNow())
                {
                    Current = restored;
                    Debug.Log($"[WalletLoginService] restored session for {restored.wallet}");
                    OnLoginSucceeded?.Invoke(restored);
                    return restored;
                }
            }

            if (inflight != null) return await inflight.Task.ConfigureAwait(true);

            inflight = new TaskCompletionSource<WalletSession>(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                // Prefer the Inspector-wired serialized field; fall back to a
                // same-GameObject GetComponent for the legacy single-GameObject
                // setup. Without this, an Inspector-wired sibling reference is
                // silently ignored here — index.html then receives empty rpcUrl
                // / npcCharacterAddress and hides the leaderboard + price chart.
                var contract = npcContract != null
                    ? npcContract
                    : FindObjectOfType<NpcCharacterContractClient>();
                var server = new WalletLoginServer(
                    siweStatement, cachedChainId, sessionTtl, persistentBridge,
                    rpcUrl: contract != null ? contract.RpcUrl : string.Empty,
                    gamePaymentAddress: gamePaymentAddress ?? string.Empty,
                    npcCharacterAddress: contract != null ? contract.NftContractAddress : string.Empty,
                    usdcAddress: usdcAddress ?? string.Empty,
                    gatewayAddress: gatewayAddress ?? string.Empty);
                server.Start(preferredPort);
                activeServer = server;

                var url = server.LoginUrl;
                Debug.Log($"[WalletLoginService] opening browser to {url} (persistentBridge={persistentBridge})");
                Application.OpenURL(url);

                var session = await server.AwaitLoginAsync(ct).ConfigureAwait(true);
                session.Save();
                Current = session;

                Debug.Log($"[WalletLoginService] login ok: {session.wallet}");
                OnLoginSucceeded?.Invoke(session);
                inflight.TrySetResult(session);
                return session;
            }
            catch (OperationCanceledException)
            {
                inflight?.TrySetCanceled(ct);
                throw;
            }
            catch (Exception ex)
            {
                inflight?.TrySetException(ex);
                throw;
            }
            finally
            {
                inflight = null;
                // In bridge mode we deliberately keep activeServer alive so
                // SendOwnerTransactionAsync has a live relay. The server is torn
                // down on Logout(), OnDestroy or OnApplicationQuit.
                if (!persistentBridge)
                {
                    activeServer?.Dispose();
                    activeServer = null;
                }
            }
#endif
        }

        /// <summary>
        /// Submit an unsigned tx through the active bridge. Requires that
        /// EnsureLoggedInAsync was previously called with persistentBridge=true and
        /// the browser tab is still open.
        ///
        /// Resolves with the tx hash that MetaMask returned from eth_sendTransaction.
        /// Throws if the bridge isn't up, the browser reports an error, or the user
        /// rejects in MetaMask.
        /// </summary>
        public async Task<string> SendOwnerTransactionAsync(WalletTxRequest req, CancellationToken ct = default)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (Current == null || !Current.IsValidNow())
                throw new InvalidOperationException(
                    "[WalletLoginService] no valid SIWE session. Call EnsureLoggedInAsync first.");
            if (req == null) throw new ArgumentNullException(nameof(req));
            if (string.IsNullOrEmpty(req.from)) req.from = Current.wallet;

            // eth_sendTransaction params: { from, to, value, data, gas? }.
            // chainId / id / label are Unity-side fields; not part of the RPC.
            // gas is intentionally omitted when blank so MetaMask estimates.
            var value = string.IsNullOrEmpty(req.value) ? "0x0" : req.value;
            var data = string.IsNullOrEmpty(req.data) ? "0x" : req.data;
            string txJson = string.IsNullOrEmpty(req.gas)
                ? JsonUtility.ToJson(new EthSendTxNoGas { from = req.from, to = req.to, value = value, data = data })
                : JsonUtility.ToJson(new EthSendTxWithGas { from = req.from, to = req.to, value = value, data = data, gas = req.gas });

            return await WebGLMetamaskBridge.SendTransactionAsync(txJson, ct).ConfigureAwait(true);
#else
            if (activeServer == null || !activeServer.IsListening)
                throw new InvalidOperationException(
                    "[WalletLoginService] no active bridge. Call EnsureLoggedInAsync with persistentBridge=true first.");
            if (activeServer.AuthenticatedSession == null)
                throw new InvalidOperationException(
                    "[WalletLoginService] bridge server is up but no wallet has logged in yet.");

            // Warn (but don't block) when the browser tab hasn't polled recently —
            // the tx will queue and eventually time out if the tab really is closed.
            if (activeServer.SecondsSinceLastBridgePoll > 60)
                Debug.LogWarning(
                    $"[WalletLoginService] bridge tab hasn't polled for {activeServer.SecondsSinceLastBridgePoll:F0}s; " +
                    "if you closed the browser tab, reopen it from the login URL.");

            return await activeServer.EnqueueOwnerTxAsync(req, ct).ConfigureAwait(true);
#endif
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        [Serializable] private class EthSendTxNoGas
        {
            public string from; public string to; public string value; public string data;
        }
        [Serializable] private class EthSendTxWithGas
        {
            public string from; public string to; public string value; public string data; public string gas;
        }
#endif

        public void Logout()
        {
            Current = null;
            WalletSession.Clear();
            try { activeServer?.Dispose(); } catch { /* ignored */ }
            activeServer = null;
            Debug.Log("[WalletLoginService] session cleared");
        }

        private void OnDestroy()
        {
            try { activeServer?.Dispose(); } catch { /* ignored */ }
        }

        private void OnApplicationQuit()
        {
            try { activeServer?.Dispose(); } catch { /* ignored */ }
        }

        // Local copy of WalletLoginServer.GenerateNonceHex — used by the WebGL
        // SIWE path where the message is built client-side (no HttpListener
        // intermediary to emit the nonce). Same algorithm: cryptographic RNG +
        // lowercase hex, 16-byte default.
        private static string GenerateNonceHex(int bytes)
        {
            var buf = new byte[bytes];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(buf);
            var sb = new StringBuilder(bytes * 2);
            foreach (var b in buf) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }
}
