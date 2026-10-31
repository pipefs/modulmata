using UnityEngine;

// Activa un punto de reaparición y permite localizar el activado más cercano al lugar de muerte.
public class PuntoReensamblaje : MonoBehaviour
{
    public Transform spawnPoint;
    public bool isActivated;
    public Sprite inactiveSprite;
    public Sprite activeSprite;
    public GameObject lights;
    public AudioSource audioSource;
    public AudioClip activationSound;

    private SpriteRenderer spriteRenderer;

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        
        if (spawnPoint == null)
        {
            Debug.LogError("PuntoReensamblaje necesita un punto de aparición.", this);
            enabled = false;
        }

        lights.gameObject.SetActive(isActivated);
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        PlayerController player = collision.GetComponentInParent<PlayerController>();

        if (isActiveAndEnabled && player != null && player.IsPlaying && !isActivated)
        {
            ActivateCheckpoint();
        }
    }

    public void ActivateCheckpoint()
    {
        if (!isActiveAndEnabled || spawnPoint == null)
        {
            return;
        }

        isActivated = true;
        lights.gameObject.SetActive(true);
        audioSource.PlayOneShot(activationSound);

        if (spriteRenderer != null && activeSprite != null)
        {
            spriteRenderer.sprite = activeSprite;
        }
    }

    public static PuntoReensamblaje FindNearest(Vector3 position)
    {
        PuntoReensamblaje nearest = null;
        float bestDistance = float.PositiveInfinity;

        foreach (PuntoReensamblaje point in FindObjectsByType<PuntoReensamblaje>(FindObjectsSortMode.None))
        {
            if (!point.isActivated || point.spawnPoint == null)
            {
                continue;
            }

            float distance = ((Vector2)(point.spawnPoint.position - position)).sqrMagnitude;

            if (distance < bestDistance)
            {
                nearest = point;
                bestDistance = distance;
            }
        }

        return nearest;
    }
}
