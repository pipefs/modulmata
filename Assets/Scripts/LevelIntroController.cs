using System;
using System.Collections;
using PrimeTween;
using UnityEngine;

// Bloquea al jugador durante la caída inicial y entrega el control después de aterrizar y revelar el HUD.
[DisallowMultipleComponent]
public sealed class LevelIntroController : MonoBehaviour
{
    [Header("Ref")]
    [SerializeField] private CanvasGroup hudGroup;

    [Header("Tiempos")]
    [SerializeField, Min(0f)] private float hudFadeDuration = 0.4f;
    [SerializeField] private float landingDuration = 0.4f;

    private PlayerController player;
    private Tween hudFadeTween;
    private float originalAlpha;
    private bool originalInteractable;
    private bool originalBlocksRaycasts;
    private bool startsGrounded;

    public bool IsComplete { get; private set; }

    private void Awake()
    {
        if (hudGroup == null)
        {
            Debug.LogError("[ERROR] Asigna HUD Group en el inspector.");
            return;
        }

        originalAlpha = hudGroup.alpha;
        originalInteractable = hudGroup.interactable;
        originalBlocksRaycasts = hudGroup.blocksRaycasts;

        hudGroup.alpha = 0f;
        hudGroup.interactable = false;
        hudGroup.blocksRaycasts = false;
    }

    // Captura al jugador inicial, bloquea sus acciones y elige la animación según su apoyo actual.
    public bool Prepare(PlayerController initialPlayer)
    {
        if (initialPlayer == null || !initialPlayer.IsInitialized)
        {
            Debug.LogError("[ERROR] No se pudo encontrar al jugador o no esta inicializado.");
            return false;
        }

        player = initialPlayer;

        // Nerf inicial
        BatterySystem bs = player.GetComponent<BatterySystem>();
        
        if (bs != null)
        {
            bs.ReduceDurability(50f);
        }

        player.SetControlLock(PlayerControlLock.Intro, true);
        player.CheckGrounded();

        startsGrounded = player.IsGrounded;
        player.PlayIntroAnimation(startsGrounded ? "Idle" : "JumpDown");

        return true;
    }

    // Espera el primer aterrizaje, entrega la cámara mediante el callback y revela el HUD antes de desbloquear.
    public IEnumerator Play(Action OnLanded)
    {
        if (!CanContinue())
        {
            yield break;
        }

        if (!startsGrounded)
        {
            while (CanContinue() && !player.IsGrounded)
            {
                yield return null;
            }

            if (!CanContinue())
            {
                yield break;
            }

            OnLanded?.Invoke();
            RevealHUD();

            player.PlayIntroAnimation("Landing");
            yield return new WaitForSeconds(landingDuration);

            if (!CanContinue())
            {
                yield break;
            }
        }
        else
        {
            OnLanded?.Invoke();
            RevealHUD();
        }

        player.PlayIntroAnimation("Idle");
        player.SetControlLock(PlayerControlLock.Intro, false);
        IsComplete = true;
    }

    private bool CanContinue()
    {
        if (player != null && player.IsInitialized && hudGroup != null)
        {
            return true;
        }

        Debug.LogError("[ERROR] La introducción perdió al jugador inicial o al HUD antes de terminar.");
        return false;
    }

    private void RevealHUD()
    {
        if (hudGroup == null)
        {
            Debug.LogError("[ERROR] El CanvasGroup del HUD no fue encontrado al momento de revelarlo.");
            return;
        }

        if (hudFadeDuration <= 0f)
        {
            hudGroup.alpha = 1f;
            hudGroup.interactable = originalInteractable;
            hudGroup.blocksRaycasts = originalBlocksRaycasts;
        }

        hudFadeTween = Tween.Alpha(hudGroup, 1f, hudFadeDuration, ease: Ease.OutQuad, useUnscaledTime: true);
    }

    // Revierte bloqueos y presentación si la escena o su preparación se interrumpen.
    public void Cancel()
    {
        if (hudFadeTween.isAlive)
        {
            hudFadeTween.Stop();
        }

        if (player != null)
        {
            player.SetControlLock(PlayerControlLock.Intro, false);
        }

        if (hudGroup != null)
        {
            hudGroup.alpha = originalAlpha;
            hudGroup.interactable = originalInteractable;
            hudGroup.blocksRaycasts = originalBlocksRaycasts;
        }
    }

    private void OnDisable()
    {
        Cancel();
    }
}
