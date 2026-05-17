using UnityEngine;
using UnityEngine.EventSystems;

[RequireComponent(typeof(RectTransform))]
public class UIDraggable : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [Header("Drag")]
    [SerializeField] private bool draggable = true;
    [SerializeField] private bool clampToParent = true;
    [SerializeField] private bool bringToFrontOnDrag = true;

    private RectTransform rectTransform;
    private RectTransform parentRectTransform;
    private Canvas rootCanvas;
    private Camera eventCamera;
    private Vector2 startPointerPosition;
    private Vector2 startAnchoredPosition;
    private bool isDragging;

    public bool Draggable
    {
        get { return draggable; }
        set { draggable = value; }
    }

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        parentRectTransform = rectTransform.parent as RectTransform;
        rootCanvas = GetComponentInParent<Canvas>();
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (!draggable || parentRectTransform == null)
        {
            return;
        }

        eventCamera = GetEventCamera(eventData);

        Vector2 parentLocalPointerPosition;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                parentRectTransform,
                eventData.position,
                eventCamera,
                out parentLocalPointerPosition))
        {
            return;
        }

        startPointerPosition = parentLocalPointerPosition;
        startAnchoredPosition = rectTransform.anchoredPosition;
        isDragging = true;

        if (bringToFrontOnDrag)
        {
            rectTransform.SetAsLastSibling();
        }
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!isDragging || parentRectTransform == null)
        {
            return;
        }

        Vector2 parentLocalPointerPosition;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                parentRectTransform,
                eventData.position,
                eventCamera,
                out parentLocalPointerPosition))
        {
            return;
        }

        Vector2 targetPosition = startAnchoredPosition + parentLocalPointerPosition - startPointerPosition;

        if (clampToParent)
        {
            targetPosition = ClampToParent(targetPosition);
        }

        rectTransform.anchoredPosition = targetPosition;
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        isDragging = false;
    }

    private Camera GetEventCamera(PointerEventData eventData)
    {
        if (rootCanvas != null && rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay)
        {
            return null;
        }

        if (eventData.pressEventCamera != null)
        {
            return eventData.pressEventCamera;
        }

        return rootCanvas != null ? rootCanvas.worldCamera : Camera.main;
    }

    private Vector2 ClampToParent(Vector2 targetPosition)
    {
        Rect parentRect = parentRectTransform.rect;
        Rect rect = rectTransform.rect;
        Vector2 anchorReference = GetAnchorReferencePoint(parentRect);
        Vector2 targetPivotPosition = anchorReference + targetPosition;

        Vector2 min = parentRect.min - new Vector2(rect.xMin, rect.yMin);
        Vector2 max = parentRect.max - new Vector2(rect.xMax, rect.yMax);

        Vector2 clampedPivotPosition = new Vector2(
            Mathf.Clamp(targetPivotPosition.x, min.x, max.x),
            Mathf.Clamp(targetPivotPosition.y, min.y, max.y));

        return clampedPivotPosition - anchorReference;
    }

    private Vector2 GetAnchorReferencePoint(Rect parentRect)
    {
        Vector2 anchor = new Vector2(
            Mathf.Lerp(rectTransform.anchorMin.x, rectTransform.anchorMax.x, rectTransform.pivot.x),
            Mathf.Lerp(rectTransform.anchorMin.y, rectTransform.anchorMax.y, rectTransform.pivot.y));

        return parentRect.min + Vector2.Scale(parentRect.size, anchor);
    }
}
