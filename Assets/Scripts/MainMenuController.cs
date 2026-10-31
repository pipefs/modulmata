using System;
using System.Collections.Generic;
using PrimeTween;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Coordina entrada, navegación animada, audio y cambio de escena desde el menú principal.
public sealed class MainMenuController : MonoBehaviour
{
    [SerializeField] private CanvasGroup menuGroup;
    [SerializeField] private Button playButton;
    [SerializeField] private Button quitButton;
    [SerializeField] private string gameplayScene = "Main";
    [SerializeField] private Transform robotVisual;
    [SerializeField] private AudioSource musicSource;
    
    [Header("Presentación de botones")]
    [SerializeField] private ButtonWrapper[] animatedButtons;
    [SerializeField, Min(0f)] private float selectionOffset = 4f;
    [SerializeField, Range(0f, 1f)] private float fadedButtonAlpha = 0.65f;
    [SerializeField, Min(0f)] private float selectionDuration = 0.15f;
    [SerializeField] private RectTransform arrowIndicator;
    [SerializeField] private CanvasGroup arrowGroup;
    [SerializeField] private Vector2 arrowOffset = new Vector2(-12f, 0f);
    [SerializeField, Min(0f)] private float arrowDuration = 0.15f;
    
    [Header("Sonidos de botones")]
    [SerializeField] private AudioSource uiAudioSource;
    [SerializeField] private AudioClip selectionSound;
    [SerializeField] private AudioClip confirmationSound;

    [Header("Cambio de Sprite")]
    [SerializeField] private SpriteRenderer headRenderer;
    [SerializeField] private Sprite spriteToChangeTo;

    private readonly List<ButtonWrapper> buttons = new();
    private ButtonWrapper highlightedButton;
    private Sequence arrowAnimation;
    private Vector2 originalArrowPosition;
    private bool arrowReady;
    private bool hasArrowPosition;
    private bool presentationBound;
    private bool started;
    private bool quitting;
    private float volumeBeforePlay;
    private bool fadingMusic;
    private bool loading;
    private bool IsMenuAvailable => isActiveAndEnabled && !loading && !quitting &&
        SceneLoader.Instance != null && SceneLoader.Instance.isActiveAndEnabled &&
        menuGroup != null && menuGroup.interactable;

    private void Awake()
    {
        if (menuGroup == null || playButton == null)
        {
            Fail("MainMenuController necesita Menu Group y Play Button asignados.");
            return;
        }

        playButton.interactable = false;
        playButton.onClick.AddListener(Play);
        if (quitButton != null)
        {
            quitButton.onClick.AddListener(Quit);
        }

        if (robotVisual != null)
        {
            Animator animator = robotVisual.GetComponent<Animator>();
            if (animator != null)
            {
                animator.Play("MenuFalling", 0, 0f);
                animator.Update(0f);
            }
        }
    }

    private void Start()
    {
        if (SceneLoader.Instance == null)
        {
            Fail("MainMenuController necesita un SceneLoader persistente activo.");
            return;
        }
        started = true;
        BindPresentation();
        if (musicSource != null && musicSource.clip != null && !musicSource.isPlaying)
        {
            musicSource.Play();
        }

        SetMenuAvailable(true);
    }

    private void OnEnable()
    {
        // Awake de los wrappers ya ocurrió cuando se reactiva este controlador.
        if (!started)
        {
            return;
        }

        BindPresentation();
        SetMenuAvailable(!loading);
    }

    // Bloquea nuevas entradas y enlaza el fundido musical al progreso de cobertura de la escena.
    public void Play()
    {
        if (!IsMenuAvailable || SceneLoader.Instance.IsLoading)
        {
            return;
        }

        loading = true;
        volumeBeforePlay = musicSource != null ? musicSource.volume : 0f;
        fadingMusic = musicSource != null;

        if (headRenderer != null && spriteToChangeTo != null)
        {
            headRenderer.sprite = spriteToChangeTo;
        }

        SetMenuAvailable(false);

        if (SceneLoader.Instance.Load(gameplayScene, SetMusicFadeProgress, RestoreAfterFailedLoad))
        {
            PlayUISound(confirmationSound);
        }
        else
        {
            RestoreAfterFailedLoad();
        }
    }

    private void RestoreAfterFailedLoad()
    {
        if (this == null)
        {
            return;
        }

        if (fadingMusic && musicSource != null)
        {
            musicSource.volume = volumeBeforePlay;
        }

        fadingMusic = false;
        loading = false;
        if (isActiveAndEnabled)
        {
            SetMenuAvailable(true);
        }
    }

    private void SetMusicFadeProgress(float progress)
    {
        if (this != null && musicSource != null)
        {
            musicSource.volume = Mathf.Lerp(volumeBeforePlay, 0f, progress);
        }
    }

    private void SetMenuAvailable(bool available)
    {
        if (menuGroup != null)
        {
            menuGroup.interactable = available;
            menuGroup.blocksRaycasts = available;
        }
        if (playButton != null)
        {
            playButton.interactable = available;
        }

        if (quitButton != null)
        {
            quitButton.interactable = available;
        }

        if (available)
        {
            SelectInitialButton();
        }
        else
        {
            ResetPresentation();
        }
    }

    // Reúne los wrappers válidos y conecta ratón, teclado y botones con una única selección visual.
    private void BindPresentation()
    {
        if (presentationBound)
        {
            return;
        }

        presentationBound = true;
        if (animatedButtons != null)
        {
            foreach (var wrapper in animatedButtons)
            {
                if (wrapper == null || !wrapper.IsReady || !wrapper.transform.IsChildOf(menuGroup.transform))
                {
                    Debug.LogWarning("Cada Animated Button debe tener un ButtonWrapper configurado dentro de Menu Group. Se omite esa entrada.", this);
                    continue;
                }
                if (buttons.Contains(wrapper))
                {
                    continue;
                }

                buttons.Add(wrapper);
                wrapper.FocusRequested += RequestFocus;
                wrapper.FocusLost += LoseFocus;
            }
        }

        arrowReady = arrowIndicator != null && arrowGroup != null &&
            arrowGroup.transform == arrowIndicator && arrowIndicator.parent is RectTransform;
        if (arrowReady)
        {
            originalArrowPosition = arrowIndicator.anchoredPosition;
            arrowGroup.alpha = 0f;
        }
        else if (arrowIndicator != null || arrowGroup != null)
        {
            Debug.LogWarning("La flecha necesita RectTransform y CanvasGroup en el mismo objeto, con un padre RectTransform. Se omite su animación.", this);
        }
    }

    private void SelectInitialButton()
    {
        foreach (var wrapper in buttons)
        {
            if (wrapper != null && wrapper.button == playButton && wrapper.IsAvailable)
            {
                SelectButton(wrapper, false);
                return;
            }
        }

        foreach (var wrapper in buttons)
        {
            if (wrapper != null && wrapper.IsAvailable)
            {
                SelectButton(wrapper, false);
                return;
            }
        }

        if (EventSystem.current != null && playButton != null && playButton.IsInteractable())
        {
            EventSystem.current.SetSelectedGameObject(playButton.gameObject);
        }
    }

    private void RequestFocus(ButtonWrapper wrapper)
    {
        SelectButton(wrapper, true);
    }

    // Sincroniza el elemento resaltado con EventSystem incluso cuando la animación ya estaba aplicada.
    private void SelectButton(ButtonWrapper wrapper, bool playSound)
    {
        if (!IsMenuAvailable || wrapper == null || !wrapper.IsAvailable || !buttons.Contains(wrapper))
        {
            return;
        }

        bool changed = highlightedButton != wrapper;
        if (changed)
        {
            highlightedButton = wrapper;
            RefreshButtons();
            MoveArrow(wrapper);
        }
        // El resaltado inicial puede existir antes de que el EventSystem tenga la selección.
        if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject != wrapper.button.gameObject)
        {
            EventSystem.current.SetSelectedGameObject(wrapper.button.gameObject);
        }

        if (changed && playSound)
        {
            PlayUISound(selectionSound);
        }
    }

    private void LoseFocus(ButtonWrapper wrapper)
    {
        if (!IsMenuAvailable || highlightedButton != wrapper)
        {
            return;
        }

        highlightedButton = null;
        RefreshButtons();
        if (arrowAnimation.isAlive)
        {
            arrowAnimation.Stop();
        }

        if (!arrowReady || arrowGroup == null)
        {
            return;
        }

        if (arrowDuration <= 0f)
        {
            arrowGroup.alpha = 0f;
        }
        else
        {
            arrowAnimation = Sequence.Create(useUnscaledTime: true)
            .Group(Tween.Alpha(arrowGroup, 0f, arrowDuration, ease: Ease.OutQuad));
        }
    }

    private void RefreshButtons()
    {
        foreach (var wrapper in buttons)
        {
            if (wrapper != null)
            {
                wrapper.Animate(wrapper == highlightedButton, highlightedButton != null && wrapper != highlightedButton,
                    selectionOffset, fadedButtonAlpha, selectionDuration);
            }
        }
    }

    private void MoveArrow(ButtonWrapper wrapper)
    {
        if (!arrowReady || arrowIndicator == null || arrowGroup == null)
        {
            return;
        }

        var parent = arrowIndicator.parent as RectTransform;
        if (parent == null)
        {
            return;
        }

        Rect rect = wrapper.buttonRect.rect;
        Vector3 world = wrapper.buttonRect.TransformPoint(new Vector3(rect.xMin, rect.center.y, 0f));
        Vector2 local = parent.InverseTransformPoint(world);
        // Convertir al espacio de la flecha, incluyendo anclajes y pivot distintos.
        Vector2 anchor = new Vector2(
            Mathf.Lerp(arrowIndicator.anchorMin.x, arrowIndicator.anchorMax.x, arrowIndicator.pivot.x),
            Mathf.Lerp(arrowIndicator.anchorMin.y, arrowIndicator.anchorMax.y, arrowIndicator.pivot.y));
        Vector2 target = local + arrowOffset - (parent.rect.min + Vector2.Scale(parent.rect.size, anchor));
        if (arrowAnimation.isAlive)
        {
            arrowAnimation.Stop();
        }

        if (!hasArrowPosition || arrowDuration <= 0f)
        {
            arrowIndicator.anchoredPosition = target;
        }

        hasArrowPosition = true;
        if (arrowDuration <= 0f)
        {
            arrowGroup.alpha = 1f;
            return;
        }
        arrowAnimation = Sequence.Create(useUnscaledTime: true)
            .Group(Tween.UIAnchoredPosition(arrowIndicator, target, arrowDuration, ease: Ease.OutQuad))
            .Group(Tween.Alpha(arrowGroup, 1f, arrowDuration, ease: Ease.OutQuad));
    }

    // Devuelve botones y flecha a su pose original al abandonar o desactivar el menú.
    private void ResetPresentation()
    {
        highlightedButton = null;
        if (arrowAnimation.isAlive)
        {
            arrowAnimation.Stop();
        }

        if (arrowReady && arrowIndicator != null && arrowGroup != null)
        {
            arrowIndicator.anchoredPosition = originalArrowPosition;
            arrowGroup.alpha = 0f;
        }
        hasArrowPosition = false;
        var events = EventSystem.current;
        foreach (var wrapper in buttons)
        {
            if (wrapper != null)
            {
                if (events != null && wrapper.button != null && events.currentSelectedGameObject == wrapper.button.gameObject)
                {
                    events.SetSelectedGameObject(null);
                }

                wrapper.ResetVisual();
            }
        }
    }

    private void UnbindPresentation()
    {
        foreach (var wrapper in buttons)
        {
            if (!ReferenceEquals(wrapper, null))
            {
                wrapper.FocusRequested -= RequestFocus;
                wrapper.FocusLost -= LoseFocus;
            }
        }

        buttons.Clear();
        presentationBound = false;
    }

    private void PlayUISound(AudioClip clip)
    {
        if (clip != null && uiAudioSource != null && uiAudioSource.isActiveAndEnabled)
        {
            uiAudioSource.PlayOneShot(clip);
        }
    }

    private void Fail(string message)
    {
        Debug.LogError(message, this);
        SetMenuAvailable(false);
        enabled = false;
    }

    public void Quit()
    {
        if (!IsMenuAvailable)
        {
            return;
        }

        quitting = true;
        PlayUISound(confirmationSound);
        SetMenuAvailable(false);
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void OnDisable()
    {
        StopAllCoroutines();
        if (!loading)
        {
            loading = false;
            if (fadingMusic)
            {
                SetMusicFadeProgress(0f);
            }

            fadingMusic = false;
        }
        SetMenuAvailable(false);
        UnbindPresentation();
    }

    private void OnDestroy()
    {
        if (playButton != null)
        {
            playButton.onClick.RemoveListener(Play);
        }

        if (quitButton != null)
        {
            quitButton.onClick.RemoveListener(Quit);
        }

        UnbindPresentation();
    }
}
