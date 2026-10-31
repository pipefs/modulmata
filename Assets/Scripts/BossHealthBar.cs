using UnityEngine;
using UnityEngine.UI;

// Encapsula la presentación de la vida del jefe y el relleno retrasado que enfatiza el daño recibido.
public sealed class BossHealthBar
{
    private readonly GameObject root;
    private readonly Image fill;
    private readonly Image slow;
    private float previous;
    private float wait;

    public BossHealthBar(GameObject root, Image fill, Image slow)
    {
        this.root = root;
        this.fill = fill;
        this.slow = slow;
    }

    public void Show(float target)
    {
        previous = target;
        wait = 0f;

        if (fill != null)
        {
            fill.fillAmount = target;
        }

        if (slow != null)
        {
            slow.fillAmount = target;
        }

        if (root != null)
        {
            root.SetActive(true);
        }
    }

    public void Tick(float target)
    {
        target = Mathf.Clamp01(target);

        if (target < previous)
        {
            wait = 0.4f;
        }
        else if (target > previous && slow != null)
        {
            slow.fillAmount = target;
        }

        previous = target;

        if (fill != null)
        {
            fill.fillAmount = Drain(fill.fillAmount, target, 12f);
        }

        if (wait > 0f)
        {
            wait -= Time.unscaledDeltaTime;
        }
        else if (slow != null)
        {
            slow.fillAmount = Drain(slow.fillAmount, target, 6f);
        }
    }

    private static float Drain(float current, float target, float speed)
    {
        return Mathf.Abs(current - target) < 0.0001f ? target : Mathf.Lerp(current, target, 1f - Mathf.Exp(-speed * Time.unscaledDeltaTime));
    }

    public void Hide()
    {
        if (root != null)
        {
            root.SetActive(false);
        }

        wait = 0f;
    }
}
