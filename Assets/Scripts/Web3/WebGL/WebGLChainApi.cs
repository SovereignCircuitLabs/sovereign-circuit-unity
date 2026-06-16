using System;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

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

        // /tba/:address/items[/balances] uses an inner items object:
        //   { tba, items: await gamePaymentService.getTbaItemBalances(tba) }
        [Serializable] public class TbaItemsResponse
        {
            public string tba;
            public TbaItems items;
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

        [Serializable] public class NpcOwnerResponse { public string tokenId; public string owner; }
        [Serializable] public class NpcExistsResponse { public string tokenId; public bool exists; }
        [Serializable] public class NpcBalanceResponse { public string owner; public string balance; }
        [Serializable] public class NpcNextTokenIdResponse { public string nextTokenId; }

        [Serializable] public class GatewayBalancesResponse
        {
            public string token;
            public string depositor;
            public string total;
            public string available;
            public string withdrawing;
            public string withdrawable;
        }

        [Serializable] public class GatewayDelayResponse { public string withdrawalDelay; }
        [Serializable] public class GatewayBlockResponse
        {
            public string token;
            public string depositor;
            public string withdrawalBlock;
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
            var dto = await ArcTradingApiClient.GetJsonAsync<GameItemPricesResponse>("/game/items/prices", ct).ConfigureAwait(true);
            return dto?.items ?? Array.Empty<ItemPriceEntry>();
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
            var entries = await GetAllItemPricesAsync(ct).ConfigureAwait(true);
            var arr = new BigInteger[entries.Length];
            for (int i = 0; i < entries.Length; i++) arr[i] = ParseBig(entries[i].sellPrice);
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
            if (string.IsNullOrWhiteSpace(tba)) return new TbaItems { ids = Array.Empty<string>(), balances = Array.Empty<string>() };
            var dto = await ArcTradingApiClient.GetJsonAsync<TbaItemsResponse>($"/tba/{tba}/items/balances", ct).ConfigureAwait(true);
            return dto?.items ?? new TbaItems { ids = Array.Empty<string>(), balances = Array.Empty<string>() };
        }

        public static async Task<TbaItems> GetTbaOwnedItemsAsync(string tba, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(tba)) return new TbaItems { ids = Array.Empty<string>(), balances = Array.Empty<string>() };
            var dto = await ArcTradingApiClient.GetJsonAsync<TbaItemsResponse>($"/tba/{tba}/items", ct).ConfigureAwait(true);
            return dto?.items ?? new TbaItems { ids = Array.Empty<string>(), balances = Array.Empty<string>() };
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
            return await ArcTradingApiClient.GetJsonAsync<GatewayBalancesResponse>(
                $"/gateway/{token}/{depositor}/balances", ct).ConfigureAwait(true);
        }

        public static async Task<BigInteger> GetGatewayTotalBalanceAsync(string token, string depositor, CancellationToken ct = default)
            => ParseBig((await GetGatewayBalancesAsync(token, depositor, ct).ConfigureAwait(true))?.total);

        public static async Task<BigInteger> GetGatewayAvailableBalanceAsync(string token, string depositor, CancellationToken ct = default)
            => ParseBig((await GetGatewayBalancesAsync(token, depositor, ct).ConfigureAwait(true))?.available);

        public static async Task<BigInteger> GetGatewayWithdrawingBalanceAsync(string token, string depositor, CancellationToken ct = default)
            => ParseBig((await GetGatewayBalancesAsync(token, depositor, ct).ConfigureAwait(true))?.withdrawing);

        public static async Task<BigInteger> GetGatewayWithdrawableBalanceAsync(string token, string depositor, CancellationToken ct = default)
            => ParseBig((await GetGatewayBalancesAsync(token, depositor, ct).ConfigureAwait(true))?.withdrawable);

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
