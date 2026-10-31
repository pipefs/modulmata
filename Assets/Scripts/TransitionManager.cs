using System;
using System.Collections;
using UnityEngine;

// Arbitra el uso exclusivo del fundido global y expone tiempos compartidos para cada transición.
public sealed class TransitionManager : MonoBehaviour
{
    public static TransitionManager Instance { get; private set; }

    [SerializeField] private ScreenFadeTransition screenFade;
    [Header("Cambio de escena")]
    [SerializeField] private Color sceneColor = Color.black;
    [SerializeField, Min(0f)] private float sceneCoverDuration = 0.3f;
    [SerializeField, Min(0f)] private float sceneRevealDuration = 0.4f;
    [SerializeField, Min(0f)] private float initialRevealDuration = 1f;
    [Header("Puertas")]
    [SerializeField, Min(0f)] private float doorCoverDuration = 0.08f;
    [SerializeField, Min(0f)] private float doorHoldDuration = 0.08f;
    [SerializeField, Min(0f)] private float doorRevealDuration = 0.35f;
    [Header("Victoria")]
    [SerializeField, Min(0f)] private float victoryCoverDuration = 1f;
    private UnityEngine.Object owner;

    public bool IsReady => isActiveAndEnabled && screenFade != null && screenFade.IsReady;
    public bool IsBusy => owner != null;
    public Color SceneColor => sceneColor;
    public float SceneCoverDuration => sceneCoverDuration;
    public float SceneRevealDuration => sceneRevealDuration;
    public float InitialRevealDuration => initialRevealDuration;
    public float DoorCoverDuration => doorCoverDuration;
    public float DoorHoldDuration => doorHoldDuration;
    public float DoorRevealDuration => doorRevealDuration;
    public float VictoryCoverDuration => victoryCoverDuration;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            // El prefab completo es una unidad persistente: descartar solo la copia nueva.
            if (Instance.transform.root != transform.root)
            {
                Destroy(transform.root.gameObject);
            }
            else
            {
                Debug.LogError("Solo puede haber un TransitionManager en PersistentSystems.", this);
            }

            enabled = false;
            return;
        }
        if (screenFade == null || !screenFade.IsReady || screenFade.transform.root != transform.root)
        {
            Debug.LogError("TransitionManager necesita un ScreenFadeTransition activo dentro de su raíz persistente.", this);
            enabled = false;
            return;
        }
        Instance = this;
    }

    // Entrega propiedad exclusiva del fundido; el mismo solicitante puede reutilizar su reserva.
    public bool TryAcquire(UnityEngine.Object requester)
    {
        if (!IsReady || requester == null || (owner != null && owner != requester))
        {
            return false;
        }

        owner = requester;
        return true;
    }

    public bool SetCovered(UnityEngine.Object requester, Color color)
    {
        return Owns(requester) && screenFade.PrepareFadeOut(color);
    }

    // Lleva la cobertura al opaco sin permitir que otro sistema interfiera durante la operación.
    public IEnumerator Cover(UnityEngine.Object requester, Color color, float duration, Action<float> progress = null)
    {
        if (!Owns(requester))
        {
            yield break;
        }
        // Conservar el alpha actual permite invertir una revelación sin saltar a transparente.
        float alpha = screenFade.Alpha;
        if (!screenFade.PrepareFadeOut(color, alpha))
        {
            yield break;
        }

        yield return screenFade.FadeTo(1f, duration, progress);
    }

    // Revela la escena manteniendo la propiedad hasta que el solicitante la libere explícitamente.
    public IEnumerator Reveal(UnityEngine.Object requester, float duration, Action<float> progress = null)
    {
        if (!Owns(requester))
        {
            yield break;
        }

        yield return screenFade.FadeOut(duration, progress);
    }

    // Libera la transición y, normalmente, retira cualquier color temporal aplicado al fundido.
    public void Release(UnityEngine.Object requester, bool clear = true)
    {
        if (owner != requester || requester == null)
        {
            return;
        }

        if (clear && screenFade != null)
        {
            screenFade.CancelColorFadeOut();
            screenFade.SetAlpha(0f);
        }
        owner = null;
    }

    private bool Owns(UnityEngine.Object requester)
    {
        return IsReady && requester != null && owner == requester;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }
}
