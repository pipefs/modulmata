using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// ? Realiza las acciones basicas del jugador (ej: moverse, saltar)
// ? tambien pregunta a "PlayerModuleManager" por "activeModule"
// ? para potenciar la accion basica o poder realizar una nueva acción
// ? dependiendo del tipo de habilidad del modulo.

public class PlayerController : MonoBehaviour
{
    private Controles controles;
    private Collider2D playerCollider;
    public Animator animator;
    private Rigidbody2D rigid;
    private PlayerModuleManager moduleManager;

    public Transform cameraTarget;

    [Header("Movimiento")]
    [SerializeField] private float moveSpeed;
    [SerializeField] private float acceleration = 60f;
    [SerializeField] private float deceleration = 45f;
    [SerializeField] private float attackMoveMultiplier = 0.4f;

    [Header("Dash (Shift, solo desde el suelo)")]
    [SerializeField, Min(0.01f)] private float dashDuration = 0.18f;
    [SerializeField, Min(0f)] private float dashSpeed = 16f;
    [SerializeField, Min(0f)] private float dashCooldown = 0.8f;
    [SerializeField, Min(0f)] private float dashInvulnerability = 0.12f;

    [Header("Efecto visual del Dash")]
    [SerializeField, Min(0.01f)] private float dashGhostInterval = 0.04f;
    [SerializeField, Min(0.01f)] private float dashGhostLifetime = 0.16f;
    [SerializeField] private Color dashGhostColor = new Color(0.2f, 1f, 0.92f, 0.55f);

    private InputAction interactInput;
    private bool isDashing;
    private float dashEndsAt;
    private float nextDashTime;
    private float dashInvulnerableUntil;
    private float dashDirection;
    private readonly RaycastHit2D[] dashHits = new RaycastHit2D[8];
    private DashAfterImage dashAfterimage;
    private bool controlsLocked;

    [Header("Salto")]
    [SerializeField] private float jumpForce;
    [SerializeField] private float defaultGravity = 3f;
    [SerializeField] private float fallGravity = 5.4f;

    [Header("Sonidos de acción")]
    [SerializeField] private AudioSource actionAudioSource;
    [SerializeField] private AudioClip jabSound;
    [SerializeField] private AudioClip crossSound;
    [SerializeField] private AudioClip dashSound;
    [SerializeField] private AudioClip jumpSound;

    [Header("Detección del Suelo")]
    [SerializeField] private Transform groundCheckPoint;
    [SerializeField] private Vector2 groundCheckSize = new Vector2(0.5f, 0.1f);
    [SerializeField] private LayerMask groundLayer;
    private bool isGrounded;

    [Header("Combate")]
    [SerializeField] private Transform attackCheckPoint;
    [SerializeField] private Vector2 attackSize;
    [SerializeField] private LayerMask enemyLayer;

    [Tooltip("Valores provisionales para las pruebas de combate.")]
    [SerializeField] private float jabDamage = 8f;
    [SerializeField] private float crossDamage = 16f;
    [SerializeField] private float damageInvulnerability = 0.75f;

    // Tiempo hasta el que se ignoran nuevos impactos después de recibir daño.
    private float invulnerableUntil;
    private BatterySystem battery;

    [Space]

    [SerializeField] private float jabDuration = 0.3f;        // Cuánto dura el Jab en segundos
    [SerializeField] private float jabHitTime = 0.1f;        // Momento exacto en que sale el golpe del Jab
    [SerializeField] private float crossDuration = 0.6f;     // Cross: mayor anticipación y compromiso
    [SerializeField] private float crossHitTime = 0.3f;

    // Enfriamientos independientes: Jab permite dos usos y Cross uno.
    // La pausa de cada tipo empieza al terminar o cancelar su último golpe.
    [SerializeField] private float jabRecovery = 0.25f;
    [SerializeField] private float crossRecovery = 0.55f;

    [Header("Ayudas")]
    [SerializeField] private float coyoteTime = 0.12f;
    [SerializeField] private float jumpBufferTime = 0.1f;

    // Timers
    private float coyoteTimer;
    private float jumpBufferTimer;
    private float attackTimer;
    private float nextJabTime;
    private float nextCrossTime;
    private int jabsUsed;

    // Estados
    private bool canDoubleJump;
    private bool doubleJumped; // Ya realizo el doble salto.
    private bool isFacingRight = true;
    // Última animación solicitada: incluye golpes, para no confundirlos con Walk.
    private string currentAnimation;
    private bool isAttacking;
    private enum PunchType { Jab, Cross }
    private PunchType currentPunch;
    private PunchType? queuedPunch;
    private bool hasHitboxTriggered;

    // Inputs
    private float moveInput;

    // Flash compartido por impactos y recogibles.
    private SpriteFlash visualFlash;
    private GameManager manager;

    private void Awake()
    {
        playerCollider = GetComponent<Collider2D>();
        rigid = GetComponent<Rigidbody2D>();
        animator = GetComponent<Animator>();
        moduleManager = GetComponent<PlayerModuleManager>();
        controles = new Controles();

        battery = GetComponent<BatterySystem>();
        actionAudioSource ??= GetComponent<AudioSource>();
        dashAfterimage = GetComponent<DashAfterImage>();

        if (dashAfterimage == null)
            dashAfterimage = gameObject.AddComponent<DashAfterImage>();

        visualFlash = GetComponent<SpriteFlash>();

        if (visualFlash == null)
            visualFlash = gameObject.AddComponent<SpriteFlash>();

        rigid.gravityScale = defaultGravity;
    }

    private void Start()
    {
        manager = GameManager.Instance;
    }

    private void OnEnable()
    {
        controles?.Gameplay.Enable();
    }

    private void Update()
    {
        if (manager == null || manager.IsDead || manager.HasWon || controlsLocked || manager.CurrentPlayer != this || !controles.Gameplay.enabled)
            return;

        if (isDashing)
        {
            if (Time.time < dashEndsAt)
                return;

            EndDash();
        }

        // Inputs
        moveInput = controles.Gameplay.Horizontal.ReadValue<float>();

        // Flip
        if (moveInput > 0 && !isFacingRight || moveInput < 0 && isFacingRight)
        {
            Flip();
        }

        CheckGrounded();

        if (controles.Gameplay.Dash.WasPressedThisFrame() && isGrounded && Time.time >= nextDashTime)
        {
            StartDash();
            return;
        }

        HandleInputs();
        UpdateAttackLogic();
        UpdateAnimations();
    }

    private void FixedUpdate()
    {
        if (manager == null || manager.IsDead || manager.HasWon || controlsLocked || manager.CurrentPlayer != this || !controles.Gameplay.enabled)
            return;

        if (isDashing)
        {
            ApplyDashMovement();
            return;
        }

        ApplyMovement();
        ApplyGravity();
    }

    private void StartDash()
    {
        CancelAttack();
        jumpBufferTimer = 0f;
        isDashing = true;
        dashDirection = isFacingRight ? 1f : -1f;
        dashEndsAt = Time.time + dashDuration;
        nextDashTime = dashEndsAt + dashCooldown;
        dashInvulnerableUntil = Time.time + Mathf.Min(dashDuration, dashInvulnerability);
        rigid.gravityScale = 0f;
        rigid.linearVelocity = Vector2.zero;
        dashAfterimage.Begin(dashDuration, dashGhostInterval, dashGhostLifetime, dashGhostColor);
        PlayActionSound(dashSound);

        animator.Play("Jugador_Punch", 0, 0f);
        animator.Update(0f);
        animator.speed = 0f;
        currentAnimation = null;
    }

    private void ApplyDashMovement()
    {
        ContactFilter2D filter = new ContactFilter2D();
        filter.SetLayerMask(groundLayer);
        filter.useTriggers = false;
        float distance = dashSpeed * Time.fixedDeltaTime;
        int count = playerCollider.Cast(Vector2.right * dashDirection, filter, dashHits, distance + 0.02f);

        for (int i = 0; i < count; i++)
        {
            RaycastHit2D hit = dashHits[i];

            if (hit.normal.x * dashDirection < -0.5f && !Physics2D.GetIgnoreCollision(playerCollider, hit.collider))
                distance = Mathf.Min(distance, Mathf.Max(0f, hit.distance - 0.02f));
        }

        rigid.linearVelocity = new Vector2(dashDirection * distance / Time.fixedDeltaTime, 0f);
    }

    private void EndDash()
    {
        if (!isDashing)
            return;

        isDashing = false;
        dashInvulnerableUntil = 0f;
        rigid.gravityScale = defaultGravity;
        rigid.linearVelocityX = 0f;
        animator.speed = 1f;
        currentAnimation = null;
        dashAfterimage.Stop();
    }

    private void UpdateAnimations()
    {
        if (isAttacking)
            return;

        if (!isGrounded)
        {
            if (rigid.linearVelocityY > 0.1f)
                ChangeAnimation("Jugador_JumpMax", 0.1f);
            else
                ChangeAnimation("Jugador_JumpDown", 0.1f);
        }
        else
        {
            if (moveInput != 0)
                ChangeAnimation("Walk", 0.1f);
            else
                ChangeAnimation("Idle", 0.05f);
        }
    }

    private void ChangeAnimation(string state, float transitionDuration)
    {
        // No volver a solicitar la misma transición en cada frame.
        if (currentAnimation == state)
            return;

        currentAnimation = state;
        animator.CrossFadeInFixedTime(state, transitionDuration);
    }

    // Checa si el jugador toca el suelo, como el nombre indica en ingles.
    // Tambien hace comprobaciones y modifica "coyoteTimer".
    private void CheckGrounded()
    {
        // Obtenemos todos los colliders que la caja de suelo está tocando
        Collider2D[] hits = Physics2D.OverlapBoxAll(groundCheckPoint.position, groundCheckSize, 0f, groundLayer);

        bool foundValidGround = false;

        // Revisamos si alguno de los colliders detectados no tiene la colisión ignorada
        foreach (Collider2D hit in hits)
        {
            // Si no estamos ignorando la colisión con este objeto, entonces es un suelo válido.
            if (!hit.isTrigger && hit != playerCollider && rigid.linearVelocityY <= 0.1f && !Physics2D.GetIgnoreCollision(playerCollider, hit))
            {
                foundValidGround = true;
                break;
            }
        }

        isGrounded = foundValidGround;

        if (isGrounded)
        {
            coyoteTimer = coyoteTime;
            canDoubleJump = false;
            doubleJumped = false;
        }
        else
        {
            coyoteTimer -= Time.deltaTime;

            if (isAttacking)
                EndAttack();

            canDoubleJump = !doubleJumped && moduleManager != null && moduleManager.HasAbility(AbilityType.DoubleJump);
        }
    }

    // Movimiento simple, consiste en aceleracion y desaceleracion, para darle "peso" al jugador.
    private void ApplyMovement()
    {
        float targetSpeed = moveInput * moveSpeed;

        // Si esta atacando reducimos la velocidad.
        if (isAttacking)
        {
            // Giro brusco.
            bool condicion = (isFacingRight && moveInput < 0) || (!isFacingRight && moveInput > 0);

            if (!condicion)
                targetSpeed *= attackMoveMultiplier;
        }

        // Detectar cambio brusco de dirección.
        bool isTurning = isGrounded && ((moveInput > 0 && rigid.linearVelocityX < -0.01f) || (moveInput < 0 && rigid.linearVelocityX > 0.01f));

        float speedRate = (Mathf.Abs(targetSpeed) > 0.01f) ? acceleration : deceleration;
        speedRate *= isTurning ? 2f : 1f; // Si hay multiplica la aceleracion/desaceleracion por 2.

        float newVelocityX = Mathf.MoveTowards(rigid.linearVelocityX, targetSpeed, speedRate * Time.fixedDeltaTime);
        rigid.linearVelocityX = newVelocityX;
    }

    // Modifica la gravedad en base a condiciones.
    private void ApplyGravity()
    {
        if (rigid.linearVelocityY < 0f) rigid.gravityScale = fallGravity;
        else rigid.gravityScale = defaultGravity;
    }

    private void HandleInputs()
    {
        // Salto
        if (controles.Gameplay.Jump.WasPressedThisFrame())
            jumpBufferTimer = jumpBufferTime;
        else
            jumpBufferTimer -= Time.deltaTime;

        if (jumpBufferTimer > 0f)
        {
            if (coyoteTimer > 0f)
            {
                CancelAttack();
                ExecuteJump(jumpForce);
                return;
            }
            else if (canDoubleJump && !doubleJumped)
            {
                CancelAttack();
                ExecuteDoubleJump();
                return;
            }
        }

        if (!isGrounded || jumpBufferTimer > 0f)
            return;

        PunchType? pressedPunch = null;

        if (controles.Gameplay.Cross.WasPressedThisFrame())
            pressedPunch = PunchType.Cross;
        else if (controles.Gameplay.Attack.WasPressedThisFrame())
            pressedPunch = PunchType.Jab;

        if (!pressedPunch.HasValue)
            return;

        if (!CanUsePunch(pressedPunch.Value))
            return;

        if (isAttacking)
        {
            if (!queuedPunch.HasValue)
                queuedPunch = pressedPunch;

            return;
        }

        StartAttack(pressedPunch.Value);
    }

    private bool CanUsePunch(PunchType punch)
    {
        if (punch == PunchType.Jab)
            return jabsUsed < 2 || Time.time >= nextJabTime;

        return Time.time >= nextCrossTime;
    }

    private void StartAttack(PunchType punch)
    {
        if (punch == PunchType.Jab)
        {
            if (Time.time >= nextJabTime)
                jabsUsed = 0;

            jabsUsed++;
            nextJabTime = float.PositiveInfinity;
        }
        else
        {
            nextCrossTime = float.PositiveInfinity;
        }

        isAttacking = true;
        currentPunch = punch;
        queuedPunch = null;
        attackTimer = 0f;
        hasHitboxTriggered = false;

        currentAnimation = punch == PunchType.Jab ? "Jugador_Punch" : "Jugador_Punch2";
        animator.Play(currentAnimation, 0, 0f);
        PlayActionSound(punch == PunchType.Jab ? jabSound : crossSound);
    }

    private void UpdateAttackLogic()
    {
        if (!isAttacking)
            return;

        attackTimer += Time.deltaTime;

        float currentDuration = currentPunch == PunchType.Jab ? jabDuration : crossDuration;
        float currentHitTime = currentPunch == PunchType.Jab ? jabHitTime : crossHitTime;

        if (!hasHitboxTriggered && attackTimer >= currentHitTime)
        {
            TriggerHitbox();
            hasHitboxTriggered = true;
        }

        if (attackTimer >= currentDuration)
        {
            PunchType? nextPunch = queuedPunch;
            EndAttack();

            if (nextPunch.HasValue && CanUsePunch(nextPunch.Value))
                StartAttack(nextPunch.Value);
        }
    }

    private void TriggerHitbox()
    {
        if (attackCheckPoint == null)
        {
            return;
        }

        Collider2D[] hitEnemies = Physics2D.OverlapBoxAll(attackCheckPoint.position, attackSize, 0f, enemyLayer);

        var damaged = new HashSet<GolemController>();

        foreach (Collider2D enemy in hitEnemies)
        {
            GolemController golem = enemy.GetComponentInParent<GolemController>();

            if (golem is GolemBossController)
                continue;

            if (golem != null && damaged.Add(golem))
                golem.TakeDamage(currentPunch == PunchType.Jab ? jabDamage : crossDamage);
        }

        GolemBossController boss = GameManager.Instance != null && GameManager.Instance.ActiveArena != null ? GameManager.Instance.ActiveArena.boss : null;
        Bounds punchBounds = new Bounds(attackCheckPoint.position, new Vector3(attackSize.x, attackSize.y, 1f));
       
        if (boss != null && boss.isActiveAndEnabled && punchBounds.Intersects(boss.DamageBounds))
            boss.TakeDamage(currentPunch == PunchType.Jab ? jabDamage : crossDamage);
    }

    private void EndAttack()
    {
        if (isAttacking)
        {
            if (currentPunch == PunchType.Jab)
                nextJabTime = Time.time + jabRecovery;
            else
                nextCrossTime = Time.time + crossRecovery;
        }

        isAttacking = false;
        queuedPunch = null;
        attackTimer = 0f;
        hasHitboxTriggered = false;
    }

    private void CancelAttack()
    {
        EndAttack();
    }

    // Salto
    private void ExecuteJump(float force)
    {
        rigid.linearVelocityY = force;
        jumpBufferTimer = 0f;
        coyoteTimer = 0f;
        PlayActionSound(jumpSound);
    }

    private void PlayActionSound(AudioClip clip)
    {
        if (actionAudioSource != null && clip != null)
            actionAudioSource.PlayOneShot(clip);
    }

    // Doble Salto
    private void ExecuteDoubleJump()
    {
        if (moduleManager == null || !moduleManager.HasAbility(AbilityType.DoubleJump))
            return;

        // Gasta un uso.
        moduleManager.UseAbility(AbilityType.DoubleJump);

        ExecuteJump(jumpForce * 0.95f);
        canDoubleJump = false;
        doubleJumped = true;
    }

    // Rota al jugador usando la escala X.
    private void Flip()
    {
        isFacingRight = !isFacingRight;

        Vector3 currentScale = transform.localScale;
        currentScale.x *= -1f;
        transform.localScale = currentScale;
    }

    public void TakeDamage(float amount) => TryTakeDamage(amount);

    public bool TryTakeDamage(float amount)
    {
        var manager = GameManager.Instance;

        if (manager == null || manager.IsDead || manager.HasWon || manager.CurrentPlayer != this || Time.time < invulnerableUntil || Time.time < dashInvulnerableUntil || amount <= 0f || battery == null)
            return false;

        invulnerableUntil = Time.time + damageInvulnerability;
        
        if (battery.CurrentDurability > 0f)
            battery.ReduceDurability(amount);
        else
            battery.ReduceBattery(amount);

        if (!manager.IsDead && manager.CurrentPlayer == this)
            PlayFlash(Color.white, 0.1f);

        return true;
    }

    public void StopControls()
    {
        EndDash();

        controles.Disable();
        moveInput = 0f;
        CancelAttack();
        rigid.linearVelocityX = 0f;
    }

    public void SetControlLock(bool shouldLock)
    {
        controlsLocked = shouldLock;

        if (!shouldLock)
            return;

        EndDash();
        CancelAttack();
        moveInput = 0f;
        rigid.linearVelocity = Vector2.zero;
    }

    public bool WasInteractPressedThisFrame()
    {
        return controles.Gameplay.Interact.WasPressedThisFrame();
    }

    public IEnumerator BecomeCorpse()
    {
        StopAllCoroutines();
        StopControls();

        controles.Dispose();
        controles = null;
        gameObject.tag = "Untagged";

        if (battery != null)
            battery.enabled = false;

        if (moduleManager != null)
        {
            moduleManager.LoseExternalModule();
            moduleManager.enabled = false;
        }

        rigid.linearVelocity = Vector2.zero;

        foreach (Collider2D collider in GetComponentsInChildren<Collider2D>())
        {
            if (collider.isTrigger)
                collider.enabled = false;
            else
                collider.excludeLayers = ~groundLayer.value;
        }

        float deathDuration = 0f;

        if (animator != null && animator.runtimeAnimatorController != null && animator.HasState(0, Animator.StringToHash("Jugador_Dead")))
        {
            animator.Play("Jugador_Dead", 0, 0f);
            animator.Update(0f);
            deathDuration = animator.GetCurrentAnimatorStateInfo(0).length;
        }
        else if (animator != null)
        {
            animator.enabled = false;
        }

        enabled = false;

        if (deathDuration > 0f)
        {
            yield return new WaitForSeconds(deathDuration);

            if (animator != null)
            {
                animator.Play("Jugador_Dead", 0, 0.999f);
                animator.Update(0f);
                animator.enabled = false;
            }
        }
    }

    public void PlayFlash(Color flashColor, float duration)
    {
        if (visualFlash != null)
            visualFlash.Play(flashColor, duration);
    }

    private void OnDisable()
    {
        EndDash();

        controles?.Disable();
    }

    private void OnDestroy()
    {
        controles?.Dispose();
    }

    // Apoyo visual en el editor para posicionar o ver si algo deberia colisionar con algo.
    private void OnDrawGizmosSelected()
    {
        if (groundCheckPoint != null)
        {
            Gizmos.color = isGrounded ? Color.green : Color.red;
            Gizmos.DrawWireCube(groundCheckPoint.position, groundCheckSize);
        }

        if (attackCheckPoint != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube(attackCheckPoint.position, attackSize);
        }
    }
}
