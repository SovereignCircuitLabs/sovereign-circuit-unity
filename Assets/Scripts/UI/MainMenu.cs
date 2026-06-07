using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class MainMenu : MonoBehaviour
{
    public Button startButton;
    public Button websiteButton;
    public string websiteURL = "https://sovereigncore-web.rcrobotcat.workers.dev/";
    public Button exitButton;

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
        Application.Quit();
    }
}