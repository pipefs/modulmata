using System.Collections;
using TMPro;
using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class WorldTutorialHint : MonoBehaviour
{
    [SerializeField] private TextMeshPro hintText;
    [SerializeField, Min(0f)] private float fadeInDuration = 0.2f;
    [SerializeField, Min(0f)] private float visibleDuration = 3.5f;
    [SerializeField, Min(0f)] private float fadeOutDuration = 0.35f;
    [SerializeField] private bool showOnlyOnce = true;

    private GameManager manager;
    private Color textColor;
    private bool shown;
    private Coroutine displayRoutine;

    private void Awake()
    {
        Collider2D trigger = GetComponent<Collider2D>();
        trigger.isTrigger = true;

        if (hintText == null)
            hintText = GetComponentInChildren<TextMeshPro>(true);

        if (hintText == null)
        {
            enabled = false;
            return;
        }

        textColor = hintText.color;
        SetAlpha(0f);
        hintText.gameObject.SetActive(false);
    }

    private void Start()
    {
        manager = GameManager.Instance;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        PlayerController player = other.GetComponentInParent<PlayerController>();

        if (manager == null || manager.IsDead || manager.HasWon || player == null || player != manager.CurrentPlayer || (showOnlyOnce && shown))
            return;

        shown = true;

        if (displayRoutine != null)
            StopCoroutine(displayRoutine);

        displayRoutine = StartCoroutine(DisplayHint());
    }

    private IEnumerator DisplayHint()
    {
        hintText.gameObject.SetActive(true);
        yield return FadeTo(1f, fadeInDuration);
        yield return new WaitForSeconds(visibleDuration);
        yield return FadeTo(0f, fadeOutDuration);
        hintText.gameObject.SetActive(false);
        displayRoutine = null;
    }

    private IEnumerator FadeTo(float targetAlpha, float duration)
    {
        float initialAlpha = hintText.color.a;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            SetAlpha(Mathf.Lerp(initialAlpha, targetAlpha,
                duration <= 0f ? 1f : elapsed / duration));
            yield return null;
        }

        SetAlpha(targetAlpha);
    }

    private void SetAlpha(float alpha) => hintText.color = new Color(textColor.r, textColor.g, textColor.b, alpha);
}
