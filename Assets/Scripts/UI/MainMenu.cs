using System;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class MainMenu : MonoBehaviour
{
    public Button startButton;
    public Button websiteButton;
    public string websiteURL = "https://sovereigncore-web.rcrobotcat.workers.dev/";
    public Button exitButton;

#if UNITY_WEBGL && !UNITY_EDITOR
    // Defined in Assets/Plugins/WebGL/ArcTradingExitBridge.jslib. Application.Quit()
    // is a no-op in WebGL — the player IS the browser tab — so the Exit button
    // routes through the jslib bridge to window.close() (with about:blank as a
    // fallback for browsers that refuse to close user-opened tabs).
    [DllImport("__Internal")]
    private static extern void ArcTradingExitWebGL();
#endif

    private void Start()
    {
        startButton.onClick.AddListener(OnStartClicked);
        websiteButton.onClick.AddListener(OnWebsiteClicked);
        exitButton.onClick.AddListener(OnExitClicked);
    }

    private void OnStartClicked()
    {
        SceneManager.LoadScene("MainScene");
    }

    private void OnWebsiteClicked()
    {
        Application.OpenURL(websiteURL);
    }

    private void OnExitClicked()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        ArcTradingExitWebGL();
#else
        Application.Quit();
#endif
    }
}