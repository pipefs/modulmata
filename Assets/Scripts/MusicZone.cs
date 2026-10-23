using UnityEngine;

public class MusicZone : MonoBehaviour
{
    [SerializeField] private AudioClip music;
    [SerializeField, Range(0f, 1f)] private float volume = 0.5f;
    [SerializeField, Min(0f)] private float fadeDuration = 1.5f;

    private void Awake() => GetComponent<Collider2D>().isTrigger = true;

    private void OnTriggerEnter2D(Collider2D other)
    {
        GameManager manager = GameManager.Instance;
        PlayerController player = other.GetComponentInParent<PlayerController>();

        if (manager != null && !manager.IsDead && !manager.HasWon && player != null && player == manager.CurrentPlayer)
            MusicManager.Instance?.PlayZoneMusic(music, volume, fadeDuration);
    }
}
