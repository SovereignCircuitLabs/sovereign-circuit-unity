// ArcTradingCryptoBridge.jslib
// ---------------------------------------------------------------------------
// JS bridge between C#'s WebGLCryptoBackend (Assets/Scripts/Web3/Crypto/) and
// the page-level crypto implementation (viem or @noble/secp256k1, loaded by
// the WebGL HTML template's <script> tag).
//
// Contract: this file declares the extern function shapes. The actual crypto
// happens in `window.ArcTradingCryptoBridge`, a JS object the page MUST install
// before any Unity code calls these functions. See the BRIDGE CONTRACT section
// at the bottom for the exact JS-side API.
//
// All functions return a 0-terminated UTF-8 string (heap pointer) holding a
// JSON object. On success the object has the expected payload fields. On
// failure it has an "error" field with a human-readable message — the C# side
// throws InvalidOperationException with that text. We don't propagate JS Error
// instances directly because emscripten can't marshal them to typed C#
// exceptions cheaply.
//
// Why JSON over raw strings: viem's signTransaction returns Promises that
// resolve immediately for local inputs, but emscripten externs are sync. We
// keep things sync by going through `window.ArcTradingCryptoBridge` which is
// expected to wrap any necessary async primitives into synchronous returns
// (or to be implemented with @noble/secp256k1 which is fully sync). See the
// reference impl in StreamingAssets/walletlogin/crypto-bridge.js (TODO Phase 5).

mergeInto(LibraryManager.library, {
  // ------------------ key management ------------------

  ArcCrypto_GenerateKey: function () {
    var bridge = window.ArcTradingCryptoBridge;
    var out;
    try {
      if (!bridge || typeof bridge.generateKey !== 'function') {
        throw new Error('window.ArcTradingCryptoBridge.generateKey not installed');
      }
      var result = bridge.generateKey();
      if (!result || !result.address || !result.privateKey) {
        throw new Error('generateKey() did not return {address, privateKey}');
      }
      out = JSON.stringify({ address: result.address, privateKey: result.privateKey });
    } catch (e) {
      out = JSON.stringify({ error: String(e && e.message ? e.message : e) });
    }
    var sz = lengthBytesUTF8(out) + 1;
    var ptr = _malloc(sz);
    stringToUTF8(out, ptr, sz);
    return ptr;
  },

  ArcCrypto_DeriveAddress: function (pkPtr) {
    var pk = UTF8ToString(pkPtr);
    var bridge = window.ArcTradingCryptoBridge;
    var out;
    try {
      if (!bridge || typeof bridge.deriveAddress !== 'function') {
        throw new Error('window.ArcTradingCryptoBridge.deriveAddress not installed');
      }
      var addr = bridge.deriveAddress(pk);
      if (typeof addr !== 'string' || addr.length === 0) {
        throw new Error('deriveAddress() did not return a non-empty string');
      }
      out = JSON.stringify({ address: addr });
    } catch (e) {
      out = JSON.stringify({ error: String(e && e.message ? e.message : e) });
    }
    var sz = lengthBytesUTF8(out) + 1;
    var ptr = _malloc(sz);
    stringToUTF8(out, ptr, sz);
    return ptr;
  },

  // ------------------ tx signing ------------------

  ArcCrypto_SignLegacyTx: function (txJsonPtr, pkPtr) {
    var txJson = UTF8ToString(txJsonPtr);
    var pk = UTF8ToString(pkPtr);
    var bridge = window.ArcTradingCryptoBridge;
    var out;
    try {
      if (!bridge || typeof bridge.signLegacyTx !== 'function') {
        throw new Error('window.ArcTradingCryptoBridge.signLegacyTx not installed');
      }
      var tx = JSON.parse(txJson);
      var signed = bridge.signLegacyTx(tx, pk);
      if (typeof signed !== 'string' || signed.indexOf('0x') !== 0) {
        throw new Error('signLegacyTx() did not return 0x-prefixed hex');
      }
      out = JSON.stringify({ signed: signed });
    } catch (e) {
      out = JSON.stringify({ error: String(e && e.message ? e.message : e) });
    }
    var sz = lengthBytesUTF8(out) + 1;
    var ptr = _malloc(sz);
    stringToUTF8(out, ptr, sz);
    return ptr;
  },

  ArcCrypto_SignEip1559Tx: function (txJsonPtr, pkPtr) {
    var txJson = UTF8ToString(txJsonPtr);
    var pk = UTF8ToString(pkPtr);
    var bridge = window.ArcTradingCryptoBridge;
    var out;
    try {
      if (!bridge || typeof bridge.signEip1559Tx !== 'function') {
        throw new Error('window.ArcTradingCryptoBridge.signEip1559Tx not installed');
      }
      var tx = JSON.parse(txJson);
      var signed = bridge.signEip1559Tx(tx, pk);
      if (typeof signed !== 'string' || signed.indexOf('0x') !== 0) {
        throw new Error('signEip1559Tx() did not return 0x-prefixed hex');
      }
      out = JSON.stringify({ signed: signed });
    } catch (e) {
      out = JSON.stringify({ error: String(e && e.message ? e.message : e) });
    }
    var sz = lengthBytesUTF8(out) + 1;
    var ptr = _malloc(sz);
    stringToUTF8(out, ptr, sz);
    return ptr;
  },

  // ------------------ message signing ------------------

  ArcCrypto_SignTypedDataV4: function (typedDataJsonPtr, pkPtr) {
    var typedDataJson = UTF8ToString(typedDataJsonPtr);
    var pk = UTF8ToString(pkPtr);
    var bridge = window.ArcTradingCryptoBridge;
    var out;
    try {
      if (!bridge || typeof bridge.signTypedDataV4 !== 'function') {
        throw new Error('window.ArcTradingCryptoBridge.signTypedDataV4 not installed');
      }
      var sig = bridge.signTypedDataV4(typedDataJson, pk);
      if (typeof sig !== 'string' || sig.indexOf('0x') !== 0) {
        throw new Error('signTypedDataV4() did not return 0x-prefixed hex');
      }
      out = JSON.stringify({ signature: sig });
    } catch (e) {
      out = JSON.stringify({ error: String(e && e.message ? e.message : e) });
    }
    var sz = lengthBytesUTF8(out) + 1;
    var ptr = _malloc(sz);
    stringToUTF8(out, ptr, sz);
    return ptr;
  },

  ArcCrypto_SignPersonalMessage: function (msgPtr, pkPtr) {
    var msg = UTF8ToString(msgPtr);
    var pk = UTF8ToString(pkPtr);
    var bridge = window.ArcTradingCryptoBridge;
    var out;
    try {
      if (!bridge || typeof bridge.signPersonalMessage !== 'function') {
        throw new Error('window.ArcTradingCryptoBridge.signPersonalMessage not installed');
      }
      var sig = bridge.signPersonalMessage(msg, pk);
      if (typeof sig !== 'string' || sig.indexOf('0x') !== 0) {
        throw new Error('signPersonalMessage() did not return 0x-prefixed hex');
      }
      out = JSON.stringify({ signature: sig });
    } catch (e) {
      out = JSON.stringify({ error: String(e && e.message ? e.message : e) });
    }
    var sz = lengthBytesUTF8(out) + 1;
    var ptr = _malloc(sz);
    stringToUTF8(out, ptr, sz);
    return ptr;
  },

  // Bonus: ABI encoder via viem so Step 4 (NPC paymentWallet writes) doesn't
  // need its own C# encoder. abi is a JSON string of the ABI fragments,
  // argsJson is a JSON array of arguments (bigints encoded as decimal strings).
  ArcCrypto_EncodeFunctionData: function (abiJsonPtr, fnNamePtr, argsJsonPtr) {
    var abiJson = UTF8ToString(abiJsonPtr);
    var fnName = UTF8ToString(fnNamePtr);
    var argsJson = UTF8ToString(argsJsonPtr);
    var bridge = window.ArcTradingCryptoBridge;
    var out;
    try {
      if (!bridge || typeof bridge.encodeFunctionData !== 'function') {
        throw new Error('window.ArcTradingCryptoBridge.encodeFunctionData not installed');
      }
      var args = JSON.parse(argsJson);
      // Convert decimal-string args back to BigInt where the ABI type is uintN
      // (viem requires bigint for those). Simpler: scan the args, anything that
      // matches /^\d+$/ becomes BigInt — addresses (0x...) and booleans pass
      // through. This works for our limited Step-4 call surface.
      var coerced = args.map(function (a) {
        if (typeof a === 'string' && /^\d+$/.test(a)) return BigInt(a);
        return a;
      });
      var encoded = bridge.encodeFunctionData(abiJson, fnName, coerced);
      if (typeof encoded !== 'string' || encoded.indexOf('0x') !== 0) {
        throw new Error('encodeFunctionData() did not return 0x-prefixed hex');
      }
      out = JSON.stringify({ data: encoded });
    } catch (e) {
      out = JSON.stringify({ error: String(e && e.message ? e.message : e) });
    }
    var sz = lengthBytesUTF8(out) + 1;
    var ptr = _malloc(sz);
    stringToUTF8(out, ptr, sz);
    return ptr;
  },

  // ------------------ MetaMask bridge (async via polling) ------------------
  // Each entry returns a request-id string immediately. C# polls
  // ArcMm_PollRequest(id) at ~30 ms cadence until the envelope is non-pending.

  ArcMm_RequestAccounts: function () {
    var b = window.ArcTradingMetamaskBridge;
    var out = b && b.requestAccounts ? b.requestAccounts()
      : JSON.stringify({ error: 'metamaskBridge.requestAccounts not installed' });
    var sz = lengthBytesUTF8(out) + 1;
    var ptr = _malloc(sz);
    stringToUTF8(out, ptr, sz);
    return ptr;
  },

  ArcMm_PersonalSign: function (msgPtr, addrPtr) {
    var b = window.ArcTradingMetamaskBridge;
    var msg = UTF8ToString(msgPtr);
    var addr = UTF8ToString(addrPtr);
    var out = b && b.personalSign ? b.personalSign(msg, addr)
      : JSON.stringify({ error: 'metamaskBridge.personalSign not installed' });
    var sz = lengthBytesUTF8(out) + 1;
    var ptr = _malloc(sz);
    stringToUTF8(out, ptr, sz);
    return ptr;
  },

  ArcMm_SendTransaction: function (txJsonPtr) {
    var b = window.ArcTradingMetamaskBridge;
    var txJson = UTF8ToString(txJsonPtr);
    var out;
    try {
      var tx = JSON.parse(txJson);
      out = b && b.sendTransaction ? b.sendTransaction(tx)
        : JSON.stringify({ error: 'metamaskBridge.sendTransaction not installed' });
    } catch (e) {
      out = JSON.stringify({ error: 'invalid tx JSON: ' + String(e && e.message ? e.message : e) });
    }
    var sz = lengthBytesUTF8(out) + 1;
    var ptr = _malloc(sz);
    stringToUTF8(out, ptr, sz);
    return ptr;
  },

  ArcMm_ChainId: function () {
    var b = window.ArcTradingMetamaskBridge;
    var out = b && b.chainId ? b.chainId()
      : JSON.stringify({ error: 'metamaskBridge.chainId not installed' });
    var sz = lengthBytesUTF8(out) + 1;
    var ptr = _malloc(sz);
    stringToUTF8(out, ptr, sz);
    return ptr;
  },

  ArcMm_PollRequest: function (idPtr) {
    var b = window.ArcTradingMetamaskBridge;
    var id = UTF8ToString(idPtr);
    var out = b && b.pollRequest ? b.pollRequest(id)
      : JSON.stringify({ error: 'metamaskBridge.pollRequest not installed' });
    var sz = lengthBytesUTF8(out) + 1;
    var ptr = _malloc(sz);
    stringToUTF8(out, ptr, sz);
    return ptr;
  },

  // ------------------ Public RPC bridge (viem publicClient for nonce/gas/chainId) ------------------

  ArcPubRpc_Configure: function (rpcUrlPtr) {
    var b = window.ArcTradingPublicRpc;
    var url = UTF8ToString(rpcUrlPtr);
    try {
      if (!b || typeof b.configure !== 'function') throw new Error('publicRpc.configure not installed');
      b.configure(url);
    } catch (e) {
      console.error('[ArcPubRpc_Configure]', e);
    }
  },

  ArcPubRpc_GetTransactionCount: function (addrPtr) {
    var b = window.ArcTradingPublicRpc;
    var addr = UTF8ToString(addrPtr);
    var out = b && b.getTransactionCount ? b.getTransactionCount(addr)
      : JSON.stringify({ error: 'publicRpc.getTransactionCount not installed' });
    var sz = lengthBytesUTF8(out) + 1;
    var ptr = _malloc(sz);
    stringToUTF8(out, ptr, sz);
    return ptr;
  },

  ArcPubRpc_GetGasPrice: function () {
    var b = window.ArcTradingPublicRpc;
    var out = b && b.getGasPrice ? b.getGasPrice()
      : JSON.stringify({ error: 'publicRpc.getGasPrice not installed' });
    var sz = lengthBytesUTF8(out) + 1;
    var ptr = _malloc(sz);
    stringToUTF8(out, ptr, sz);
    return ptr;
  },

  ArcPubRpc_GetChainId: function () {
    var b = window.ArcTradingPublicRpc;
    var out = b && b.getChainId ? b.getChainId()
      : JSON.stringify({ error: 'publicRpc.getChainId not installed' });
    var sz = lengthBytesUTF8(out) + 1;
    var ptr = _malloc(sz);
    stringToUTF8(out, ptr, sz);
    return ptr;
  },

  ArcPubRpc_PollRequest: function (idPtr) {
    var b = window.ArcTradingPublicRpc;
    var id = UTF8ToString(idPtr);
    var out = b && b.pollRequest ? b.pollRequest(id)
      : JSON.stringify({ error: 'publicRpc.pollRequest not installed' });
    var sz = lengthBytesUTF8(out) + 1;
    var ptr = _malloc(sz);
    stringToUTF8(out, ptr, sz);
    return ptr;
  },
});

// ===========================================================================
// BRIDGE CONTRACT — window.ArcTradingCryptoBridge
// ===========================================================================
// The WebGL build's HTML template MUST install this global before Unity boots.
// Suggested location: Assets/StreamingAssets/walletlogin/crypto-bridge.js +
// a <script> tag in the WebGL template that imports viem (or @noble/secp256k1).
//
// Reference implementation using viem (~80 kb gzipped, fully tree-shakeable):
// ---------------------------------------------------------------------------
// <script type="module">
//   import {
//     generatePrivateKey,
//     privateKeyToAccount,
//   } from 'https://esm.sh/viem@2/accounts';
//   import { serializeTransaction } from 'https://esm.sh/viem@2';
//
//   window.ArcTradingCryptoBridge = {
//     generateKey() {
//       const pk = generatePrivateKey();            // 0x + 32-byte hex
//       const acc = privateKeyToAccount(pk);
//       return { address: acc.address, privateKey: pk };
//     },
//     deriveAddress(pk) {
//       return privateKeyToAccount(pk).address;
//     },
//     signLegacyTx(tx, pk) {
//       // tx is the JSON we built in WebGLCryptoBackend.SignLegacyTxAsync:
//       //   { to, nonce, gasPrice, gasLimit, value, data, chainId }
//       // viem expects bigint for numeric fields and lowercase address.
//       const acc = privateKeyToAccount(pk);
//       // signTransaction returns a Promise, but for legacy txs without async
//       // gas estimation it resolves in a microtask. We can't await here, so
//       // either:
//       //  (a) use viem's lower-level "sign" + "serializeTransaction" combo
//       //      and avoid the Promise entirely, or
//       //  (b) replace this method with one built on @noble/secp256k1 +
//       //      hand-rolled RLP (~30 kb total, fully sync).
//       // Option (a) sketch:
//       const hash = keccak256_of_rlp_legacy_unsigned(tx);   // implement
//       const sig = sign({ hash, privateKey: pk });          // sync
//       return serializeTransaction({ ...tx, type: 'legacy' }, sig);
//     },
//     signEip1559Tx(tx, pk) { /* analogous */ },
//     signTypedDataV4(typedDataJson, pk) {
//       const acc = privateKeyToAccount(pk);
//       // viem's signTypedData returns Promise — must use the sync internal:
//       //   sign({ hash: hashTypedData(typedData), privateKey: pk })
//       // and adjust the returned signature shape (r,s,v concatenation).
//     },
//     signPersonalMessage(message, pk) { /* analogous */ },
//   };
// </script>
// ---------------------------------------------------------------------------
//
// Recommendation: use @noble/secp256k1 + @noble/hashes for a fully synchronous
// implementation. viem internally uses these libs anyway, but its public API
// is Promise-wrapped which makes the sync extern shape awkward.
//
// Acceptance criteria for the JS impl:
//   1. generateKey() returns { address: EIP-55 string, privateKey: 0x+64hex }
//      synchronously (no Promise).
//   2. signLegacyTx(tx, pk) returns 0x-prefixed signed RLP hex, sync. The tx
//      object follows the JSON shape WebGLCryptoBackend builds (see
//      LegacyTxBridgeBody): string nonce/gasPrice/gasLimit/value (decimal
//      base-10), string to (0x or empty), string data (0x-prefixed hex),
//      number chainId.
//   3. signEip1559Tx(tx, pk) analogous, with maxFeePerGas + maxPriorityFeePerGas.
//   4. signTypedDataV4(typedDataJson, pk) takes the EIP-712 JSON spec
//      (domain + types + primaryType + message) and returns 0x signature.
//   5. signPersonalMessage(message, pk) prefixes "\x19Ethereum Signed Message:\n"
//      + utf8-bytes-length, hashes with keccak256, signs, returns 0x signature.
//   6. Any error MUST be thrown — this jslib catches and surfaces as
//      {error: "..."}; never return a partial / undefined result on failure.
