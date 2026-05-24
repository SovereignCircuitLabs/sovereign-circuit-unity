using System;
using UnityEngine;
using UnityEngine.UI;

public class DisplayBtn : MonoBehaviour
{
    private Button btn;

    public RectTransform scrollview;
    public RectTransform popupDetailView;
    public RectTransform worldEventsView;

    public RectTransform agentView;
    public bool isMacroAgentBtn = false;

    private void Start()
    {
        btn = GetComponent<Button>();
        btn.onClick.AddListener(OnBtnClicked);
    }

    private void OnBtnClicked()
    {
        if (!isMacroAgentBtn)
        {
            if (scrollview.gameObject.activeSelf)
            {
                popupDetailView.gameObject.SetActive(false);
                scrollview.gameObject.SetActive(false);
                worldEventsView.gameObject.SetActive(false);
            }
            else
            {
                scrollview.gameObject.SetActive(true);
                worldEventsView.gameObject.SetActive(true);
            }
        }
        else
        {
            if (agentView.gameObject.activeSelf)
            {
                agentView.gameObject.SetActive(false);
            }
            else
            {
                agentView.gameObject.SetActive(true);
            }
        }
    }
}