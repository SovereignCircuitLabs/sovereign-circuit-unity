using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace ArcTrading.Crypto
{
    /// <summary>
    /// Cross-platform interface for the ETH crypto primitives the NPC operational
    /// stack needs. There are two implementations:
    ///
    ///   - <c>NethereumCryptoBackend</c> (Desktop / Editor): wraps the existing
    ///     Nethereum types. Bit-for-bit identical to today's behavior.
    ///   - <c>WebGLCryptoBackend</c>: uses a .jslib bridge into viem running in
    ///     the host page, so secp256k1 + RLP / EIP-712 / EIP-191 all execute in
    ///     the browser sandbox using browser crypto.
    ///
    /// All operations are CPU-only (key generation, signing). RPC calls are NOT
    /// part of this interface — Desktop sends txs via Nethereum's RPC client and
    /// WebGL sends pre-signed raw hex via the server's <c>POST /tx/send-raw</c>
    /// relay. Both paths are wired in <see cref="EthRawTxSender"/> at a layer
    /// above this interface.
    ///
    /// Use <see cref="EthCryptoBackend.Current"/> to get the platform-correct
    /// instance — its <c>#if</c> selection guarantees Desktop never touches
    /// jslib externs and WebGL never touches Nethereum.
    /// </summary>
    public interface IEthCryptoBackend
    {
        // ------------------ key management ------------------

        /// <summary>Generate a fresh secp256k1 key pair. Used by NPC paymentWallet bootstrap.</summary>
        Task<GeneratedKey> GenerateKeyAsync(CancellationToken ct = default);

        /// <summary>EIP-55 checksummed address from a 32-byte hex private key.</summary>
        string DeriveAddress(string privateKey);

        // ------------------ tx signing ------------------

        /// <summary>Sign a legacy (type 0) transaction. Returns 0x-prefixed signed RLP hex.</summary>
        Task<string> SignLegacyTxAsync(LegacyTxParams tx, string privateKey, CancellationToken ct = default);

        /// <summary>Sign an EIP-1559 (type 2) transaction. Returns 0x-prefixed signed RLP hex.</summary>
        Task<string> SignEip1559TxAsync(Eip1559TxParams tx, string privateKey, CancellationToken ct = default);

        // ------------------ message signing ------------------

        /// <summary>Sign an EIP-712 v4 typed data payload (JSON form). Used for EIP-3009 transferWithAuthorization.</summary>
        Task<string> SignTypedDataV4Async(string typedDataJson, string privateKey, CancellationToken ct = default);

        /// <summary>Sign an EIP-191 personal_sign message. Used for SIWE-style auth.</summary>
        Task<string> SignPersonalMessageAsync(string message, string privateKey, CancellationToken ct = default);
    }

    public struct GeneratedKey
    {
        public string Address;     // EIP-55 checksum
        public string PrivateKey;  // 0x-prefixed 32-byte hex
    }

    /// <summary>Parameters for a legacy EIP-155 transaction. All BigInteger fields are wei / chain-native units.</summary>
    public class LegacyTxParams
    {
        public string To;             // 0x-prefixed address; null/empty for contract create
        public BigInteger Nonce;
        public BigInteger GasPrice;   // wei
        public BigInteger GasLimit;
        public BigInteger Value;      // wei
        public string Data;           // 0x-prefixed hex; empty / "0x" for no data
        public long ChainId;
    }

    public class Eip1559TxParams
    {
        public string To;
        public BigInteger Nonce;
        public BigInteger MaxFeePerGas;
        public BigInteger MaxPriorityFeePerGas;
        public BigInteger GasLimit;
        public BigInteger Value;
        public string Data;
        public long ChainId;
    }
}
