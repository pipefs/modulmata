using UnityEngine;

// Representa la barrera física de la arena y mantiene su animación sincronizada con su collider.
public class BossDoor : MonoBehaviour
{
    public bool isOpenByDefault;

    public bool IsOpen { get; private set; }

    private Collider2D puertaCollider;
    private Animator animator;
    private BossArena arena;
    public void AssignArena(BossArena owner)
    {
        arena = owner;
    }

    private void Awake()
    {
        puertaCollider = GetComponent<Collider2D>();
        animator = GetComponent<Animator>();

        if (puertaCollider == null)
        {
            Debug.LogError("[ERROR] BossDoor necesita un Collider2D para bloquear el paso.", this);
            enabled = false;
            return;
        }

        SetOpen(isOpenByDefault, false);
    }

    public void Open()
    {
        SetOpen(true, true);
    }

    public void Close()
    {
        if (arena != null && !arena.IsFighting)
        {
            return;
        }

        SetOpen(false, true);
    }

    private void SetOpen(bool open, bool animate)
    {
        IsOpen = open;

        if (puertaCollider != null)
        {
            puertaCollider.enabled = !open;
        }

        if (animate && animator != null && animator.runtimeAnimatorController != null)
        {
            animator.Play(open ? "Abrir" : "Cerrar");
        }
    }
}
