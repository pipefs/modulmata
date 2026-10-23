using System.Collections;
using UnityEngine;

public class ScreenFadeTransition : MonoBehaviour
{
    [SerializeField, Min(0.01f)] private float fadeOutDuration = 0.08f;
    [SerializeField, Min(0f)] private float blackHoldDuration = 0.08f;
    [SerializeField, Min(0.01f)] private float fadeInDuration = 0.35f;

    [SerializeField] private CanvasGroup canvasGroup;

    public float FadeOutDuration => fadeOutDuration;
    public float BlackHoldDuration => blackHoldDuration;
    public float FadeInDuration => fadeInDuration;

    private void Awake()
    {
        SetAlpha(0f);
    }

    public IEnumerator FadeTo(float targetAlpha, float duration)
    {
        float startAlpha = canvasGroup.alpha;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            SetAlpha(Mathf.Lerp(startAlpha, targetAlpha, elapsed / duration));
            yield return null;
        }

        SetAlpha(targetAlpha);
    }

    public void SetAlpha(float alpha)
    {
        if (canvasGroup != null)
            canvasGroup.alpha = Mathf.Clamp01(alpha);
    }
}
