// ArcTradingClipboardBridge.jslib
// ---------------------------------------------------------------------------
// Bridges Unity C# clipboard reads/writes to the browser's clipboard.
//
// In WebGL builds, `GUIUtility.systemCopyBuffer` and Unity InputField's
// internal Ctrl+C / Ctrl+V plumbing do NOT reach the host page's clipboard:
//   - copy: Unity writes to an internal buffer, but the browser-side clipboard
//     stays empty so the user can't paste into another tab.
//   - paste: Unity's InputField listens for an emscripten-synthesized paste
//     event that fires only when the canvas is focused under a specific input
//     mode — easy to miss, and inconsistent across Chrome / Firefox.
//
// This jslib gives C# a direct line to `navigator.clipboard.writeText` and
// `navigator.clipboard.readText`. The read path is async (browser shows a
// permission prompt the first time), so it uses the same request-id polling
// pattern as the MetaMask / PublicRpc bridges.

mergeInto(LibraryManager.library, {

  // Writes `text` to navigator.clipboard. Best-effort: fires the promise and
  // ignores the result. Most browsers require this to be triggered from a
  // user gesture; we can't enforce that from C# but Unity button clicks DO
  // count as gestures because emscripten preserves the gesture context across
  // the SendMessage boundary.
  ArcClipboard_WriteText: function (textPtr) {
    var text = UTF8ToString(textPtr);
    var legacyWrite = function (s) {
      try {
        var ta = document.createElement('textarea');
        ta.value = s;
        ta.style.position = 'fixed';
        ta.style.left = '-9999px';
        document.body.appendChild(ta);
        ta.focus(); ta.select();
        try { document.execCommand('copy'); } catch (_) {}
        document.body.removeChild(ta);
      } catch (e) {
        console.error('[ArcClipboard] legacy fallback failed', e);
      }
    };
    try {
      if (navigator.clipboard && navigator.clipboard.writeText) {
        navigator.clipboard.writeText(text).catch(function (err) {
          console.warn('[ArcClipboard] writeText failed, falling back', err);
          legacyWrite(text);
        });
      } else {
        legacyWrite(text);
      }
    } catch (e) {
      console.error('[ArcClipboard] writeText threw', e);
    }
  },

  // Kicks off an async read and returns a request id. C# polls
  // ArcClipboard_PollRead(id) until the envelope is non-pending.
  ArcClipboard_BeginRead: function () {
    var out;
    try {
      if (!window.__arcClipboardPending) {
        window.__arcClipboardPending = new Map();
        window.__arcClipboardNextId = 1;
      }
      var id = String(window.__arcClipboardNextId++);
      window.__arcClipboardPending.set(id, { resolved: false });
      if (navigator.clipboard && navigator.clipboard.readText) {
        navigator.clipboard.readText().then(
          function (text) { window.__arcClipboardPending.set(id, { resolved: true, result: text }); },
          function (err)  { window.__arcClipboardPending.set(id, { resolved: true, error: String(err && err.message ? err.message : err) }); }
        );
      } else {
        window.__arcClipboardPending.set(id, { resolved: true, error: 'navigator.clipboard.readText unsupported' });
      }
      out = id;
    } catch (e) {
      out = '';
    }
    var sz = lengthBytesUTF8(out) + 1;
    var ptr = _malloc(sz);
    stringToUTF8(out, ptr, sz);
    return ptr;
  },

  ArcClipboard_PollRead: function (idPtr) {
    var id = UTF8ToString(idPtr);
    var out;
    try {
      var entry = window.__arcClipboardPending && window.__arcClipboardPending.get(id);
      if (!entry) out = JSON.stringify({ error: 'unknown clipboard request ' + id });
      else if (!entry.resolved) out = JSON.stringify({ pending: true });
      else {
        window.__arcClipboardPending.delete(id);
        if (entry.error) out = JSON.stringify({ error: entry.error });
        else out = JSON.stringify({ text: entry.result || '' });
      }
    } catch (e) {
      out = JSON.stringify({ error: String(e && e.message ? e.message : e) });
    }
    var sz = lengthBytesUTF8(out) + 1;
    var ptr = _malloc(sz);
    stringToUTF8(out, ptr, sz);
    return ptr;
  },
});
