using UnityEngine;

// Restaura durabilidad al jugador y consume el recogible solo cuando la recuperación fue efectiva.
public class DurabilityPickup : MonoBehaviour
{
    [Min(0f)] public float restoreAmount = 20f;
    public Color pickupColor;
    public AudioClip pickupSound;

    private bool collected;

    private void OnTriggerEnter2D(Collider2D other)
    {
        PlayerController player = other.GetComponentInParent<PlayerController>();

        if (collected || player == null || !player.IsPlaying || restoreAmount <= 0f)
        {
            return;
        }

        BatterySystem battery = player.GetComponent<BatterySystem>();

        if (battery == null || battery.AddDurability(restoreAmount) <= 0f)
        {
            return;
        }

        collected = true;
        player.PlayActionSound(pickupSound);
        player.PlayFlash(pickupColor, 0.4f);
        Destroy(gameObject);
    }
}
