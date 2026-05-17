using System;
using UnityEngine;
using UnityEngine.UI;

public class DisplayBtn : MonoBehaviour
{
    private Button btn;

    public RectTransform scrollview;
    public RectTransform popupDetailView;

    private void Start()
    {
        btn = GetComponent<Button>();
        btn.onClick.AddListener(OnBtnClicked);
    }

    private void OnBtnClicked()
    {
        if (scrollview.gameObject.activeSelf)
        {
            popupDetailView.gameObject.SetActive(false);
            scrollview.gameObject.SetActive(false);
        }
        else
        {
            scrollview.gameObject.SetActive(true);
        }
    }
}