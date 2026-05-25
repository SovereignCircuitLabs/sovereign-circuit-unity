using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;

[Serializable]
internal class NpcPaymentVaultEntry
{
    // Lowercased contract address — vault is partitioned by (contractAddress, chainId)
    // so two deployments of NpcCharacter on different chains never collide on tokenId.
    public string contractAddress;
    public long   chainId;

    // tokenId stored as decimal string so the file survives BigInteger > ulong.
    public string tokenId;

    // EIP-55 checksum address derived from the encrypted private key.
    public string address;

    // AES-GCM ciphertext (cipher || tag) and 12-byte nonce, both base64.
    public string encryptedPrivateKey;
    public string nonce;

    // Snapshot of npcPaymentVersion[tokenId] at bind time. Used purely as a
    // cache-invalidation tripwire — version mismatch means NFT has changed
    // custody since we bound, and the local key MUST be forgotten.
    public ulong cachedVersion;
}

[Serializable]
internal class NpcPaymentVaultFile
{
    public int version = 1;
    public List<NpcPaymentVaultEntry> entries = new List<NpcPaymentVaultEntry>();
}

/// <summary>
/// Local encrypted store of NPC x402 operator keys, keyed by
/// (contractAddress, chainId, tokenId).
///
/// Encryption strategy: AES-256-GCM with a key derived via PBKDF2 from
/// SystemInfo.deviceUniqueIdentifier. This is "device-bound" — copying the
/// vault file to another machine yields ciphertext that won't decrypt.
/// It does NOT defeat a local attacker who can read the user's process
/// memory or replay the device id; for that, layer a player-supplied
/// passphrase on top. The current model is appropriate for low-value
/// operator keys whose blast radius is capped by the gateway-wallet balance.
/// </summary>
public class NpcPaymentKeyVault
{
    private readonly string filePath;
    private NpcPaymentVaultFile cache;

    public NpcPaymentKeyVault(string fileName = "npc-payment-keys.json")
    {
        filePath = Path.Combine(Application.persistentDataPath, fileName);
        cache = LoadFromDisk();
    }

    public bool TryGet(string contractAddress, long chainId, BigInteger tokenId,
        out string address, out string privateKey, out ulong cachedVersion)
    {
        address = privateKey = null;
        cachedVersion = 0;
        var entry = Find(contractAddress, chainId, tokenId);
        if (entry == null) return false;

        try
        {
            privateKey = Decrypt(entry.encryptedPrivateKey, entry.nonce);
        }
        catch (Exception ex)
        {
            // Decryption failure here typically means the file was copied from
            // another device, or the device id changed. Treat as missing so the
            // service will route to rebind rather than crash.
            Debug.LogError($"[NpcPaymentKeyVault] decrypt failed for tokenId {tokenId}: {ex.Message}");
            return false;
        }

        address = entry.address;
        cachedVersion = entry.cachedVersion;
        return true;
    }

    public void Put(string contractAddress, long chainId, BigInteger tokenId,
        string address, string privateKey, ulong cachedVersion)
    {
        var (cipher, nonce) = Encrypt(privateKey);

        // Replace any prior entry for the same (contract, chainId, tokenId).
        var existing = Find(contractAddress, chainId, tokenId);
        if (existing != null) cache.entries.Remove(existing);

        cache.entries.Add(new NpcPaymentVaultEntry
        {
            contractAddress     = contractAddress.ToLowerInvariant(),
            chainId             = chainId,
            tokenId             = tokenId.ToString(CultureInfo.InvariantCulture),
            address             = address,
            encryptedPrivateKey = cipher,
            nonce               = nonce,
            cachedVersion       = cachedVersion
        });
        Save();
    }

    public void UpdateVersion(string contractAddress, long chainId, BigInteger tokenId, ulong newVersion)
    {
        var entry = Find(contractAddress, chainId, tokenId);
        if (entry == null) return;
        entry.cachedVersion = newVersion;
        Save();
    }

    public void Forget(string contractAddress, long chainId, BigInteger tokenId)
    {
        var entry = Find(contractAddress, chainId, tokenId);
        if (entry == null) return;
        cache.entries.Remove(entry);
        Save();
    }

    private NpcPaymentVaultEntry Find(string contractAddress, long chainId, BigInteger tokenId)
    {
        var contractKey = contractAddress.ToLowerInvariant();
        var tokenStr = tokenId.ToString(CultureInfo.InvariantCulture);
        return cache.entries.Find(x =>
            x.chainId == chainId
            && string.Equals(x.contractAddress, contractKey, StringComparison.OrdinalIgnoreCase)
            && x.tokenId == tokenStr);
    }

    // ---------- Persistence ----------

    private NpcPaymentVaultFile LoadFromDisk()
    {
        try
        {
            if (!File.Exists(filePath)) return new NpcPaymentVaultFile();
            var json = File.ReadAllText(filePath);
            return JsonConvert.DeserializeObject<NpcPaymentVaultFile>(json) ?? new NpcPaymentVaultFile();
        }
        catch (Exception ex)
        {
            // Corrupt vault file: start empty rather than crash. The user can
            // rebind from chain; nothing irrecoverable was stored here.
            Debug.LogError($"[NpcPaymentKeyVault] load failed, starting empty: {ex.Message}");
            return new NpcPaymentVaultFile();
        }
    }

    private void Save()
    {
        // Atomic write via temp file + Replace so a crash mid-write can't
        // leave a half-truncated vault on disk.
        var tmp = filePath + ".tmp";
        File.WriteAllText(tmp, JsonConvert.SerializeObject(cache, Formatting.Indented));
        if (File.Exists(filePath)) File.Replace(tmp, filePath, null);
        else File.Move(tmp, filePath);
    }

    // ---------- Crypto: AES-256-GCM, device-bound key ----------

    private const int KeyBytes   = 32;
    private const int NonceBytes = 12;
    private const int TagBytes   = 16;

    // Salt is constant per app — uniqueness comes from the device id.
    // Bumping the suffix would force a vault rotation.
    private static readonly byte[] Salt = Encoding.UTF8.GetBytes("ArcTrading::NpcPaymentKeyVault::v1");

    private static byte[] DeriveKey()
    {
        // SystemInfo.deviceUniqueIdentifier is stable per machine on Windows/
        // standalone builds. If it ever returns empty (rare on some platforms)
        // we fall back to a constant so the vault still works — at the cost
        // of being machine-portable, which we explicitly document.
        var device = SystemInfo.deviceUniqueIdentifier;
        if (string.IsNullOrEmpty(device)) device = "ArcTrading::fallback-device-id";

        using var pbkdf2 = new Rfc2898DeriveBytes(device, Salt, 200_000, HashAlgorithmName.SHA256);
        return pbkdf2.GetBytes(KeyBytes);
    }

    private static (string cipherB64, string nonceB64) Encrypt(string plaintextHex)
    {
        var key = DeriveKey();
        var nonce = new byte[NonceBytes];
        RandomNumberGenerator.Fill(nonce);

        var plaintext = Encoding.UTF8.GetBytes(plaintextHex);
        var cipher = new byte[plaintext.Length];
        var tag = new byte[TagBytes];

        // .NET Standard 2.1's AesGcm has no explicit tag-size constructor —
        // the tag length is inferred from the tag buffer size we pass below.
        using (var aes = new AesGcm(key))
        {
            aes.Encrypt(nonce, plaintext, cipher, tag);
        }

        // Store cipher || tag concatenated; nonce stored separately for clarity.
        var combined = new byte[cipher.Length + tag.Length];
        Buffer.BlockCopy(cipher, 0, combined, 0, cipher.Length);
        Buffer.BlockCopy(tag, 0, combined, cipher.Length, tag.Length);
        return (Convert.ToBase64String(combined), Convert.ToBase64String(nonce));
    }

    private static string Decrypt(string cipherB64, string nonceB64)
    {
        var key = DeriveKey();
        var combined = Convert.FromBase64String(cipherB64);
        var nonce = Convert.FromBase64String(nonceB64);

        if (combined.Length < TagBytes)
            throw new CryptographicException("ciphertext too short");

        var cipher = new byte[combined.Length - TagBytes];
        var tag = new byte[TagBytes];
        Buffer.BlockCopy(combined, 0, cipher, 0, cipher.Length);
        Buffer.BlockCopy(combined, cipher.Length, tag, 0, TagBytes);

        var plaintext = new byte[cipher.Length];
        using (var aes = new AesGcm(key))
        {
            aes.Decrypt(nonce, cipher, tag, plaintext);
        }
        return Encoding.UTF8.GetString(plaintext);
    }
}
