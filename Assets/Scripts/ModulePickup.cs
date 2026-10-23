using UnityEngine;
using PrimeTween;

// ? Se encarga de la logica de recoger el modulo. Esta separado
// ? por modularizadad y mejor debug. Quizas mas adelante se encargara
// ? de cosas un poco mas complejas como:
// ?    1. Obtener la posicion de "spawn" para despues de cierto tiempo respawnear.
// ?    2. Animaciones de recoger y despawn.

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

    [SerializeField] private Color flashColor = Color.cyan;
    [SerializeField] private float flashDuration = 0.1f;

    private void Start()
    {
        initialLocalPos = transform.localPosition;
        initialScale = transform.localScale;

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
    private void OnDestroy()
    {
        if (hoverTween.isAlive)
            hoverTween.Stop();
        if (pulseTween.isAlive)
            pulseTween.Stop();
    }

    // Entregar una copia de los datos al jugador y retirar el objeto del escenario.
    private void OnTriggerEnter2D(Collider2D collision)
    {
        GameManager manager = GameManager.Instance;
        PlayerController player = collision.GetComponentInParent<PlayerController>();

        if (manager == null || manager.IsDead || manager.HasWon || player == null || player != manager.CurrentPlayer || collected || data == null)
            return;

        collected = true;

        // Indicador de que agarramos algo.
        player.PlayFlash(flashColor, flashDuration);

        // Equipar usando una copia de data.
        if (data.type == ModuleType.External)
        {
            player.GetComponent<PlayerModuleManager>().EquipExternalFromPickup(new ModuleData(data));
        }
        else
        {
            player.GetComponent<PlayerModuleManager>().UnlockInternalModule(new ModuleData(data));
        }

        Destroy(gameObject);
    }
}
