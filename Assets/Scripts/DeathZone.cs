using UnityEngine;

// Elimina al jugador activo cuando cualquiera de sus colliders entra en esta zona.
public class DeathZone : MonoBehaviour
{
    private void OnTriggerEnter2D(Collider2D collision)
    {
        PlayerController player = collision.GetComponentInParent<PlayerController>();

        if (player != null && player.IsPlaying)
        {
            player.Kill();
        }
    }
}
