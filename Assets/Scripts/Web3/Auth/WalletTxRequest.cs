using System;

namespace ArcTrading.Auth
{
    /// <summary>
    /// Unsigned Ethereum transaction handed to the browser for MetaMask signing.
    /// All hex fields use 0x-prefixed lowercase; value/gas may be omitted (the
    /// browser falls back to wallet defaults).
    /// </summary>
    [Serializable]
    public class WalletTxRequest
    {
        public string id;       // server-side correlation id; auto-generated if blank
        public string from;     // 0x...; auto-filled with the SIWE-logged wallet if blank
        public string to;       // 0x... target contract / EOA
        public string value;    // 0x-hex wei, default "0x0"
        public string data;     // 0x-prefixed calldata, default "0x"
        public string gas;      // 0x-hex gas limit; empty => let MetaMask estimate
        public long chainId;

        /// <summary>Human-readable label for UI / logs. Not sent on-chain.</summary>
        public string label;
    }
}
