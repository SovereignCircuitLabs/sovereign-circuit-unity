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
        TokenId    = tokenId;
        Address    = address;
        PrivateKey = privateKey;
        Version    = version;
    }
}

// ---------- Exceptions ----------

public class NpcSignerException : Exception
{
    public NpcSignerException(string message) : base(message) { }
}

public class NpcSignerRevoked : NpcSignerException
{
    public NpcSignerRevoked(BigInteger tokenId)
        : base($"NPC {tokenId} has no bound payment wallet on chain.") { }
}

public class NpcSignerStale : NpcSignerException
{
    public NpcSignerStale(BigInteger tokenId)
        : base($"Local key for NPC {tokenId} no longer matches chain wallet "
               + "(NFT transferred or rebound elsewhere).") { }
}

public class NpcSignerMissing : NpcSignerException
{
    public NpcSignerMissing(BigInteger tokenId)
        : base($"No local key for NPC {tokenId}; call EnsureBoundAsync first.") { }
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

    /// <summary>
    /// Ensure a usable local key is bound for `tokenId`: 
    ///   - if chain wallet matches local key and version is current → return it;
    ///   - if local key is stale or absent and chain wallet is empty → generate
    ///     a fresh key, bind on-chain, store locally, return it;
    ///   - if chain has a wallet this device doesn't own → throw
    ///     NpcSignerNotOwned so the caller can decide whether to ForceRebind.
    /// </summary>
    public async Task<NpcPaymentSigner> EnsureBoundAsync(BigInteger tokenId)
    {
        var chainId      = await npcContract.GetChainIdAsync();
        var contractAddr = npcContract.ContractAddress;

        var (chainWallet, chainVersion) = await npcContract.GetPaymentBindingAsync(tokenId);

        if (vault.TryGet(contractAddr, chainId, tokenId, out var addr, out var pk, out var cachedV))
        {
            if (AddressEquals(addr, chainWallet))
            {
                if (cachedV != chainVersion)
                {
                    // Address matches but version moved — NFT custody changed
                    // and somehow the new owner rebound to the same address.
                    // Be strict: forget locally and rebind via the standard path.
                    vault.Forget(contractAddr, chainId, tokenId);
                }
                else
                {
                    return new NpcPaymentSigner(tokenId, chainWallet, pk, chainVersion);
                }
            }
            else
            {
                // Local key no longer matches chain. Drop it before we move on.
                vault.Forget(contractAddr, chainId, tokenId);
            }
        }

        if (!IsZeroAddress(chainWallet))
        {
            // Chain is bound, but we don't (or no longer) have the PK.
            // Recovery requires explicit owner action.
            throw new NpcSignerNotOwned(tokenId, chainWallet);
        }

        return await GenerateAndBindAsync(tokenId, contractAddr, chainId);
    }

    /// <summary>
    /// Wipe whatever is bound on-chain and re-provision a fresh local key.
    /// Use when the operator key is lost on this device, or suspected leaked,
    /// or another device bound a wallet we don't control.
    ///
    /// Requires NFT-owner key in NpcCharacterContractClient because the
    /// contract enforces msg.sender == ownerOf for both clear and bind.
    /// </summary>
    public async Task<NpcPaymentSigner> ForceRebindAsync(BigInteger tokenId)
    {
        var chainId      = await npcContract.GetChainIdAsync();
        var contractAddr = npcContract.ContractAddress;

        var (chainWallet, _) = await npcContract.GetPaymentBindingAsync(tokenId);

        vault.Forget(contractAddr, chainId, tokenId);
        if (!IsZeroAddress(chainWallet))
        {
            await npcContract.ClearPaymentWalletAsync(tokenId);
        }

        return await GenerateAndBindAsync(tokenId, contractAddr, chainId);
    }

    /// <summary>
    /// Called immediately before each x402 signature. ALWAYS reads chain — no
    /// cache — and only returns a signer if all four invariants hold:
    ///
    ///   1. chainWallet != 0x0
    ///   2. local vault has an entry for this tokenId
    ///   3. ecrecover(localPK).address == chainWallet
    ///   4. cachedVersion == chainVersion
    ///
    /// </summary>
    public async Task<NpcPaymentSigner> VerifySignerForSigningAsync(BigInteger tokenId)
    {
        var chainId      = await npcContract.GetChainIdAsync();
        var contractAddr = npcContract.ContractAddress;

        var (chainWallet, chainVersion) = await npcContract.GetPaymentBindingAsync(tokenId);

        if (IsZeroAddress(chainWallet))
        {
            vault.Forget(contractAddr, chainId, tokenId);
            throw new NpcSignerRevoked(tokenId);
        }

        if (!vault.TryGet(contractAddr, chainId, tokenId, out var addr, out var pk, out var cachedV))
        {
            throw new NpcSignerMissing(tokenId);
        }

        if (!AddressEquals(addr, chainWallet))
        {
            vault.Forget(contractAddr, chainId, tokenId);
            throw new NpcSignerStale(tokenId);
        }

        // Defensive: if the on-disk address column was tampered with but the
        // PK was not, the EIP-3009 signature would still recover to a wallet
        // != chainWallet. Recompute from the PK we are about to use.
        var derived = new EthECKey(pk).GetPublicAddress();
        if (!AddressEquals(derived, chainWallet))
        {
            vault.Forget(contractAddr, chainId, tokenId);
            throw new NpcSignerStale(tokenId);
        }

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
        var key  = EthECKey.GenerateKey();
        var pk   = key.GetPrivateKey();
        var addr = key.GetPublicAddress();

        await npcContract.BindPaymentWalletAsync(tokenId, addr);

        // Round-trip the binding to catch reorgs and to capture the version
        // that was current at the moment our bind tx confirmed.
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

    // EIP-55 addresses differ in case but represent the same bytes. Compare on
    // hex content only.
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
        foreach (var c in hex) if (c != '0') return false;
        return true;
    }
}
