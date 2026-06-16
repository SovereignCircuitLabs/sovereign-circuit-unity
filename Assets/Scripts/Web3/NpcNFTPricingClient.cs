using System.Numerics;
using System.Threading.Tasks;
using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Web3;
using UnityEngine;

[FunctionOutput]
public class QuoteNpcPriceOutputDTO : IFunctionOutputDTO
{
    [Parameter("uint256", "price", 1)] public BigInteger Price { get; set; }
    [Parameter("uint256", "tbaTotalValue", 2)] public BigInteger TbaTotalValue { get; set; }
    [Parameter("uint256", "scarcityMultiplierBps", 3)] public BigInteger ScarcityMultiplierBps { get; set; }
}

[FunctionOutput]
public class NpcClassMarketOutputDTO : IFunctionOutputDTO
{
    [Parameter("uint256", "totalSupply", 1)] public BigInteger TotalSupply { get; set; }
    [Parameter("uint256", "listedSupply", 2)] public BigInteger ListedSupply { get; set; }
    [Parameter("uint256", "virtualLiquidity", 3)] public BigInteger VirtualLiquidity { get; set; }
    [Parameter("uint256", "basePrice", 4)] public BigInteger BasePrice { get; set; }
    [Parameter("uint256", "maxMultiplierBps", 5)] public BigInteger MaxMultiplierBps { get; set; }
    [Parameter("uint256", "scarcityWeightBps", 6)] public BigInteger ScarcityWeightBps { get; set; }
    [Parameter("bool", "exists", 7)] public bool Exists { get; set; }
}

[FunctionOutput]
public class NpcTbaValueBreakdownOutputDTO : IFunctionOutputDTO
{
    [Parameter("address", "tba", 1)] public string Tba { get; set; }
    [Parameter("uint256", "itemValue", 2)] public BigInteger ItemValue { get; set; }
    [Parameter("uint256", "cashValue", 3)] public BigInteger CashValue { get; set; }
    [Parameter("uint256", "tbaTotalValue", 4)] public BigInteger TbaTotalValue { get; set; }
}

/// <summary>
/// Read-only wrapper around the on-chain NpcNFTPricing contract.
/// Quote price = (basePrice + tbaTotalValue) * scarcityMultiplierBps / BPS,
/// where scarcityMultiplierBps depends on per-archetype class market state.
/// </summary>
public class NpcNFTPricingClient : MonoBehaviour
{
    [SerializeField] private string rpcUrl = "https://rpc.testnet.arc.network";
    [SerializeField] private string pricingContractAddress;

    public string PricingContractAddress => pricingContractAddress;
    public string RpcUrl => rpcUrl;

    private const string Abi = @"[
      {""inputs"":[{""internalType"":""uint256"",""name"":""tokenId"",""type"":""uint256""}],""name"":""getNpcClassId"",""outputs"":[{""internalType"":""uint256"",""name"":"""",""type"":""uint256""}],""stateMutability"":""view"",""type"":""function""},
      {""inputs"":[{""internalType"":""uint256"",""name"":""tokenId"",""type"":""uint256""}],""name"":""getNpcTbaTotalValue"",""outputs"":[{""internalType"":""uint256"",""name"":"""",""type"":""uint256""}],""stateMutability"":""view"",""type"":""function""},
      {""inputs"":[{""internalType"":""uint256"",""name"":""tokenId"",""type"":""uint256""}],""name"":""getNpcTbaValueBreakdown"",""outputs"":[{""internalType"":""address"",""name"":""tba"",""type"":""address""},{""internalType"":""uint256"",""name"":""itemValue"",""type"":""uint256""},{""internalType"":""uint256"",""name"":""cashValue"",""type"":""uint256""},{""internalType"":""uint256"",""name"":""tbaTotalValue"",""type"":""uint256""}],""stateMutability"":""view"",""type"":""function""},
      {""inputs"":[{""internalType"":""uint256"",""name"":""classId"",""type"":""uint256""}],""name"":""getScarcityMultiplierBps"",""outputs"":[{""internalType"":""uint256"",""name"":"""",""type"":""uint256""}],""stateMutability"":""view"",""type"":""function""},
      {""inputs"":[{""internalType"":""uint256"",""name"":""tokenId"",""type"":""uint256""}],""name"":""quoteNpcPrice"",""outputs"":[{""internalType"":""uint256"",""name"":""price"",""type"":""uint256""},{""internalType"":""uint256"",""name"":""tbaTotalValue"",""type"":""uint256""},{""internalType"":""uint256"",""name"":""scarcityMultiplierBps"",""type"":""uint256""}],""stateMutability"":""view"",""type"":""function""},
      {""inputs"":[{""internalType"":""uint256"",""name"":"""",""type"":""uint256""}],""name"":""classMarkets"",""outputs"":[{""internalType"":""uint256"",""name"":""totalSupply"",""type"":""uint256""},{""internalType"":""uint256"",""name"":""listedSupply"",""type"":""uint256""},{""internalType"":""uint256"",""name"":""virtualLiquidity"",""type"":""uint256""},{""internalType"":""uint256"",""name"":""basePrice"",""type"":""uint256""},{""internalType"":""uint256"",""name"":""maxMultiplierBps"",""type"":""uint256""},{""internalType"":""uint256"",""name"":""scarcityWeightBps"",""type"":""uint256""},{""internalType"":""bool"",""name"":""exists"",""type"":""bool""}],""stateMutability"":""view"",""type"":""function""},
      {""inputs"":[],""name"":""BPS"",""outputs"":[{""internalType"":""uint256"",""name"":"""",""type"":""uint256""}],""stateMutability"":""view"",""type"":""function""},
      {""inputs"":[],""name"":""npcCharacter"",""outputs"":[{""internalType"":""address"",""name"":"""",""type"":""address""}],""stateMutability"":""view"",""type"":""function""},
      {""inputs"":[],""name"":""usdc"",""outputs"":[{""internalType"":""address"",""name"":"""",""type"":""address""}],""stateMutability"":""view"",""type"":""function""},
      {""inputs"":[],""name"":""gamePayment"",""outputs"":[{""internalType"":""address"",""name"":"""",""type"":""address""}],""stateMutability"":""view"",""type"":""function""}
    ]";

    private Web3 readOnlyWeb3;

    private void Awake()
    {
        readOnlyWeb3 = new Web3(rpcUrl);
    }

    public async Task<BigInteger> GetNpcClassIdAsync(BigInteger tokenId)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        return await ArcTrading.WebGL.WebGLChainApi.GetNpcClassIdAsync(tokenId);
#else
        var contract = readOnlyWeb3.Eth.GetContract(Abi, pricingContractAddress);
        return await contract.GetFunction("getNpcClassId").CallAsync<BigInteger>(tokenId);
#endif
    }

    public async Task<BigInteger> GetNpcTbaTotalValueAsync(BigInteger tokenId)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        return await ArcTrading.WebGL.WebGLChainApi.GetNpcTbaTotalValueAsync(tokenId);
#else
        var contract = readOnlyWeb3.Eth.GetContract(Abi, pricingContractAddress);
        return await contract.GetFunction("getNpcTbaTotalValue").CallAsync<BigInteger>(tokenId);
#endif
    }

    public async Task<NpcTbaValueBreakdownOutputDTO> GetNpcTbaValueBreakdownAsync(BigInteger tokenId)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        var dto = await ArcTrading.WebGL.WebGLChainApi.GetNpcTbaValueBreakdownAsync(tokenId);
        if (dto == null) return null;
        return new NpcTbaValueBreakdownOutputDTO
        {
            Tba = dto.tba,
            ItemValue = string.IsNullOrEmpty(dto.itemValue) ? BigInteger.Zero : BigInteger.Parse(dto.itemValue),
            CashValue = string.IsNullOrEmpty(dto.cashValue) ? BigInteger.Zero : BigInteger.Parse(dto.cashValue),
            TbaTotalValue = string.IsNullOrEmpty(dto.tbaTotalValue) ? BigInteger.Zero : BigInteger.Parse(dto.tbaTotalValue),
        };
#else
        var contract = readOnlyWeb3.Eth.GetContract(Abi, pricingContractAddress);
        return await contract.GetFunction("getNpcTbaValueBreakdown")
            .CallDeserializingToObjectAsync<NpcTbaValueBreakdownOutputDTO>(tokenId);
#endif
    }

    public async Task<BigInteger> GetScarcityMultiplierBpsAsync(BigInteger classId)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        return await ArcTrading.WebGL.WebGLChainApi.GetScarcityMultiplierBpsAsync(classId);
#else
        var contract = readOnlyWeb3.Eth.GetContract(Abi, pricingContractAddress);
        return await contract.GetFunction("getScarcityMultiplierBps").CallAsync<BigInteger>(classId);
#endif
    }

    public async Task<NpcClassMarketOutputDTO> GetClassMarketAsync(BigInteger classId)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        var m = await ArcTrading.WebGL.WebGLChainApi.GetClassMarketAsync(classId);
        if (m == null) return null;
        return new NpcClassMarketOutputDTO
        {
            TotalSupply = string.IsNullOrEmpty(m.totalSupply) ? BigInteger.Zero : BigInteger.Parse(m.totalSupply),
            ListedSupply = string.IsNullOrEmpty(m.listedSupply) ? BigInteger.Zero : BigInteger.Parse(m.listedSupply),
            VirtualLiquidity = string.IsNullOrEmpty(m.virtualLiquidity) ? BigInteger.Zero : BigInteger.Parse(m.virtualLiquidity),
            BasePrice = string.IsNullOrEmpty(m.basePrice) ? BigInteger.Zero : BigInteger.Parse(m.basePrice),
            MaxMultiplierBps = string.IsNullOrEmpty(m.maxMultiplierBps) ? BigInteger.Zero : BigInteger.Parse(m.maxMultiplierBps),
            ScarcityWeightBps = string.IsNullOrEmpty(m.scarcityWeightBps) ? BigInteger.Zero : BigInteger.Parse(m.scarcityWeightBps),
            Exists = m.exists,
        };
#else
        var contract = readOnlyWeb3.Eth.GetContract(Abi, pricingContractAddress);
        return await contract.GetFunction("classMarkets")
            .CallDeserializingToObjectAsync<NpcClassMarketOutputDTO>(classId);
#endif
    }

    public async Task<QuoteNpcPriceOutputDTO> QuoteNpcPriceAsync(BigInteger tokenId)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        var dto = await ArcTrading.WebGL.WebGLChainApi.QuoteNpcPriceAsync(tokenId);
        if (dto == null) return null;
        return new QuoteNpcPriceOutputDTO
        {
            Price = string.IsNullOrEmpty(dto.price) ? BigInteger.Zero : BigInteger.Parse(dto.price),
            TbaTotalValue = string.IsNullOrEmpty(dto.tbaTotalValue) ? BigInteger.Zero : BigInteger.Parse(dto.tbaTotalValue),
            ScarcityMultiplierBps = string.IsNullOrEmpty(dto.scarcityMultiplierBps) ? BigInteger.Zero : BigInteger.Parse(dto.scarcityMultiplierBps),
        };
#else
        var contract = readOnlyWeb3.Eth.GetContract(Abi, pricingContractAddress);
        return await contract.GetFunction("quoteNpcPrice")
            .CallDeserializingToObjectAsync<QuoteNpcPriceOutputDTO>(tokenId);
#endif
    }
}
