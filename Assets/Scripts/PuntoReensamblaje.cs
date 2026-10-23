using UnityEngine;

public class PuntoReensamblaje : MonoBehaviour
{
    public Transform spawnPoint;
    public bool isActivated;
    public Sprite inactiveSprite;
    public Sprite activeSprite;

    private SpriteRenderer spriteRenderer;

    private void Awake() => spriteRenderer = GetComponent<SpriteRenderer>();

    private void OnTriggerEnter2D(Collider2D collision)
    {
        GameManager manager = GameManager.Instance;
        PlayerController player = collision.GetComponentInParent<PlayerController>();

        if (manager != null && !manager.IsDead && !manager.HasWon && player != null && player == manager.CurrentPlayer && !isActivated)
            ActivateCheckpoint();
    }

    public void ActivateCheckpoint()
    {
        isActivated = true;

        if (spriteRenderer != null && activeSprite != null)
            spriteRenderer.sprite = activeSprite;
    }

    public static PuntoReensamblaje FindNearest(Vector3 position)
    {
        PuntoReensamblaje nearest = null;
        float bestDistance = float.PositiveInfinity;

        foreach (PuntoReensamblaje point in FindObjectsByType<PuntoReensamblaje>(FindObjectsSortMode.None))
        {
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
