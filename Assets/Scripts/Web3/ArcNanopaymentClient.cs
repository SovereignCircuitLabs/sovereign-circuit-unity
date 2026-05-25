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
        private const string DomainName = "GatewayWalletBatched";
        private const string DomainVersion = "1";
        private const string PrimaryType = "TransferWithAuthorization";
        
        private const int authorizationTtlSeconds = 604900;

        [Header("Configuration")]
        public string x402ServerUrl = "http://localhost:4021/risk-profile";
        // Upper bound for any single x402 payment, in USDC. The server quotes the actual price
        // via the 402 PAYMENT-REQUIRED header; we refuse to sign if it exceeds this cap.
        public float maxNanopaymentUsdc = 0.5f;

        // Smallest-unit amount signed for in the most recent successful x402 call.
        // Callers can read this immediately after awaiting FetchPaywalledResourceAsync to know the exact
        // amount that will be settled out of the gateway wallet (batch settlement lags chain reads).
        public BigInteger LastPaidAmountSmallestUnits { get; private set; }
        [SerializeField] private long fallbackChainId = 5042002;
        [SerializeField] private int httpTimeoutSeconds = 20;
        [SerializeField] private bool verboseLogging = false;
        
        [ContextMenu("Fetch Paywalled Resource Test")]
        public async void FetchPaywalledResourceTest()
        {
            tradingContractClient = GetComponent<ArcTradingContractClient>();
            rpcUrl = tradingContractClient.RpcUrl;
            privateKey = tradingContractClient.PrivateKey;
            gatewayReadOnlyWeb3 = new Web3(rpcUrl);

            await ApproveIfNeededThenGatewayDepositAsync(1); // deposit 1 usdc into gateway

            var content = await FetchPaywalledResourceAsync(
                x402ServerUrl,
                tradingContractClient.PrivateKey,
                Erc20UsdcHelper.ParseUsdc(0.5m));
            Debug.Log($"Fetched content: {content}");
        }
        
        public async Task<string> FetchPaywalledResourceAsync(
            string url,
            BigInteger tokenId,
            NpcPaymentWalletService walletService,
            BigInteger maxPaymentAmount)
        {
            if (walletService == null) throw new ArgumentNullException(nameof(walletService));
            
            var signer = await walletService.VerifySignerForSigningAsync(tokenId);

            if (verboseLogging)
                Debug.Log($"[ArcNanopayment] verified signer for NPC {tokenId}: addr={signer.Address}, version={signer.Version}");

            return await FetchPaywalledResourceAsync(url, signer.PrivateKey, maxPaymentAmount);
        }

        public async Task<string> FetchPaywalledResourceAsync(string url, string npcPrivateKey, BigInteger maxPaymentAmount)
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

                // Step 4 — Reattach the PAYMENT-SIGNATURE header and retry the HTTP request
                using (var retry = UnityWebRequest.Get(url))
                {
                    retry.timeout = httpTimeoutSeconds;
                    retry.SetRequestHeader(PaymentSignatureHeader, paymentSignatureHeader);
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

                    return retry.downloadHandler != null ? retry.downloadHandler.text : string.Empty;
                }
            }
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