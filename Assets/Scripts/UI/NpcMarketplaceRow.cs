using System;
using System.Numerics;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Binds a single NPC listing onto a row prefab. Build the prefab yourself in the
/// Editor (background image, labels, Buy button…) and wire the [SerializeField]
/// refs below in the inspector. The controller calls Bind(listing, onBuy).
/// </summary>
public class NpcMarketplaceRow : MonoBehaviour
{
    [Header("Labels")]
    [SerializeField] private Text tokenIdLabel;
    [SerializeField] private Text nameLabel;
    [SerializeField] private Text archetypeLabel;
    [SerializeField] private Text priceLabel;
    [SerializeField] private Text tbaValueLabel;
    [SerializeField] private Text sellerLabel;

    [Header("Action")]
    [SerializeField] private Button buyButton;
    [SerializeField] private Text buyButtonLabel;
    [SerializeField] private string buyLabelTemplate = "Buy {0} USDC";

    public Button BuyButton => buyButton;

    private NpcListingInfo cached;
    private Action<NpcListingInfo> onBuy;

    public void Bind(NpcListingInfo info, Action<NpcListingInfo> onBuyClicked)
    {
        cached = info;
        onBuy = onBuyClicked;

        if (tokenIdLabel != null)
            tokenIdLabel.text = $"#{info.TokenId}";

        if (nameLabel != null)
            nameLabel.text = info.NpcData != null && !string.IsNullOrEmpty(info.NpcData.NpcName)
                ? info.NpcData.NpcName
                : $"NPC #{info.TokenId}";

        if (archetypeLabel != null)
            archetypeLabel.text = info.NpcData != null
                ? ((TradingNpcArchetype)info.NpcData.Archetype).ToString()
                : "(unknown)";

        decimal priceUsdc = ToUsdc(info.QuotedPrice);
        decimal tbaValueUsdc = ToUsdc(info.TbaTotalValue);

        if (priceLabel != null)
            priceLabel.text = $"{priceUsdc:0.######} USDC";

        if (tbaValueLabel != null)
            tbaValueLabel.text = $"{tbaValueUsdc:0.######} USDC";

        if (sellerLabel != null)
            sellerLabel.text = ShortAddress(info.Seller);

        if (buyButtonLabel != null)
            buyButtonLabel.text = string.Format(buyLabelTemplate, priceUsdc.ToString("0.######"));

        if (buyButton != null)
        {
            buyButton.onClick.RemoveAllListeners();
            buyButton.onClick.AddListener(HandleBuyClicked);
        }
    }

    public void SetInteractable(bool interactable)
    {
        if (buyButton != null) buyButton.interactable = interactable;
    }

    private void HandleBuyClicked()
    {
        if (cached == null || onBuy == null) return;
        onBuy(cached);
    }

    private static decimal ToUsdc(BigInteger raw)
    {
        return (decimal)raw / 1_000_000m;
    }

    private static string ShortAddress(string addr)
    {
        if (string.IsNullOrEmpty(addr) || addr.Length < 12) return addr ?? "";
        return addr.Substring(0, 6) + "…" + addr.Substring(addr.Length - 4);
    }
}
