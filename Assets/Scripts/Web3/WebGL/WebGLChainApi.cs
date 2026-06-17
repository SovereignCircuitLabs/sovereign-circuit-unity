using System;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace ArcTrading.WebGL
{
    /// <summary>
    /// Typed read-only entry points for the TypeScript server's GET routes
    /// (see <c>contractRoutes.ts</c>). Every method returns native .NET types
    /// (string, BigInteger, decimal, plain DTOs) so callsites that already
    /// expect those shapes can swap implementations behind <c>#if UNITY_WEBGL</c>
    /// without any Gameplay-layer changes.
    ///
    /// Phase 1 surface only - writes / signing / x402 / Gateway tx flows live
    /// in <c>WebGLWalletApi</c> and are intentionally not wired yet.
    ///
    /// Bigint convention: the server runs every response through
    /// <c>bigintToStringDeep</c>, so every numeric field that could exceed
    /// IEEE-754 safe range arrives as a string. Parse with
    /// <see cref="BigInteger.Parse(string)"/>. We keep them as string in DTOs
    /// because <see cref="UnityEngine.JsonUtility"/> can't deserialize BigInteger.
    /// </summary>
    public static class WebGLChainApi
    {
        // --------------------- Response DTOs ---------------------
        // Each DTO mirrors the server's sendJson(...) shape verbatim.
        // Field names MUST match the JSON keys; JsonUtility is case-sensitive
        // and silently leaves unknown fields null.

        [Serializable] public class BalanceResponse { public string owner; public string balance; }
        [Serializable] public class AllowanceResponse { public string owner; public string spender; public string allowance; }

        [Serializable] public class GameItemIdsResponse { public string[] ids; }

        // Response from /game/items/prices. Server: { items: await gamePaymentService.getPrices() }
        // getPrices() is expected to return [{ id, buyPrice, sellPrice, circulatingSupply? }, ...].
        [Serializable] public class GameItemPricesResponse { public ItemPriceEntry[] items; }
        [Serializable] public class ItemPriceEntry
        {
            public string id;
            public string buyPrice;
            public string sellPrice;
            public string circulatingSupply;
        }

        [Serializable] public class GameItemPriceResponse
        {
            public string id;
            public string buyPrice;
            public string sellPrice;
            public string circulatingSupply;
        }

        // Response from /game/config. Field set is whatever
        // gamePaymentService.constants() returns. We name the fields we
        // actually consume; extras are ignored.
        [Serializable] public class GameConfigResponse
        {
            public string baselinePrice;
            public string BASELINE_PRICE; // tolerate either casing - server may not have settled
            public string priceSlope;
            public string PRICE_SLOPE;
            public string sellSpreadBps;
            public string SELL_SPREAD_BPS;
            public string bpsDenominator;
            public string BPS_DENOMINATOR;
            public string numTypes;
            public string NUM_TYPES;
            public string itemsAddress;
            public string items;
            public string gatewayAddress;
            public string gateway;
            public string usdcAddress;
            public string usdc;
        }

        [Serializable] public class GamePaymentGatewayResponse
        {
            public string contractBalance;
            public string availableBalance;
            public string withdrawableBalance;
            public string withdrawingBalance;
            public string totalBalance;
            public string withdrawalBlock;
            public string withdrawalDelay;
            public bool tokenSupported;
            public bool authorized;
        }

        [Serializable] public class NpcTbaResponse { public string tokenId; public string tba; }

        // /npc/:tokenId/items and /npc/:tokenId/items/balances use spread:
        //   { tokenId, ...await gamePaymentService.getNpcTbaItemBalances(tokenId) }
        // Service likely returns { tba, ids, balances } based on the Solidity tuple naming.
        [Serializable] public class NpcTbaItemsResponse
        {
            public string tokenId;
            public string tba;
            public string[] ids;
            public string[] balances;
        }

        // /tba/:address/items[/balances] returns an array of per-item entries:
        //   { tba, items: [ {id, balance}, {id, balance}, ... ] }
        // The earlier "{ tba, items: { ids:[...], balances:[...] } }" comment
        // was wrong (presumably from before the server flattened the viem-
        // decoded tuple into a list of objects). We keep TbaItems around for
        // downstream callers that expect parallel string[] ids / balances —
        // GetTbaItemBalancesAsync reshapes the entry array into it.
        [Serializable] public class TbaItemsResponse
        {
            public string tba;
            public TbaItemEntry[] items;
        }
        [Serializable] public class TbaItemEntry
        {
            public string id;
            public string balance;
        }
        [Serializable] public class TbaItems
        {
            public string[] ids;
            public string[] balances;
        }

        [Serializable] public class Erc1155BalanceResponse
        {
            public string token;
            public string account;
            public string id;
            public string balance;
        }

        [Serializable] public class PaymentBindingResponse
        {
            public string tokenId;
            public string wallet;
            public string version;
        }

        // /npc-character/:tokenId -> { tokenId, npc: NpcData }
        // viem returns uint8/uint16/uint32 as JS numbers and uint64 as bigints;
        // bigintToStringDeep then turns the bigints into JSON strings. So small
        // ints stay as numbers (JsonUtility maps to byte/ushort/uint just fine)
        // and uint64 fields arrive as strings — declared as string + parsed.
        [Serializable] public class NpcPortfolioData
        {
            public ushort livingNeedsWeightBps;
            public ushort reserveWeightBps;
            public ushort tradingWeightBps;
            public string minimumLivingBudgetUSDC;
            public string minimumReserveBudgetUSDC;
            public uint rebalanceIntervalSeconds;
            public uint chainActionCooldownSeconds;
            public string minTradeUSDC;
            public string maxTradeUSDC;
        }
        [Serializable] public class NpcData
        {
            public string npcName;
            public string metadataURI;
            public byte archetype;
            public byte riskLevel;
            public ushort level;
            public uint reputation;
            public NpcPortfolioData portfolio;
        }
        [Serializable] public class NpcGetResponse
        {
            public string tokenId;
            public NpcData npc;
        }

        [Serializable] public class NpcOwnerResponse { public string tokenId; public string owner; }
        [Serializable] public class NpcExistsResponse { public string tokenId; public bool exists; }
        [Serializable] public class NpcBalanceResponse { public string owner; public string balance; }
        [Serializable] public class NpcNextTokenIdResponse { public string nextTokenId; }

        // Server is inconsistent about field names — /game-payment-gateway/...
        // uses the *Balance suffix (see GamePaymentGatewayResponse) while early
        // /gateway/{token}/{depositor}/balances builds used the short names.
        // Declare both so JsonUtility picks up whichever shows up; accessors
        // below coalesce.
        [Serializable] public class GatewayBalancesResponse
        {
            public string token;
            public string depositor;
            public string total;
            public string totalBalance;
            public string available;
            public string availableBalance;
            public string withdrawing;
            public string withdrawingBalance;
            public string withdrawable;
            public string withdrawableBalance;
        }

        [Serializable] public class GatewayDelayResponse { public string withdrawalDelay; }
        [Serializable] public class GatewayBlockResponse
        {
            public string token;
            public string depositor;
            public string withdrawalBlock;
        }

        // /npc-marketplace/listing/:tokenId -> { tokenId, listing: { seller, minPrice, active } }
        [Serializable] public class MarketplaceListing
        {
            public string seller;
            public string minPrice;
            public bool active;
        }
        [Serializable] public class MarketplaceListingResponse
        {
            public string tokenId;
            public MarketplaceListing listing;
        }

        // /npc-pricing/:tokenId/quote -> spread: { tokenId, price, tbaTotalValue, scarcityMultiplierBps }
        [Serializable] public class PricingQuoteResponse
        {
            public string tokenId;
            public string price;
            public string tbaTotalValue;
            public string scarcityMultiplierBps;
        }

        [Serializable] public class PricingClassIdResponse { public string tokenId; public string classId; }
        [Serializable] public class PricingTbaValueResponse { public string tokenId; public string tbaTotalValue; }

        // /npc-pricing/:tokenId/tba-value-breakdown -> spread: { tokenId, tba, itemValue, cashValue, tbaTotalValue }
        [Serializable] public class PricingTbaBreakdownResponse
        {
            public string tokenId;
            public string tba;
            public string itemValue;
            public string cashValue;
            public string tbaTotalValue;
        }

        [Serializable] public class PricingScarcityResponse { public string classId; public string scarcityMultiplierBps; }

        // /npc-pricing/class/:classId/market -> { classId, market: { ... exists } }
        [Serializable] public class PricingClassMarket
        {
            public string totalSupply;
            public string listedSupply;
            public string virtualLiquidity;
            public string basePrice;
            public string maxMultiplierBps;
            public string scarcityWeightBps;
            public bool exists;
        }
        [Serializable] public class PricingClassMarketResponse
        {
            public string classId;
            public PricingClassMarket market;
        }

        // /npc-character/:owner/approval-for-all/:operator -> { owner, operator, approved }
        [Serializable] public class NpcCharacterApprovalForAllResponse
        {
            public string owner;
            // "operator" is a C# keyword — JsonUtility accepts the property name verbatim, so we
            // expose it via [SerializeField] is not necessary; field name must literally be "operator".
            // We use @operator to escape the keyword; JsonUtility maps the field by its actual name "operator".
            public string @operator;
            public bool approved;
        }
        [Serializable] public class NpcCharacterApprovedResponse
        {
            public string tokenId;
            public string approved; // address of approved operator (or zero address)
        }

        // --------------------- Tx receipts ---------------------

        // Mirrors /tx/receipt/:hash. status is one of: "success", "reverted", "pending".
        // blockNumber / gasUsed are null while pending — declared as string so JsonUtility
        // tolerates absence; consumers use ParseBig which returns 0 for null.
        [Serializable] public class TxReceiptResponse
        {
            public string txHash;
            public string status;
            public string blockNumber;
            public string gasUsed;
        }

        public enum TxReceiptStatus { Pending, Success, Reverted, Unknown }

        public static async Task<(TxReceiptStatus status, BigInteger blockNumber)> GetTxReceiptAsync(
            string txHash, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(txHash))
                return (TxReceiptStatus.Unknown, BigInteger.Zero);
            var dto = await ArcTradingApiClient.GetJsonAsync<TxReceiptResponse>(
                $"/tx/receipt/{txHash}", ct).ConfigureAwait(true);
            if (dto == null) return (TxReceiptStatus.Unknown, BigInteger.Zero);
            var status = dto.status switch
            {
                "success" => TxReceiptStatus.Success,
                "reverted" => TxReceiptStatus.Reverted,
                "pending" => TxReceiptStatus.Pending,
                _ => TxReceiptStatus.Unknown,
            };
            return (status, ParseBig(dto.blockNumber));
        }

        // --------------------- USDC ---------------------

        public static async Task<BigInteger> GetUsdcBalanceAsync(string owner, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(owner)) return BigInteger.Zero;
            var dto = await ArcTradingApiClient.GetJsonAsync<BalanceResponse>($"/usdc/{owner}/balance", ct).ConfigureAwait(true);
            return ParseBig(dto?.balance);
        }

        public static async Task<BigInteger> GetUsdcAllowanceAsync(string owner, string spender, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(spender)) return BigInteger.Zero;
            var dto = await ArcTradingApiClient.GetJsonAsync<AllowanceResponse>($"/usdc/{owner}/allowance/{spender}", ct).ConfigureAwait(true);
            return ParseBig(dto?.allowance);
        }

        // --------------------- Game items / prices ---------------------

        public static async Task<BigInteger[]> GetItemIdsAsync(CancellationToken ct = default)
        {
            var dto = await ArcTradingApiClient.GetJsonAsync<GameItemIdsResponse>("/game/items/ids", ct).ConfigureAwait(true);
            return ParseBigArray(dto?.ids);
        }

        public static async Task<ItemPriceEntry[]> GetAllItemPricesAsync(CancellationToken ct = default)
        {
            Debug.Log("[WebGLChainApi.GetAllItemPricesAsync] GET /game/items/prices");
            var dto = await ArcTradingApiClient.GetJsonAsync<GameItemPricesResponse>("/game/items/prices", ct).ConfigureAwait(true);
            if (dto == null)
            {
                Debug.LogWarning("[WebGLChainApi.GetAllItemPricesAsync] dto = null (empty/invalid body)");
                return Array.Empty<ItemPriceEntry>();
            }
            if (dto.items == null)
            {
                Debug.LogWarning("[WebGLChainApi.GetAllItemPricesAsync] dto.items = null — server JSON shape mismatch with GameItemPricesResponse?");
                return Array.Empty<ItemPriceEntry>();
            }
            Debug.Log($"[WebGLChainApi.GetAllItemPricesAsync] items.Length={dto.items.Length}");
            for (int i = 0; i < dto.items.Length; i++)
            {
                var e = dto.items[i];
                Debug.Log($"  [{i}] id={e?.id} buyPrice={e?.buyPrice} sellPrice={e?.sellPrice} circulating={e?.circulatingSupply}");
            }
            return dto.items;
        }

        public static async Task<BigInteger[]> GetAllBuyPricesRawAsync(CancellationToken ct = default)
        {
            var entries = await GetAllItemPricesAsync(ct).ConfigureAwait(true);
            var arr = new BigInteger[entries.Length];
            for (int i = 0; i < entries.Length; i++) arr[i] = ParseBig(entries[i].buyPrice);
            return arr;
        }

        public static async Task<BigInteger[]> GetAllSellPricesRawAsync(CancellationToken ct = default)
        {
            Debug.Log("[WebGLChainApi.GetAllSellPricesRawAsync] start");
            var entries = await GetAllItemPricesAsync(ct).ConfigureAwait(true);
            var arr = new BigInteger[entries.Length];
            for (int i = 0; i < entries.Length; i++) arr[i] = ParseBig(entries[i].sellPrice);
            Debug.Log($"[WebGLChainApi.GetAllSellPricesRawAsync] parsed {arr.Length} sell prices: [{string.Join(",", arr)}]");
            return arr;
        }

        public static async Task<BigInteger> GetBuyPriceAsync(BigInteger itemId, CancellationToken ct = default)
        {
            var dto = await ArcTradingApiClient.GetJsonAsync<GameItemPriceResponse>($"/game/item/{itemId}/price", ct).ConfigureAwait(true);
            return ParseBig(dto?.buyPrice);
        }

        public static async Task<BigInteger> GetSellPriceAsync(BigInteger itemId, CancellationToken ct = default)
        {
            var dto = await ArcTradingApiClient.GetJsonAsync<GameItemPriceResponse>($"/game/item/{itemId}/price", ct).ConfigureAwait(true);
            return ParseBig(dto?.sellPrice);
        }

        public static async Task<BigInteger> GetCirculatingSupplyAsync(BigInteger itemId, CancellationToken ct = default)
        {
            var dto = await ArcTradingApiClient.GetJsonAsync<GameItemPriceResponse>($"/game/item/{itemId}/price", ct).ConfigureAwait(true);
            return ParseBig(dto?.circulatingSupply);
        }

        public static async Task<BigInteger> GetBaselinePriceAsync(CancellationToken ct = default)
        {
            var dto = await ArcTradingApiClient.GetJsonAsync<GameConfigResponse>("/game/config", ct).ConfigureAwait(true);
            if (dto == null) return BigInteger.Zero;
            return ParseBig(dto.baselinePrice ?? dto.BASELINE_PRICE);
        }

        public static async Task<string> GetItemsAddressAsync(CancellationToken ct = default)
        {
            var dto = await ArcTradingApiClient.GetJsonAsync<GameConfigResponse>("/game/config", ct).ConfigureAwait(true);
            return dto?.itemsAddress ?? dto?.items;
        }

        // --------------------- ERC1155 (single id read) ---------------------

        public static async Task<BigInteger> GetErc1155BalanceAsync(
            string tokenContract, string account, BigInteger id, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(tokenContract) || string.IsNullOrWhiteSpace(account)) return BigInteger.Zero;
            var dto = await ArcTradingApiClient.GetJsonAsync<Erc1155BalanceResponse>(
                $"/erc1155/{tokenContract}/balance/{account}/{id}", ct).ConfigureAwait(true);
            return ParseBig(dto?.balance);
        }

        [Serializable] public class Erc1155ApprovalResponse
        {
            public string token; public string account; public string @operator;
            public bool approved;
        }

        public static async Task<bool> GetErc1155IsApprovedForAllAsync(
            string tokenContract, string account, string operatorAddr, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(tokenContract) || string.IsNullOrWhiteSpace(account) || string.IsNullOrWhiteSpace(operatorAddr))
                return false;
            var dto = await ArcTradingApiClient.GetJsonAsync<Erc1155ApprovalResponse>(
                $"/erc1155/{tokenContract}/approval/{account}/{operatorAddr}", ct).ConfigureAwait(true);
            return dto != null && dto.approved;
        }

        // --------------------- TBA inventory ---------------------

        public static async Task<TbaItems> GetTbaItemBalancesAsync(string tba, CancellationToken ct = default)
        {
            Debug.Log($"[WebGLChainApi.GetTbaItemBalancesAsync] tba={tba ?? "<null>"}");
            if (string.IsNullOrWhiteSpace(tba))
            {
                Debug.LogWarning("[WebGLChainApi.GetTbaItemBalancesAsync] tba is null/empty -> returning empty TbaItems");
                return new TbaItems { ids = Array.Empty<string>(), balances = Array.Empty<string>() };
            }

            var dto = await ArcTradingApiClient.GetJsonAsync<TbaItemsResponse>(
                $"/tba/{tba}/items/balances", ct).ConfigureAwait(true);
            return ReshapeTbaItems(tba, "balances", dto);
        }

        public static async Task<TbaItems> GetTbaOwnedItemsAsync(string tba, CancellationToken ct = default)
        {
            Debug.Log($"[WebGLChainApi.GetTbaOwnedItemsAsync] tba={tba ?? "<null>"}");
            if (string.IsNullOrWhiteSpace(tba)) return new TbaItems { ids = Array.Empty<string>(), balances = Array.Empty<string>() };
            var dto = await ArcTradingApiClient.GetJsonAsync<TbaItemsResponse>(
                $"/tba/{tba}/items", ct).ConfigureAwait(true);
            return ReshapeTbaItems(tba, "items", dto);
        }

        // Server sends both /tba/{tba}/items and /tba/{tba}/items/balances as
        //   { tba, items: [ {id, balance}, ... ] }
        // Downstream callers expect parallel string[] ids / balances arrays, so
        // we walk the entry list once and split it into those two arrays.
        private static TbaItems ReshapeTbaItems(string tba, string endpoint, TbaItemsResponse dto)
        {
            if (dto == null)
            {
                Debug.LogWarning($"[WebGLChainApi.GetTba{endpoint}Async] dto=null for tba={tba} (empty/invalid response)");
                return new TbaItems { ids = Array.Empty<string>(), balances = Array.Empty<string>() };
            }
            if (dto.items == null)
            {
                Debug.LogWarning($"[WebGLChainApi.GetTba{endpoint}Async] dto.items=null for tba={tba}");
                return new TbaItems { ids = Array.Empty<string>(), balances = Array.Empty<string>() };
            }

            var n = dto.items.Length;
            var ids = new string[n];
            var balances = new string[n];
            for (int i = 0; i < n; i++)
            {
                ids[i] = dto.items[i]?.id ?? "0";
                balances[i] = dto.items[i]?.balance ?? "0";
            }
            Debug.Log(
                $"[WebGLChainApi.GetTba{endpoint}Async] tba={tba} entries={n} " +
                $"ids=[{string.Join(",", ids)}] balances=[{string.Join(",", balances)}]");
            return new TbaItems { ids = ids, balances = balances };
        }

        public static async Task<NpcTbaItemsResponse> GetNpcTbaItemBalancesAsync(BigInteger tokenId, CancellationToken ct = default)
        {
            return await ArcTradingApiClient.GetJsonAsync<NpcTbaItemsResponse>($"/npc/{tokenId}/items/balances", ct).ConfigureAwait(true);
        }

        public static async Task<NpcTbaItemsResponse> GetNpcTbaOwnedItemsAsync(BigInteger tokenId, CancellationToken ct = default)
        {
            return await ArcTradingApiClient.GetJsonAsync<NpcTbaItemsResponse>($"/npc/{tokenId}/items", ct).ConfigureAwait(true);
        }

        // --------------------- NPC TBA address ---------------------

        public static async Task<string> GetNpcTbaAsync(BigInteger tokenId, CancellationToken ct = default)
        {
            var dto = await ArcTradingApiClient.GetJsonAsync<NpcTbaResponse>($"/npc/{tokenId}/tba", ct).ConfigureAwait(true);
            return dto?.tba;
        }

        // --------------------- NPC payment binding ---------------------

        public static async Task<(string wallet, ulong version)> GetPaymentBindingAsync(BigInteger tokenId, CancellationToken ct = default)
        {
            var dto = await ArcTradingApiClient.GetJsonAsync<PaymentBindingResponse>($"/npc-character/{tokenId}/payment-binding", ct).ConfigureAwait(true);
            if (dto == null) return (null, 0);
            ulong version = 0;
            if (!string.IsNullOrEmpty(dto.version)) ulong.TryParse(dto.version, out version);
            return (dto.wallet, version);
        }

        public static async Task<NpcData> GetNpcAsync(BigInteger tokenId, CancellationToken ct = default)
        {
            var dto = await ArcTradingApiClient.GetJsonAsync<NpcGetResponse>($"/npc-character/{tokenId}", ct).ConfigureAwait(true);
            return dto?.npc;
        }

        public static async Task<string> NpcOwnerOfAsync(BigInteger tokenId, CancellationToken ct = default)
        {
            var dto = await ArcTradingApiClient.GetJsonAsync<NpcOwnerResponse>($"/npc-character/{tokenId}/owner", ct).ConfigureAwait(true);
            return dto?.owner;
        }

        public static async Task<bool> NpcExistsAsync(BigInteger tokenId, CancellationToken ct = default)
        {
            var dto = await ArcTradingApiClient.GetJsonAsync<NpcExistsResponse>($"/npc-character/{tokenId}/exists", ct).ConfigureAwait(true);
            return dto != null && dto.exists;
        }

        public static async Task<BigInteger> NpcBalanceOfAsync(string owner, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(owner)) return BigInteger.Zero;
            var dto = await ArcTradingApiClient.GetJsonAsync<NpcBalanceResponse>($"/npc-character/{owner}/balance", ct).ConfigureAwait(true);
            return ParseBig(dto?.balance);
        }

        public static async Task<BigInteger> NpcNextTokenIdAsync(CancellationToken ct = default)
        {
            var dto = await ArcTradingApiClient.GetJsonAsync<NpcNextTokenIdResponse>("/npc-character/next-token-id", ct).ConfigureAwait(true);
            return ParseBig(dto?.nextTokenId);
        }

        // --------------------- Gateway (Circle) balances ---------------------

        public static async Task<GatewayBalancesResponse> GetGatewayBalancesAsync(
            string token, string depositor, CancellationToken ct = default)
        {
            var path = $"/gateway/{token}/{depositor}/balances";
            Debug.Log($"[WebGLChainApi.GetGatewayBalancesAsync] GET {path}");
            var raw = await ArcTradingApiClient.GetAsync(path, ct).ConfigureAwait(true);
            Debug.Log($"[WebGLChainApi.GetGatewayBalancesAsync] raw json:\n{raw}");
            if (string.IsNullOrEmpty(raw))
            {
                Debug.LogWarning($"[WebGLChainApi.GetGatewayBalancesAsync] empty body for token={token} depositor={depositor}");
                return null;
            }
            var dto = JsonUtility.FromJson<GatewayBalancesResponse>(raw);
            if (dto == null)
            {
                Debug.LogWarning($"[WebGLChainApi.GetGatewayBalancesAsync] dto=null for token={token} depositor={depositor}");
                return null;
            }
            Debug.Log(
                $"[WebGLChainApi.GetGatewayBalancesAsync] token={dto.token} depositor={dto.depositor} " +
                $"total={dto.total ?? dto.totalBalance} available={dto.available ?? dto.availableBalance} " +
                $"withdrawing={dto.withdrawing ?? dto.withdrawingBalance} withdrawable={dto.withdrawable ?? dto.withdrawableBalance}");
            return dto;
        }

        public static async Task<BigInteger> GetGatewayTotalBalanceAsync(string token, string depositor, CancellationToken ct = default)
        {
            var dto = await GetGatewayBalancesAsync(token, depositor, ct).ConfigureAwait(true);
            var raw = dto?.total ?? dto?.totalBalance;
            var parsed = ParseBig(raw);
            Debug.Log($"[WebGLChainApi.GetGatewayTotalBalanceAsync] raw=\"{raw ?? "<null>"}\" parsed={parsed}");
            return parsed;
        }

        public static async Task<BigInteger> GetGatewayAvailableBalanceAsync(string token, string depositor, CancellationToken ct = default)
        {
            var dto = await GetGatewayBalancesAsync(token, depositor, ct).ConfigureAwait(true);
            var raw = dto?.available ?? dto?.availableBalance;
            var parsed = ParseBig(raw);
            Debug.Log($"[WebGLChainApi.GetGatewayAvailableBalanceAsync] token={token} depositor={depositor} raw=\"{raw ?? "<null>"}\" parsed={parsed}");
            return parsed;
        }

        public static async Task<BigInteger> GetGatewayWithdrawingBalanceAsync(string token, string depositor, CancellationToken ct = default)
        {
            var dto = await GetGatewayBalancesAsync(token, depositor, ct).ConfigureAwait(true);
            return ParseBig(dto?.withdrawing ?? dto?.withdrawingBalance);
        }

        public static async Task<BigInteger> GetGatewayWithdrawableBalanceAsync(string token, string depositor, CancellationToken ct = default)
        {
            var dto = await GetGatewayBalancesAsync(token, depositor, ct).ConfigureAwait(true);
            return ParseBig(dto?.withdrawable ?? dto?.withdrawableBalance);
        }

        public static async Task<BigInteger> GetGatewayWithdrawalDelayAsync(CancellationToken ct = default)
        {
            var dto = await ArcTradingApiClient.GetJsonAsync<GatewayDelayResponse>("/gateway/withdrawal-delay", ct).ConfigureAwait(true);
            return ParseBig(dto?.withdrawalDelay);
        }

        public static async Task<BigInteger> GetGatewayWithdrawalBlockAsync(
            string token, string depositor, CancellationToken ct = default)
        {
            var dto = await ArcTradingApiClient.GetJsonAsync<GatewayBlockResponse>(
                $"/gateway/{token}/{depositor}/withdrawal-block", ct).ConfigureAwait(true);
            return ParseBig(dto?.withdrawalBlock);
        }

        // --------------------- NPC marketplace ---------------------

        public static async Task<MarketplaceListing> GetMarketplaceListingAsync(BigInteger tokenId, CancellationToken ct = default)
        {
            var dto = await ArcTradingApiClient.GetJsonAsync<MarketplaceListingResponse>(
                $"/npc-marketplace/listing/{tokenId}", ct).ConfigureAwait(true);
            return dto?.listing;
        } 

        // --------------------- NPC pricing ---------------------

        public static async Task<PricingQuoteResponse> QuoteNpcPriceAsync(BigInteger tokenId, CancellationToken ct = default)
        {
            return await ArcTradingApiClient.GetJsonAsync<PricingQuoteResponse>(
                $"/npc-pricing/{tokenId}/quote", ct).ConfigureAwait(true);
        }

        public static async Task<BigInteger> GetNpcClassIdAsync(BigInteger tokenId, CancellationToken ct = default)
        {
            var dto = await ArcTradingApiClient.GetJsonAsync<PricingClassIdResponse>(
                $"/npc-pricing/{tokenId}/class", ct).ConfigureAwait(true);
            return ParseBig(dto?.classId);
        }

        public static async Task<BigInteger> GetNpcTbaTotalValueAsync(BigInteger tokenId, CancellationToken ct = default)
        {
            var dto = await ArcTradingApiClient.GetJsonAsync<PricingTbaValueResponse>(
                $"/npc-pricing/{tokenId}/tba-value", ct).ConfigureAwait(true);
            return ParseBig(dto?.tbaTotalValue);
        }

        public static async Task<PricingTbaBreakdownResponse> GetNpcTbaValueBreakdownAsync(BigInteger tokenId, CancellationToken ct = default)
        {
            return await ArcTradingApiClient.GetJsonAsync<PricingTbaBreakdownResponse>(
                $"/npc-pricing/{tokenId}/tba-value-breakdown", ct).ConfigureAwait(true);
        }

        public static async Task<BigInteger> GetScarcityMultiplierBpsAsync(BigInteger classId, CancellationToken ct = default)
        {
            var dto = await ArcTradingApiClient.GetJsonAsync<PricingScarcityResponse>(
                $"/npc-pricing/class/{classId}/scarcity", ct).ConfigureAwait(true);
            return ParseBig(dto?.scarcityMultiplierBps);
        }

        public static async Task<PricingClassMarket> GetClassMarketAsync(BigInteger classId, CancellationToken ct = default)
        {
            var dto = await ArcTradingApiClient.GetJsonAsync<PricingClassMarketResponse>(
                $"/npc-pricing/class/{classId}/market", ct).ConfigureAwait(true);
            return dto?.market;
        }

        // --------------------- NPC ERC-721 marketplace approvals ---------------------

        public static async Task<bool> NpcIsApprovedForAllAsync(string owner, string operatorAddr, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(operatorAddr)) return false;
            var dto = await ArcTradingApiClient.GetJsonAsync<NpcCharacterApprovalForAllResponse>(
                $"/npc-character/{owner}/approval-for-all/{operatorAddr}", ct).ConfigureAwait(true);
            return dto != null && dto.approved;
        }

        public static async Task<string> NpcGetApprovedAsync(BigInteger tokenId, CancellationToken ct = default)
        {
            var dto = await ArcTradingApiClient.GetJsonAsync<NpcCharacterApprovedResponse>(
                $"/npc-character/{tokenId}/approved", ct).ConfigureAwait(true);
            return dto?.approved;
        }

        // --------------------- GamePayment-owned Gateway pool (admin pool) ---------------------

        public static async Task<GamePaymentGatewayResponse> GetGamePaymentGatewayAsync(
            string addr = null, CancellationToken ct = default)
        {
            var path = string.IsNullOrEmpty(addr) ? "/game/gateway" : $"/game/gateway?addr={addr}";
            return await ArcTradingApiClient.GetJsonAsync<GamePaymentGatewayResponse>(path, ct).ConfigureAwait(true);
        }

        // --------------------- internals ---------------------

        private static BigInteger ParseBig(string s)
        {
            if (string.IsNullOrEmpty(s)) return BigInteger.Zero;
            return BigInteger.Parse(s);
        }

        private static BigInteger[] ParseBigArray(string[] arr)
        {
            if (arr == null || arr.Length == 0) return Array.Empty<BigInteger>();
            var result = new BigInteger[arr.Length];
            for (int i = 0; i < arr.Length; i++) result[i] = ParseBig(arr[i]);
            return result;
        }
    }
}
