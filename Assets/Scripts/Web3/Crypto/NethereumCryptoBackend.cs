using System.Threading;
using System.Threading.Tasks;
using Nethereum.ABI.EIP712;
using Nethereum.Hex.HexConvertors.Extensions;
using Nethereum.Model;
using Nethereum.Signer;
using Nethereum.Signer.EIP712;

namespace ArcTrading.Crypto
{
    /// <summary>
    /// Desktop / Editor crypto backend. Pure CPU operations — no RPC. Implements
    /// the same primitives that the WebGL .jslib bridge will (eventually) provide,
    /// so callsites are bit-for-bit identical on both targets.
    ///
    /// Every method here is synchronous in Nethereum; the <c>Task</c>-wrapping is
    /// purely to match <see cref="IEthCryptoBackend"/> which has to be async for
    /// the WebGL bridge's promise-based JS calls.
    /// </summary>
    public class NethereumCryptoBackend : IEthCryptoBackend
    {
        public Task<GeneratedKey> GenerateKeyAsync(CancellationToken ct = default)
        {
            var key = EthECKey.GenerateKey();
            return Task.FromResult(new GeneratedKey
            {
                Address = key.GetPublicAddress(),
                PrivateKey = key.GetPrivateKey(),
            });
        }

        public string DeriveAddress(string privateKey)
        {
            return new EthECKey(privateKey).GetPublicAddress();
        }

        public Task<string> SignLegacyTxAsync(LegacyTxParams tx, string privateKey, CancellationToken ct = default)
        {
            var signer = new LegacyTransactionSigner();
            // Nethereum's signer takes byte arrays for `to` and `data`; null `to`
            // is permitted for contract creation.
            var dataBytes = string.IsNullOrEmpty(tx.Data) ? new byte[0] : tx.Data.HexToByteArray();
            var signed = signer.SignTransaction(
                privateKey,
                tx.ChainId,
                tx.To,
                tx.Value,
                tx.Nonce,
                tx.GasPrice,
                tx.GasLimit,
                dataBytes.Length == 0 ? "0x" : "0x" + dataBytes.ToHex());
            return Task.FromResult("0x" + signed);
        }

        public Task<string> SignEip1559TxAsync(Eip1559TxParams tx, string privateKey, CancellationToken ct = default)
        {
            var signer = new Transaction1559Signer();
            var dataHex = string.IsNullOrEmpty(tx.Data) ? "0x" : tx.Data;
            var t = new Transaction1559(
                tx.ChainId,
                tx.Nonce,
                tx.MaxPriorityFeePerGas,
                tx.MaxFeePerGas,
                tx.GasLimit,
                tx.To,
                tx.Value,
                dataHex,
                null);
            signer.SignTransaction(new EthECKey(privateKey), t);
            return Task.FromResult("0x" + t.GetRLPEncoded().ToHex());
        }

        public Task<string> SignTypedDataV4Async(string typedDataJson, string privateKey, CancellationToken ct = default)
        {
            // Nethereum's Eip712TypedDataSigner.SignTypedDataV4 supports a JSON
            // entry point via the TypedDataRawJsonConversion path.
            var signer = new Eip712TypedDataSigner();
            var key = new EthECKey(privateKey);
            var signature = signer.SignTypedDataV4(typedDataJson, key);
            return Task.FromResult(signature);
        }

        public Task<string> SignPersonalMessageAsync(string message, string privateKey, CancellationToken ct = default)
        {
            var signer = new EthereumMessageSigner();
            var signature = signer.EncodeUTF8AndSign(message, new EthECKey(privateKey));
            return Task.FromResult(signature);
        }
    }
}
