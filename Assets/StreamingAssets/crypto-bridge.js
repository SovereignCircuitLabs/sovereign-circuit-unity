// crypto-bridge.js
// ---------------------------------------------------------------------------
// Browser-side implementation of the contract documented in
// Assets/Plugins/WebGL/ArcTradingCryptoBridge.jslib.
//
// Installs two globals:
//   window.ArcTradingCryptoBridge  — local-paymentWallet crypto (sync)
//   window.ArcTradingMetamaskBridge — player MetaMask interactions (async,
//                                     polled from C# via PollRequest)
//   window.ArcTradingPublicRpc      — viem publicClient wrapper for nonce /
//                                     gasPrice / chainId reads (async, polled)
//
// Loading: add this to the WebGL build's HTML template (Unity copies
// StreamingAssets next to the build's index.html):
//
//   <script type="module" src="StreamingAssets/crypto-bridge.js"></script>
//
// MUST be loaded before Unity's bootstrap script so the bridge is installed
// before any NPC tries to generate / sign / read RPC.
//
// Deps via esm.sh (~150 kB gzipped total; for production replace with a
// bundled local copy of viem 2 + @noble/secp256k1 2 + @noble/hashes 1):
import {
  keccak256 as viemKeccak256,
  serializeTransaction,
  hashTypedData,
  hashMessage,
  encodeFunctionData as viemEncodeFunctionData,
  createPublicClient,
  http,
} from 'https://esm.sh/viem@2'
import * as secp from 'https://esm.sh/@noble/secp256k1@2'
import { hmac } from 'https://esm.sh/@noble/hashes@1/hmac'
import { sha256 } from 'https://esm.sh/@noble/hashes@1/sha2'

const WALLET_SESSION_KEY = 'arc_wallet_session_v1'

// @noble/secp256k1 v2 ships sync sign/verify, but the consumer must inject the
// HMAC hash function before any sync call — otherwise sign() throws
// "etc.hmacSha256Sync not set" on first use. In v2 the injection slot moved
// from v1's `secp.utils.hmacSha256Sync` / mythical `secp.hashes.*` to
// `secp.etc.hmacSha256Sync`; sha256 itself is wired internally by the
// library, so we only need to provide HMAC.
secp.etc.hmacSha256Sync = (key, ...msgs) =>
  hmac(sha256, key, secp.etc.concatBytes(...msgs))

// ---------------------------------------------------------------------------
// internals
// ---------------------------------------------------------------------------

function stripHex (s) { return typeof s === 'string' && s.startsWith('0x') ? s.slice(2) : s }
function ensureHex (s) { return typeof s === 'string' && s.startsWith('0x') ? s : '0x' + s }

function bytesToHexStr (bytes) {
  let s = ''
  for (let i = 0; i < bytes.length; i++) s += bytes[i].toString(16).padStart(2, '0')
  return s
}
function hexToBytes (hex) {
  hex = stripHex(hex)
  if (hex.length % 2) hex = '0' + hex
  const out = new Uint8Array(hex.length / 2)
  for (let i = 0; i < out.length; i++) out[i] = parseInt(hex.substr(i * 2, 2), 16)
  return out
}
function bigintToHex32 (n) {
  return '0x' + n.toString(16).padStart(64, '0')
}

function readCachedWalletSession () {
  if (window.__arcWalletSession) return window.__arcWalletSession
  try {
    const raw = window.localStorage && window.localStorage.getItem(WALLET_SESSION_KEY)
    return raw ? JSON.parse(raw) : null
  } catch (_) {
    return null
  }
}

function writeCachedWalletSession (session) {
  if (!session) return
  window.__arcWalletSession = session
  try {
    if (window.localStorage) window.localStorage.setItem(WALLET_SESSION_KEY, JSON.stringify(session))
  } catch (_) {
    // localStorage may be unavailable in private mode; the in-memory cache still works.
  }
}

function clearCachedWalletSession () {
  window.__arcWalletSession = null
  try {
    if (window.localStorage) window.localStorage.removeItem(WALLET_SESSION_KEY)
  } catch (_) {}
}

// EIP-55 mixed-case checksum address.
function toChecksumAddress (addressLower) {
  const clean = stripHex(addressLower).toLowerCase()
  const hashHex = stripHex(viemKeccak256(new TextEncoder().encode(clean)))
  let out = '0x'
  for (let i = 0; i < clean.length; i++) {
    out += parseInt(hashHex[i], 16) >= 8 ? clean[i].toUpperCase() : clean[i]
  }
  return out
}

function deriveAddress (privateKeyHex) {
  const pk = hexToBytes(privateKeyHex)
  // secp v2: getPublicKey(pk, isCompressed). uncompressed = 65 bytes (0x04 + x + y).
  const pub = secp.getPublicKey(pk, false)
  const xy = pub.slice(1)
  // keccak256(pubKeyXY)[12:] is the address.
  const hashBytes = hexToBytes(viemKeccak256(xy))
  const addrLower = '0x' + bytesToHexStr(hashBytes.slice(12))
  return toChecksumAddress(addrLower)
}

// @noble v2 sign returns a Signature with .r .s .recovery (all sync).
function signHashBytes (privateKeyHex, hashBytes) {
  const pk = hexToBytes(privateKeyHex)
  const sig = secp.sign(hashBytes, pk)
  return { r: sig.r, s: sig.s, recovery: sig.recovery }
}

// ---------------------------------------------------------------------------
// window.ArcTradingCryptoBridge — paymentWallet local crypto (sync)
// ---------------------------------------------------------------------------

window.ArcTradingCryptoBridge = {
  generateKey () {
    const pk = secp.utils.randomPrivateKey()        // Uint8Array(32)
    const privateKey = '0x' + bytesToHexStr(pk)
    const address = deriveAddress(privateKey)
    return { address, privateKey }
  },

  deriveAddress (privateKey) {
    return deriveAddress(privateKey)
  },

  // tx = { to, nonce, gasPrice, gasLimit, value, data, chainId } all strings
  // (decimal for numerics, hex for to/data, except chainId which arrives as number)
  signLegacyTx (tx, privateKey) {
    const txObj = {
      type: 'legacy',
      to: tx.to && tx.to.length > 0 ? tx.to : undefined,
      nonce: Number(tx.nonce),
      gasPrice: BigInt(tx.gasPrice),
      gas: BigInt(tx.gasLimit),
      value: BigInt(tx.value),
      data: tx.data && tx.data !== '0x' ? ensureHex(tx.data) : undefined,
      chainId: Number(tx.chainId),
    }
    const unsigned = serializeTransaction(txObj)
    const hash = hexToBytes(viemKeccak256(unsigned))
    const sig = signHashBytes(privateKey, hash)
    // EIP-155 v = chainId*2 + 35 + recovery
    const v = BigInt(tx.chainId) * 2n + 35n + BigInt(sig.recovery)
    return serializeTransaction(txObj, {
      r: bigintToHex32(sig.r),
      s: bigintToHex32(sig.s),
      v,
    })
  },

  // tx = { to, nonce, maxFeePerGas, maxPriorityFeePerGas, gasLimit, value, data, chainId }
  signEip1559Tx (tx, privateKey) {
    const txObj = {
      type: 'eip1559',
      to: tx.to && tx.to.length > 0 ? tx.to : undefined,
      nonce: Number(tx.nonce),
      maxFeePerGas: BigInt(tx.maxFeePerGas),
      maxPriorityFeePerGas: BigInt(tx.maxPriorityFeePerGas),
      gas: BigInt(tx.gasLimit),
      value: BigInt(tx.value),
      data: tx.data && tx.data !== '0x' ? ensureHex(tx.data) : undefined,
      chainId: Number(tx.chainId),
    }
    const unsigned = serializeTransaction(txObj)
    const hash = hexToBytes(viemKeccak256(unsigned))
    const sig = signHashBytes(privateKey, hash)
    return serializeTransaction(txObj, {
      r: bigintToHex32(sig.r),
      s: bigintToHex32(sig.s),
      yParity: sig.recovery,
    })
  },

  // EIP-712 v4: typedDataJson is the EIP-712 payload (domain, types, primaryType, message).
  // Returns 65-byte sig: r || s || v where v = 27 + recovery.
  signTypedDataV4 (typedDataJson, privateKey) {
    const typedData = JSON.parse(typedDataJson)
    const hashHex = hashTypedData(typedData)
    const hash = hexToBytes(hashHex)
    const sig = signHashBytes(privateKey, hash)
    const r = sig.r.toString(16).padStart(64, '0')
    const s = sig.s.toString(16).padStart(64, '0')
    const v = (27 + sig.recovery).toString(16).padStart(2, '0')
    return '0x' + r + s + v
  },

  // EIP-191 personal_sign: keccak256("\x19Ethereum Signed Message:\n" + len(msg) + msg)
  signPersonalMessage (message, privateKey) {
    const hashHex = hashMessage(message)
    const hash = hexToBytes(hashHex)
    const sig = signHashBytes(privateKey, hash)
    const r = sig.r.toString(16).padStart(64, '0')
    const s = sig.s.toString(16).padStart(64, '0')
    const v = (27 + sig.recovery).toString(16).padStart(2, '0')
    return '0x' + r + s + v
  },

  // Bonus helper for Step 4: build calldata so C# doesn't need its own ABI encoder.
  // abi = JSON string OR array literal of ABI fragments. args is an array of
  // strings/numbers/bigints (BigInt-as-string preferred for uint256 to avoid IEEE-754
  // overflow on the JS side).
  encodeFunctionData (abi, functionName, args) {
    return viemEncodeFunctionData({
      abi: typeof abi === 'string' ? JSON.parse(abi) : abi,
      functionName,
      args,
    })
  },
}

// ---------------------------------------------------------------------------
// async polling pattern shared by MetaMask + public RPC bridges
// ---------------------------------------------------------------------------
// Both expose: methodFoo(args...) → returns requestId immediately.
//              pollRequest(id) → { pending: true } | { result } | { error }
// C# poll loop: call method, then pollRequest in a yielding loop until non-pending.
// (PollRequest returns JSON-encoded {pending,result,error} string for marshalling.)

const __pending = new Map()
let __nextRequestId = 1

function startAsync (promise) {
  const id = String(__nextRequestId++)
  __pending.set(id, { resolved: false })
  promise.then(
    (result) => __pending.set(id, { resolved: true, result }),
    (err) => __pending.set(id, { resolved: true, error: String(err && err.message ? err.message : err) }),
  )
  return id
}

function pollAsync (id) {
  const entry = __pending.get(id)
  if (!entry) return { error: 'unknown request id ' + id }
  if (!entry.resolved) return { pending: true }
  __pending.delete(id)
  if (entry.error) return { error: entry.error }
  // Unity's JsonUtility can't deserialize variable result types into a single
  // C# field, so we coerce non-string results to JSON strings here. C# parses
  // further when needed (e.g. requestAccounts returns the array stringified).
  const r = entry.result
  return { result: typeof r === 'string' ? r : JSON.stringify(r) }
}

// ---------------------------------------------------------------------------
// window.ArcTradingMetamaskBridge — player MetaMask interactions
// ---------------------------------------------------------------------------

function ensureEthereum () {
  if (!window.ethereum) {
    return Promise.reject(new Error('MetaMask not detected. Install the MetaMask extension and reload.'))
  }
  return Promise.resolve(window.ethereum)
}

window.ArcTradingMetamaskBridge = {
  // Returns request ID immediately. C# polls until done.
  // On success the result is an array of addresses; we expose [0] as the
  // connected wallet.
  requestAccounts () {
    return startAsync(ensureEthereum().then((eth) => eth.request({ method: 'eth_requestAccounts' })))
  },

  // Silent read of currently-permitted accounts via eth_accounts. Used by
  // WebGL pre-flight before eth_sendTransaction to detect a stale Unity-side
  // SIWE session vs. real MetaMask permissions. Never pops a popup.
  getAccounts () {
    return startAsync(ensureEthereum().then((eth) => eth.request({ method: 'eth_accounts' })))
  },

  personalSign (message, address) {
    return startAsync(
      ensureEthereum().then((eth) => eth.request({ method: 'personal_sign', params: [message, address] })),
    )
  },

  // tx is the eth_sendTransaction JSON-RPC param object (hex-prefixed
  // numerics, lowercase addresses). MetaMask both signs AND broadcasts; the
  // returned promise resolves with the tx hash.
  sendTransaction (tx) {
    return startAsync(ensureEthereum().then((eth) => eth.request({ method: 'eth_sendTransaction', params: [tx] })))
  },

  chainId () {
    return startAsync(ensureEthereum().then((eth) => eth.request({ method: 'eth_chainId' })))
  },

  cacheSession (session) {
    writeCachedWalletSession(typeof session === 'string' ? JSON.parse(session) : session)
  },

  getCachedSession () {
    const session = readCachedWalletSession()
    return session ? JSON.stringify(session) : ''
  },

  clearCachedSession () {
    clearCachedWalletSession()
  },

  // Shared poll endpoint. Returns a JSON-stringified envelope so emscripten
  // marshalling stays simple on the C# side.
  pollRequest (id) {
    return JSON.stringify(pollAsync(id))
  },
}

if (window.ethereum && window.ethereum.on) {
  window.ethereum.on('accountsChanged', () => clearCachedWalletSession())
  window.ethereum.on('chainChanged', () => clearCachedWalletSession())
}

// ---------------------------------------------------------------------------
// window.ArcTradingPublicRpc — viem publicClient wrapper for nonce / gas / chainId
// ---------------------------------------------------------------------------
// Used by Step 4 (NPC paymentWallet writes): C# builds a tx, so it needs
// nonce + gasPrice + chainId. We expose them through viem instead of forcing
// Unity to do raw JSON-RPC.
//
// Configure with the same RPC URL the Unity wallet-login config carries.

let __publicClient = null

window.ArcTradingPublicRpc = {
  configure (rpcUrl) {
    __publicClient = createPublicClient({ transport: http(rpcUrl) })
  },

  isConfigured () { return __publicClient !== null },

  getTransactionCount (address) {
    if (!__publicClient) return startAsync(Promise.reject(new Error('publicRpc not configured')))
    return startAsync(__publicClient.getTransactionCount({ address }).then((n) => n.toString()))
  },

  getGasPrice () {
    if (!__publicClient) return startAsync(Promise.reject(new Error('publicRpc not configured')))
    return startAsync(__publicClient.getGasPrice().then((p) => p.toString()))
  },

  getChainId () {
    if (!__publicClient) return startAsync(Promise.reject(new Error('publicRpc not configured')))
    return startAsync(__publicClient.getChainId().then((c) => c.toString()))
  },

  // Shares the same polling envelope as the MetaMask bridge.
  pollRequest (id) {
    return JSON.stringify(pollAsync(id))
  },
}

console.log('[ArcTradingCryptoBridge] installed: cryptoBridge=', !!window.ArcTradingCryptoBridge,
  'metamaskBridge=', !!window.ArcTradingMetamaskBridge,
  'publicRpc=', !!window.ArcTradingPublicRpc)
