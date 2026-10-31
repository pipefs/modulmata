using System;
using PrimeTween;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class ButtonWrapper : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
{
    [SerializeField] private RectTransform visual;
    [SerializeField] private CanvasGroup visualGroup;
    private Vector2 originalPosition;
    private float originalAlpha;
    private Sequence buttonAnimation;
    private bool pointerInside;
    private bool selected;

    public Button button;
    public RectTransform buttonRect;

    public bool IsReady { get; private set; }
    public bool IsAvailable => IsReady && button != null && button.IsInteractable();
    public event Action<ButtonWrapper> FocusRequested;
    public event Action<ButtonWrapper> FocusLost;

    private void Awake()
    {
        button = GetComponent<Button>();
        buttonRect = GetComponent<RectTransform>();

        if (button == null || buttonRect == null || visual == null || visualGroup == null)
        {
            return;
        }

        if (visual == buttonRect || !visual.IsChildOf(buttonRect) || visualGroup.transform != visual)
        {
            return;
        }

        originalPosition = visual.anchoredPosition;
        originalAlpha = visualGroup.alpha;
        IsReady = true;
    }

    public void Animate(bool highlighted, bool dimmed, float offset, float fadedAlpha, float duration)
    {
        if (!IsReady || visual == null || visualGroup == null)
        {
            return;
        }

        if (buttonAnimation.isAlive)
        {
            buttonAnimation.Stop();
        }

        if (!isActiveAndEnabled)
        {
            ResetVisual();
            return;
        }

        float x = originalPosition.x + (highlighted ? offset : 0f);
        float alpha = originalAlpha * (dimmed ? Mathf.Clamp01(fadedAlpha) : 1f);

        if (!(duration > 0f))
        {
            visual.anchoredPosition = new Vector2(x, originalPosition.y);
            visualGroup.alpha = alpha;
            return;
        }

        buttonAnimation = Sequence.Create(useUnscaledTime: true)
            .Group(Tween.UIAnchoredPositionX(visual, x, duration, ease: Ease.OutQuad))
            .Group(Tween.Alpha(visualGroup, alpha, duration, ease: Ease.OutQuad));
    }

    public void ResetVisual()
    {
        if (buttonAnimation.isAlive)
        {
            buttonAnimation.Stop();
        }

        if (!IsReady)
        {
            return;
        }

        if (visual != null)
        {
            visual.anchoredPosition = originalPosition;
        }

        if (visualGroup != null)
        {
            visualGroup.alpha = originalAlpha;
        }

        pointerInside = selected = false;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!IsAvailable)
        {
            return;
        }

        pointerInside = true;
        FocusRequested?.Invoke(this);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        pointerInside = false;

        if (!selected)
        {
            FocusLost?.Invoke(this);
        }
    }

    public void OnSelect(BaseEventData eventData)
    {
        selected = true;

        if (IsAvailable)
        {
            FocusRequested?.Invoke(this);
        }
    }

    public void OnDeselect(BaseEventData eventData)
    {
        selected = false;

        if (!pointerInside)
        {
            FocusLost?.Invoke(this);
        }
    }

    private void OnDisable()
    {
        ResetVisual();
        FocusLost?.Invoke(this);
    }
}
