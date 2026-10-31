using UnityEngine;
using PrimeTween;

// Entrega módulos y conserva su presentación local.

public class ModulePickup : MonoBehaviour
{
    // Evitar procesar varias entradas de colliders antes de destruir el pickup.
    private bool collected;

    public ModuleData data;

    [SerializeField] private float hoverAmplitude = 0.25f;
    [SerializeField] private float hoverDuration = 1.5f;
    [SerializeField] private Vector3 pulseScale = new Vector3(1.1f, 1.1f, 1f);
    [SerializeField] private float pulseDuration = 0.6f;

    private Tween hoverTween;
    private Tween pulseTween;

    private Vector3 initialLocalPos;
    private Vector3 initialScale;
    private bool initialized;

    [SerializeField] private Color flashColor = Color.cyan;
    [SerializeField] private AudioClip pickupSound;

    private void Start()
    {
        initialLocalPos = transform.localPosition;
        initialScale = transform.localScale;
        initialized = true;

        StartHover();
        StartPulse();
    }

    private void StartHover()
    {
        Vector3 targetPos = initialLocalPos + new Vector3(0f, hoverAmplitude, 0f);

        hoverTween = Tween.LocalPosition(
            target: transform,
            endValue: targetPos,
            duration: hoverDuration,
            ease: Ease.InOutSine,
            cycles: -1,
            cycleMode: CycleMode.Yoyo
        );
    }

    private void StartPulse()
    {
        Vector3 targetScale = Vector3.Scale(initialScale, pulseScale);

        pulseTween = Tween.Scale(
            target: transform,
            endValue: targetScale,
            duration: pulseDuration,
            ease: Ease.InOutSine,
            cycles: -1,
            cycleMode: CycleMode.Yoyo
        );
    }

    // Detener las animaciones de PrimeTween cuando desaparezca este objeto.
    private void OnEnable()
    {
        if (!initialized || collected)
        {
            return;
        }

        transform.localPosition = initialLocalPos;
        transform.localScale = initialScale;
        StartHover();
        StartPulse();
    }

    private void OnDisable()
    {
        StopAnimation();
    }

    private void OnDestroy()
    {
        StopAnimation();
    }

    private void StopAnimation()
    {
        if (hoverTween.isAlive)
        {
            hoverTween.Stop();
        }

        if (pulseTween.isAlive)
        {
            pulseTween.Stop();
        }
    }

    // Entregar una copia de los datos al jugador y retirar el objeto del escenario.
    private void OnTriggerEnter2D(Collider2D collision)
    {
        PlayerController player = collision.GetComponentInParent<PlayerController>();

        if (player == null || !player.IsPlaying || collected || data == null)
        {
            return;
        }

        var modules = player.GetComponent<PlayerModuleManager>();
        if (modules == null)
        {
            return;
        }

        ModuleData instance = data.CreatePickupInstance();
        bool accepted = instance.type == ModuleType.External
            ? modules.EquipExternalFromPickup(instance)
            : modules.UnlockInternalModule(instance);
        if (!accepted)
        {
            return;
        }

        collected = true;
        player.PlayActionSound(pickupSound);
        player.PlayFlash(flashColor, 0.4f);
        Destroy(gameObject);
    }
}
