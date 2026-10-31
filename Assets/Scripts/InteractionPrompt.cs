using System.Collections;
using TMPro;
using PrimeTween;
using UnityEngine;

// Controla la aparición, desaparición y pulso de un texto de interacción sin ser un componente de escena.
public sealed class InteractionPrompt
{
    private readonly MonoBehaviour owner;
    private readonly TextMeshPro text;
    private readonly float fadeDuration;
    private readonly float pulseScale;
    private readonly float pulseDuration;
    private readonly Vector3 scale;
    private Coroutine fade;
    private Tween pulse;

    public InteractionPrompt(MonoBehaviour owner, TextMeshPro text,
        float fadeDuration, float pulseScale, float pulseDuration)
    {
        this.owner = owner;
        this.text = text;
        this.fadeDuration = Mathf.Max(0f, fadeDuration);
        this.pulseScale = pulseScale;
        this.pulseDuration = Mathf.Max(0.01f, pulseDuration);
        scale = text != null ? text.transform.localScale : Vector3.one;
        HideImmediate();
    }

    public void SetText(string message)
    {
        if (text != null && text.text != message)
        {
            text.text = message;
        }
    }

    public void Show()
    {
        if (text == null || owner == null || !owner.isActiveAndEnabled)
        {
            return;
        }

        Stop();
        text.gameObject.SetActive(true);
        fade = owner.StartCoroutine(Fade(1f, false));
        pulse = Tween.Scale(text.transform, scale * pulseScale, pulseDuration,
            ease: Ease.InOutSine, cycles: -1, cycleMode: CycleMode.Yoyo);
    }

    public void Hide()
    {
        if (text == null || !text.gameObject.activeSelf)
        {
            return;
        }

        Stop();
        if (owner != null && owner.isActiveAndEnabled)
        {
            fade = owner.StartCoroutine(Fade(0f, true));
        }
        else
        {
            HideImmediate();
        }
    }

    public void HideImmediate()
    {
        Stop();
        if (text == null)
        {
            return;
        }

        SetAlpha(0f);
        text.gameObject.SetActive(false);
    }

    // Sustituye cualquier fundido anterior y desactiva el texto al completar una ocultación.
    private IEnumerator Fade(float target, bool hide)
    {
        float start = text.color.a;
        float elapsed = 0f;
        // Ceder un frame también con duración cero evita referencias de corrutina obsoletas.
        do
        {
            elapsed += Time.unscaledDeltaTime;
            if (text == null)
            {
                break;
            }

            SetAlpha(Mathf.Lerp(start, target, fadeDuration > 0f ? elapsed / fadeDuration : 1f));
            yield return null;
        } while (elapsed < fadeDuration);
        if (text != null)
        {
            SetAlpha(target);
            if (hide)
            {
                text.gameObject.SetActive(false);
            }
        }
        fade = null;
    }

    private void SetAlpha(float alpha)
    {
        Color color = text.color;
        color.a = alpha;
        text.color = color;
    }

    private void Stop()
    {
        if (fade != null && owner != null)
        {
            owner.StopCoroutine(fade);
        }

        fade = null;
        if (pulse.isAlive)
        {
            pulse.Stop();
        }

        if (text != null)
        {
            text.transform.localScale = scale;
        }
    }
}
