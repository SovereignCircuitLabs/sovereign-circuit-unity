using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;
using UnityEngine;

[Serializable]
internal class NpcPaymentVaultEntry
{
    public string contractAddress;
    public long chainId;
    public string tokenId;

    // AES-GCM ciphertext (cipher || tag) and 12-byte nonce, both base64
    public string encryptedPrivateKey;
    public string nonce;
    
    // EIP-55 checksum address derived from the encrypted private key
    public string address;

    public ulong cachedVersion;
}

[Serializable]
internal class NpcPaymentVaultFile
{
    public int version = 1;
    public List<NpcPaymentVaultEntry> entries = new List<NpcPaymentVaultEntry>();
}

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

        var existing = Find(contractAddress, chainId, tokenId);
        if (existing != null) cache.entries.Remove(existing);

        cache.entries.Add(new NpcPaymentVaultEntry
        {
            contractAddress = contractAddress.ToLowerInvariant(),
            chainId = chainId,
            tokenId = tokenId.ToString(CultureInfo.InvariantCulture),
            address = address,
            encryptedPrivateKey = cipher,
            nonce = nonce,
            cachedVersion = cachedVersion
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
            Debug.LogError($"[NpcPaymentKeyVault] load failed, starting empty: {ex.Message}");
            return new NpcPaymentVaultFile();
        }
    }

    private void Save()
    {
        var tmp = filePath + ".tmp";
        File.WriteAllText(tmp, JsonConvert.SerializeObject(cache, Formatting.Indented));
        if (File.Exists(filePath)) File.Replace(tmp, filePath, null);
        else File.Move(tmp, filePath);
    }

    // ---------- Crypto: AES-256-GCM, device-bound key ----------

    private const int KeyBytes = 32;
    private const int NonceBytes = 12;
    private const int TagBytes = 16;

    private static readonly byte[] Salt = Encoding.UTF8.GetBytes("ArcTrading::NpcPaymentKeyVault::v1");

    private static byte[] DeriveKey()
    {
        var device = SystemInfo.deviceUniqueIdentifier;
        if (string.IsNullOrEmpty(device)) device = "ArcTrading::fallback-device-id";

        using var pbkdf2 = new Rfc2898DeriveBytes(device, Salt, 200_000, HashAlgorithmName.SHA256);
        return pbkdf2.GetBytes(KeyBytes);
    }

    // BouncyCastle is used here instead of System.Security.Cryptography.AesGcm because
    // AesGcm throws PlatformNotSupportedException on Unity's Mono runtime on Windows.
    private static (string cipherB64, string nonceB64) Encrypt(string plaintextHex)
    {
        var key = DeriveKey();
        var nonce = new byte[NonceBytes];
        RandomNumberGenerator.Fill(nonce);

        var plaintext = Encoding.UTF8.GetBytes(plaintextHex);

        var gcm = new GcmBlockCipher(new AesEngine());
        gcm.Init(true, new AeadParameters(new KeyParameter(key), TagBytes * 8, nonce));

        var combined = new byte[gcm.GetOutputSize(plaintext.Length)];
        var written = gcm.ProcessBytes(plaintext, 0, plaintext.Length, combined, 0);
        gcm.DoFinal(combined, written);

        return (Convert.ToBase64String(combined), Convert.ToBase64String(nonce));
    }

    private static string Decrypt(string cipherB64, string nonceB64)
    {
        var key = DeriveKey();
        var combined = Convert.FromBase64String(cipherB64);
        var nonce = Convert.FromBase64String(nonceB64);

        if (combined.Length < TagBytes)
            throw new CryptographicException("ciphertext too short");

        var gcm = new GcmBlockCipher(new AesEngine());
        gcm.Init(false, new AeadParameters(new KeyParameter(key), TagBytes * 8, nonce));

        var plaintext = new byte[gcm.GetOutputSize(combined.Length)];
        var written = gcm.ProcessBytes(combined, 0, combined.Length, plaintext, 0);
        written += gcm.DoFinal(plaintext, written);

        return Encoding.UTF8.GetString(plaintext, 0, written);
    }
}