using System.Collections;
using TMPro;
using UnityEngine;

// Muestra un mensaje contextual al jugador y administra sus fundidos y repetición.
[RequireComponent(typeof(Collider2D))]
public class WorldTutorialHint : MonoBehaviour
{
    [SerializeField] private TextMeshPro hintText;
    [SerializeField, Min(0f)] private float fadeInDuration = 0.2f;
    [SerializeField, Min(0f)] private float visibleDuration = 3.5f;
    [SerializeField, Min(0f)] private float fadeOutDuration = 0.35f;
    [SerializeField] private bool showOnlyOnce = true;

    private Color textColor;
    private bool shown;
    private Coroutine displayRoutine;

    private void Awake()
    {
        Collider2D trigger = GetComponent<Collider2D>();
        if (trigger == null)
        {
            Debug.LogError("WorldTutorialHint necesita un Collider2D.", this);
            enabled = false;
            return;
        }
        trigger.isTrigger = true;

        if (hintText == null)
        {
            hintText = GetComponentInChildren<TextMeshPro>(true);
        }

        if (hintText == null)
        {
            Debug.LogWarning("WorldTutorialHint no tiene texto asignado.", this);
            enabled = false;
            return;
        }

        textColor = hintText.color;
        SetAlpha(0f);
        hintText.gameObject.SetActive(false);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!isActiveAndEnabled || hintText == null || (showOnlyOnce && shown))
        {
            return;
        }

        PlayerController player = other.GetComponentInParent<PlayerController>();
        GameManager manager = GameManager.Instance;
        if (player == null || !player.IsInitialized || !player.isActiveAndEnabled ||
            manager == null || !manager.isActiveAndEnabled || manager.CurrentPlayer != player ||
            manager.IsDead || manager.HasWon)
        {
            return;
        }

        shown = true;

        if (displayRoutine != null)
        {
            StopCoroutine(displayRoutine);
        }

        displayRoutine = StartCoroutine(DisplayHint());
    }

    // Encadena entrada, permanencia y salida del mensaje sin bloquear el control del jugador.
    private IEnumerator DisplayHint()
    {
        if (hintText == null)
        {
            yield break;
        }

        hintText.gameObject.SetActive(true);
        yield return FadeTo(1f, fadeInDuration);
        yield return new WaitForSeconds(visibleDuration);
        yield return FadeTo(0f, fadeOutDuration);
        if (hintText != null)
        {
            hintText.gameObject.SetActive(false);
        }

        displayRoutine = null;
    }

    private IEnumerator FadeTo(float targetAlpha, float duration)
    {
        if (hintText == null)
        {
            yield break;
        }

        float initialAlpha = hintText.color.a;
        float elapsed = 0f;

        while (hintText != null && elapsed < duration)
        {
            elapsed += Time.deltaTime;
            SetAlpha(Mathf.Lerp(initialAlpha, targetAlpha,
                duration <= 0f ? 1f : elapsed / duration));
            yield return null;
        }

        SetAlpha(targetAlpha);
    }

    private void OnDisable()
    {
        if (displayRoutine != null)
        {
            StopCoroutine(displayRoutine);
        }

        displayRoutine = null;
        if (hintText != null)
        {
            SetAlpha(0f);
            hintText.gameObject.SetActive(false);
        }
    }

    private void SetAlpha(float alpha)
    {
        if (hintText != null)
        {
            hintText.color = new Color(textColor.r, textColor.g, textColor.b, alpha);
        }
    }
}
