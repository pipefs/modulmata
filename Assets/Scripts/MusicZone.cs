using UnityEngine;

// Solicita una pista al gestor global cuando el jugador válido entra en esta región.
[RequireComponent(typeof(Collider2D))]
public class MusicZone : MonoBehaviour
{
    [SerializeField] private AudioClip music;
    [SerializeField, Range(0f, 1f)] private float volume = 0.5f;
    [SerializeField, Min(0f)] private float fadeDuration = 1.5f;

    private void Awake()
    {
        var trigger = GetComponent<Collider2D>();
        if (trigger == null)
        {
            Debug.LogError("MusicZone necesita un Collider2D.", this);
            enabled = false;
            return;
        }
        trigger.isTrigger = true;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        GameManager manager = GameManager.Instance;
        PlayerController player = other.GetComponentInParent<PlayerController>();

        // La música puede empezar durante la introducción, antes de habilitar el control.
        if (isActiveAndEnabled && manager != null && manager.isActiveAndEnabled &&
            player != null && player.IsInitialized && player.isActiveAndEnabled &&
            manager.CurrentPlayer == player && !manager.IsDead && !manager.HasWon)
        {
            MusicManager.Instance?.PlayZoneMusic(music, volume, fadeDuration);
        }
    }
}
