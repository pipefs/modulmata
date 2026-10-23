using TMPro;
using PrimeTween;
using UnityEngine;

[RequireComponent(typeof(BoxCollider2D))]
public class TransitionDoor : MonoBehaviour
{
    [SerializeField] private TransitionDoor destination;
    [SerializeField] private Vector2 exitOffset = new Vector2(2f, 0f);
    [SerializeField] private bool requiresSurfaceKey;
    [SerializeField] private bool activatesFinalCamera;

    [Header("Interacción")]
    [SerializeField] private TextMeshPro prompt;
    [SerializeField, Min(0f)] private float promptFadeDuration = 0.15f;
    [SerializeField, Min(0f)] private float promptPulseScale = 1.06f;
    [SerializeField, Min(0.01f)] private float promptPulseDuration = 0.65f;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip lockedSound;
    [SerializeField] private AudioClip transitionSound;

    private GameManager manager;
    private PlayerController nearbyPlayer;
    private Color promptColor;
    private Vector3 promptScale;
    private Coroutine promptFadeRoutine;
    private Tween promptPulseTween;

    private void Awake()
    {
        BoxCollider2D trigger = GetComponent<BoxCollider2D>();

        if (trigger == null)
            trigger = gameObject.AddComponent<BoxCollider2D>();

        trigger.isTrigger = true;

        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();

        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();

        if (prompt == null)
            prompt = GetComponentInChildren<TextMeshPro>(true);

        if (prompt == null)
        {
            enabled = false;
            return;
        }

        promptColor = prompt.color;
        promptScale = prompt.transform.localScale;
        SetPromptAlpha(0f);
        prompt.gameObject.SetActive(false);
        UpdatePrompt();
    }

    private void Start()
    {
        manager = GameManager.Instance;
    }

    private void Update()
    {
        if (nearbyPlayer != null)
            UpdatePrompt();

        if (nearbyPlayer == null || !nearbyPlayer.WasInteractPressedThisFrame())
            return;

        TryUseDoor();
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        PlayerController player = other.GetComponentInParent<PlayerController>();

        if (manager == null || manager.IsDead || manager.HasWon || player != manager.CurrentPlayer)
            return;

        nearbyPlayer = player;
        UpdatePrompt();
        ShowPrompt();
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (other.GetComponentInParent<PlayerController>() != nearbyPlayer)
            return;

        nearbyPlayer = null;
        HidePrompt();
    }

    private void TryUseDoor()
    {
        if (manager == null || destination == null || nearbyPlayer != manager.CurrentPlayer)
            return;

        if (requiresSurfaceKey && !manager.HasA3Key)
        {
            if (audioSource != null && lockedSound != null)
                audioSource.PlayOneShot(lockedSound);

            UpdatePrompt();
            return;
        }

        HidePrompt();
        nearbyPlayer = null;

        if (audioSource != null && transitionSound != null)
            audioSource.PlayOneShot(transitionSound);

        manager.BeginDoorTransition(
            destination.transform.position + (Vector3)destination.exitOffset,
            activatesFinalCamera);
    }

    private void ShowPrompt()
    {
        if (prompt == null)
            return;

        StopPromptAnimation();
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

    private System.Collections.IEnumerator FadePrompt(float targetAlpha, bool disableWhenFinished)
    {
        float initialAlpha = prompt.color.a;
        float elapsed = 0f;

        while (elapsed < promptFadeDuration)
        {
            elapsed += Time.deltaTime;
            SetPromptAlpha(Mathf.Lerp(initialAlpha, targetAlpha,
                promptFadeDuration <= 0f ? 1f : elapsed / promptFadeDuration));
            yield return null;
        }

        SetPromptAlpha(targetAlpha);

        if (disableWhenFinished)
            prompt.gameObject.SetActive(false);

        promptFadeRoutine = null;
    }

    private void SetPromptAlpha(float alpha)
    {
        if (prompt != null)
            prompt.color = new Color(promptColor.r, promptColor.g, promptColor.b, alpha);
    }

    private void StopPromptAnimation()
    {
        if (prompt == null)
            return;

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

    private void UpdatePrompt()
    {
        if (prompt == null)
            return;

        var manager = GameManager.Instance;
        bool locked = requiresSurfaceKey && (manager == null || !manager.HasA3Key);
        prompt.text = locked ? "<sprite name=\"Z\"> LLAVE REQUERIDA" : "<sprite name=\"Z\"> ENTRAR";
    }

    private void OnDisable()
    {
        StopPromptAnimation();
    }
}
