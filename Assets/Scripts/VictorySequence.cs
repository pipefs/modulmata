using System.Collections;
using TMPro;
using PrimeTween;
using UnityEngine;

public class VictorySequence : MonoBehaviour
{
    [SerializeField] private TextMeshPro prompt;
    [SerializeField, Min(0f)] private float promptFadeDuration = 0.15f;
    [SerializeField, Min(0f)] private float promptPulseScale = 1.06f;
    [SerializeField, Min(0.01f)] private float promptPulseDuration = 0.65f;

    [SerializeField] private Transform boardingPoint;
    [SerializeField, Min(0f)] private float ascentHeight = 8f;
    [SerializeField, Min(0.01f)] private float ascentDuration = 2.5f;
    [SerializeField] private AnimationCurve ascentCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    private GameManager manager;
    private PlayerController nearbyPlayer;
    private Color promptColor;
    private Vector3 promptScale;
    private Coroutine promptFadeRoutine;
    private Tween promptPulseTween;
    private bool sequenceStarted;

    private void Awake()
    {
        BoxCollider2D trigger = GetComponent<BoxCollider2D>();
        trigger.isTrigger = true;

        if (prompt == null)
            prompt = GetComponentInChildren<TextMeshPro>(true);

        if (prompt == null)
        {
            Debug.LogError("FinalVictorySequence necesita un TextMeshPro hijo como indicador.", this);
            enabled = false;
            return;
        }

        promptColor = prompt.color;
        promptScale = prompt.transform.localScale;
        SetPromptAlpha(0f);
        prompt.gameObject.SetActive(false);
    }

    private void Start()
    {
        manager = GameManager.Instance;
    }

    private void Update()
    {
        if (sequenceStarted || nearbyPlayer == null || !nearbyPlayer.WasInteractPressedThisFrame())
            return;

        StartFinalSequence();
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        PlayerController player = other.GetComponentInParent<PlayerController>();

        if (sequenceStarted || manager == null || manager.IsDead || manager.HasWon ||  player == null || player != manager.CurrentPlayer)
            return;

        nearbyPlayer = player;
        ShowPrompt();
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (other.GetComponentInParent<PlayerController>() != nearbyPlayer)
            return;

        nearbyPlayer = null;
        HidePrompt();
    }

    private void StartFinalSequence()
    {
        if (manager == null || nearbyPlayer == null || nearbyPlayer != manager.CurrentPlayer)
            return;

        sequenceStarted = true;
        HidePrompt();
        manager.Win();
        StartCoroutine(Ascend(nearbyPlayer));
        nearbyPlayer = null;
    }

    private IEnumerator Ascend(PlayerController player)
    {
        player.SetControlLock(true);
        player.animator.Play("Jugador_JumpMax");

        Rigidbody2D body = player.GetComponent<Rigidbody2D>();

        if (body != null)
        {
            body.linearVelocity = Vector2.zero;
            body.simulated = false;
        }

        Vector3 playerStart = boardingPoint.position;
        Vector3 ascent = Vector3.up * ascentHeight;

        player.transform.position = playerStart;

        float elapsed = 0f;
        while (elapsed < ascentDuration)
        {
            elapsed += Time.deltaTime;
            float progress = Mathf.Clamp01(elapsed / ascentDuration);
            float curvedProgress = ascentCurve.Evaluate(progress);

            player.transform.position = playerStart + ascent * curvedProgress;

            yield return null;
        }

        manager.VictoryFadeIn();
        player.transform.position = playerStart + ascent;
    }

    private void ShowPrompt()
    {
        StopPromptAnimation();
        prompt.text = "<sprite name=\"Z\"> ASCENDER";
        prompt.gameObject.SetActive(true);
        StartPromptFade(1f, false);
        promptPulseTween = Tween.Scale(
            target: prompt.transform,
            endValue: promptScale * promptPulseScale,
            duration: promptPulseDuration,
            ease: Ease.InOutSine,
            cycles: -1,
            cycleMode: CycleMode.Yoyo);
    }

    private void HidePrompt()
    {
        if (prompt == null || !prompt.gameObject.activeSelf)
            return;

        StopPromptPulse();
        StartPromptFade(0f, true);
    }

    private void StartPromptFade(float targetAlpha, bool disableWhenFinished)
    {
        if (promptFadeRoutine != null)
            StopCoroutine(promptFadeRoutine);

        promptFadeRoutine = StartCoroutine(FadePrompt(targetAlpha, disableWhenFinished));
    }

    private IEnumerator FadePrompt(float targetAlpha, bool disableWhenFinished)
    {
        float initialAlpha = prompt.color.a;
        float elapsed = 0f;

        while (elapsed < promptFadeDuration)
        {
            elapsed += Time.deltaTime;
            float progress = promptFadeDuration <= 0f ? 1f : elapsed / promptFadeDuration;
            SetPromptAlpha(Mathf.Lerp(initialAlpha, targetAlpha, progress));
            yield return null;
        }

        SetPromptAlpha(targetAlpha);

        if (disableWhenFinished)
            prompt.gameObject.SetActive(false);

        promptFadeRoutine = null;
    }

    private void SetPromptAlpha(float alpha)
    {
        prompt.color = new Color(promptColor.r, promptColor.g, promptColor.b, alpha);
    }

    private void StopPromptAnimation()
    {
        if (promptFadeRoutine != null)
            StopCoroutine(promptFadeRoutine);

        StopPromptPulse();
        prompt.transform.localScale = promptScale;
    }

    private void StopPromptPulse()
    {
        if (promptPulseTween.isAlive)
            promptPulseTween.Stop();

        if (prompt != null)
            prompt.transform.localScale = promptScale;
    }

    private void OnDisable()
    {
        if (prompt != null)
            StopPromptAnimation();
    }
}
