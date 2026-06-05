using System;
using UnityEngine;

namespace ArcTrading.Auth
{
    /// <summary>
    /// Locally-issued wallet session produced after a successful SIWE login.
    /// Contains the raw SIWE message + signature so a future remote backend
    /// (IRemoteSessionExchange) can re-verify and mint a JWT without re-prompting
    /// the user.
    /// No private key material — safe to persist in PlayerPrefs.
    /// </summary>
    [Serializable]
    public class WalletSession
    {
        public int schemaVersion = 1;
        public string wallet;       // EIP-55-or-lowercase, normalized to lowercase by Save()
        public long chainId;
        public string issuedAt;     // ISO-8601 UTC
        public string expiresAt;    // ISO-8601 UTC
        public string message;      // raw EIP-4361 SIWE message bytes that were signed
        public string signature;    // 0x-prefixed personal_sign output

        private const string PlayerPrefsKey = "arc_wallet_session_v1";
        private const int CurrentSchema = 1;

        public bool IsValidNow()
        {
            if (string.IsNullOrEmpty(wallet) || string.IsNullOrEmpty(expiresAt)) return false;
            if (!DateTimeOffset.TryParse(expiresAt, out var exp)) return false;
            return exp > DateTimeOffset.UtcNow;
        }

        public static WalletSession LoadOrNull()
        {
            var json = PlayerPrefs.GetString(PlayerPrefsKey, string.Empty);
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                var parsed = JsonUtility.FromJson<WalletSession>(json);
                if (parsed == null || parsed.schemaVersion != CurrentSchema) return null;
                return parsed;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[WalletSession] failed to parse stored session, discarding: {ex.Message}");
                PlayerPrefs.DeleteKey(PlayerPrefsKey);
                return null;
            }
        }

        public void Save()
        {
            schemaVersion = CurrentSchema;
            if (!string.IsNullOrEmpty(wallet)) wallet = wallet.ToLowerInvariant();
            PlayerPrefs.SetString(PlayerPrefsKey, JsonUtility.ToJson(this));
            PlayerPrefs.Save();
        }

        public static void Clear()
        {
            PlayerPrefs.DeleteKey(PlayerPrefsKey);
            PlayerPrefs.Save();
        }
    }
}
