using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Gollux conserva la física y las capacidades comunes del gólem.
public class GolemBossController : GolemController
{
    [Header("Ataque B")]
    [SerializeField] private string attackBState = "AttackB";
    [SerializeField, Min(0f)] private float attackBDamage = 22f;
    [SerializeField, Min(0f)] private float attackBRecovery = 1.6f;

    [Header("Apertura del impacto")]
    [Tooltip("Segundos de juego desde el inicio del ataque. Considerar la velocidad del Animator; no son segundos del clip.")]
    [SerializeField, Min(0f)] private float attackAOpensAt = 0.5f;
    [Tooltip("Segundos de juego desde el inicio del ataque. Considerar la velocidad del Animator; no son segundos del clip.")]
    [SerializeField, Min(0f)] private float attackBOpensAt = 0.41666666f;

    [Header("Dash de persecución y salida del punto ciego")]
    [SerializeField, Min(0f)] private float catchUpDashSpeed = 12f;
    [SerializeField, Min(0.01f)] private float stalledDistanceTime = 0.4f;
    [SerializeField, Min(0f)] private float catchUpMinimumGap;
    [SerializeField, Min(0f)] private float catchUpMaximumGap = 4f;
    [SerializeField, Min(0f)] private float catchUpDashDuration = 0.22f;
    [SerializeField, Min(0f)] private float catchUpDashCooldown = 2f;
    [Tooltip("Separación a la que termina la persecución. El punto ciego se detecta por solapamiento de los colliders.")]
    [SerializeField, Min(0f)] private float catchUpDashStopDistance = 0.2f;
    [SerializeField, Min(0f)] private float minimumPlayerRunSpeed = 1f;
    [SerializeField, Min(0f)] private float playerMovementMemory = 0.55f;
    [SerializeField, Min(0f)] private float gapClosingTolerance = 0.05f;
    [SerializeField, Min(0f)] private float blockedBeforeFleeTime = 0.12f;
    [SerializeField] private Color catchUpDashGhostColor = new Color(0.5f, 0.95f, 1f, 0.42f);

    [Header("Áreas del jefe (coordenadas locales)")]
    [SerializeField] private Vector2 hurtboxOffset = new Vector2(0f, -0.5f);
    [SerializeField] private Vector2 hurtboxSize = new Vector2(5f, 7f);
    [SerializeField] private Vector2 heavyAttackOffset = new Vector2(3.5f, -3f);
    [SerializeField] private Vector2 heavyAttackSize = new Vector2(5f, 2.5f);

    private const float HitboxDuration = 0.15f;
    private const float AnimationStallTimeout = 1f;
    private const float ReturnStallTimeout = 2f;
    private const float PositionTolerance = 0.001f;
    private const float HomeTolerance = 0.03f;
    private const float CollisionSkin = 0.02f;
    private const float GhostInterval = 0.04f;
    private const float GhostLifetime = 0.13f;
    // Motivo del dash, utilizado para elegir distancia y comportamiento al encontrar obstáculos.
    private enum DashMode
    {
        Pursuit,
        EscapeBlindSpot,
        FleeWall
    }

    public bool IsVulnerable => !IsDead && State != GolemState.Dormant && State != GolemState.Returning;
    public bool HasReturnedHome { get; private set; } = true;
    public override Bounds DamageBounds => LocalBounds(hurtboxOffset, hurtboxSize);

    private DashAfterImage dashAfterimage;
    private readonly RaycastHit2D[] dashHits = new RaycastHit2D[8];
    private readonly WaitForFixedUpdate dashStep = new WaitForFixedUpdate();
    private readonly List<AnimatorClipInfo> attackClips = new List<AnimatorClipInfo>(2);
    private bool nextAttackIsB;
    private int actionVersion;
    private float nextCatchUpDashTime;

    // Historial de persecución; se descarta al cambiar de acción u objetivo.
    private PlayerController trackedPlayer;
    private Rigidbody2D trackedPlayerBody;
    private float stalledSince = -1f;
    private float previousHorizontalGap = -1f;
    private float previousPlayerHorizontalPosition;
    private float lastPlayerMovementTime = float.NegativeInfinity;
    private float lastPlayerMovementDirection;
    private float blockedSince = -1f;
    private float blockedPosition;
    private float returnDeadline;
    private float returnStalledTime;
    private float closestReturnDistance;

    protected override void Awake()
    {
        base.Awake();
        if (!IsInitialized)
        {
            return;
        }

        State = GolemState.Dormant;
        dashAfterimage = GetComponent<DashAfterImage>();
    }

    protected override void Update()
    {
        if (IsDead || State == GolemState.Dormant || State == GolemState.Returning)
        {
            return;
        }

        base.Update();
        if (State != GolemState.Move || action != null)
        {
            ResetPursuit();
        }
    }

    // La base llama aquí solo después de descartar un ataque y orientar al objetivo.
    protected override void Pursue(PlayerController player, Collider2D target)
    {
        TrackPlayer(player);
        float direction = facingRight ? 1f : -1f;
        if (IsWallAhead(direction))
        {
            BeginDash(-direction, DashMode.FleeWall);
            return;
        }
        if (!HasGroundAhead(direction))
        {
            StopHorizontalMovement();
            Play(idleState);
            ResetPursuit();
            return;
        }
        if (HasStalledWhileMoving())
        {
            BeginDash(-direction, DashMode.FleeWall);
            return;
        }
        if (!TryStartCatchUpDash(target))
        {
            SetWalkingVelocity(direction * moveSpeed);
        }
    }

    private void TrackPlayer(PlayerController player)
    {
        if (trackedPlayer != player)
        {
            ResetPursuit();
            trackedPlayer = player;
            trackedPlayerBody = player.GetComponent<Rigidbody2D>();
            previousPlayerHorizontalPosition = player.transform.position.x;
            return;
        }
        float position = player.transform.position.x;
        float displacement = position - previousPlayerHorizontalPosition;
        previousPlayerHorizontalPosition = position;
        if (Mathf.Abs(displacement) <= PositionTolerance)
        {
            return;
        }

        lastPlayerMovementTime = Time.time;
        lastPlayerMovementDirection = Mathf.Sign(displacement);
    }

    private bool HasStalledWhileMoving()
    {
        if (moveSpeed <= 0f)
        {
            blockedSince = -1f;
            return false;
        }
        float position = transform.position.x;
        if (blockedSince < 0f || Mathf.Abs(position - blockedPosition) > PositionTolerance)
        {
            blockedSince = Time.time;
            blockedPosition = position;
            return false;
        }
        return Time.time > blockedSince && Time.time - blockedSince >= blockedBeforeFleeTime;
    }

    private bool TryStartCatchUpDash(Collider2D target)
    {
        if (trackedPlayerBody == null || Time.time < nextCatchUpDashTime)
        {
            ResetGapTracking();
            return false;
        }
        float difference = target.bounds.center.x - bodyCollider.bounds.center.x;
        float gap = Mathf.Abs(difference);
        var separation = bodyCollider.Distance(target);
        bool insideBlindSpot = separation.isValid && separation.isOverlapped;
        float direction = insideBlindSpot ? (facingRight ? 1f : -1f) : Mathf.Sign(difference);
        bool playerRunsAway = trackedPlayerBody.linearVelocityX * direction >= minimumPlayerRunSpeed ||
            (Time.time - lastPlayerMovementTime <= playerMovementMemory && lastPlayerMovementDirection * direction > 0f);
        if (!playerRunsAway || gap < catchUpMinimumGap || gap > catchUpMaximumGap ||
            (!insideBlindSpot && gap <= catchUpDashStopDistance))
        {
            ResetGapTracking();
            return false;
        }
        if (previousHorizontalGap >= 0f && gap >= previousHorizontalGap - gapClosingTolerance)
        {
            if (stalledSince < 0f)
            {
                stalledSince = Time.time;
            }

            if (Time.time - stalledSince >= stalledDistanceTime)
            {
                nextCatchUpDashTime = Time.time + catchUpDashCooldown;
                BeginDash(direction, insideBlindSpot ? DashMode.EscapeBlindSpot : DashMode.Pursuit);
                return true;
            }
        }
        else
        {
            stalledSince = -1f;
        }

        previousHorizontalGap = gap;
        return false;
    }

    private void BeginDash(float direction, DashMode mode)
    {
        ResetPursuit();
        SetFacingDirection(direction);
        action = StartCoroutine(Dash(direction, mode));
    }

    private IEnumerator Dash(float direction, DashMode mode)
    {
        int version = actionVersion;
        State = GolemState.Flee;
        StopHorizontalMovement();
        Play(moveState);
        // Ceder antes de cualquier salida permite asignar la referencia de la corrutina.
        yield return null;
        if (version != actionVersion)
        {
            yield break;
        }

        var manager = GameManager.Instance;
        var player = manager != null ? manager.CurrentPlayer : null;
        var target = player != null ? player.GetComponent<Collider2D>() : null;
        if (player == null || !player.IsPlaying || target == null)
        {
            FinishAction();
            yield break;
        }
        if (dashAfterimage != null)
        {
            dashAfterimage.Begin(catchUpDashDuration, GhostInterval, GhostLifetime,
                catchUpDashGhostColor, sortingOrderOffset: 1);
        }

        var filter = new ContactFilter2D();
        filter.SetLayerMask(groundLayer);
        filter.useTriggers = false;
        float endsAt = Time.time + catchUpDashDuration;
        while (Time.time < endsAt && player != null && player.IsPlaying && target != null && target.enabled)
        {
            if (version != actionVersion)
            {
                yield break;
            }

            float difference = target.bounds.center.x - bodyCollider.bounds.center.x;
            if (mode == DashMode.Pursuit &&
                (difference * direction <= 0f || Mathf.Abs(difference) <= catchUpDashStopDistance))
            {
                break;
            }

            float distance = GetSafeDashDistance(direction, filter);
            if (distance <= 0f)
            {
                break;
            }

            body.linearVelocityX = direction * distance / Time.fixedDeltaTime;
            yield return dashStep;
        }
        if (version != actionVersion)
        {
            yield break;
        }

        StopHorizontalMovement();
        if (dashAfterimage != null)
        {
            dashAfterimage.Stop();
        }

        State = GolemState.Move;
        action = null;
    }

    private float GetSafeDashDistance(float direction, ContactFilter2D filter)
    {
        float distance = catchUpDashSpeed * Time.fixedDeltaTime;
        int count = bodyCollider.Cast(Vector2.right * direction, filter, dashHits, distance + CollisionSkin);
        for (int i = 0; i < count; i++)
        {
            var hit = dashHits[i];
            if (hit.normal.x * direction < -0.5f && !Physics2D.GetIgnoreCollision(bodyCollider, hit.collider))
            {
                distance = Mathf.Min(distance, Mathf.Max(0f, hit.distance - CollisionSkin));
            }
        }
        // Revisar el recorrido por pasos evita saltar una junta sin suelo durante el dash.
        float step = Mathf.Max(0.01f, groundProbeWidth * 0.5f);
        if (!HasGroundAhead(direction))
        {
            return 0f;
        }

        for (float advance = Mathf.Min(step, distance); advance <= distance && advance > 0f;)
        {
            if (!HasGroundAhead(direction, advance))
            {
                return 0f;
            }

            if (advance >= distance)
            {
                break;
            }

            advance = Mathf.Min(advance + step, distance);
        }
        return distance;
    }

    private void ResetGapTracking()
    {
        stalledSince = -1f;
        previousHorizontalGap = -1f;
    }

    private void ResetPursuit()
    {
        ResetGapTracking();
        trackedPlayer = null;
        trackedPlayerBody = null;
        lastPlayerMovementTime = float.NegativeInfinity;
        lastPlayerMovementDirection = 0f;
        blockedSince = -1f;
    }

    private Bounds LocalBounds(Vector2 offset, Vector2 size)
    {
        Vector3 scale = transform.lossyScale;
        return new Bounds(transform.TransformPoint(offset),
            new Vector3(size.x * Mathf.Abs(scale.x), size.y * Mathf.Abs(scale.y), 1f));
    }

    // A conserva su tamaño en mundo; B y la hurtbox conservan su tamaño local.
    protected override Bounds GetAttackBounds()
    {
        Vector3 center = attackPoint != null ? attackPoint.position :
            transform.TransformPoint(new Vector3(2.2f, -3f, 0f));
        return new Bounds(center, new Vector3(attackSize.x, attackSize.y, 1f));
    }

    private Bounds AttackBounds(bool heavy)
    {
        return heavy ? LocalBounds(heavyAttackOffset, heavyAttackSize) : GetAttackBounds();
    }

    protected override bool IsInsideAttackArea(Collider2D target)
    {
        return target != null && target.enabled &&
        (AttackBounds(false).Intersects(target.bounds) || AttackBounds(true).Intersects(target.bounds));
    }

    public void BeginFight()
    {
        if (!IsInitialized || !isActiveAndEnabled)
        {
            return;
        }

        if (State == GolemState.Dormant && HasReturnedHome && action == null)
        {
            FinishAction();
        }
    }

    // Elige un ataque, sincroniza su ventana de daño con el Animator y espera su recuperación real.
    protected override IEnumerator Attack()
    {
        int version = actionVersion;
        State = GolemState.Attack;
        var manager = GameManager.Instance;
        var player = manager != null ? manager.CurrentPlayer : null;
        var target = player != null ? player.GetComponent<Collider2D>() : null;
        bool heavy = nextAttackIsB || target == null || !GetAttackBounds().Intersects(target.bounds);
        nextAttackIsB = !heavy;
        string animation = heavy ? attackBState : attackState;
        int stateHash = Animator.StringToHash(animation);
        int idleHash = Animator.StringToHash(idleState);
        float damage = heavy ? attackBDamage : attackDamage;
        float recovery = heavy ? attackBRecovery : attackRecovery;
        float opens = Mathf.Max(0f, heavy ? attackBOpensAt : attackAOpensAt);
        // Asignar action antes de ejecutar callbacks o terminar por configuración inválida.
        yield return null;
        if (version != actionVersion)
        {
            yield break;
        }

        if (player == null || !player.IsPlaying || target == null)
        {
            FinishAction();
            yield break;
        }
        if (!AnimatorReady || string.IsNullOrEmpty(animation) || !animator.HasState(0, stateHash))
        {
            Debug.LogWarning("Gollux: no puede iniciar la animación de ataque " + animation, this);
            FinishAction();
            yield break;
        }
        Play(animation, true);
        animator.Update(0f);
        var state = animator.GetCurrentAnimatorStateInfo(0);
        float length = ReadAttackClipLength(state);
        if (!Matches(state, stateHash) || !(length > 0f) || float.IsInfinity(length))
        {
            Debug.LogWarning("Gollux: el estado de ataque no contiene una animación válida.", this);
            FinishAction();
            yield break;
        }
        float speed = AnimationSpeed(state);
        float deadline = AnimationDeadline(length, state.normalizedTime, speed);
        float lastProgress = state.normalizedTime;
        float stalled = 0f;
        float startedAt = Time.time;
        float elapsed = 0f;
        bool connected = false;
        bool normalExit = false;
        while (true)
        {
            if (version != actionVersion)
            {
                yield break;
            }

            if (player == null || !player.IsPlaying)
            {
                FinishAction();
                yield break;
            }

            if (!AnimatorReady)
            {
                WarnInterruptedAttack();
                break;
            }
            state = animator.GetCurrentAnimatorStateInfo(0);
            if (!Matches(state, stateHash))
            {
                // Un frame largo también puede abarcar toda la transición normal.
                if (!Matches(state, idleHash))
                {
                    WarnInterruptedAttack();
                }

                break;
            }
            float currentSpeed = AnimationSpeed(state);
            if (!(currentSpeed > 0f) || float.IsInfinity(currentSpeed))
            {
                WarnInterruptedAttack();
                break;
            }
            bool exiting = animator.IsInTransition(0);
            if (exiting && !Matches(animator.GetNextAnimatorStateInfo(0), idleHash))
            {
                WarnInterruptedAttack();
                break;
            }
            if (exiting && !normalExit)
            {
                normalExit = true;
                lastProgress = -1f;
                stalled = 0f;
                deadline = Time.time + Mathf.Max(AnimationStallTimeout, length / currentSpeed * 2f);
            }
            float previous = elapsed;
            elapsed = Time.time - startedAt;
            float activeElapsed = state.normalizedTime >= 1f
                ? elapsed - (state.normalizedTime - 1f) * length / currentSpeed : elapsed;
            if (!connected && activeElapsed >= opens && previous <= opens + HitboxDuration && target != null && target.enabled &&
                AttackBounds(heavy).Intersects(target.bounds))
            {
                connected = player.TryTakeDamage(damage);
            }

            if (version != actionVersion)
            {
                yield break;
            }

            if (!exiting && state.normalizedTime >= 1f)
            {
                break;
            }

            if (!Mathf.Approximately(speed, currentSpeed))
            {
                deadline = exiting ? Time.time + Mathf.Max(AnimationStallTimeout, length / currentSpeed * 2f)
                    : AnimationDeadline(length, state.normalizedTime, currentSpeed);
            }

            speed = currentSpeed;
            float progress = exiting ? animator.GetAnimatorTransitionInfo(0).normalizedTime : state.normalizedTime;
            if (progress > lastProgress + 0.00001f)
            {
                lastProgress = progress;
                stalled = 0f;
            }
            else
            {
                stalled += Time.deltaTime;
            }

            if (stalled >= AnimationStallTimeout || Time.time >= deadline)
            {
                Debug.LogWarning("Gollux: la animación de ataque no termina; se cancela por seguridad.", this);
                break;
            }
            yield return null;
        }
        State = GolemState.Recovering;
        yield return new WaitForSeconds(recovery);
        if (version == actionVersion)
        {
            FinishAction();
        }
    }

    private bool AnimatorReady => animator != null && animator.isActiveAndEnabled && animator.runtimeAnimatorController != null;
    private static bool Matches(AnimatorStateInfo state, int hash)
    {
        return state.shortNameHash == hash || state.fullPathHash == hash;
    }

    private float AnimationSpeed(AnimatorStateInfo state)
    {
        return animator.speed * state.speed * state.speedMultiplier;
    }

    private void WarnInterruptedAttack()
    {
        Debug.LogWarning("Gollux: ataque interrumpido o Animator detenido/desactivado.", this);
    }

    private float ReadAttackClipLength(AnimatorStateInfo state)
    {
        animator.GetCurrentAnimatorClipInfo(0, attackClips);
        float length = state.length;
        float weight = -1f;
        foreach (var clip in attackClips)
        {
            if (clip.clip == null || clip.weight <= weight)
            {
                continue;
            }

            length = clip.clip.length;
            weight = clip.weight;
        }
        return length;
    }

    private static float AnimationDeadline(float length, float progress, float speed)
    {
        return Time.time + Mathf.Max(0f, 1f - progress) * length / Mathf.Max(0.01f, speed) * 2f + 0.5f;
    }

    // Suspender o invalidar una acción también retira todo su estado temporal.
    private void InvalidateAction()
    {
        actionVersion++;
        dashAfterimage?.Stop();
        ResetPursuit();
    }

    public override void SuspendCombat()
    {
        InvalidateAction();
        base.SuspendCombat();
    }

    // Cancela el combate y comienza el regreso físico que BossArena espera antes de reiniciar.
    public void BeginReturnHome()
    {
        if (!IsInitialized || IsDead || !isActiveAndEnabled || State == GolemState.Returning)
        {
            return;
        }

        SuspendCombat();
        HasReturnedHome = false;
        State = GolemState.Returning;
        closestReturnDistance = Mathf.Abs(initialPosition.x - transform.position.x);
        returnStalledTime = 0f;
        returnDeadline = Time.time + closestReturnDistance / Mathf.Max(moveSpeed, 0.1f) * 3f + ReturnStallTimeout;
    }

    // Mueve el regreso por física y detecta bloqueos para no dejar la arena esperando indefinidamente.
    private void FixedUpdate()
    {
        if (State != GolemState.Returning)
        {
            return;
        }

        float difference = initialPosition.x - transform.position.x;
        float distance = Mathf.Abs(difference);
        if (distance <= HomeTolerance)
        {
            CompleteReturnHome();
            return;
        }
        if (distance < closestReturnDistance - PositionTolerance)
        {
            closestReturnDistance = distance;
            returnStalledTime = 0f;
        }
        else
        {
            returnStalledTime += Time.fixedDeltaTime;
        }

        if (returnStalledTime >= ReturnStallTimeout || Time.time >= returnDeadline)
        {
            Debug.LogWarning("Gollux: regreso bloqueado; se recupera la posición inicial para liberar el siguiente intento.", this);
            CompleteReturnHome();
            return;
        }
        SetFacingDirection(Mathf.Sign(difference));
        MoveForward();
        float maximumSpeed = distance / Time.fixedDeltaTime;
        body.linearVelocityX = Mathf.Clamp(body.linearVelocityX, -maximumSpeed, maximumSpeed);
    }

    private void CompleteReturnHome()
    {
        action = null;
        body.linearVelocity = Vector2.zero;
        body.angularVelocity = 0f;
        RestoreInitialPose();
        HasReturnedHome = true;
        State = GolemState.Dormant;
        Play(idleState, true);
    }

    public override void ResetCombat()
    {
        if (!IsInitialized)
        {
            return;
        }

        InvalidateAction();
        base.ResetCombat();
        nextAttackIsB = false;
        nextCatchUpDashTime = 0f;
        State = GolemState.Dormant;
        HasReturnedHome = true;
    }

    public override bool TryTakeDamage(float amount)
    {
        if (!IsInitialized || !isActiveAndEnabled || !IsVulnerable)
        {
            return false;
        }

        if (!(amount > 0f) || float.IsInfinity(amount))
        {
            return false;
        }

        CurrentHealth = Mathf.Max(0f, CurrentHealth - amount);
        RestartHitFlash();
        if (CurrentHealth <= 0f)
        {
            Die();
        }

        return true;
    }

    protected override void Die()
    {
        InvalidateAction();
        base.Die();
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        if (!IsInitialized)
        {
            return;
        }
        // Desactivar durante el regreso no debe perder la espera que mantiene BossArena.
        if (!HasReturnedHome && !IsDead)
        {
            BeginReturnHome();
        }
    }

    protected override void OnDisable()
    {
        InvalidateAction();
        base.OnDisable();
    }

    protected override void DropReward()
    {
    }

    protected override void OnDeathAnimationFinished()
    {
        if (animator != null)
        {
            animator.enabled = false;
        }
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
}
