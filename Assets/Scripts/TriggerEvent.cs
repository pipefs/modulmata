using UnityEngine;
using UnityEngine.Events;

// Disparar un evento una vez y permitir que otro sistema reinicie el trigger.
public class TriggerEvent : MonoBehaviour
{
    private bool oneTime;
    public string colliderTag = "Player";
    public UnityEvent triggerEvent = new UnityEvent();

    // Comprobar etiqueta, estado del jugador y si el evento ya se ejecutó.
    private void OnTriggerEnter2D(Collider2D collision)
    {
        var player = collision.GetComponentInParent<PlayerController>();

        if (!collision.CompareTag(colliderTag) || oneTime)
            return;

        // Los eventos de otras etiquetas siguen funcionando. Para Player, exigir la instancia activa.
        if (player != null || colliderTag == "Player")
        {
            var manager = GameManager.Instance;

            if (manager == null || manager.IsDead || manager.HasWon ||
                player == null || player != manager.CurrentPlayer)
            {
                return;
            }
        }

        oneTime = true;
        triggerEvent.Invoke();
    }

    // Permitir que el mismo evento vuelva a activarse en un nuevo intento.
    public void ResetTrigger() =>
        oneTime = false;
}
