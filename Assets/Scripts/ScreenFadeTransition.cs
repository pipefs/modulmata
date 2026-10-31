using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

// Manipula la cobertura visual de pantalla completa utilizada por las transiciones globales.
[DefaultExecutionOrder(-1100)]
public class ScreenFadeTransition : MonoBehaviour
{
    [SerializeField, Range(0f, 1f)]
    private float initialAlpha;

    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private Image fadeImage;
    private Color originalFadeColor;
    private bool hasTemporaryColor;

    public bool IsReady => isActiveAndEnabled && canvasGroup != null && canvasGroup.gameObject.activeInHierarchy &&
        fadeImage != null && fadeImage.isActiveAndEnabled && fadeImage.transform.IsChildOf(canvasGroup.transform) &&
        canvasGroup.transform.root == transform.root;
    public float Alpha => canvasGroup != null ? canvasGroup.alpha : 0f;

    private void Awake()
    {
        if (canvasGroup == null)
        {
            canvasGroup = GetComponent<CanvasGroup>();
        }

        if (fadeImage == null && canvasGroup != null)
        {
            fadeImage = canvasGroup.GetComponentInChildren<Image>(true);
        }

        if (fadeImage != null)
        {
            fadeImage.raycastTarget = false;
        }

        if (canvasGroup != null)
        {
            canvasGroup.blocksRaycasts = false;
        }

        SetAlpha(initialAlpha);
    }

    // Interpola en tiempo no escalado para que las transiciones funcionen aunque el juego esté pausado.
    public IEnumerator FadeTo(float targetAlpha, float duration, Action<float> onProgress = null)
    {
        if (!IsReady)
        {
            onProgress?.Invoke(1f);
            yield break;
        }
        float startAlpha = canvasGroup.alpha;
        float elapsed = 0f;
        onProgress?.Invoke(0f);

        while (IsReady && elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            SetAlpha(Mathf.Lerp(startAlpha, targetAlpha, elapsed / duration));
            onProgress?.Invoke(Mathf.Clamp01(elapsed / duration));
            yield return null;
        }

        SetAlpha(IsReady ? targetAlpha : 0f);
        onProgress?.Invoke(1f);
    }

    // Normalmente empieza opaco; el menú puede empezar transparente para oscurecer después.
    // Aplica un color temporal y fija el punto de partida antes de revelar la pantalla.
    public bool PrepareFadeOut(Color color, float startAlpha = 1f)
    {
        if (!IsReady || fadeImage == null || !fadeImage.isActiveAndEnabled)
        {
            Debug.LogError("ScreenFadeTransition necesita un CanvasGroup activo y un Fade Image activo para el fundido.", this);
            return false;
        }
        CancelColorFadeOut();
        originalFadeColor = fadeImage.color;
        hasTemporaryColor = true;
        color.a = 1f;
        fadeImage.color = color;
        SetAlpha(startAlpha);
        return true;
    }

    public IEnumerator FadeOut(float duration, Action<float> onProgress = null)
    {
        yield return FadeTo(0f, Mathf.Max(0f, duration), onProgress);
        CancelColorFadeOut();
    }

    public void CancelColorFadeOut()
    {
        if (!hasTemporaryColor)
        {
            return;
        }

        SetAlpha(0f);
        if (fadeImage != null)
        {
            fadeImage.color = originalFadeColor;
        }

        hasTemporaryColor = false;
    }

    private void OnDisable()
    {
        CancelColorFadeOut();
        SetAlpha(0f);
    }

    public void SetAlpha(float alpha)
    {
        if (canvasGroup != null)
        {
            canvasGroup.alpha = Mathf.Clamp01(alpha);
        }
    }
}
