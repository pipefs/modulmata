using UnityEngine;

// Permite atravesar una plataforma desde abajo alternando su colisión con el jugador actual.
[RequireComponent(typeof(Collider2D))]
public class OneWayPlatform : MonoBehaviour
{
    private const float SurfaceTolerance = 0.05f;

    private Collider2D platformCollider;
    private Collider2D playerCollider;
    private GameManager manager;
    private bool droppingThrough;

    private void Awake()
    {
        platformCollider = GetComponent<Collider2D>();

        if (platformCollider != null)
        {
            return;
        }

        Debug.LogError("OneWayPlatform necesita un Collider2D.", this);
        enabled = false;
    }

    private void OnEnable()
    {
        Connect();
    }

    private void Connect()
    {
        var current = GameManager.Instance;

        if (ReferenceEquals(manager, current))
        {
            return;
        }

        if (!ReferenceEquals(manager, null))
        {
            manager.PlayerChanged -= Bind;
        }

        manager = current;

        if (manager != null)
        {
            manager.PlayerChanged += Bind;
        }

        Bind(manager != null ? manager.CurrentPlayer : null);
    }

    private void Bind(PlayerController player)
    {
        if (playerCollider != null && platformCollider != null)
        {
            Physics2D.IgnoreCollision(playerCollider, platformCollider, false);
        }

        droppingThrough = false;
        playerCollider = player != null ? player.GetComponent<Collider2D>() : null;
    }

    // PlayerController solo llama este método después de confirmar que la plataforma es su apoyo actual.
    internal bool TryDropThrough(PlayerController player)
    {
        if (player == null || playerCollider == null || platformCollider == null ||
            playerCollider != player.GetComponent<Collider2D>())
        {
            return false;
        }

        droppingThrough = true;
        Physics2D.IgnoreCollision(playerCollider, platformCollider, true);
        return true;
    }

    private void Update()
    {
        Connect();

        if (manager == null || manager.IsDead || playerCollider == null || platformCollider == null)
        {
            return;
        }

        float platformTop = platformCollider.bounds.max.y;

        // Mantener el descenso forzado hasta que el cuerpo completo quede bajo la superficie.
        if (droppingThrough && playerCollider.bounds.min.y < platformTop - SurfaceTolerance)
        {
            droppingThrough = false;
        }

        bool approachingFromBelow = playerCollider.bounds.min.y < platformTop - SurfaceTolerance;
        Physics2D.IgnoreCollision(playerCollider, platformCollider, droppingThrough || approachingFromBelow);
    }

    private void OnDisable()
    {
        if (!ReferenceEquals(manager, null))
        {
            manager.PlayerChanged -= Bind;
        }

        manager = null;
        Bind(null);
    }
}
