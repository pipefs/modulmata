using UnityEngine;

public class BossDoor : MonoBehaviour
{
    public bool isOpenByDefault;

    public bool isOpen { get; private set; }

    private Collider2D puertaCollider;
    private Animator animator;

    private void Awake()
    {
        puertaCollider = GetComponent<Collider2D>();
        animator = GetComponent<Animator>();

        SetOpen(isOpenByDefault, false);
    }

    public void Open() => SetOpen(true, true);
    public void Close() => SetOpen(false, true);

    private void SetOpen(bool open, bool animate)
    {
        isOpen = open;

        if (puertaCollider != null)
            puertaCollider.enabled = !open;

        if (animate && animator != null && animator.runtimeAnimatorController != null)
        {
            if (open)
                animator.Play("Abrir");
            else
                animator.Play("Cerrar");
        }
    }
}
