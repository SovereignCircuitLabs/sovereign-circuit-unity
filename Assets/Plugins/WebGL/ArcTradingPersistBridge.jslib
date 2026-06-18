// ArcTradingPersistBridge.jslib
// ---------------------------------------------------------------------------
// Flushes Unity's persistentDataPath (mounted at /idbfs/<hash>/ via Emscripten
// IDBFS) into the browser's IndexedDB store.
//
// Why this exists:
//   `File.WriteAllText(Application.persistentDataPath, ...)` writes to the
//   in-WASM MEMFS layer only. The data does NOT reach IndexedDB until
//   `FS.syncfs(false, cb)` is called. Unity tries to flush on `beforeunload`,
//   but that handler is unreliable when the tab is backgrounded and evicted,
//   when the user navigates to another page, or when bfcache restores the
//   page. The symptom we hit: NpcPaymentKeyVault writes the freshly bound
//   payment wallet, the user clicks away to another tab/page, comes back,
//   and the vault file is empty — so the NPC re-generates and re-binds,
//   wasting gas.
//
// Contract:
//   ArcPersist_SyncToIDB() is fire-and-forget. It schedules a flush from
//   MEMFS to IndexedDB and returns immediately. Vault writes are infrequent
//   (only on bind / clear / version-bump) so we don't bother coalescing —
//   each Save() can post its own IDB transaction.

mergeInto(LibraryManager.library, {

  ArcPersist_SyncToIDB: function () {
    try {
      if (typeof FS === 'undefined' || !FS || typeof FS.syncfs !== 'function') {
        // Non-IDBFS build (shouldn't happen on WebGL, but be defensive).
        return;
      }
      FS.syncfs(false, function (err) {
        if (err) {
          console.warn('[ArcPersist] FS.syncfs failed', err);
        }
      });
    } catch (e) {
      console.error('[ArcPersist] SyncToIDB threw', e);
    }
  },
});
