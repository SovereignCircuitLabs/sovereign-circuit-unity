using System;
using System.Globalization;
using System.Text.RegularExpressions;
using Nethereum.Signer;

namespace ArcTrading.Auth
{
    /// <summary>
    /// Minimal EIP-4361 (Sign-In With Ethereum) build + parse + verify.
    /// Verification is a self-contained ECDSA recover — no network, no JWT.
    /// </summary>
    public static class SiweMessage
    {
        public struct BuildArgs
        {
            public string Domain;        // e.g. "localhost:7777"
            public string Address;       // EIP-55 or lowercase 0x...; emitted as-is
            public string Statement;     // human-readable "Sign in to ArcTrading"
            public string Uri;           // e.g. "http://localhost:7777"
            public long ChainId;
            public string Nonce;         // hex16+
            public DateTimeOffset IssuedAt;
            public DateTimeOffset ExpirationTime;
        }

        public struct VerifyResult
        {
            public bool Ok;
            public string WalletLower;
            public string Reason;
        }

        public static string Build(BuildArgs args)
        {
            // EIP-4361 uses LF newlines, statement bracketed by blank lines, ISO-8601 timestamps.
            return
                $"{args.Domain} wants you to sign in with your Ethereum account:\n" +
                $"{args.Address}\n" +
                $"\n" +
                $"{args.Statement}\n" +
                $"\n" +
                $"URI: {args.Uri}\n" +
                $"Version: 1\n" +
                $"Chain ID: {args.ChainId.ToString(CultureInfo.InvariantCulture)}\n" +
                $"Nonce: {args.Nonce}\n" +
                $"Issued At: {args.IssuedAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture)}\n" +
                $"Expiration Time: {args.ExpirationTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture)}";
        }

        /// <summary>
        /// Verify a SIWE message + personal_sign signature.
        /// </summary>
        public static VerifyResult Verify(
            string rawMessage,
            string signature,
            string expectedNonce,
            long expectedChainId)
        {
            if (string.IsNullOrEmpty(rawMessage))
                return new VerifyResult { Ok = false, Reason = "empty message" };
            if (string.IsNullOrEmpty(signature))
                return new VerifyResult { Ok = false, Reason = "empty signature" };

            var addressInMsg = MatchField(rawMessage, @"^(0x[0-9a-fA-F]{40})$");
            if (string.IsNullOrEmpty(addressInMsg))
                return new VerifyResult { Ok = false, Reason = "address line missing" };

            var nonceInMsg = MatchField(rawMessage, @"^Nonce:\s*(\S+)\s*$");
            if (string.IsNullOrEmpty(nonceInMsg))
                return new VerifyResult { Ok = false, Reason = "nonce field missing" };
            if (!string.Equals(nonceInMsg, expectedNonce, StringComparison.Ordinal))
                return new VerifyResult { Ok = false, Reason = "nonce mismatch" };

            var chainStr = MatchField(rawMessage, @"^Chain ID:\s*(\d+)\s*$");
            if (string.IsNullOrEmpty(chainStr))
                return new VerifyResult { Ok = false, Reason = "chain id field missing" };
            if (!long.TryParse(chainStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var chainInMsg))
                return new VerifyResult { Ok = false, Reason = "chain id not numeric" };
            // chainId == 0 means "do not enforce" — useful when caller hasn't resolved the RPC yet.
            if (expectedChainId != 0 && chainInMsg != expectedChainId)
                return new VerifyResult { Ok = false, Reason = $"chain id mismatch (msg={chainInMsg}, expected={expectedChainId})" };

            var expStr = MatchField(rawMessage, @"^Expiration Time:\s*(\S+)\s*$");
            if (string.IsNullOrEmpty(expStr))
                return new VerifyResult { Ok = false, Reason = "expiration field missing" };
            if (!DateTimeOffset.TryParse(expStr, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var exp))
                return new VerifyResult { Ok = false, Reason = "expiration not parseable" };
            if (exp <= DateTimeOffset.UtcNow)
                return new VerifyResult { Ok = false, Reason = "challenge expired" };

            string recovered;
            try
            {
                recovered = new EthereumMessageSigner().EncodeUTF8AndEcRecover(rawMessage, signature);
            }
            catch (Exception ex)
            {
                return new VerifyResult { Ok = false, Reason = $"ecrecover failed: {ex.Message}" };
            }

            if (string.IsNullOrEmpty(recovered))
                return new VerifyResult { Ok = false, Reason = "ecrecover returned empty" };

            if (!string.Equals(recovered, addressInMsg, StringComparison.OrdinalIgnoreCase))
                return new VerifyResult
                {
                    Ok = false,
                    Reason = $"recovered address {recovered.ToLowerInvariant()} != signer {addressInMsg.ToLowerInvariant()}"
                };

            return new VerifyResult
            {
                Ok = true,
                WalletLower = recovered.ToLowerInvariant()
            };
        }

        private static string MatchField(string text, string pattern)
        {
            var m = Regex.Match(text, pattern, RegexOptions.Multiline);
            return m.Success ? m.Groups[1].Value : null;
        }
    }
}
