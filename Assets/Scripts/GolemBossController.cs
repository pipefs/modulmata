using System.Collections;
using UnityEngine;

// Especializa el gólem común: introducción, ataques alternados y curación limitada.
public class GolemBossController : GolemController
{
    [Header("Jefe: valores provisionales, ajustar después de medir la pelea")]
    public string attackBState = "AttackB";
    public string introState = "";

    // La duración de la introducción es independiente del tiempo de los ataques.
    [Min(0f)] public float introDuration;

    // El ataque A usa los campos heredados; estos valores corresponden al ataque B.
    [Min(0f)] public float attackBDamage = 22f;
    [Min(0f)] public float attackBWindup = 1.2f;
    [Min(0f)] public float attackBRecovery = 1.6f;

    [Header("Dash anti-punto-ciego")]
    [Min(0f)] public float catchUpDashSpeed = 12f;
    [Min(0.01f)] public float stalledDistanceTime = 0.4f;
    [Min(0f)] public float catchUpMinimumGap = 0f;
    [Min(0f)] public float catchUpMaximumGap = 4f;
    [Min(0f)] public float catchUpDashDuration = 0.22f;
    [Min(0f)] public float catchUpDashCooldown = 2f;
    [Min(0f)] public float catchUpDashStopDistance = 0.2f;
    [Min(0f)] public float minimumPlayerRunSpeed = 1f;
    [Min(0f)] public float playerMovementMemory = 0.55f;
    [Min(0f)] public float gapClosingTolerance = 0.05f;
    [Min(0f)] public float blockedBeforeFleeTime = 0.12f;
    public Color catchUpDashGhostColor = new Color(0.5f, 0.95f, 1f, 0.42f);

    [Header("Áreas del jefe (coordenadas locales)")]
    public Vector2 hurtboxOffset = new Vector2(0f, -0.5f);
    public Vector2 hurtboxSize = new Vector2(5f, 7f);
    public Vector2 heavyAttackOffset = new Vector2(3.5f, -3f);
    public Vector2 heavyAttackSize = new Vector2(5f, 2.5f);

    [Header("Protección si falta un Animation Event")]
    [Min(0.1f)] public float maximumHitWindow = 0.6f;
    [Min(0.1f)] public float maximumAttackDuration = 5f;

    public bool IsVulnerable => !IsDead && State != GolemState.Dormant;

    private bool useAttackB;
    private bool currentAttackB;
    private bool attackFinished;
    private bool hitboxOpen;
    private bool hitboxUsed;
    private bool hitConnected;
    private float hitboxClosesAt;
    private Collider2D bossCollider;
    private float stalledSince = -1f;
    private float nextCatchUpDashTime;
    private float previousHorizontalGap = -1f;
    private float previousPlayerHorizontalPosition;
    private float lastPlayerMovementTime = float.NegativeInfinity;
    private float lastPlayerMovementDirection;
    private bool isTrackingPlayerPosition;
    private float blockedSince = -1f;
    private readonly RaycastHit2D[] dashHits = new RaycastHit2D[8];
    private DashAfterImage dashAfterimage;

    // La hurtbox se comprueba geométricamente: no depende de la matriz de colisiones.
    public Bounds DamageBounds => LocalBounds(hurtboxOffset, hurtboxSize);

    private Bounds LocalBounds(Vector2 offset, Vector2 size)
    {
        Vector3 scale = transform.lossyScale;
        return new Bounds(transform.TransformPoint(offset),
            new Vector3(size.x * Mathf.Abs(scale.x), size.y * Mathf.Abs(scale.y), 1f));
    }

    protected override void Awake()
    {
        base.Awake();
        State = GolemState.Dormant;
        bossCollider = GetComponent<Collider2D>();
        dashAfterimage = GetComponent<DashAfterImage>();

        if (dashAfterimage == null)
            dashAfterimage = gameObject.AddComponent<DashAfterImage>();

        // Unity entrega los eventos al objeto que contiene el Animator.
        if (animator != null && animator.gameObject != gameObject &&
            animator.GetComponent<GolluxAnimationEvents>() == null)
            animator.gameObject.AddComponent<GolluxAnimationEvents>();
    }

    protected override void Update()
    {
        if (!TryStartWallFlee())
            TryStartCatchUpDash();
        base.Update();

        var manager = GameManager.Instance;
        if (manager == null || manager.IsDead || manager.HasWon || !IsVulnerable)
        {
            DisableHitbox();
            return;
        }

        if (!hitboxOpen)
            return;

        if (Time.time >= hitboxClosesAt)
        {
            DisableHitbox();
            return;
        }

        var player = manager.CurrentPlayer;
        var target = player != null ? player.GetComponent<Collider2D>() : null;
        if (!hitConnected && target != null && target.enabled &&
            AttackBounds(currentAttackB).Intersects(target.bounds))
        {
            // Una esquiva no consume el impacto: puede acertar si la ventana sigue abierta.
            hitConnected = player.TryTakeDamage(currentAttackB ? attackBDamage : attackDamage);
        }
    }

    // Si el jugador corre junto a Gollux y la distancia no se reduce, acelera para
    // recuperar el alcance. Cuando queda prácticamente dentro de su collider,
    // sale hacia delante por completo: así no sigue "escapando" caminando.
    private void TryStartCatchUpDash()
    {
        var manager = GameManager.Instance;
        var player = manager != null ? manager.CurrentPlayer : null;
        var target = player != null ? player.GetComponent<Collider2D>() : null;
        var playerBody = player != null ? player.GetComponent<Rigidbody2D>() : null;

        if (action != null || State != GolemState.Move || target == null ||
            playerBody == null || bossCollider == null || Time.time < nextCatchUpDashTime)
        {
            ResetCatchUpCheck();
            return;
        }

        float horizontalDifference = target.bounds.center.x - bossCollider.bounds.center.x;
        float gap = Mathf.Abs(horizontalDifference);

        // Cerca del centro ambos colliders pueden solaparse. Mantener la dirección
        // actual permite atravesar ese punto ciego y reaparecer delante del jugador.
        bool isInsideBlindSpot = gap <= catchUpDashStopDistance;
        float direction = isInsideBlindSpot
            ? (facingRight ? 1f : -1f)
            : Mathf.Sign(horizontalDifference);

        // El dash anti-punto-ciego nunca puede empujar al jefe contra una pared.
        // Si la encuentra ahora, reemplaza esa acción por la huida opuesta.
        if (IsWallAhead(direction))
        {
            BeginWallFlee(direction);
            ResetCatchUpCheck();
            return;
        }

        // Fuera del punto ciego mira al jugador antes de iniciar el dash. Así el
        // ataque que sigue usa el lado correcto del jefe.
        if (!isInsideBlindSpot)
            Face(target.bounds.center.x);

        RememberPlayerMovement(player.transform.position.x);

        // La velocidad puede ser cero entre dos pulsaciones de A/D. Conservar la
        // última dirección durante un instante hace que esos cortes sigan contando
        // como una carrera junto al jefe.
        bool playerRunsAway = playerBody.linearVelocityX * direction >= minimumPlayerRunSpeed ||
            (Time.time - lastPlayerMovementTime <= playerMovementMemory &&
             lastPlayerMovementDirection * direction > 0f);
        bool validGap = gap >= catchUpMinimumGap && gap <= catchUpMaximumGap;

        if (!playerRunsAway || !validGap)
        {
            ResetCatchUpCheck();
            return;
        }

        bool isNotClosingGap = previousHorizontalGap >= 0f &&
            gap >= previousHorizontalGap - gapClosingTolerance;

        if (isNotClosingGap)
        {
            if (stalledSince < 0f)
                stalledSince = Time.time;

            if (Time.time >= stalledSince + stalledDistanceTime)
            {
                action = StartCoroutine(CatchUpDash(direction, isInsideBlindSpot));
                nextCatchUpDashTime = Time.time + catchUpDashCooldown;
                ResetCatchUpCheck();
                return;
            }
        }
        else
        {
            stalledSince = -1f;
        }

        previousHorizontalGap = gap;
    }

    private void RememberPlayerMovement(float currentPosition)
    {
        if (!isTrackingPlayerPosition)
        {
            previousPlayerHorizontalPosition = currentPosition;
            isTrackingPlayerPosition = true;
            return;
        }

        float displacement = currentPosition - previousPlayerHorizontalPosition;
        previousPlayerHorizontalPosition = currentPosition;

        if (Mathf.Abs(displacement) > 0.001f)
        {
            lastPlayerMovementTime = Time.time;
            lastPlayerMovementDirection = Mathf.Sign(displacement);
        }
    }

    // Si se encuentra una pared mientras persigue, girar y usar un dash para
    // despegarse de ella. Al terminar vuelve a Move y retoma la persecución.
    private bool TryStartWallFlee()
    {
        if (action != null || State != GolemState.Move || bossCollider == null)
        {
            blockedSince = -1f;
            return false;
        }

        float currentDirection = facingRight ? 1f : -1f;
        if (IsWallAhead(currentDirection))
        {
            blockedSince = -1f;
            BeginWallFlee(currentDirection);
            return true;
        }

        // Respaldo para paredes cuyo collider no aparece en el raycast: si el jefe
        // quiso avanzar, pero quedó inmóvil, huye al lado contrario sin consultar
        // el normal de la pared.
        bool isBlocked = Mathf.Abs(body.linearVelocityX) < 0.05f;
        if (!isBlocked)
        {
            blockedSince = -1f;
            return false;
        }

        if (blockedSince < 0f)
        {
            blockedSince = Time.time;
            return true;
        }

        if (Time.time < blockedSince + blockedBeforeFleeTime)
            return true;

        blockedSince = -1f;
        BeginWallFlee(currentDirection);
        return true;
    }

    private void BeginWallFlee(float wallDirection)
    {
        float fleeDirection = -wallDirection;
        SetFacingDirection(fleeDirection);
        action = StartCoroutine(FleeFromWall(fleeDirection));
    }

    private IEnumerator CatchUpDash(float direction, bool leavesBlindSpot)
    {
        // Un rastro breve hace visible el movimiento incluso sin una animación exclusiva.
        if (dashAfterimage != null)
        {
            // Gollux y los tilemaps están en el mismo sorting layer. El eco va un
            // orden por encima para que no quede oculto detrás del escenario.
            dashAfterimage.Begin(catchUpDashDuration, 0.04f, 0.13f,
                catchUpDashGhostColor, sortingOrderOffset: 1);
        }

        float endsAt = Time.time + catchUpDashDuration;
        var filter = new ContactFilter2D();
        filter.SetLayerMask(groundLayer);
        filter.useTriggers = false;

        while (Time.time < endsAt)
        {
            var manager = GameManager.Instance;
            var player = manager != null ? manager.CurrentPlayer : null;
            var target = player != null ? player.GetComponent<Collider2D>() : null;

            if (target == null || manager.IsDead || manager.HasWon)
                break;

            float horizontalDifference = target.bounds.center.x - bossCollider.bounds.center.x;
            float gap = Mathf.Abs(horizontalDifference);

            // En una persecución normal termina a distancia de golpe. Si el jugador
            // está dentro del punto ciego, completa el dash para salir de él y luego
            // gira para atacar desde el lado opuesto.
            if (!leavesBlindSpot &&
                (horizontalDifference * direction <= 0f || gap <= catchUpDashStopDistance))
                break;

            float distance = GetSafeDashDistance(direction, filter);

            body.linearVelocityX = direction * distance / Time.fixedDeltaTime;
            yield return new WaitForFixedUpdate();
        }

        StopHorizontalMovement();

        if (dashAfterimage != null)
            dashAfterimage.Stop();

        // Liberar Update: en el siguiente frame decide si ya corresponde atacar.
        action = null;
    }

    private IEnumerator FleeFromWall(float direction)
    {
        State = GolemState.Flee;

        // Mostrar el giro antes de aplicar velocidad. De esta forma el dash siempre
        // se lee como una huida y no como un intento de atravesar la pared.
        yield return null;

        if (dashAfterimage != null)
            dashAfterimage.Begin(catchUpDashDuration, 0.04f, 0.13f,
                catchUpDashGhostColor, sortingOrderOffset: 1);

        float endsAt = Time.time + catchUpDashDuration;
        var filter = new ContactFilter2D();
        filter.SetLayerMask(groundLayer);
        filter.useTriggers = false;

        while (Time.time < endsAt)
        {
            float distance = GetSafeDashDistance(direction, filter);
            if (distance <= 0f)
                break;

            body.linearVelocityX = direction * distance / Time.fixedDeltaTime;
            yield return new WaitForFixedUpdate();
        }

        StopHorizontalMovement();

        if (dashAfterimage != null)
            dashAfterimage.Stop();

        State = GolemState.Move;
        action = null;
    }

    private float GetSafeDashDistance(float direction, ContactFilter2D filter)
    {
        float distance = catchUpDashSpeed * Time.fixedDeltaTime;
        int hitCount = bossCollider.Cast(Vector2.right * direction, filter, dashHits, distance + 0.02f);

        for (int i = 0; i < hitCount; i++)
        {
            var hit = dashHits[i];

            if (hit.normal.x * direction < -0.5f)
                distance = Mathf.Min(distance, Mathf.Max(0f, hit.distance - 0.02f));
        }

        return distance;
    }

    private void ResetCatchUpCheck()
    {
        stalledSince = -1f;
        previousHorizontalGap = -1f;
    }

    private Bounds AttackBounds(bool heavy)
    {
        if (heavy)
            return LocalBounds(heavyAttackOffset, heavyAttackSize);

        Vector3 center = attackPoint != null ? attackPoint.position :
            transform.TransformPoint(new Vector3(2.2f, -3f, 0f));
        return new Bounds(center, new Vector3(attackSize.x, attackSize.y, 1f));
    }

    protected override bool IsInsideAttackArea(Collider2D target)
    {
        // El pesado puede iniciarse desde más lejos. Nunca golpear solo por solaparse.
        return target != null && target.enabled &&
            (AttackBounds(false).Intersects(target.bounds) || AttackBounds(true).Intersects(target.bounds));
    }

    // Punto de entrada utilizado por BossArena al cruzar su trigger.
    public void BeginFight()
    {
        if (State != GolemState.Dormant || IsDead)
            return;

        action = StartCoroutine(Introduction());
    }

    // Mantener al jefe inactivo mientras transcurre su introducción.
    private IEnumerator Introduction()
    {
        State = GolemState.Dormant;
        Play(introState, true);
        // La corrutina siempre cede un frame para conservar una referencia válida.
        yield return null;

        if (!string.IsNullOrEmpty(introState) && introDuration > 0f)
            yield return new WaitForSeconds(introDuration);

        FinishAction();
    }

    // Los eventos del clip abren/cierra el daño; esta corrutina solo espera el final.
    protected override IEnumerator Attack()
    {
        var player = GameManager.Instance.CurrentPlayer;
        var target = player != null ? player.GetComponent<Collider2D>() : null;
        currentAttackB = useAttackB || target == null ||
            !AttackBounds(false).Intersects(target.bounds);
        useAttackB = !currentAttackB;
        attackFinished = false;
        hitboxUsed = false;
        hitConnected = false;
        hitboxOpen = false;

        State = GolemState.Attack;
        Play(currentAttackB ? attackBState : attackState, true);

        float deadline = Time.time + maximumAttackDuration;
        while (!attackFinished && Time.time < deadline)
            yield return null;

        if (!attackFinished)
            Debug.LogWarning("Gollux: falta FinishAttack o el clip supera el tiempo máximo.", this);

        DisableHitbox();
        State = GolemState.Recovering;
        yield return new WaitForSeconds(currentAttackB ? attackBRecovery : attackRecovery);

        FinishAction();
    }

    public void TriggerHitbox()
    {
        if (State != GolemState.Attack || hitboxUsed || attackFinished)
            return;

        hitboxUsed = true;
        hitboxOpen = true;
        hitboxClosesAt = Time.time + maximumHitWindow;
    }

    public void DisableHitbox()
    {
        hitboxOpen = false;
    }

    public void FinishAttack()
    {
        if (State != GolemState.Attack)
            return;

        DisableHitbox();
        attackFinished = true;
    }

    // Cada impacto causa daño y flash. No interrumpir clips que necesitan sus eventos.
    public override void TakeDamage(float amount)
    {
        if (!IsVulnerable || amount <= 0f)
            return;

        CurrentHealth = Mathf.Max(0f, CurrentHealth - amount);
        RestartHitFlash();

        if (CurrentHealth <= 0f)
            Die();

        // Hurt no reinicia la ventana de recuperación ni permite aturdir al jefe indefinidamente.
    }

    // Además del reinicio común, restaurar la alternancia y la espera inicial.
    public override void ResetCombat()
    {
        DisableHitbox();
        attackFinished = true;
        base.ResetCombat();

        useAttackB = false;

        State = GolemState.Dormant;
    }

    protected override void Die()
    {
        DisableHitbox();
        base.Die();
    }

    protected override void OnDisable()
    {
        DisableHitbox();
        base.OnDisable();
    }

    protected override void OnDrawGizmosSelected()
    {
        base.OnDrawGizmosSelected();
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireCube(DamageBounds.center, DamageBounds.size);
        Gizmos.color = Color.magenta;
        Bounds heavy = AttackBounds(true);
        Gizmos.DrawWireCube(heavy.center, heavy.size);
    }

    protected override void OnDeathAnimationFinished()
    {
        // Conservar al jefe derrotado para las consultas de BossArena y VictoryTrigger.
        if (animator != null)
            animator.enabled = false;
    }
}
