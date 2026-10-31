using System;
using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;

// Motivos acumulables por los que otros sistemas pueden retirar temporalmente el control del jugador.
[Flags]
public enum PlayerControlLock
{
    None = 0,
    Transition = 1,
    Victory = 2,
    Legacy = 4,
    Intro = 8
}

// Reúne movimiento, entrada, animación y estado vital del jugador y delega combate, módulos e interacción.
// Reúne movimiento, entrada, animación y estado vital del jugador y delega combate, módulos e interacción.
[RequireComponent(typeof(BatterySystem), typeof(PlayerModuleManager), typeof(Rigidbody2D))]
public class PlayerController : MonoBehaviour, IDamageable
{
    private Controles controles;
    private Collider2D playerCollider;
    public Animator animator;
    private Rigidbody2D rigid;
    private PlayerModuleManager moduleManager;

    public Transform cameraTarget;

    [Header("Muerte")]
    [SerializeField] private SpriteRenderer deathHeadRenderer;
    [SerializeField] private Sprite poweredOffHeadSprite;
    public SpriteRenderer DeathHeadRenderer => deathHeadRenderer;
    public Sprite PoweredOffHeadSprite => poweredOffHeadSprite;

    [Header("Movimiento")]
    [SerializeField] private float moveSpeed;
    [SerializeField] private float acceleration = 60f;
    [SerializeField] private float deceleration = 45f;

    [Header("Dash")]
    [SerializeField, Min(0.01f)] private float dashDuration = 0.18f;
    [SerializeField, Min(0f)] private float dashSpeed = 16f;
    [SerializeField, Min(0f)] private float dashCooldown = 0.8f;
    [SerializeField, Min(0f)] private float dashInvulnerability = 0.12f;
    [SerializeField] private GameObject dashSmokePrefab;

    [Header("Efecto Dash")]
    [SerializeField, Min(0.01f)] private float dashGhostInterval = 0.04f;
    [SerializeField, Min(0.01f)] private float dashGhostLifetime = 0.16f;
    [SerializeField] private Color dashGhostColor = new Color(0.2f, 1f, 0.92f, 0.55f);

    private InputAction sacrificeInput;
    private bool isDashing;
    private float dashEndsAt;
    private float nextDashTime;
    private float dashInvulnerableUntil;
    private float dashDirection;
    private readonly RaycastHit2D[] dashHits = new RaycastHit2D[8];
    private DashAfterImage dashAfterimage;
    private PlayerControlLock controlLocks;
    private bool controlsLocked => controlLocks != PlayerControlLock.None;
    private PlayerCombat combat;
    private PlayerCannon cannon;
    private readonly PlayerInteraction interaction = new();
    public bool IsInitialized { get; private set; }
    public bool IsPlaying => GameManager.Instance != null && GameManager.Instance.IsPlaying(this);
    public bool CanAct => IsPlaying && !controlsLocked && controles != null && controles.Gameplay.enabled;
    public bool IsGrounded => isGrounded;
    public bool IsDamageTarget => IsPlaying;
    public float FacingDirection => isFacingRight ? 1f : -1f;
    public bool CanFireCannon => CanAct && isGrounded && !isAttacking && !isDashing && rigid.linearVelocityY <= 0.1f;
    public Bounds DamageBounds => playerCollider != null ? playerCollider.bounds : new Bounds(transform.position, Vector3.zero);

    [Header("Salto")]
    [SerializeField, Min(0f)] private float jumpForce;
    [SerializeField, Min(0.01f)] private float jumpVelocityMultiplier = 1.05f;
    [SerializeField, Range(0f, 1f)] private float jumpReleaseVelocityMultiplier = 0.5f;
    [SerializeField] private float defaultGravity = 3f;
    [SerializeField] private float fallGravity = 5.4f;
    [SerializeField, Min(0f)] private float apexVelocityThreshold = 1.5f;
    [SerializeField, Range(0.01f, 1f)] private float apexGravityMultiplier = 0.8f;
    [SerializeField, Min(0.01f)] private float maximumFallSpeed = 22f;
    [SerializeField] private GameObject jumpSmokePrefab;

    [Header("Control Aéreo")]
    [SerializeField, Min(0f)] private float airAcceleration = 28f;
    [SerializeField, Min(0f)] private float airDeceleration = 42f;
    [SerializeField, Min(1f)] private float airTurnMultiplier = 1.5f;

    [Header("Sonidos")]
    [SerializeField] private AudioSource actionAudioSource;
    [SerializeField] private AudioClip jabSound;
    [SerializeField, Range(0f, 1f)] private float jabSoundVolume = 1f;
    [SerializeField] private AudioClip jabImpactSound;
    [SerializeField, Range(0f, 1f)] private float jabImpactVolume = 0.35f;
    [SerializeField] private AudioClip crossSound;
    [SerializeField] private AudioClip dashSound;
    [SerializeField] private AudioClip jumpSound;
    [SerializeField] private AudioClip hurtSound;

    [Header("Pasos")]
    [SerializeField]
    private AudioSource footstepAudioSource;
    [SerializeField] private AudioClip[] footstepClips;
    [SerializeField, Range(0f, 1f)] private float footstepVolume = 0.35f;
    [SerializeField]
    private Vector2 footstepPitchRange = new Vector2(0.95f, 1.05f);
    [SerializeField]
    private float firstFootContact = 0.30f;
    [SerializeField]
    private float secondFootContact = 0.70f;
    private static readonly int WalkStateHash = Animator.StringToHash("Walk");
    private AudioClip[] footstepBag = Array.Empty<AudioClip>();
    private AudioClip lastFootstep;
    private AudioClip lastFirstFootstep;
    private AudioClip lastSecondFootstep;
    private int nextFootstep;
    private float previousWalkTime;
    private bool trackingWalk;
    private bool warnedFootContacts;

    [Header("Detección del Suelo")]
    [SerializeField] private Transform groundCheckPoint;
    [SerializeField] private Vector2 groundCheckSize = new Vector2(0.5f, 0.1f);
    [SerializeField] private LayerMask groundLayer;
    private bool isGrounded;
    private OneWayPlatform supportingOneWayPlatform;

    [Header("Combate")]
    [SerializeField] private Transform attackCheckPoint;
    [SerializeField] private Vector2 attackSize;
    [SerializeField] private LayerMask enemyLayer;

    [SerializeField] private float damageInvulnerability = 0.75f;

    // Tiempo hasta el que se ignoran nuevos impactos después de recibir daño.
    private float invulnerableUntil;
    private BatterySystem battery;

    [Header("Ayudas")]
    [SerializeField] private float coyoteTime = 0.12f;
    [SerializeField] private float jumpBufferTime = 0.1f;

    // Ventanas temporales de salto, dash, daño y consumo de batería.
    private float coyoteTimer;
    private float jumpBufferTimer;
    private bool playerJumpInProgress;
    private bool jumpCutApplied;

    // Estado locomotor que comparten movimiento, animación y restricciones de acciones.
    private bool canDoubleJump;
    // Evita recuperar otro doble salto hasta volver a tocar el suelo.
    private bool doubleJumped;
    private bool isFacingRight = true;
    // Última animación solicitada: incluye golpes, para no confundirlos con Walk.
    private string currentAnimation;
    private bool isAttacking => combat != null && combat.IsAttacking;

    // Lecturas de entrada conservadas hasta que Update y FixedUpdate terminan de consumirlas.
    private float moveInput;

    // Flash compartido por impactos y recogibles.
    private SpriteFlash visualFlash;

    private void Awake()
    {
        playerCollider = GetComponent<Collider2D>();
        rigid = GetComponent<Rigidbody2D>();
        animator = GetComponent<Animator>();
        moduleManager = GetComponent<PlayerModuleManager>();
        battery = GetComponent<BatterySystem>();
        actionAudioSource ??= GetComponent<AudioSource>();
        dashAfterimage = GetComponent<DashAfterImage>();
        visualFlash = GetComponent<SpriteFlash>();
        cannon = GetComponent<PlayerCannon>();

        if (rigid == null || playerCollider == null || animator == null ||
            moduleManager == null || battery == null || groundCheckPoint == null || attackCheckPoint == null)
        {
            Debug.LogError("PlayerController necesita Rigidbody2D, Collider2D, Animator, módulos, batería y puntos de suelo/ataque.", this);
            enabled = false;
            return;
        }
        try
        {
            controles = new Controles();
            sacrificeInput = controles.asset.FindAction("Gameplay/Sacrifice");
            if (sacrificeInput == null)
            {
                Debug.LogError("PlayerController necesita Gameplay/Sacrifice en Controles.inputactions y regenerar Controles.cs.", this);
                enabled = false;
                return;
            }
        }
        catch (Exception error)
        {
            Debug.LogError("PlayerController no pudo inicializar Controles: " + error.Message, this);
            enabled = false;
            return;
        }
        combat = new PlayerCombat(attackCheckPoint, attackSize, enemyLayer, PlayPunch, PlayPunchImpact);
        rigid.gravityScale = defaultGravity;
        IsInitialized = true;
        PrepareFootsteps();
    }

    private void OnEnable()
    {
        if (!IsInitialized)
        {
            enabled = false;
            return;
        }
        controles?.Gameplay.Enable();

        if (battery != null)
        {
            battery.BatteryDepleted += HandleBatteryDepleted;
        }
    }

    private void Update()
    {
        // La introducción necesita detectar el aterrizaje con las acciones bloqueadas.
        CheckGrounded();
        if (!IsPlaying)
        {
            return;
        }

        // Las puertas bloquean acciones, pero no pausaban el consumo del prototipo.
        battery.ConsumeBattery(Time.deltaTime);

        if (!isActiveAndEnabled || battery.IsDepleted || !CanAct)
        {
            return;
        }

        if (sacrificeInput.WasPressedThisFrame())
        {
            moduleManager.SacrificeCurrentExternalModule();
        }

        interaction.Tick(this, WasInteractPressedThisFrame());
        if (!CanAct)
        {
            return;
        }

        if (isDashing)
        {
            if (Time.time < dashEndsAt)
            {
                return;
            }

            EndDash();
        }


        // Procesar acciones antes de la física permite conservar pulsaciones breves entre fixed updates.
        moveInput = controles.Gameplay.Horizontal.ReadValue<float>();

        // La carga y el recoil conservan el movimiento permitido, pero consumen las demás acciones.
        if (cannon != null && cannon.IsFiring)
        {
            if (cannon.AllowsTurning && (moveInput > 0 && !isFacingRight || moveInput < 0 && isFacingRight))
            {
                Flip();
            }

            cannon.SetFireHeld(controles.Gameplay.Cross.IsPressed());
            jumpBufferTimer = 0f;
            return;
        }

        if (controles.Gameplay.PlatformBack.WasPressedThisFrame() && TryDropThroughPlatform())
        {
            return;
        }

        // La orientación sigue la intención horizontal incluso mientras el movimiento está limitado.
        if (moveInput > 0 && !isFacingRight || moveInput < 0 && isFacingRight)
        {
            Flip();
        }

        if (controles.Gameplay.Dash.WasPressedThisFrame() && isGrounded && Time.time >= nextDashTime)
        {
            StartDash();
            return;
        }

        HandleInputs();
        HandleJumpRelease();
        combat.Tick(Time.deltaTime);
        if (CanAct)
        {
            UpdateAnimations();
        }
    }

    private void FixedUpdate()
    {
        if ((controlLocks & PlayerControlLock.Intro) != 0)
        {
            ApplyGravity();
            return;
        }
        if (!CanAct)
        {
            return;
        }

        if (isDashing)
        {
            ApplyDashMovement();
            return;
        }

        ApplyMovement();
        ApplyGravity();
    }

    private void PrepareFootsteps()
    {
        if (footstepClips == null || footstepClips.Length == 0)
        {
            return;
        }

        var unique = new List<AudioClip>(footstepClips.Length);
        foreach (AudioClip clip in footstepClips)
        {
            if (clip != null && !unique.Contains(clip))
            {
                unique.Add(clip);
            }
        }

        footstepBag = unique.ToArray();
        nextFootstep = footstepBag.Length;
        if (unique.Count != footstepClips.Length)
        {
            Debug.LogWarning("Pasos: se omiten los clips vacíos o repetidos.", this);
        }

        if (unique.Count == 1)
        {
            Debug.LogWarning("Pasos: con un solo clip no se pueden evitar repeticiones consecutivas.", this);
        }

        if (unique.Count == 2)
        {
            Debug.LogWarning("Pasos: con dos clips se evita la repetición consecutiva; para variar ambos contactos se necesitan al menos tres.", this);
        }

        if (footstepAudioSource == null || footstepAudioSource == actionAudioSource)
        {
            Debug.LogWarning("Pasos: asigna un Footstep Audio Source exclusivo, distinto de Action Audio Source. Se omite el efecto.", this);
            footstepBag = Array.Empty<AudioClip>();
        }
    }

    // Reproduce contactos de pasos a partir del progreso real del estado Walk del Animator.
    private void LateUpdate()
    {
        if (footstepBag.Length == 0 || !CanAct || !isGrounded || isAttacking || isDashing ||
            currentAnimation != "Walk" || Mathf.Abs(rigid.linearVelocityX) <= 0.1f || rigid.linearVelocityY > 0.1f ||
            Time.deltaTime <= 0f || animator == null || !animator.isActiveAndEnabled || animator.speed <= 0f)
        {
            trackingWalk = false;
            return;
        }
        if (!(firstFootContact >= 0f && firstFootContact < 1f && secondFootContact >= 0f && secondFootContact < 1f) ||
            Mathf.Approximately(firstFootContact, secondFootContact))
        {
            if (!warnedFootContacts)
            {
                Debug.LogWarning("Pasos: los contactos deben ser diferentes y estar entre 0 (incluido) y 1 (excluido).", this);
            }

            warnedFootContacts = true;
            trackingWalk = false;
            return;
        }
        warnedFootContacts = false;
        AnimatorStateInfo state = animator.IsInTransition(0)
            ? animator.GetNextAnimatorStateInfo(0) : animator.GetCurrentAnimatorStateInfo(0);
        if (state.shortNameHash != WalkStateHash || !state.loop || state.speed * state.speedMultiplier <= 0f)
        {
            trackingWalk = false;
            return;
        }
        float time = state.normalizedTime;
        if (!trackingWalk || time < previousWalkTime)
        {
            trackingWalk = true;
            previousWalkTime = time;
            return;
        }
        bool crossedFirst = CrossedFootContact(firstFootContact, time);
        bool crossedSecond = CrossedFootContact(secondFootContact, time);
        previousWalkTime = time;
        // Un solo sonido por frame, aunque un salto de tiempo atraviese varios contactos.
        if ((!crossedFirst && !crossedSecond) || footstepVolume <= 0f ||
            footstepAudioSource == null || !footstepAudioSource.isActiveAndEnabled)
        {
            return;
        }

        bool first = crossedFirst && (!crossedSecond ||
            Mathf.Floor(time - firstFootContact) + firstFootContact > Mathf.Floor(time - secondFootContact) + secondFootContact);
        float minimumPitch = Mathf.Clamp(Mathf.Min(footstepPitchRange.x, footstepPitchRange.y), 0.01f, 3f);
        float maximumPitch = Mathf.Clamp(Mathf.Max(footstepPitchRange.x, footstepPitchRange.y), 0.01f, 3f);
        footstepAudioSource.pitch = UnityEngine.Random.Range(minimumPitch, maximumPitch);
        footstepAudioSource.PlayOneShot(NextFootstep(first), footstepVolume);
    }

    private bool CrossedFootContact(float contact, float time)
    {
        return Mathf.Floor(time - contact) > Mathf.Floor(previousWalkTime - contact);
    }

    private AudioClip NextFootstep(bool firstContact)
    {
        if (nextFootstep >= footstepBag.Length)
        {
            ShuffleFootsteps();
        }

        int chosen = FindAvailableFootstep();
        if (chosen < 0)
        {
            // Tras parar o reiniciar Walk puede repetirse el mismo contacto.
            // Priorizar sus exclusiones si los clips restantes de la bolsa no sirven.
            ShuffleFootsteps();
            chosen = FindAvailableFootstep();
        }
        (footstepBag[nextFootstep], footstepBag[chosen]) = (footstepBag[chosen], footstepBag[nextFootstep]);
        lastFootstep = footstepBag[nextFootstep++];
        if (firstContact)
        {
            lastFirstFootstep = lastFootstep;
        }
        else
        {
            lastSecondFootstep = lastFootstep;
        }

        return lastFootstep;
    }

    private int FindAvailableFootstep()
    {
        for (int i = nextFootstep; i < footstepBag.Length; i++)
        {
            if (footstepBag.Length == 1 || (footstepBag[i] != lastFootstep &&
                (footstepBag.Length < 3 || (footstepBag[i] != lastFirstFootstep && footstepBag[i] != lastSecondFootstep))))
            {
                return i;
            }
        }

        return -1;
    }

    private void ShuffleFootsteps()
    {
        for (int i = footstepBag.Length - 1; i > 0; i--)
        {
            int other = UnityEngine.Random.Range(0, i + 1);
            (footstepBag[i], footstepBag[other]) = (footstepBag[other], footstepBag[i]);
        }
        nextFootstep = 0;
    }

    // Inicia el dash, cancela acciones incompatibles y conserva la gravedad previa para restaurarla después.
    private void StartDash()
    {
        cannon?.CancelFire();
        CancelAttack();
        jumpBufferTimer = 0f;
        ResetPlayerJumpState();
        isDashing = true;
        dashDirection = isFacingRight ? 1f : -1f;
        dashEndsAt = Time.time + dashDuration;
        nextDashTime = dashEndsAt + dashCooldown;
        dashInvulnerableUntil = Time.time + Mathf.Min(dashDuration, dashInvulnerability);
        rigid.gravityScale = 0f;
        rigid.linearVelocity = Vector2.zero;
        
        if (dashAfterimage != null)
        {
            dashAfterimage.Begin(dashDuration, dashGhostInterval, dashGhostLifetime, dashGhostColor);
        }

        if (dashSmokePrefab != null)
        {
            Vector3 spawnPos = new Vector3(transform.position.x, playerCollider.bounds.min.y, 0f);
            GameObject dS = Instantiate(dashSmokePrefab, spawnPos, Quaternion.identity);
            dS.transform.localScale = new Vector3(transform.localScale.x, 1, 1);
        }

        PlayActionSound(dashSound);

        animator.Play("PunchLight", 0, 0f);
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
            {
                distance = Mathf.Min(distance, Mathf.Max(0f, hit.distance - 0.02f));
            }
        }

        rigid.linearVelocity = new Vector2(dashDirection * distance / Time.fixedDeltaTime, 0f);
    }

    private void EndDash()
    {
        if (!isDashing)
        {
            return;
        }

        isDashing = false;
        dashInvulnerableUntil = 0f;
        if (rigid != null)
        {
            rigid.gravityScale = defaultGravity;
            rigid.linearVelocityX = 0f;
        }
        if (animator != null)
        {
            animator.speed = 1f;
        }

        currentAnimation = null;
        if (dashAfterimage != null)
        {
            dashAfterimage.Stop();
        }
    }

    private void UpdateAnimations()
    {
        if (isAttacking || (cannon != null && cannon.ControlsAnimation))
        {
            return;
        }

        if (!isGrounded)
        {
            if (rigid.linearVelocityY > 0.1f)
            {
                ChangeAnimation("JumpMax", 0.1f);
            }
            else
            {
                ChangeAnimation("JumpDown", 0.1f);
            }
        }
        else
        {
            if (moveInput != 0)
            {
                ChangeAnimation("Walk", 0.1f);
            }
            else
            {
                ChangeAnimation("Idle", 0.05f);
            }
        }
    }

    private void ChangeAnimation(string state, float transitionDuration)
    {
        // No volver a solicitar la misma transición en cada frame.
        if (currentAnimation == state)
        {
            return;
        }

        currentAnimation = state;
        animator.CrossFadeInFixedTime(state, transitionDuration);
    }

    // Consulta todos los apoyos bajo el jugador y actualiza aterrizaje, coyote time y doble salto.
    internal void CheckGrounded()
    {
        // Una consulta de volumen tolera uniones entre colliders mejor que un único rayo.
        Collider2D[] hits = Physics2D.OverlapBoxAll(groundCheckPoint.position, groundCheckSize, 0f, groundLayer);

        bool foundValidGround = false;
        OneWayPlatform oneWaySupport = null;

        // Un collider ignorado por una plataforma atravesable no cuenta como apoyo válido.
        foreach (Collider2D hit in hits)
        {
            if (!hit.isTrigger && hit != playerCollider && rigid.linearVelocityY <= 0.1f && !Physics2D.GetIgnoreCollision(playerCollider, hit))
            {
                foundValidGround = true;
                OneWayPlatform oneWay = hit.GetComponentInParent<OneWayPlatform>();

                if (oneWay == null)
                {
                    // Un suelo sólido tiene prioridad: ignorar una plataforma no haría caer al jugador.
                    oneWaySupport = null;
                    break;
                }

                oneWaySupport ??= oneWay;
            }
        }

        isGrounded = foundValidGround;
        supportingOneWayPlatform = foundValidGround ? oneWaySupport : null;

        if (isGrounded)
        {
            ResetPlayerJumpState();
            coyoteTimer = coyoteTime;
            canDoubleJump = false;
            doubleJumped = false;
        }
        else
        {
            coyoteTimer -= Time.deltaTime;

            if (isAttacking)
            {
                EndAttack();
            }

            canDoubleJump = !doubleJumped && moduleManager != null && moduleManager.HasAbility(AbilityType.DoubleJump);
        }
    }

    // Acelera y desacelera horizontalmente para conservar peso sin perder una respuesta precisa.
    private void ApplyMovement()
    {
        float targetSpeed = moveInput * moveSpeed;

        // Ataques y cañón limitan el desplazamiento mediante multiplicadores independientes.
        if (isAttacking)
        {
            targetSpeed *= combat.MovementMultiplier;
        }

        if (cannon != null)
        {
            targetSpeed *= cannon.MovementMultiplier;
        }

        bool isTurning = (moveInput > 0f && rigid.linearVelocityX < -0.01f) ||
            (moveInput < 0f && rigid.linearVelocityX > 0.01f);
        float speedRate;

        if (isGrounded)
        {
            speedRate = Mathf.Abs(targetSpeed) > 0.01f ? acceleration : deceleration;
            speedRate *= isTurning ? 2f : 1f;
        }
        else
        {
            speedRate = Mathf.Abs(targetSpeed) > 0.01f ? airAcceleration : airDeceleration;
            speedRate *= isTurning ? airTurnMultiplier : 1f;
        }

        float newVelocityX = Mathf.MoveTowards(rigid.linearVelocityX, targetSpeed, speedRate * Time.fixedDeltaTime);
        rigid.linearVelocityX = newVelocityX;
    }

    // Diferencia subida, ápice y caída, anticipando el siguiente paso para respetar la velocidad terminal.
    private void ApplyGravity()
    {
        float verticalSpeed = rigid.linearVelocityY;
        float gravityScale;

        if (playerJumpInProgress && Mathf.Abs(verticalSpeed) <= apexVelocityThreshold)
        {
            gravityScale = defaultGravity * apexGravityMultiplier;
        }
        else
        {
            gravityScale = verticalSpeed < 0f ? fallGravity : defaultGravity;
        }

        float gravityStep = Physics2D.gravity.y * gravityScale * Time.fixedDeltaTime;
        float projectedSpeed = verticalSpeed + gravityStep;

        if (projectedSpeed <= -maximumFallSpeed)
        {
            // Compensar el paso que Physics2D aplicará después de FixedUpdate sin apagar la gravedad.
            rigid.linearVelocityY = -maximumFallSpeed - gravityStep;
            rigid.gravityScale = gravityScale;
            return;
        }

        rigid.gravityScale = gravityScale;
    }

    // Prioriza acciones excluyentes y conserva los buffers necesarios para salto y combate.
    private void HandleInputs()
    {
        // El salto almacenado puede ejecutarse al tocar suelo o durante la ventana de coyote time.
        if (controles.Gameplay.Jump.WasPressedThisFrame())
        {
            jumpBufferTimer = jumpBufferTime;
        }
        else
        {
            jumpBufferTimer -= Time.deltaTime;
        }

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
        {
            return;
        }

        if (controles.Gameplay.Cross.WasPressedThisFrame())
        {
            if (moduleManager.HasActiveModule && moduleManager.ActiveExternalModule.ability == AbilityType.EnergyCannon)
            {
                if (cannon != null && cannon.TryFire())
                {
                    cannon.SetFireHeld(controles.Gameplay.Cross.IsPressed());
                }
            }
            else
            {
                combat.Request(PlayerCombat.Punch.Cross);
            }
        }
        else if (controles.Gameplay.Attack.WasPressedThisFrame())
        {
            combat.Request(PlayerCombat.Punch.Jab);
        }
    }

    // Suelta solo la plataforma que actualmente funciona como apoyo y fuerza el primer paso descendente.
    private bool TryDropThroughPlatform()
    {
        if (!isGrounded || supportingOneWayPlatform == null || isDashing ||
            !supportingOneWayPlatform.TryDropThrough(this))
        {
            return false;
        }

        cannon?.CancelFire();
        CancelAttack();
        jumpBufferTimer = 0f;
        coyoteTimer = 0f;
        ResetPlayerJumpState();
        isGrounded = false;
        supportingOneWayPlatform = null;
        rigid.linearVelocityY = Mathf.Min(rigid.linearVelocityY, -1f);
        currentAnimation = null;
        return true;
    }

    private void PlayPunch(PlayerCombat.Punch punch)
    {
        currentAnimation = punch == PlayerCombat.Punch.Jab ? "PunchLight" : "PunchHeavy";
        animator.Play(currentAnimation, 0, 0f);
        PlayActionSound(punch == PlayerCombat.Punch.Jab ? jabSound : crossSound,
            punch == PlayerCombat.Punch.Jab ? jabSoundVolume : 1f);
    }

    // Registra una animación exclusiva del cañón para que locomoción no la reemplace durante la secuencia.
    internal void PlayCannonAnimation(int stateHash)
    {
        currentAnimation = null;
        animator.Play(stateHash, 0, 0f);
        animator.Update(0f);
    }

    internal void ReleaseCannonAnimation()
    {
        currentAnimation = null;
    }

    internal void StopHorizontalMovement()
    {
        rigid.linearVelocityX = 0f;
    }

    private void PlayPunchImpact(PlayerCombat.Punch punch)
    {
        if (punch == PlayerCombat.Punch.Jab)
        {
            PlayActionSound(jabImpactSound, jabImpactVolume);
        }
    }

    private void EndAttack()
    {
        combat?.Cancel();
    }

    private void CancelAttack()
    {
        combat?.Cancel();
    }

    // Aplica el impulso vertical y habilita corte y ápice únicamente para saltos iniciados por el jugador.
    private void ExecuteJump(float force)
    {
        rigid.linearVelocityY = force * jumpVelocityMultiplier;
        jumpBufferTimer = 0f;
        coyoteTimer = 0f;
        playerJumpInProgress = true;
        jumpCutApplied = false;

        // Un salto almacenado cuyo botón ya fue soltado debe conservar la intención de salto corto.
        if (!controles.Gameplay.Jump.IsPressed())
        {
            ApplyJumpCut();
        }

        PlayActionSound(jumpSound);
    }

    private void HandleJumpRelease()
    {
        if (controles.Gameplay.Jump.WasReleasedThisFrame())
        {
            ApplyJumpCut();
        }
    }

    private void ApplyJumpCut()
    {
        if (!playerJumpInProgress || jumpCutApplied || rigid.linearVelocityY <= 0f)
        {
            return;
        }

        rigid.linearVelocityY *= jumpReleaseVelocityMultiplier;
        jumpCutApplied = true;
    }

    private void ResetPlayerJumpState()
    {
        playerJumpInProgress = false;
        jumpCutApplied = false;
    }

    public void PlayActionSound(AudioClip clip, float volume = 1f)
    {
        if (actionAudioSource != null && actionAudioSource.isActiveAndEnabled && clip != null)
        {
            actionAudioSource.PlayOneShot(clip, volume);
        }
    }

    // Consume el módulo correspondiente antes de aplicar el segundo impulso.
    private void ExecuteDoubleJump()
    {
        if (moduleManager == null || !moduleManager.HasAbility(AbilityType.DoubleJump))
        {
            return;
        }

        if (!moduleManager.TryUseAbility(AbilityType.DoubleJump))
        {
            return;
        }

        if (jumpSmokePrefab != null)
        {
            Vector3 spawnPos = new Vector3(transform.position.x, playerCollider.bounds.min.y, 0f);
            GameObject jS = Instantiate(jumpSmokePrefab, spawnPos, Quaternion.identity);
            jS.transform.localScale = new Vector3(0.9f, 0.9f, 1);
        }

        ExecuteJump(jumpForce * 0.90f);
        canDoubleJump = false;
        doubleJumped = true;
    }

    // Invierte la jerarquía visual y conserva la dirección que usan ataques y proyectiles.
    private void Flip()
    {
        isFacingRight = !isFacingRight;

        Vector3 currentScale = transform.localScale;
        currentScale.x *= -1f;
        transform.localScale = currentScale;
    }

    public void TakeDamage(float amount)
    {
        TryTakeDamage(amount);
    }

    // Aplica invulnerabilidad breve y deriva la muerte al agotamiento de durabilidad.
    public bool TryTakeDamage(float amount)
    {
        var manager = GameManager.Instance;

        if (manager == null || !manager.IsPlaying(this))
        {
            return false;
        }

        if (battery == null || Time.time < invulnerableUntil || Time.time < dashInvulnerableUntil)
        {
            return false;
        }

        if (battery.ApplyDamage(amount) <= 0f)
        {
            return false;
        }

        invulnerableUntil = Time.time + damageInvulnerability;

        // ApplyDamage puede desencadenar la muerte antes de regresar.
        if (manager.IsPlaying(this))
        {
            PlayFlash(Color.white, 0.2f);
        }

        actionAudioSource.PlayOneShot(hurtSound);

        return true;
    }

    // Marca la instancia como muerta una sola vez y transfiere la secuencia al GameManager.
    public void Kill()
    {
        var manager = GameManager.Instance;

        if (manager == null || !manager.IsPlaying(this))
        {
            return;
        }

        battery.ReduceBattery(battery.CurrentBattery);
    }

    private void HandleBatteryDepleted()
    {
        GameManager.Instance?.HandlePlayerDeath(this);
    }

    public void StopControls()
    {
        cannon?.CancelFire();
        EndDash();

        controles?.Disable();
        moveInput = 0f;
        supportingOneWayPlatform = null;
        ResetPlayerJumpState();
        CancelAttack();
        if (rigid != null)
        {
            rigid.linearVelocityX = 0f;
        }
    }

    public void SetControlLock(bool shouldLock)
    {
        SetControlLock(PlayerControlLock.Legacy, shouldLock);
    }

    internal void PlayIntroAnimation(string state)
    {
        currentAnimation = state;
        animator.CrossFadeInFixedTime(state, 0.1f);
    }

    // Combina motivos independientes para que liberar uno no reactive controles bloqueados por otro sistema.
    public void SetControlLock(PlayerControlLock reason, bool shouldLock)
    {
        if (!IsInitialized)
        {
            return;
        }

        if (shouldLock)
        {
            controlLocks |= reason;
            interaction.Clear();
        }
        else
        {
            controlLocks &= ~reason;
        }

        if (!shouldLock)
        {
            return;
        }

        cannon?.CancelFire();
        EndDash();
        CancelAttack();
        ResetPlayerJumpState();
        moveInput = 0f;
        rigid.linearVelocity = Vector2.zero;
    }

    public bool WasInteractPressedThisFrame()
    {
        return CanAct && controles.Gameplay.Interact.WasPressedThisFrame();
    }

    public void RegisterInteraction(MonoBehaviour target, Collider2D contact)
    {
        interaction.Enter(target, contact);
    }

    public void UnregisterInteraction(MonoBehaviour target, Collider2D contact)
    {
        interaction.Exit(target, contact);
    }

    public void ClearInteractions()
    {
        interaction.Clear();
    }

    public void PlayFlash(Color flashColor, float duration)
    {
        if (visualFlash != null)
        {
            visualFlash.Play(flashColor, duration);
        }
    }

    private void OnDisable()
    {
        cannon?.CancelFire();
        trackingWalk = false;
        if (battery != null)
        {
            battery.BatteryDepleted -= HandleBatteryDepleted;
        }

        EndDash();
        combat?.Cancel();
        interaction.Clear();
        supportingOneWayPlatform = null;
        ResetPlayerJumpState();
        controles?.Disable();
    }

    private void OnDestroy()
    {
        GameManager.Instance?.UnregisterPlayer(this);
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
