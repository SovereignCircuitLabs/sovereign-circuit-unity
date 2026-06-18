using System.Runtime.InteropServices;
using UnityEngine;

namespace ArcTrading.WebGL
{
    /// <summary>
    /// Flushes Unity's persistentDataPath to IndexedDB on WebGL.
    ///
    /// Why: <c>Application.persistentDataPath</c> on WebGL is mounted via
    /// Emscripten IDBFS at <c>/idbfs/&lt;hash&gt;/</c>. <c>File.WriteAllText</c>
    /// only writes to the in-memory MEMFS layer — the data does NOT persist
    /// to IndexedDB until <c>FS.syncfs(false, cb)</c> is called. Unity tries
    /// to flush on page unload, but that fires unreliably when the user
    /// navigates to another page, switches tabs (background eviction), or
    /// when bfcache restores the page. Symptom: NpcPaymentKeyVault writes
    /// the freshly bound payment wallet, the user clicks away to another
    /// page, comes back, and the vault file is empty — the NPC re-generates
    /// and re-binds, wasting gas.
    ///
    /// Call <see cref="RequestSync"/> after every write to persistentDataPath
    /// that must survive a navigation. Desktop / Editor: no-op.
    /// </summary>
    public static class WebGLPersistBridge
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern void ArcPersist_SyncToIDB();
#else
        private static void ArcPersist_SyncToIDB() { /* desktop / editor: persistentDataPath is a real disk path */ }
#endif

        /// <summary>
        /// Fire-and-forget flush of the IDBFS-backed persistentDataPath into
        /// IndexedDB. Cheap to call — the JS side coalesces concurrent
        /// requests into a single in-flight IDB transaction.
        /// </summary>
        public static void RequestSync()
        {
            try
            {
                ArcPersist_SyncToIDB();
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[WebGLPersistBridge] sync request failed: {ex.Message}");
            }
        }
    }
}
