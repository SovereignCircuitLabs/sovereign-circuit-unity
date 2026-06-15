using System;
using System.Numerics;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Binds one owned-NPC entry onto a row prefab in the "List my NPCs for sale"
/// menu. The prefab is built in the Editor (background, labels, input field,
/// button) and wired through the [SerializeField] refs below. The controller
/// calls Bind(info, onList, onCancel).
///
/// Right-side action is a single Button that flips meaning based on listing
/// state:
///   - not listed → "List for Sell" → invokes onList(info, minPrice)
///   - listed     → "Cancel Listing" → invokes onCancel(info)
/// </summary>
public class NpcListingRow : MonoBehaviour
{
    [Header("Labels")]
    [SerializeField] private Text tokenIdLabel;
    [SerializeField] private Text nameLabel;
    [SerializeField] private Text archetypeLabel;
    [SerializeField] private Text quotedPriceLabel;
    [SerializeField] private Text tbaValueLabel;
    [SerializeField] private Text listingStatusLabel;

    [Header("Min price input")]
    [Tooltip("USDC value (decimal, e.g. 12.5) the seller is willing to accept. " +
             "Pre-filled with the current on-chain quote on Bind.")]
    [SerializeField] private InputField minPriceInput;

    [Header("Action")]
    [SerializeField] private Button actionButton;
    [SerializeField] private Text actionButtonLabel;
    [SerializeField] private string listLabelTemplate = "List for Sell";
    [SerializeField] private string cancelLabelTemplate = "Cancel Listing";

    public Button ActionButton => actionButton;

    private OwnedNpcSellableInfo cached;
    private Action<OwnedNpcSellableInfo, BigInteger> onList;
    private Action<OwnedNpcSellableInfo> onCancel;

    public void Bind(
        OwnedNpcSellableInfo info,
        Action<OwnedNpcSellableInfo, BigInteger> onListClicked,
        Action<OwnedNpcSellableInfo> onCancelClicked)
    {
        cached = info;
        onList = onListClicked;
        onCancel = onCancelClicked;

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

        decimal quotedUsdc = ToUsdc(info.QuotedPrice);
        decimal tbaValueUsdc = ToUsdc(info.TbaTotalValue);

        if (quotedPriceLabel != null)
            quotedPriceLabel.text = $"{quotedUsdc:0.######} USDC";

        if (tbaValueLabel != null)
            tbaValueLabel.text = $"{tbaValueUsdc:0.######} USDC";

        if (listingStatusLabel != null)
        {
            listingStatusLabel.text = info.IsListed
                ? $"Listed @ {ToUsdc(info.ListedMinPrice):0.######} USDC min"
                : "Not listed";
        }

        // Pre-fill the input with the live quote so the player gets a sane default
        // they can override. If the NPC is already listed, show the existing minPrice
        // instead so they see what they previously committed to.
        if (minPriceInput != null)
        {
            decimal defaultValue = info.IsListed ? ToUsdc(info.ListedMinPrice) : quotedUsdc;
            minPriceInput.text = defaultValue.ToString("0.######");
            minPriceInput.interactable = !info.IsListed;
        }

        if (actionButtonLabel != null)
            actionButtonLabel.text = info.IsListed ? cancelLabelTemplate : listLabelTemplate;

        if (actionButton != null)
        {
            actionButton.onClick.RemoveAllListeners();
            actionButton.onClick.AddListener(HandleActionClicked);
        }
    }

    public void SetInteractable(bool interactable)
    {
        if (actionButton != null) actionButton.interactable = interactable;
        if (minPriceInput != null && cached != null && !cached.IsListed)
            minPriceInput.interactable = interactable;
    }

    private void HandleActionClicked()
    {
        if (cached == null) return;

        if (cached.IsListed)
        {
            onCancel?.Invoke(cached);
            return;
        }

        if (!TryReadMinPriceRaw(out var minPriceRaw))
        {
            Debug.LogWarning(
                $"[NpcListingRow] tokenId={cached.TokenId} — invalid min price input " +
                $"'{(minPriceInput != null ? minPriceInput.text : "<null>")}'.");
            return;
        }

        onList?.Invoke(cached, minPriceRaw);
    }

    /// <summary>
    /// Parse the input field as a decimal USDC value and convert to 6-decimal
    /// uint256 raw units. Returns false on empty / unparseable / negative input.
    /// </summary>
    private bool TryReadMinPriceRaw(out BigInteger raw)
    {
        raw = BigInteger.Zero;
        if (minPriceInput == null) return false;

        var text = minPriceInput.text;
        if (string.IsNullOrWhiteSpace(text)) return false;

        if (!decimal.TryParse(
                text,
                System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture,
                out var usdc))
        {
            return false;
        }

        if (usdc < 0) return false;

        // 6-decimal USDC, round half-up to nearest base unit.
        decimal scaled = decimal.Round(usdc * 1_000_000m, 0, MidpointRounding.AwayFromZero);
        raw = new BigInteger(scaled);
        return true;
    }

    private static decimal ToUsdc(BigInteger rawValue)
    {
        return (decimal)rawValue / 1_000_000m;
    }
}
