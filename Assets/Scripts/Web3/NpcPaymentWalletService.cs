using System;
using System.Numerics;
using System.Threading.Tasks;
using Nethereum.Signer;
using UnityEngine;

public readonly struct NpcPaymentSigner
{
    public readonly BigInteger TokenId;
    public readonly string Address;
    public readonly string PrivateKey;
    public readonly ulong Version;

    public NpcPaymentSigner(BigInteger tokenId, string address, string privateKey, ulong version)
    {
        TokenId = tokenId;
        Address = address;
        PrivateKey = privateKey;
        Version = version;
    }
}

// ---------- Exceptions ----------

public class NpcSignerException : Exception
{
    public NpcSignerException(string message) : base(message)
    {
    }
}

public class NpcSignerRevoked : NpcSignerException
{
    public NpcSignerRevoked(BigInteger tokenId)
        : base($"NPC {tokenId} has no bound payment wallet on chain.")
    {
    }
}

public class NpcSignerStale : NpcSignerException
{
    public NpcSignerStale(BigInteger tokenId)
        : base($"Local key for NPC {tokenId} no longer matches chain wallet "
               + "(NFT transferred or rebound elsewhere).")
    {
    }
}

public class NpcSignerMissing : NpcSignerException
{
    public NpcSignerMissing(BigInteger tokenId)
        : base($"No local key for NPC {tokenId}; call EnsureBoundAsync first.")
    {
    }
}

public class NpcSignerNotOwned : NpcSignerException
{
    public string ChainWallet { get; }

    public NpcSignerNotOwned(BigInteger tokenId, string chainWallet)
        : base($"NPC {tokenId} is bound to {chainWallet} on chain, but this device does "
               + "not hold its key. Use ForceRebindAsync to clear and rebind.")
    {
        ChainWallet = chainWallet;
    }
}

public class NpcPaymentWalletService : MonoBehaviour
{
    [SerializeField] private NpcCharacterContractClient npcContract;

    // File name within Application.persistentDataPath
    [SerializeField] private string vaultFileName = "npc-payment-keys.json";

    private NpcPaymentKeyVault vault;

    public NpcCharacterContractClient NpcContract => npcContract;

    private void Awake()
    {
        vault = new NpcPaymentKeyVault(vaultFileName);
    }
    
    public async Task<NpcPaymentSigner> EnsureBoundAsync(BigInteger tokenId)
    {
        var chainId = await npcContract.GetChainIdAsync();
        var contractAddr = npcContract.NftContractAddress;

        var (chainWallet, chainVersion) = await npcContract.GetPaymentBindingAsync(tokenId);

        if (vault.TryGet(contractAddr, chainId, tokenId, 
                out var addr, out var pk, out var cachedV))
        {
            if (AddressEquals(addr, chainWallet))
            {
                if (cachedV != chainVersion)
                {
                    vault.Forget(contractAddr, chainId, tokenId);
                }
                else
                {
                    return new NpcPaymentSigner(tokenId, chainWallet, pk, chainVersion);
                }
            }
            else
            {
                vault.Forget(contractAddr, chainId, tokenId);
            }
        }

        if (!IsZeroAddress(chainWallet))
        {
            throw new NpcSignerNotOwned(tokenId, chainWallet);
        }

        return await GenerateAndBindAsync(tokenId, contractAddr, chainId);
    }

    public async Task<NpcPaymentSigner> EnsureBoundOrRebindAsync(BigInteger tokenId)
    {
        try
        {
            return await EnsureBoundAsync(tokenId);
        }
        catch (NpcSignerNotOwned ex)
        {
            Debug.LogWarning($"[NpcPaymentWalletService] tokenId={tokenId} stale binding "
                             + $"({ex.ChainWallet}) — auto-rebinding from this device. "
                             + "Any funds at the old operator address are abandoned.");
            return await ForceRebindAsync(tokenId);
        }
    }

    public async Task<NpcPaymentSigner> ForceRebindAsync(BigInteger tokenId)
    {
        var chainId = await npcContract.GetChainIdAsync();
        var contractAddr = npcContract.NftContractAddress;

        var (chainWallet, _) = await npcContract.GetPaymentBindingAsync(tokenId);

        vault.Forget(contractAddr, chainId, tokenId);
        if (!IsZeroAddress(chainWallet))
        {
            await npcContract.ClearPaymentWalletAsync(tokenId);
        }

        return await GenerateAndBindAsync(tokenId, contractAddr, chainId);
    }

    public async Task<NpcPaymentSigner> VerifySignerForSigningAsync(BigInteger tokenId)
    {
        var chainId = await npcContract.GetChainIdAsync();
        var contractAddr = npcContract.NftContractAddress;

        var (chainWallet, chainVersion) = await npcContract.GetPaymentBindingAsync(tokenId);

        // chainWallet != 0x0
        if (IsZeroAddress(chainWallet))
        {
            vault.Forget(contractAddr, chainId, tokenId);
            throw new NpcSignerRevoked(tokenId);
        }

        // local vault has an entry for this tokenId
        if (!vault.TryGet(contractAddr, chainId, tokenId,
                out var addr, out var pk, out var cachedV))
        {
            throw new NpcSignerMissing(tokenId);
        }

        // ecrecover(localPK).address == chainWallet
        if (!AddressEquals(addr, chainWallet))
        {
            vault.Forget(contractAddr, chainId, tokenId);
            throw new NpcSignerStale(tokenId);
        }

        // check if the on-disk address column was tampered with but the PK was not
        var derived = new EthECKey(pk).GetPublicAddress();
        if (!AddressEquals(derived, chainWallet))
        {
            vault.Forget(contractAddr, chainId, tokenId);
            throw new NpcSignerStale(tokenId);
        }

        // cachedVersion == chainVersion
        if (cachedV != chainVersion)
        {
            vault.Forget(contractAddr, chainId, tokenId);
            throw new NpcSignerStale(tokenId);
        }

        return new NpcPaymentSigner(tokenId, chainWallet, pk, chainVersion);
    }

    // ---------- Internals ----------

    private async Task<NpcPaymentSigner> GenerateAndBindAsync(
        BigInteger tokenId, string contractAddr, long chainId)
    {
        var key = EthECKey.GenerateKey();
        var pk = key.GetPrivateKey();
        var addr = key.GetPublicAddress();

        await npcContract.BindPaymentWalletAsync(tokenId, addr);
        
        // confirmation
        var (verifyWallet, verifyVersion) = await npcContract.GetPaymentBindingAsync(tokenId);
        if (!AddressEquals(verifyWallet, addr))
        {
            throw new InvalidOperationException(
                $"Post-bind verification failed: chain wallet={verifyWallet}, expected={addr}. "
                + "Possible reorg or competing bind tx.");
        }

        vault.Put(contractAddr, chainId, tokenId, addr, pk, verifyVersion);
        return new NpcPaymentSigner(tokenId, addr, pk, verifyVersion);
    }
    
    private static bool AddressEquals(string a, string b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
        var ha = a.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? a.Substring(2) : a;
        var hb = b.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? b.Substring(2) : b;
        return string.Equals(ha, hb, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsZeroAddress(string addr)
    {
        if (string.IsNullOrEmpty(addr)) return true;
        var hex = addr.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? addr.Substring(2) : addr;
        foreach (var c in hex)
            if (c != '0')
                return false;
        return true;
    }
}