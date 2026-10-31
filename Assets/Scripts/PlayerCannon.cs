using UnityEngine;

// Presenta el módulo de cañón, dirige su carga animada y crea el proyectil con la potencia acumulada.
[DisallowMultipleComponent]
public sealed class PlayerCannon : MonoBehaviour
{
    private enum FiringState
    {
        Idle,
        Charging,
        HoldingCharge,
        Recoil
    }

    private static readonly int ChargeStateHash = Animator.StringToHash("Base Layer.CannonCharge");
    private static readonly int RecoilStateHash = Animator.StringToHash("Base Layer.CannonRecoil");

    [Header("Ref")]
    [SerializeField] private SpriteRenderer rightUpperArm;
    [SerializeField] private SpriteRenderer rightLowerArm;
    [SerializeField] private SpriteRenderer rightHand;
    [SerializeField] private Transform cannonMuzzle;

    [Header("Sprites")]
    [SerializeField] private Sprite upperArmSprite;
    [SerializeField] private Sprite lowerArmSprite;
    [SerializeField] private Sprite handSprite;

    [Header("Proyectil")]
    [SerializeField] private EnergyOrbProjectile projectilePrefab;
    [SerializeField, Min(0.01f)] private float damage = 60f;
    [SerializeField, Min(0.01f)] private float projectileSpeed = 16f;
    [SerializeField, Min(0.01f)] private float projectileRange = 20f;

    [Header("Carga")]
    [SerializeField, Min(0f)] private float minimumShotDelay = 0.15f;
    [SerializeField, Min(0.01f)] private float maximumChargeDuration = 1f;
    [SerializeField, Min(1f)] private float maximumDamageMultiplier = 1.5f;
    [SerializeField, Range(0.01f, 1f)] private float minimumSizeMultiplier = 0.75f;
    [SerializeField, Min(1f)] private float maximumSizeMultiplier = 1.25f;
    [SerializeField, Min(0.01f)] private float chargeCompletionFlashDuration = 0.15f;
    [SerializeField, Range(0f, 1f)] private float firingMovementMultiplier = 0.5f;

    [Header("Recuperación")]
    [SerializeField, Min(0f)] private float postRecoilDuration = 0.4f;
    [SerializeField, Range(0f, 1f)] private float postRecoilMovementMultiplier = 0.2f;
    [SerializeField, Min(0.01f)] private float shotInterval = 0.65f;

    [Header("Audio")]
    [SerializeField] private AudioClip chargeSound;
    [SerializeField, Range(0f, 1f)] private float chargeVolume = 0.65f;
    [SerializeField] private AudioClip shotSound;
    [SerializeField, Range(0f, 1f)] private float shotVolume = 0.65f;

    private PlayerController player;
    private PlayerModuleManager modules;
    private Animator animator;
    private EnergyOrbProjectile chargePreview;
    private Sprite originalUpper;
    private Sprite originalLower;
    private Sprite originalHand;
    private FiringState state;
    private float chargeStartedAt;
    private float releasedCharge;
    private float lockedDirection = 1f;
    private float nextShotAt;
    private float movementPenaltyUntil;
    private float animatorSpeedBeforeFiring = 1f;
    private bool ready;
    private bool subscribed;
    private bool launched;
    private bool releaseRequested;
    private bool chargeSoundPlaying;

    public bool IsReady => ready && isActiveAndEnabled;
    public bool IsFiring => state != FiringState.Idle;
    public bool ControlsAnimation => IsFiring;
    public bool AllowsTurning => !releaseRequested &&
        (state == FiringState.Charging || state == FiringState.HoldingCharge);
    public float MovementMultiplier
    {
        get
        {
            float actionMultiplier = state switch
            {
                FiringState.Charging => firingMovementMultiplier,
                FiringState.HoldingCharge => firingMovementMultiplier,
                FiringState.Recoil => 0f,
                _ => 1f
            };

            float recoveryMultiplier = Time.time < movementPenaltyUntil
                ? postRecoilMovementMultiplier
                : 1f;

            return actionMultiplier * recoveryMultiplier;
        }
    }

    private bool HasModule => modules != null && modules.HasActiveModule &&
        modules.ActiveExternalModule.ability == AbilityType.EnergyCannon;

    private void Start()
    {
        player = GetComponent<PlayerController>();
        modules = GetComponent<PlayerModuleManager>();
        animator = player != null ? player.animator : null;

        if (player == null || !player.IsInitialized || modules == null || !modules.isActiveAndEnabled ||
            animator == null || !animator.isActiveAndEnabled ||
            rightUpperArm == null || rightLowerArm == null || rightHand == null || cannonMuzzle == null ||
            upperArmSprite == null || lowerArmSprite == null || handSprite == null ||
            !rightUpperArm.transform.IsChildOf(transform) || rightLowerArm.transform.parent != rightUpperArm.transform ||
            rightHand.transform.parent != rightLowerArm.transform || cannonMuzzle == rightHand.transform ||
            !cannonMuzzle.IsChildOf(rightHand.transform) || projectilePrefab == null ||
            !projectilePrefab.enabled || !projectilePrefab.gameObject.activeSelf || !projectilePrefab.ValidateConfiguration() ||
            !PositiveFinite(damage) || !PositiveFinite(projectileSpeed) || !PositiveFinite(projectileRange) ||
            minimumShotDelay < 0f || float.IsNaN(minimumShotDelay) || float.IsInfinity(minimumShotDelay) ||
            !PositiveFinite(maximumChargeDuration) || minimumShotDelay > maximumChargeDuration ||
            maximumDamageMultiplier < 1f ||
            !PositiveFinite(minimumSizeMultiplier) || maximumSizeMultiplier < minimumSizeMultiplier ||
            !PositiveFinite(chargeCompletionFlashDuration) ||
            postRecoilDuration < 0f || float.IsNaN(postRecoilDuration) || float.IsInfinity(postRecoilDuration) ||
            !PositiveFinite(shotInterval))
        {
            Fail("PlayerCannon necesita jugador, Animator, módulos, las tres piezas del brazo, Cannon Muzzle, sprites y EnergyOrbProjectile configurados.");
            return;
        }

        if (!animator.HasState(0, ChargeStateHash) || !animator.HasState(0, RecoilStateHash))
        {
            Fail("PlayerCannon necesita los estados Base Layer.CannonCharge y Base Layer.CannonRecoil en el Animator del jugador.");
            return;
        }

        originalUpper = rightUpperArm.sprite;
        originalLower = rightLowerArm.sprite;
        originalHand = rightHand.sprite;
        state = FiringState.Idle;
        ready = true;
        Subscribe();
    }

    private static bool PositiveFinite(float value)
    {
        return value > 0f && !float.IsInfinity(value);
    }

    private void OnEnable()
    {
        if (ready)
        {
            Subscribe();
        }
    }

    private void Subscribe()
    {
        if (!subscribed)
        {
            modules.ExternalModuleChanged += RefreshAppearance;
            subscribed = true;
        }

        RefreshAppearance();
    }

    // Inicia la carga solo si el módulo sigue equipado y el jugador puede disparar desde el suelo.
    public bool TryFire()
    {
        if (!IsReady || !HasModule || IsFiring || !player.CanFireCannon || Time.time < nextShotAt ||
            animator.speed <= 0f)
        {
            return false;
        }

        animatorSpeedBeforeFiring = animator.speed;
        chargeStartedAt = Time.time;
        releasedCharge = 0f;
        lockedDirection = player.FacingDirection;
        launched = false;
        releaseRequested = false;
        state = FiringState.Charging;

        chargePreview = Instantiate(projectilePrefab, cannonMuzzle.position, Quaternion.identity, cannonMuzzle);
        if (!chargePreview.BeginPreview(minimumSizeMultiplier))
        {
            chargePreview.Discard();
            chargePreview = null;
            state = FiringState.Idle;
            return false;
        }

        player.PlayCannonAnimation(ChargeStateHash);
        PlayChargeSound();
        return true;
    }

    // Recibe el estado del botón desde PlayerController, que permanece como único propietario del Input.
    public void SetFireHeld(bool held)
    {
        if (!IsFiring || state == FiringState.Recoil || held || releaseRequested)
        {
            return;
        }

        releasedCharge = CurrentCharge;
        lockedDirection = player.FacingDirection;
        releaseRequested = true;
        StopChargeSound();
    }

    private float CurrentCharge => Mathf.Clamp01((Time.time - chargeStartedAt) / maximumChargeDuration);

    private void LateUpdate()
    {
        UpdateChargePreview();

        switch (state)
        {
            case FiringState.Charging:
                UpdateCharging();
                break;

            case FiringState.HoldingCharge:
                UpdateHeldCharge();
                break;

            case FiringState.Recoil:
                UpdateRecoil();
                break;
        }
    }

    private void UpdateChargePreview()
    {
        if (chargePreview == null || state == FiringState.Recoil || state == FiringState.Idle)
        {
            return;
        }

        float charge = releaseRequested ? releasedCharge : CurrentCharge;
        float sizeMultiplier = Mathf.Lerp(minimumSizeMultiplier, maximumSizeMultiplier, charge);
        chargePreview.UpdatePreviewSize(sizeMultiplier);
    }

    private void UpdateCharging()
    {
        if (!player.CanFireCannon || !HasModule)
        {
            CancelFire();
            return;
        }

        if ((!releaseRequested && CurrentCharge >= 1f) || (releaseRequested && releasedCharge >= 1f))
        {
            CompleteMaximumCharge();
            return;
        }

        if (releaseRequested && MinimumShotDelayElapsed)
        {
            LaunchChargedShot();
            return;
        }

        AnimatorStateInfo animation = animator.GetCurrentAnimatorStateInfo(0);
        if (animation.fullPathHash != ChargeStateHash)
        {
            CancelFire();
            return;
        }

        if (animation.normalizedTime < 1f)
        {
            return;
        }

        animator.speed = 0f;
        state = FiringState.HoldingCharge;
    }

    private void UpdateHeldCharge()
    {
        if (!player.CanFireCannon || !HasModule)
        {
            CancelFire();
            return;
        }

        if ((!releaseRequested && CurrentCharge >= 1f) || (releaseRequested && releasedCharge >= 1f))
        {
            CompleteMaximumCharge();
            return;
        }

        if (releaseRequested && MinimumShotDelayElapsed)
        {
            LaunchChargedShot();
        }
    }

    private bool MinimumShotDelayElapsed => Time.time - chargeStartedAt >= minimumShotDelay;

    private void CompleteMaximumCharge()
    {
        releasedCharge = 1f;
        lockedDirection = player.FacingDirection;
        releaseRequested = true;
        StopChargeSound();
        chargePreview?.PlayChargeCompletionFlash(chargeCompletionFlashDuration);
        LaunchChargedShot();
    }

    private void PlayChargeSound()
    {
        if (chargeSound == null || chargeVolume <= 0f)
        {
            return;
        }

        chargeSoundPlaying = chargePreview != null && chargePreview.PlayChargeSound(chargeSound, chargeVolume);
    }

    private void StopChargeSound()
    {
        if (!chargeSoundPlaying)
        {
            return;
        }

        chargePreview?.StopChargeSound();
        chargeSoundPlaying = false;
    }

    private void UpdateRecoil()
    {
        if (!player.CanAct)
        {
            CancelFire();
            return;
        }

        AnimatorStateInfo animation = animator.GetCurrentAnimatorStateInfo(0);
        if (animation.fullPathHash != RecoilStateHash)
        {
            CancelFire();
            return;
        }

        if (animation.normalizedTime >= 1f)
        {
            FinishRecoil();
        }
    }

    private void LaunchChargedShot()
    {
        float charge = releaseRequested ? releasedCharge : CurrentCharge;
        RestoreAnimatorSpeed();

        if (!Launch(charge))
        {
            return;
        }

        state = FiringState.Recoil;
        player.StopHorizontalMovement();
        player.PlayCannonAnimation(RecoilStateHash);
    }

    // Crea la orb, valida su corredor inicial y consume el uso únicamente después de inicializarla.
    private bool Launch(float charge)
    {
        if (projectilePrefab == null)
        {
            Fail("PlayerCannon perdió el prefab de proyectil antes del disparo. No se consume ningún uso.");
            return false;
        }

        float damageMultiplier = Mathf.Lerp(1f, maximumDamageMultiplier, charge);
        float sizeMultiplier = Mathf.Lerp(minimumSizeMultiplier, maximumSizeMultiplier, charge);
        Vector2 direction = Vector2.right * lockedDirection;
        Vector3 muzzlePosition = cannonMuzzle.position;
        // Comprobar a la misma altura desde el centro del jugador hasta la boca.
        Vector2 barrelOrigin = new Vector2(player.transform.position.x, muzzlePosition.y);
        EnergyOrbProjectile projectile = chargePreview;
        chargePreview = null;

        if (projectile != null)
        {
            projectile.transform.SetParent(null, true);
            projectile.transform.position = muzzlePosition;
        }
        else
        {
            projectile = Instantiate(projectilePrefab, muzzlePosition, Quaternion.identity);
        }

        if (!projectile.Initialize(player, direction, damage * damageMultiplier, projectileSpeed,
            projectileRange, sizeMultiplier))
        {
            projectile.Discard();
            Fail("No se pudo inicializar la Energy Orb. No se consume ningún uso.");
            return false;
        }

        // La notificación del último uso debe conservar el brazo hasta acabar el recoil.
        launched = true;
        if (!modules.TryUseAbility(AbilityType.EnergyCannon))
        {
            projectile.Discard();
            CancelFire();
            return false;
        }

        nextShotAt = Time.time + shotInterval;
        projectile.PlayShotSound(shotSound, shotVolume);
        projectile.Launch(barrelOrigin);
        return true;
    }

    private void FinishRecoil()
    {
        RestoreAnimatorSpeed();
        state = FiringState.Idle;
        launched = false;
        releaseRequested = false;
        movementPenaltyUntil = Time.time + postRecoilDuration;
        player.ReleaseCannonAnimation();
        SetSprites(HasModule);
    }

    private void RefreshAppearance()
    {
        if (!ready)
        {
            return;
        }

        if (IsFiring && !HasModule)
        {
            if (launched)
            {
                return;
            }

            CancelFire();
            return;
        }

        SetSprites(HasModule);
    }

    private void SetSprites(bool cannon)
    {
        if (rightUpperArm != null)
        {
            rightUpperArm.sprite = cannon ? upperArmSprite : originalUpper;
        }

        if (rightLowerArm != null)
        {
            rightLowerArm.sprite = cannon ? lowerArmSprite : originalLower;
        }

        if (rightHand != null)
        {
            rightHand.sprite = cannon ? handSprite : originalHand;
        }
    }

    // Aborta la secuencia y devuelve al controlador el dominio de la animación.
    public void CancelFire()
    {
        bool controlledAnimation = IsFiring;

        RestoreAnimatorSpeed();
        state = FiringState.Idle;
        launched = false;
        releaseRequested = false;
        StopChargeSound();

        if (chargePreview != null)
        {
            chargePreview.Discard();
            chargePreview = null;
        }

        if (controlledAnimation && player != null)
        {
            player.ReleaseCannonAnimation();
        }

        if (ready)
        {
            SetSprites(HasModule);
        }
    }

    private void RestoreAnimatorSpeed()
    {
        if (animator != null)
        {
            animator.speed = animatorSpeedBeforeFiring > 0f ? animatorSpeedBeforeFiring : 1f;
        }
    }

    private void Fail(string message)
    {
        Debug.LogError(message, this);
        enabled = false;
    }

    private void OnDisable()
    {
        if (subscribed && !ReferenceEquals(modules, null))
        {
            modules.ExternalModuleChanged -= RefreshAppearance;
        }

        subscribed = false;
        CancelFire();
        movementPenaltyUntil = 0f;

        if (ready)
        {
            SetSprites(false);
        }
    }
}
