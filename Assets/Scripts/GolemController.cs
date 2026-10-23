using System.Collections;
using UnityEngine;
using UnityEngine.Events;

// Estados compartidos por el gólem común y el jefe.
public enum GolemState
{
    Dormant,
    Idle,
    Move,
    Flee,
    Attack,
    Recovering,
    Hurt,
    Dead
}

// Comportamiento del gólem común. El jefe reutiliza sus referencias y movimiento.
[RequireComponent(typeof(Rigidbody2D), typeof(Collider2D))]
public class GolemController : MonoBehaviour
{
    [Header("Combate provisional")]
    [Min(1f)] public float maxHealth = 48f;
    [Min(0f)] public float attackDamage = 8f;

    // Movimiento y distancias desde las que detecta o ataca al jugador.
    [Min(0f)] public float moveSpeed = 2f;
    [Min(0f)] public float detectionRange = 8f;
    [Min(0f)] public float attackRange = 1.5f;

    // Tiempo antes del impacto, recuperación del golpe y pausa antes del siguiente.
    [Min(0f)] public float attackWindup = 0.45f;
    [Min(0f)] public float attackRecovery = 0.65f;
    [Min(0f)] public float attackCooldown = 0.8f;

    // Origen y tamaño de la zona que se comprueba en el instante del impacto.
    public Transform attackPoint;
    public Vector2 attackSize = new Vector2(1.5f, 2f);

    public LayerMask groundLayer = 8;

    [Header("Detección de bordes y giro")]
    [Min(0.01f)] public float groundProbeDistance = 0.6f;
    [Min(0.01f)] public float groundProbeWidth = 0.25f;
    [Tooltip("Desplazamiento local del volumen que detecta paredes.")]
    public Vector2 wallProbeOffset;
    [Min(0f)] public float turnDeadZone = 0.2f;

    public UnityEvent onDied = new UnityEvent();

    [Header("Recompensa del gólem común")]
    public DurabilityPickup durabilityPickupPrefab;
    [Min(0f)] public float durabilityDropAmount = 20f;

    [Header("Estados del Animator (ambas variantes usan los mismos nombres)")]
    public string idleState = "Idle";
    public string moveState = "Walk";
    public string attackState = "Attack";
    public string hurtState = "Hurt";
    public string dieState = "Die";
    [Min(0f)] public float hurtDuration = 0.25f;
    [Min(0f)] public float hurtRetaliationDelay = 0.15f;

    public GolemState State { get; protected set; }
    public bool IsDead => State == GolemState.Dead;
    public float CurrentHealth { get; protected set; }

    // Referencias que también utiliza el controlador del jefe.
    protected Rigidbody2D body;
    protected Animator animator;

    // Mientras esta corrutina exista, Update no inicia otra acción de combate.
    protected Coroutine action;
    protected float nextAttackTime;
    protected bool facingRight;

    // Guardar la configuración inicial para restaurarla al reiniciar el combate.
    protected Vector3 initialPosition;
    protected Vector3 initialScale;
    private Quaternion initialRotation;

    // Estado original de los colliders y nombre de la animación en reproducción.
    private Collider2D[] colliders;
    private bool[] initialColliderStates;
    private Collider2D bodyCollider;
    private string playingState;
    private Vector3 dropPosition;
    private float nextHurtTime;
    private SpriteFlash hitFlash;

    // Reutilizar los resultados de las sondas sin crear arrays en cada frame.
    private readonly RaycastHit2D[] groundHits = new RaycastHit2D[8];
    private readonly RaycastHit2D[] wallHits = new RaycastHit2D[8];

    // Obtener componentes y guardar la configuración inicial del enemigo.
    protected virtual void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        animator = GetComponentInChildren<Animator>();
        bodyCollider = GetComponent<Collider2D>();
        hitFlash = GetComponent<SpriteFlash>();

        if (hitFlash == null)
            hitFlash = gameObject.AddComponent<SpriteFlash>();

        colliders = GetComponentsInChildren<Collider2D>();
        initialColliderStates = new bool[colliders.Length];

        for (int i = 0; i < colliders.Length; i++)
            initialColliderStates[i] = colliders[i].enabled;

        initialPosition = transform.position;
        initialScale = transform.localScale;
        initialRotation = transform.rotation;
        facingRight = initialScale.x >= 0f;

        CurrentHealth = maxHealth;
        State = GolemState.Idle;
    }

    // Elegir entre esperar, acercarse o atacar al jugador activo.
    protected virtual void Update()
    {
        if (IsDead || State == GolemState.Dormant)
            return;

        var manager = GameManager.Instance;
        var player = manager != null ? manager.CurrentPlayer : null;
        if (player == null || manager.IsDead || manager.HasWon)
        {
            StopHorizontalMovement();
            return;
        }

        if (action != null)
            return;

        var target = player.GetComponent<Collider2D>();
        if (target == null || !target.enabled)
        {
            StopHorizontalMovement();
            return;
        }

        // Los pivotes de los sprites pueden estar a alturas distintas aunque ambos pisen el suelo.
        float distance = Vector2.Distance(bodyCollider.bounds.center, target.bounds.center);
        if (distance > detectionRange)
        {
            State = GolemState.Idle;
            StopHorizontalMovement();
            Play(idleState);
            return;
        }

        // Si el jugador atraviesa al gólem, conservar el frente hasta que salga de su cuerpo.
        // No hace falta que sus capas colisionen para consultar esta separación geométrica.
        var separation = bodyCollider.Distance(target);
        if (!separation.isOverlapped)
            Face(target.bounds.center.x);

        // Usar la misma zona para iniciar el ataque y comprobar su impacto.
        if (IsInsideAttackArea(target))
        {
            StopHorizontalMovement();
            if (Time.time >= nextAttackTime)
                action = StartCoroutine(Attack());
            else
            {
                State = GolemState.Idle;
                Play(idleState);
            }
        }
        else
        {
            State = GolemState.Move;
            MoveTowardsPlayer();
        }
    }

    // Secuencia del golpe: anticipación, impacto y recuperación.
    protected virtual IEnumerator Attack()
    {
        State = GolemState.Attack;
        Play(attackState, true);

        yield return new WaitForSeconds(attackWindup);

        HitPlayer(attackDamage);

        State = GolemState.Recovering;

        yield return new WaitForSeconds(attackRecovery);

        FinishAction();
    }

    protected void HitPlayer(float damage)
    {
        // El objetivo es únicamente el jugador activo, nunca un cadáver ni varias hitboxes.
        var manager = GameManager.Instance;
        var player = manager != null ? manager.CurrentPlayer : null;

        if (player == null || manager.IsDead || manager.HasWon)
            return;

        var target = player.GetComponent<Collider2D>();
        if (target == null)
            return;

        if (IsInsideAttackArea(target))
            player.TakeDamage(damage);
    }

    protected virtual bool IsInsideAttackArea(Collider2D target)
    {
        var separation = bodyCollider.Distance(target);

        if (!separation.isValid)
            return false;

        // Sin colisión Jugador/Enemigo, ambos cuerpos pueden ocupar el mismo lugar.
        if (separation.isOverlapped)
            return true;

        return separation.distance <= attackRange && GetAttackBounds().Intersects(target.bounds);
    }

    private Bounds GetAttackBounds()
    {
        Vector2 center;

        if (attackPoint != null)
        {
            center = attackPoint.position;
        }
        else
        {
            // Sin Attack Point, colocar la caja delante del collider, a la altura del cuerpo.
            Bounds bounds = bodyCollider.bounds;
            float direction = Application.isPlaying ? (facingRight ? 1f : -1f) :
                (transform.localScale.x >= 0f ? 1f : -1f);

            center = new Vector2(
                bounds.center.x + direction * (bounds.extents.x + attackSize.x * 0.5f),
                bounds.center.y);
        }

        return new Bounds(center, attackSize);
    }

    // Comprobar suelo y pared antes de avanzar, para no caminar hacia un vacío.
    protected void MoveTowardsPlayer()
    {
        float direction = facingRight ? 1f : -1f;
        Bounds bounds = bodyCollider.bounds;

        // Los triggers en la capa Suelo no cuentan como paredes ni como piso.
        var filter = new ContactFilter2D();
        filter.SetLayerMask(groundLayer);
        filter.useTriggers = false;

        // Una sonda con ancho tolera pequeñas juntas entre colliders mejor que un único rayo.
        Vector2 foot = GetGroundProbeOrigin(bounds, direction);
        int groundCount = Physics2D.BoxCast(foot, new Vector2(groundProbeWidth, 0.05f),
            0f, Vector2.down, filter, groundHits, groundProbeDistance);

        bool floorAhead = false;

        for (int i = 0; i < groundCount; i++)
        {
            var hit = groundHits[i];

            if (hit.normal.y > 0.5f && !Physics2D.GetIgnoreCollision(bodyCollider, hit.collider))
            {
                floorAhead = true;
                break;
            }
        }

        bool wallAhead = IsWallAhead(direction);

        body.linearVelocityX = floorAhead && !wallAhead ? direction * moveSpeed : 0f;

        Play(Mathf.Abs(body.linearVelocityX) > 0f ? moveState : idleState);
    }

    // El jefe usa la misma lectura antes de decidir su dash de huida.
    protected bool IsWallAhead(float direction)
    {
        if (bodyCollider == null)
            return false;

        var filter = new ContactFilter2D();
        filter.SetLayerMask(groundLayer);
        filter.useTriggers = false;

        // Proyectar una caja del tamaño del collider. Su centro puede moverse desde
        // el Inspector para ignorar obstáculos que no deban contar como pared.
        Bounds bounds = bodyCollider.bounds;
        Vector2 probeCenter = GetWallProbeCenter();
        int wallCount = Physics2D.BoxCast(probeCenter, bounds.size, 0f,
            Vector2.right * direction, filter, wallHits, bounds.extents.x + 0.2f);

        for (int i = 0; i < wallCount; i++)
        {
            var hit = wallHits[i];

            // Una superficie horizontal no es una pared que deba detener el avance.
            if (hit.normal.x * direction < -0.5f &&
                !Physics2D.GetIgnoreCollision(bodyCollider, hit.collider))
            {
                return true;
            }
        }

        return false;
    }

    private Vector2 GetWallProbeCenter()
    {
        // TransformVector respeta el giro de izquierda/derecha del enemigo.
        return (Vector2)bodyCollider.bounds.center + (Vector2)transform.TransformVector(wallProbeOffset);
    }

    private Vector2 GetGroundProbeOrigin(Bounds bounds, float direction)
    {
        return new Vector2(
            bounds.center.x + direction * (bounds.extents.x + 0.1f),
            bounds.min.y + 0.1f);
    }

    // Girar la raíz también cambia de lado el punto desde el que se golpea.
    protected void Face(float targetX)
    {
        float difference = targetX - bodyCollider.bounds.center.x;

        if (Mathf.Abs(difference) <= turnDeadZone)
            return;

        SetFacingDirection(Mathf.Sign(difference));
    }

    // Algunos movimientos especiales deben girar con certeza antes de desplazarse.
    protected void SetFacingDirection(float direction)
    {
        Vector3 scale = transform.localScale;
        facingRight = direction > 0f;
        scale.x = Mathf.Abs(scale.x) * (facingRight ? 1f : -1f);
        transform.localScale = scale;
    }

    // El primer impacto interrumpe; los siguientes dañan sin aturdir indefinidamente.
    public virtual void TakeDamage(float amount)
    {
        if (IsDead || State == GolemState.Dormant || amount <= 0f)
            return;

        CurrentHealth = Mathf.Max(0f, CurrentHealth - amount);
        RestartHitFlash();

        if (CurrentHealth <= 0f)
            Die();
        else if (State != GolemState.Hurt && Time.time >= nextHurtTime)
        {
            if (action != null)
                StopCoroutine(action);

            action = StartCoroutine(Hurt());
        }
    }

    // Mostrar Hurt completo y dejar una pausa visible antes de la respuesta.
    private IEnumerator Hurt()
    {
        State = GolemState.Hurt;
        StopHorizontalMovement();
        Play(hurtState, true);

        float duration = hurtDuration;

        if (animator != null && animator.enabled && animator.runtimeAnimatorController != null &&
            animator.HasState(0, Animator.StringToHash(hurtState)))
        {
            animator.Update(0f);
            duration = Mathf.Max(duration, animator.GetCurrentAnimatorStateInfo(0).length);
        }

        yield return new WaitForSeconds(duration);

        // Darle tiempo para preparar una respuesta sin volver a entrar en Hurt.
        nextAttackTime = Mathf.Max(nextAttackTime, Time.time + hurtRetaliationDelay);
        nextHurtTime = nextAttackTime + attackWindup + 0.1f;
        State = GolemState.Idle;
        action = null;
        Play(idleState, true);
    }

    protected void RestartHitFlash()
    {
        // Daño y golpe mortal usan el mismo blanco plano y visible.
        if (hitFlash != null)
            hitFlash.Play(Color.white, 0.1f);
    }

    // Detener combate y colisiones antes de completar la animación de muerte.
    protected virtual void Die()
    {
        // Cancelar el combate sin cortar el flash del golpe mortal.
        if (action != null)
            StopCoroutine(action);

        action = null;

        State = GolemState.Dead;
        StopHorizontalMovement();
        Play(dieState, true);

        // Un collider desactivado devuelve bounds vacíos; guardar el centro antes.
        dropPosition = bodyCollider.bounds.center;

        foreach (var collider in colliders)
            collider.enabled = false;
        body.simulated = false;

        onDied.Invoke();

        if (IsDead)
            StartCoroutine(CompleteDeath());
    }

    private IEnumerator CompleteDeath()
    {
        // Esperar una sola reproducción, incluso si el clip se configuró por error en bucle.
        if (animator != null && animator.enabled && animator.runtimeAnimatorController != null &&
            animator.HasState(0, Animator.StringToHash(dieState)))
        {
            animator.Update(0f);

            while (animator != null && animator.enabled &&
                animator.GetCurrentAnimatorStateInfo(0).normalizedTime < 1f)
            {
                yield return null;
            }
        }

        // Dar un frame también cuando no haya clip para terminar de notificar la derrota.
        yield return null;

        // Incluso sin clip Die, dejar que el último impacto complete su flash.
        while (hitFlash != null && hitFlash.IsFlashing)
            yield return null;

        OnDeathAnimationFinished();
    }

    protected virtual void OnDeathAnimationFinished()
    {
        // El jefe sobrescribe este método; solo los comunes sueltan esta recompensa.
        if (durabilityDropAmount > 0f)
        {
            Vector3 position = dropPosition;

            if (durabilityPickupPrefab != null)
            {
                var pickup = Instantiate(durabilityPickupPrefab, position, Quaternion.identity);
                pickup.restoreAmount = durabilityDropAmount;
            }
        }

        Destroy(gameObject);
    }

    // Cancelar la acción pendiente y recuperar posición, vida, física y Animator.
    public virtual void ResetCombat()
    {
        StopAllCoroutines();

        if (hitFlash != null)
            hitFlash.StopAndRestore();

        nextHurtTime = 0f;
        action = null;

        transform.SetPositionAndRotation(initialPosition, initialRotation);
        transform.localScale = initialScale;
        facingRight = initialScale.x >= 0f;

        body.simulated = true;
        body.linearVelocity = Vector2.zero;
        body.angularVelocity = 0f;

        CurrentHealth = maxHealth;
        nextAttackTime = 0f;

        for (int i = 0; i < colliders.Length; i++)
            colliders[i].enabled = initialColliderStates[i];

        if (animator != null && animator.runtimeAnimatorController != null)
        {
            animator.enabled = true;
            animator.Rebind();
            animator.Update(0f);
        }

        playingState = null;
        State = GolemState.Idle;
        Play(idleState, true);
    }

    // Liberar la acción actual y establecer cuándo se permite el siguiente ataque.
    protected void FinishAction()
    {
        State = GolemState.Idle;
        nextAttackTime = Time.time + attackCooldown;
        action = null;
        Play(idleState, true);
    }

    protected void StopHorizontalMovement()
    {
        body.linearVelocityX = 0f;
    }

    // Buscar un estado por nombre y evitar reiniciarlo en cada Update.
    protected void Play(string state, bool restart = false)
    {
        if (animator == null || animator.runtimeAnimatorController == null || string.IsNullOrEmpty(state))
            return;

        if (!restart && playingState == state)
            return;

        if (!animator.HasState(0, Animator.StringToHash(state)))
            return;

        animator.Play(state, 0, 0f);
        playingState = state;
    }

    // Una acción interrumpida no debe seguir ejecutándose con el enemigo desactivado.
    protected virtual void OnDisable()
    {
        StopAllCoroutines();
        if (hitFlash != null)
            hitFlash.StopAndRestore();
        action = null;
        if (body != null)
            StopHorizontalMovement();
    }

    // Mostrar los alcances para ajustar detección y golpes desde la escena.
    protected virtual void OnDrawGizmosSelected()
    {
        if (bodyCollider == null)
            bodyCollider = GetComponent<Collider2D>();

        if (bodyCollider == null)
            return;

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(bodyCollider.bounds.center, detectionRange);

        Gizmos.color = Color.red;
        Bounds attack = GetAttackBounds();
        Gizmos.DrawWireCube(attack.center, attack.size);

        // Cian: zona revisada por la sonda de piso.
        float direction = Application.isPlaying ? (facingRight ? 1f : -1f) :
            (transform.localScale.x >= 0f ? 1f : -1f);
        Vector2 foot = GetGroundProbeOrigin(bodyCollider.bounds, direction);

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireCube(foot + Vector2.down * groundProbeDistance * 0.5f,
            new Vector2(groundProbeWidth, groundProbeDistance));

        // Detector de pared: verde si el camino está libre; rojo si ve una pared.
        // Los dos cuadros muestran el collider completo al inicio y al final del cast.
        Vector3 start = GetWallProbeCenter();
        float wallProbeDistance = bodyCollider.bounds.extents.x + 0.2f;
        Vector3 end = start + Vector3.right * direction * wallProbeDistance;
        bool wallDetected = Application.isPlaying && IsWallAhead(direction);
        Gizmos.color = wallDetected ? Color.red : Color.green;
        Gizmos.DrawWireCube(start, bodyCollider.bounds.size);
        Gizmos.DrawLine(start, end);
        Gizmos.DrawWireCube(end, bodyCollider.bounds.size);
    }
}
