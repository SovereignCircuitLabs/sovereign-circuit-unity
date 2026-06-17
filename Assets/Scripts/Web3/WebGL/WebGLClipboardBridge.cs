using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ArcTrading.WebGL
{
    /// <summary>
    /// Browser-clipboard bridge for WebGL builds.
    ///
    /// Why this exists:
    ///   Unity's <see cref="GUIUtility.systemCopyBuffer"/> writes to an in-WASM
    ///   buffer that never reaches <c>navigator.clipboard</c>, so the user
    ///   can't paste copied text into another tab or app. Symmetric story for
    ///   Ctrl+V — the InputField's internal paste handler is wired to a
    ///   buffer that browsers refuse to populate without explicit clipboard
    ///   permissions.
    ///
    /// What it does:
    ///   * <see cref="WriteText"/> forwards into <see cref="GUIUtility.systemCopyBuffer"/>
    ///     (Desktop / Editor) AND <c>navigator.clipboard.writeText</c> (WebGL).
    ///     The latter requires a user gesture in most browsers; calls triggered
    ///     by Unity button clicks inherit that gesture so they work without
    ///     a permission prompt.
    ///   * On WebGL boot a singleton GameObject named
    ///     <c>WebGLClipboardBridge</c> is created. The page-level paste
    ///     listener (installed in the WebGL template's index.html) calls
    ///     <c>unityInstance.SendMessage("WebGLClipboardBridge", "OnPasteFromBrowser", text)</c>
    ///     whenever the user hits Ctrl+V / Cmd+V; this component then inserts
    ///     the text into the focused <see cref="InputField"/>.
    ///
    /// Desktop / Editor: <see cref="WriteText"/> falls through to
    /// <see cref="GUIUtility.systemCopyBuffer"/>; the singleton GameObject is
    /// still created but never receives messages (it's harmless).
    /// </summary>
    public class WebGLClipboardBridge : MonoBehaviour
    {
        private const string SingletonName = "WebGLClipboardBridge";

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern void ArcClipboard_WriteText(string text);
        [DllImport("__Internal")] private static extern string ArcClipboard_BeginRead();
        [DllImport("__Internal")] private static extern string ArcClipboard_PollRead(string id);
#else
        private static void ArcClipboard_WriteText(string _) { /* desktop no-op */ }
        private static string ArcClipboard_BeginRead() => string.Empty;
        private static string ArcClipboard_PollRead(string _) => "{\"error\":\"desktop\"}";
#endif

        /// <summary>
        /// Bootstrapping: runs before any scene loads, so callers don't need to
        /// remember to add the bridge to every scene. The GameObject is marked
        /// DontDestroyOnLoad so it survives scene transitions.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void EnsureSingleton()
        {
            if (GameObject.Find(SingletonName) != null) return;
            var go = new GameObject(SingletonName);
            go.AddComponent<WebGLClipboardBridge>();
            DontDestroyOnLoad(go);
        }

        /// <summary>
        /// Copies <paramref name="text"/> to the system clipboard. Always
        /// writes <see cref="GUIUtility.systemCopyBuffer"/> so Desktop / Editor
        /// behavior is unchanged; on WebGL also fires the browser clipboard
        /// extern. Call from button click handlers (gesture context required
        /// by most browsers for clipboard writes).
        /// </summary>
        public static void WriteText(string text)
        {
            if (text == null) text = string.Empty;
            GUIUtility.systemCopyBuffer = text;
#if UNITY_WEBGL && !UNITY_EDITOR
            try { ArcClipboard_WriteText(text); }
            catch (Exception ex) { Debug.LogWarning($"[WebGLClipboardBridge] writeText extern failed: {ex.Message}"); }
#endif
        }

        // ------------------------------------------------------------------
        // Browser-paste forwarding
        // ------------------------------------------------------------------
        // The WebGL template's index.html installs a document-level paste
        // listener that calls SendMessage("WebGLClipboardBridge",
        // "OnPasteFromBrowser", text). When that fires we look at the
        // currently-selected GameObject; if it owns an InputField we insert
        // the text at the caret. No-op otherwise (the user pasted while the
        // focus was on something Unity doesn't own).

        public void OnPasteFromBrowser(string text)
        {
            if (string.IsNullOrEmpty(text)) return;

            var current = EventSystem.current?.currentSelectedGameObject;
            if (current == null) return;

            var input = current.GetComponent<InputField>();
            if (input == null) return;

            InsertAtCaret(input, text);
        }

        private static void InsertAtCaret(InputField input, string toInsert)
        {
            // Replace any active selection with the pasted text so behavior
            // matches what users expect from a desktop paste.
            int caret = Mathf.Clamp(input.caretPosition, 0, input.text.Length);
            int selStart = Mathf.Min(input.selectionAnchorPosition, input.selectionFocusPosition);
            int selEnd = Mathf.Max(input.selectionAnchorPosition, input.selectionFocusPosition);
            string current = input.text ?? string.Empty;
            string before, after;
            if (selStart != selEnd && selStart >= 0 && selEnd <= current.Length)
            {
                before = current.Substring(0, selStart);
                after = current.Substring(selEnd);
                caret = selStart;
            }
            else
            {
                before = current.Substring(0, caret);
                after = current.Substring(caret);
            }

            string next = before + toInsert + after;
            if (input.characterLimit > 0 && next.Length > input.characterLimit)
                next = next.Substring(0, input.characterLimit);

            input.text = next;
            int newCaret = Mathf.Min(caret + toInsert.Length, next.Length);
            input.caretPosition = newCaret;
            input.selectionAnchorPosition = newCaret;
            input.selectionFocusPosition = newCaret;
            input.ForceLabelUpdate();
        }
    }
}
