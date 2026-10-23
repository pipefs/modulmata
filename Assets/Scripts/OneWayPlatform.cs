using UnityEngine;

public class OneWayPlatform : MonoBehaviour
{
    private Collider2D platformCollider;
    private Collider2D playerCollider;

    private void Awake()
    {
        platformCollider = GetComponent<Collider2D>();
    }

    private void Update()
    {
        GameManager gameManager = GameManager.Instance;
        PlayerController player = gameManager != null ? gameManager.CurrentPlayer : null;
        Collider2D currentCollider = player != null ? player.GetComponent<Collider2D>() : null;

        // Al morir o reaparecer, retirar la relación con el collider anterior.
        if (currentCollider != playerCollider)
        {
            if (playerCollider != null)
                Physics2D.IgnoreCollision(playerCollider, platformCollider, false);

            playerCollider = currentCollider;
        }

        if (gameManager == null || gameManager.IsDead || playerCollider == null)
            return;

        bool porDebajo = playerCollider.bounds.min.y < platformCollider.bounds.max.y - 0.05f;
        Physics2D.IgnoreCollision(playerCollider, platformCollider, porDebajo);
    }
}
