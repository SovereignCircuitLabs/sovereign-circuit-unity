// Exit-button bridge for the Unity main menu.
// Application.Quit() is a no-op in WebGL builds (the player is the browser
// tab — nothing to "quit"). The Exit button in MainMenu.cs routes here so
// the click actually unloads the page.
mergeInto(LibraryManager.library, {
  ArcTradingExitWebGL: function () {
    // Browsers refuse window.close() on tabs the user opened themselves
    // (only the script that opened a tab is allowed to close it). We try
    // anyway in case we're embedded as a popup, then fall back to a hard
    // navigation so the game is at least unloaded.
    try { window.close(); } catch (e) {}
    setTimeout(function () {
      try {
        // about:blank is the most universally accepted fallback; it doesn't
        // require user gesture and guarantees the WebGL canvas is torn down.
        window.location.href = 'about:blank';
      } catch (e) {}
    }, 50);
  },
});
