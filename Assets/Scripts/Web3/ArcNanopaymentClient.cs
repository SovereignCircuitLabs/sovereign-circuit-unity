using System;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Nethereum.ABI.EIP712;
using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Hex.HexConvertors.Extensions;
using Nethereum.Signer;
using Nethereum.Signer.EIP712;
using Nethereum.Web3;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

// nanopayments - buyer/client
namespace ArcTrading.Nanopayment
{
    [Struct("TransferWithAuthorization")]
    public class TransferWithAuthorizationMessage
    {
        [Parameter("address", "from", 1)] public string From { get; set; }
        [Parameter("address", "to", 2)] public string To { get; set; }
        [Parameter("uint256", "value", 3)] public BigInteger Value { get; set; }

        [Parameter("uint256", "validAfter", 4)]
        public BigInteger ValidAfter { get; set; }

        [Parameter("uint256", "validBefore", 5)]
        public BigInteger ValidBefore { get; set; }

        [Parameter("bytes32", "nonce", 6)] public byte[] Nonce { get; set; }
    }

    [RequireComponent(typeof(ArcTradingContractClient))]
    public partial class ArcNanopaymentClient : MonoBehaviour
    {
        private const string PaymentRequiredHeader = "PAYMENT-REQUIRED";
        private const string PaymentSignatureHeader = "PAYMENT-SIGNATURE";
        private const string PaymentResponseHeader = "PAYMENT-RESPONSE";
        private const string NpcTbaHeader = "X-NPC-TBA";
        private const string NpcTokenIdHeader = "X-NPC-TOKEN-ID";
        private const string DomainName = "GatewayWalletBatched";
        private const string DomainVersion = "1";
        private const string PrimaryType = "TransferWithAuthorization";
        
        private const int authorizationTtlSeconds = 604900;

        [Header("Configuration")]
        public string x402ServerBaseUrl = "http://localhost:4021/item/";
        // Upper bound for any single x402 payment, in USDC. The server quotes the actual price
        // via the 402 PAYMENT-REQUIRED header; we refuse to sign if it exceeds this cap.
        public float maxNanopaymentUsdc = 5f;

        // Smallest-unit amount signed for in the most recent successful x402 call.
        // Callers can read this immediately after awaiting FetchPaywalledResourceAsync to know the exact
        // amount that will be settled out of the gateway wallet (batch settlement lags chain reads).
        public BigInteger LastPaidAmountSmallestUnits { get; private set; }
        [SerializeField] private long fallbackChainId = 5042002;
        [SerializeField] private int httpTimeoutSeconds = 20;
        [SerializeField] private bool verboseLogging = false;

        [Header("Order Polling")]
        // After the server settles the x402 payment it returns an order id and continues the mint
        // asynchronously. We poll /order/:orderId until the order reaches a terminal state.
        [SerializeField] private float orderPollIntervalSeconds = 2f;
        [SerializeField] private int orderPollMaxAttempts = 90;
        
        // [ContextMenu("Fetch Paywalled Resource Test")]
        // public async void FetchPaywalledResourceTest()
        // {
        //     tradingContractClient = GetComponent<ArcTradingContractClient>();
        //     rpcUrl = tradingContractClient.RpcUrl;
        //     gatewayReadOnlyWeb3 = new Web3(rpcUrl);
        //
        //     await ApproveIfNeededThenGatewayDepositAsync(1); // deposit 1 usdc into gateway
        //
        //     var content = await FetchPaywalledResourceAsync(
        //         x402ServerBaseUrl,
        //         tradingContractClient.PrivateKey,
        //         Erc20UsdcHelper.ParseUsdc(0.5m));
        //     Debug.Log($"Fetched content: {content}");
        // }
        
        public async Task<string> FetchPaywalledResourceAsync(
            string url,
            BigInteger tokenId,
            NpcPaymentWalletService walletService,
            BigInteger maxPaymentAmount,
            string tbaAddress = null)
        {
            if (walletService == null) throw new ArgumentNullException(nameof(walletService));

            var signer = await walletService.VerifySignerForSigningAsync(tokenId);

            if (verboseLogging)
                Debug.Log($"[ArcNanopayment] verified signer for NPC {tokenId}: addr={signer.Address}, version={signer.Version}");

            return await FetchPaywalledResourceAsync(url, signer.PrivateKey, maxPaymentAmount, tbaAddress, tokenId);
        }

        public async Task<string> FetchPaywalledResourceAsync(
            string url,
            string npcPrivateKey,
            BigInteger maxPaymentAmount,
            string tbaAddress = null,
            BigInteger? tokenId = null)
        {
            if (string.IsNullOrWhiteSpace(url))
                throw new ArgumentException("url is required", nameof(url));
            if (string.IsNullOrWhiteSpace(npcPrivateKey))
                throw new ArgumentException("npcPrivateKey is required", nameof(npcPrivateKey));
            if (maxPaymentAmount <= BigInteger.Zero)
                throw new ArgumentOutOfRangeException(nameof(maxPaymentAmount), "maxPaymentAmount must be positive (smallest token units).");

            // Step 1 — Initiate the initial request and expect a 402 payment challenge
            using (var probe = UnityWebRequest.Get(url))
            {
                probe.timeout = httpTimeoutSeconds;
                await SendAndAwait(probe);

                if (probe.responseCode == 200)
                {
                    return probe.downloadHandler != null ? probe.downloadHandler.text : string.Empty;
                }

                if (probe.responseCode != 402)
                {
                    throw new InvalidOperationException(
                        $"[ArcNanopayment] Unexpected status {probe.responseCode} on probe: {probe.error}");
                }

                // Step 2 — Capture the 402 response and parse the PAYMENT-REQUIRED header encoded as Base64(JSON)
                var paymentRequiredHeader = probe.GetResponseHeader(PaymentRequiredHeader);
                if (string.IsNullOrEmpty(paymentRequiredHeader))
                    throw new InvalidOperationException(
                        "[ArcNanopayment] 402 received but PAYMENT-REQUIRED header missing.");

                var requirements = DecodePaymentRequirements(paymentRequiredHeader);
                if (verboseLogging)
                    Debug.Log($"[ArcNanopayment] Paywall challenge: {requirements.ToString(Formatting.None)}");

                // Step 3 — Perform local off-chain EIP-3009 signature generation
                var signedJson = GenerateEip3009Signature(npcPrivateKey, requirements, maxPaymentAmount);
                var paymentSignatureHeader = Convert.ToBase64String(Encoding.UTF8.GetBytes(signedJson));

                // Step 4 — Reattach the PAYMENT-SIGNATURE header and retry the HTTP request.
                // X-NPC-TBA / X-NPC-TOKEN-ID let the server route the mint to the NPC's canonical
                // ERC6551 TBA after on-chain validation, instead of dropping it on the operator
                // wallet that signed the EIP-3009 authorization.
                using (var retry = UnityWebRequest.Get(url))
                {
                    retry.timeout = httpTimeoutSeconds;
                    retry.SetRequestHeader(PaymentSignatureHeader, paymentSignatureHeader);
                    if (!string.IsNullOrWhiteSpace(tbaAddress))
                        retry.SetRequestHeader(NpcTbaHeader, tbaAddress);
                    if (tokenId.HasValue)
                        retry.SetRequestHeader(NpcTokenIdHeader, tokenId.Value.ToString());
                    await SendAndAwait(retry);

                    if (retry.responseCode != 200)
                    {
                        var body = retry.downloadHandler != null ? retry.downloadHandler.text : string.Empty;
                        throw new InvalidOperationException(
                            $"[ArcNanopayment] Retry rejected ({retry.responseCode}): {retry.error} body={body}");
                    }

                    // Step 5 — Verify the Circle Gateway batch net-settlement receipt
                    var settlementHeader = retry.GetResponseHeader(PaymentResponseHeader);
                    if (!string.IsNullOrEmpty(settlementHeader))
                    {
                        ValidateSettlement(settlementHeader);
                    }
                    else if (verboseLogging)
                    {
                        Debug.LogWarning("[ArcNanopayment] 200 OK without PAYMENT-RESPONSE header.");
                    }

                    // Step 6 — Settlement succeeded; the server may still be minting. Poll /order
                    // until the order reaches a terminal state (COMPLETED / REFUNDED / *_FAILED).
                    var initialOrderJson = retry.downloadHandler != null ? retry.downloadHandler.text : string.Empty;
                    return await ResolveOrderAsync(url, initialOrderJson);
                }
            }
        }

        // Poll /order/:orderId until the order reaches a terminal state. On success the order
        // view JSON (same shape returned inline by /item/:id) is handed back so downstream callers
        // can keep using `x402_tx`, `amount`, etc.
        private async Task<string> ResolveOrderAsync(string itemUrl, string initialOrderJson)
        {
            JObject order;
            try
            {
                order = JObject.Parse(initialOrderJson);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"[ArcNanopayment] /item response was not a valid order view: {ex.Message}; body={initialOrderJson}");
            }

            var orderId = order.Value<string>("order_id");
            if (string.IsNullOrEmpty(orderId))
                throw new InvalidOperationException(
                    $"[ArcNanopayment] /item response missing order_id; body={initialOrderJson}");

            var state = order.Value<string>("state");
            if (verboseLogging)
                Debug.Log($"[ArcNanopayment] inline order state for {orderId}: {state}");
            if (IsTerminalOrderState(state))
                return FinalizeOrder(order);

            var orderUrl = DeriveOrderUrl(itemUrl, orderId);
            for (var attempt = 1; attempt <= orderPollMaxAttempts; attempt++)
            {
                await DelaySecondsAsync(orderPollIntervalSeconds);

                using (var poll = UnityWebRequest.Get(orderUrl))
                {
                    poll.timeout = httpTimeoutSeconds;
                    await SendAndAwait(poll);

                    if (poll.responseCode != 200)
                    {
                        var body = poll.downloadHandler != null ? poll.downloadHandler.text : string.Empty;
                        throw new InvalidOperationException(
                            $"[ArcNanopayment] /order poll failed ({poll.responseCode}) for {orderId}: {poll.error} body={body}");
                    }

                    var json = poll.downloadHandler != null ? poll.downloadHandler.text : string.Empty;
                    JObject polled;
                    try
                    {
                        polled = JObject.Parse(json);
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidOperationException(
                            $"[ArcNanopayment] /order returned non-JSON for {orderId}: {ex.Message}; body={json}");
                    }

                    var polledState = polled.Value<string>("state");
                    if (verboseLogging)
                        Debug.Log($"[ArcNanopayment] poll #{attempt} order={orderId} state={polledState}");
                    if (IsTerminalOrderState(polledState))
                        return FinalizeOrder(polled);
                }
            }

            throw new TimeoutException(
                $"[ArcNanopayment] order {orderId} did not reach a terminal state after {orderPollMaxAttempts} polls; " +
                $"last state was {order.Value<string>("state")}.");
        }

        private static bool IsTerminalOrderState(string state)
        {
            switch (state)
            {
                case "COMPLETED":
                case "REFUNDED":
                case "REFUND_FAILED":
                case "FAILED":
                    return true;
                default:
                    return false;
            }
        }

        // Translate a terminal order into either the success body or a descriptive exception.
        // REFUNDED / REFUND_FAILED / FAILED all mean "no NFT was delivered"; we throw rather than
        // silently returning so the caller can mark the activity as failed.
        private static string FinalizeOrder(JObject order)
        {
            var state = order.Value<string>("state");
            if (state == "COMPLETED")
                return order.ToString(Formatting.None);

            var orderId = order.Value<string>("order_id");
            var x402Tx = order.Value<string>("x402_tx");
            var error = order.Value<string>("error");

            switch (state)
            {
                case "REFUNDED":
                    throw new InvalidOperationException(
                        $"[ArcNanopayment] mint failed and was refunded (order {orderId}, x402_tx {x402Tx}): {error}");
                case "REFUND_FAILED":
                    throw new InvalidOperationException(
                        $"[ArcNanopayment] mint failed AND refund failed (order {orderId}, x402_tx {x402Tx}) — operator intervention required: {error}");
                case "FAILED":
                    throw new InvalidOperationException(
                        $"[ArcNanopayment] order {orderId} failed before settlement: {error}");
                default:
                    throw new InvalidOperationException(
                        $"[ArcNanopayment] order {orderId} ended in unexpected terminal state {state}: {error}");
            }
        }

        // The x402 base URL points at /item/. Derive the sibling /order/<id> from the same host.
        private static string DeriveOrderUrl(string itemUrl, string orderId)
        {
            var uri = new Uri(itemUrl);
            return $"{uri.Scheme}://{uri.Authority}/order/{Uri.EscapeDataString(orderId)}";
        }

        private static Task DelaySecondsAsync(float seconds)
        {
            if (seconds <= 0f) return Task.CompletedTask;
            var ms = Math.Max(1, (int)Math.Round(seconds * 1000f));
            return Task.Delay(ms);
        }

        private string GenerateEip3009Signature(string privateKey, JObject paymentRequirements, BigInteger maxPaymentAmount)
        {
            if (paymentRequirements == null) throw new ArgumentNullException(nameof(paymentRequirements));

            // Some x402-compatible gateways expose the payment requirement under accepts[0].
            // Fallback to direct/flattened JSON payload parsing for compatibility.
            var spec = paymentRequirements["accepts"] is JArray accepts && accepts.Count > 0
                ? (JObject)accepts[0]
                : paymentRequirements;

            var extra = spec["extra"] as JObject ?? new JObject();

            var verifyingContract = FirstNonEmpty(
                spec.Value<string>("verifyingContract"),
                extra.Value<string>("verifyingContract"),
                spec.Value<string>("asset"));
            if (string.IsNullOrEmpty(verifyingContract))
                throw new InvalidOperationException("verifyingContract missing in payment requirements.");

            var payTo = spec.Value<string>("payTo");
            if (string.IsNullOrEmpty(payTo))
                throw new InvalidOperationException("payTo missing in payment requirements.");
            
            // Server-priced (x402 standard): the server tells us the exact amount, we sign for that.
            // The NPC-supplied paymentAmount acts as an upper bound — refuse to overpay past it.
            var requiredString = FirstNonEmpty(
                spec.Value<string>("amount"),
                spec.Value<string>("maxAmountRequired"),
                spec.Value<string>("value"));
            if (string.IsNullOrEmpty(requiredString)
                || !BigInteger.TryParse(requiredString, NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out var required))
            {
                throw new InvalidOperationException(
                    $"Server did not specify a payment amount in PAYMENT-REQUIRED ('{requiredString}').");
            }
            
            // maxPaymentAmount is the NPC's upper bound (smallest token units).
            // The server determines the actual price via the 402 PAYMENT-REQUIRED header;
            // we refuse to sign if it exceeds this cap.
            if (required > maxPaymentAmount)
            {
                throw new InvalidOperationException(
                    $"Server-required {required} exceeds NPC max {maxPaymentAmount}; refusing to sign.");
            }

            var value = required;
            LastPaidAmountSmallestUnits = value;

            var chainId = spec.Value<long?>("chainId")
                          ?? extra.Value<long?>("chainId")
                          ?? fallbackChainId;

            var key = new EthECKey(privateKey);
            var from = key.GetPublicAddress();

            var nowSeconds = (long)Math.Floor(
                (DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds);
            var validAfter = BigInteger.Zero;
            var validBefore = new BigInteger(nowSeconds + authorizationTtlSeconds);

            // EIP-3009 requires the nonce to be a 32-byte cryptographically secure random value
            // to prevent replay attacks
            var nonce = new byte[32];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(nonce);

            var message = new TransferWithAuthorizationMessage
            {
                From = from,
                To = payTo,
                Value = value,
                ValidAfter = validAfter,
                ValidBefore = validBefore,
                Nonce = nonce
            };

            var typedData = new TypedData<Domain>
            {
                Domain = new Domain
                {
                    Name = extra.Value<string>("name") ?? DomainName,
                    Version = extra.Value<string>("version") ?? DomainVersion,
                    ChainId = new BigInteger(chainId),
                    VerifyingContract = verifyingContract
                },
                Types = MemberDescriptionFactory.GetTypesMemberDescription(
                    typeof(Domain),
                    typeof(TransferWithAuthorizationMessage)),
                PrimaryType = PrimaryType
            };

            var signature = new Eip712TypedDataSigner().SignTypedDataV4(message, typedData, key);

            // Circle x402 payload
            var payload = new JObject
            {
                ["x402Version"] = 2,
                ["scheme"] = spec.Value<string>("scheme") ?? "exact",
                ["network"] = spec.Value<string>("network") ?? "arc-testnet",
                ["payload"] = new JObject
                {
                    ["signature"] = signature,
                    ["authorization"] = new JObject
                    {
                        ["from"] = from,
                        ["to"] = payTo,
                        ["value"] = value.ToString(CultureInfo.InvariantCulture),
                        ["validAfter"] = validAfter.ToString(CultureInfo.InvariantCulture),
                        ["validBefore"] = validBefore.ToString(CultureInfo.InvariantCulture),
                        ["nonce"] = "0x" + nonce.ToHex()
                    }
                }
            };

            return payload.ToString(Formatting.None);
        }

        private static JObject DecodePaymentRequirements(string headerValue)
        {
            string json;
            try
            {
                json = Encoding.UTF8.GetString(Convert.FromBase64String(headerValue));
            }
            catch (FormatException)
            {
                json = headerValue; // Fallback handling: some gateways may return raw JSON directly
            }

            return JObject.Parse(json);
        }

        private static void ValidateSettlement(string headerValue)
        {
            string json;
            try
            {
                json = Encoding.UTF8.GetString(Convert.FromBase64String(headerValue));
            }
            catch (FormatException)
            {
                json = headerValue;
            }

            JObject settlement;
            try
            {
                settlement = JObject.Parse(json);
            }
            catch (JsonReaderException)
            {
                return;
            }

            var success = settlement.Value<bool?>("success")
                          ?? settlement.Value<bool?>("settled")
                          ?? true;
            if (!success)
            {
                throw new InvalidOperationException($"Circle settlement rejected: {json}");
            }
        }

        private static string FirstNonEmpty(params string[] candidates)
        {
            for (var i = 0; i < candidates.Length; i++)
            {
                if (!string.IsNullOrEmpty(candidates[i])) return candidates[i];
            }

            return null;
        }

        private static Task SendAndAwait(UnityWebRequest request)
        {
            var tcs = new TaskCompletionSource<bool>();
            var op = request.SendWebRequest();
            if (op.isDone) tcs.TrySetResult(true);
            else op.completed += _ => tcs.TrySetResult(true);
            return tcs.Task;
        }
    }
}