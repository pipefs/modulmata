using System.Collections.Generic;
using UnityEngine;

// Proyectil de daño directo. Su explosión es visual, no vuelve a aplicar daño.
[DisallowMultipleComponent]
public sealed class EnergyOrbProjectile : MonoBehaviour
{
    [SerializeField] private SpriteRenderer visual;
    [SerializeField] private Sprite[] flightFrames;
    [SerializeField, Min(0.01f)] private float flightFrameDuration = 0.1f;
    [SerializeField] private Sprite[] impactFrames;
    [SerializeField, Min(0.01f)] private float impactFrameDuration = 0.03f;
    [Header("Colisiones")]
    [SerializeField] private Vector2 hitboxSize = new Vector2(0.36f, 0.36f);
    [SerializeField] private LayerMask worldLayers = 1 << 3;
    [SerializeField] private LayerMask damageLayers = 1 << 7;
    [Header("Impacto")]
    [SerializeField] private AudioSource impactAudioSource;
    [SerializeField] private AudioClip impactSound;
    [SerializeField, Range(0f, 1f)] private float impactVolume = 0.5f;

    private readonly List<RaycastHit2D> worldHits = new(8);
    private readonly List<Collider2D> worldOverlaps = new(4);
    private ContactFilter2D worldFilter;
    private PlayerController owner;
    private GameManager session;
    private SpriteFlash chargeFlash;
    private Vector3 baseVisualScale;
    private Vector2 direction;
    private Vector2 effectiveHitboxSize;
    private float damage;
    private float speed;
    private float range;
    private float travelled;
    private float visualTime;
    private bool initialized;
    private bool launched;
    private bool impacting;
    private bool previewing;
    private bool discarded;

    // Comprueba que toda la colisión sea dirigida por consultas del script y no por física del prefab.
    public bool ValidateConfiguration()
    {
        SpriteFlash configuredFlash = chargeFlash != null ? chargeFlash : GetComponent<SpriteFlash>();
        if (visual == null || !visual.enabled || !visual.transform.IsChildOf(transform) ||
            !FramesValid(flightFrames) || !FramesValid(impactFrames) ||
            !PositiveFinite(flightFrameDuration) || !PositiveFinite(impactFrameDuration) ||
            !PositiveFinite(hitboxSize.x) || !PositiveFinite(hitboxSize.y) ||
            worldLayers.value == 0 || damageLayers.value == 0 || (worldLayers.value & damageLayers.value) != 0 ||
            configuredFlash == null || !configuredFlash.enabled || !configuredFlash.IsConfigured)
        {
            Debug.LogError("EnergyOrbProjectile necesita Visual hijo, sprites de vuelo e impacto, SpriteFlash activo, tiempos/tamaño positivos y máscaras de escenario y daño distintas y no vacías.", this);
            return false;
        }
        if (GetComponentInChildren<Rigidbody2D>(true) != null || GetComponentInChildren<Collider2D>(true) != null ||
            GetComponentInChildren<Animator>(true) != null)
        {
            Debug.LogError("EnergyOrbProjectile mueve y anima sus sprites por código: su prefab no debe incluir Rigidbody2D, Collider2D ni Animator.", this);
            return false;
        }
        if (impactAudioSource != null && !impactAudioSource.transform.IsChildOf(transform))
        {
            Debug.LogError("El AudioSource de impacto debe pertenecer al prefab de EnergyOrbProjectile.", this);
            return false;
        }
        return true;
    }

    private static bool FramesValid(Sprite[] frames)
    {
        if (frames == null || frames.Length == 0)
        {
            return false;
        }

        foreach (Sprite frame in frames)
        {
            if (frame == null)
            {
                return false;
            }
        }

        return true;
    }

    private static bool PositiveFinite(float value)
    {
        return value > 0f && !float.IsInfinity(value);
    }

    // Muestra el proyectil como una previsualización sin activar movimiento, colisiones ni daño.
    public bool BeginPreview(float sizeMultiplier)
    {
        if (!isActiveAndEnabled || initialized || launched || discarded || !PositiveFinite(sizeMultiplier))
        {
            return false;
        }

        previewing = true;
        visualTime = 0f;
        ApplySize(sizeMultiplier);
        visual.sprite = flightFrames[0];
        return true;
    }

    public void UpdatePreviewSize(float sizeMultiplier)
    {
        if (previewing && PositiveFinite(sizeMultiplier))
        {
            ApplySize(sizeMultiplier);
        }
    }

    public void PlayChargeCompletionFlash(float duration)
    {
        if (previewing && chargeFlash != null)
        {
            chargeFlash.Play(Color.white, duration);
        }
    }

    public bool PlayChargeSound(AudioClip clip, float volume)
    {
        if (!previewing || impactAudioSource == null || !impactAudioSource.isActiveAndEnabled ||
            clip == null || volume <= 0f)
        {
            return false;
        }

        impactAudioSource.Stop();
        impactAudioSource.PlayOneShot(clip, volume);
        return true;
    }

    public void StopChargeSound()
    {
        if (previewing && impactAudioSource != null)
        {
            impactAudioSource.Stop();
        }
    }

    public void PlayShotSound(AudioClip clip, float volume)
    {
        if (impactAudioSource != null && impactAudioSource.isActiveAndEnabled && clip != null && volume > 0f)
        {
            impactAudioSource.PlayOneShot(clip, volume);
        }
    }

    private void Awake()
    {
        chargeFlash = GetComponent<SpriteFlash>();
        if (!ValidateConfiguration())
        {
            enabled = false;
            return;
        }
        worldFilter = new ContactFilter2D { useTriggers = false };
        worldFilter.SetLayerMask(worldLayers);
        baseVisualScale = visual.transform.localScale;
        effectiveHitboxSize = hitboxSize;
        visual.sprite = flightFrames[0];
    }

    // Vincula el proyectil a la sesión y al jugador que deben seguir válidos durante su recorrido.
    public bool Initialize(PlayerController source, Vector2 shotDirection, float shotDamage, float shotSpeed,
        float shotRange, float sizeMultiplier)
    {
        if (!isActiveAndEnabled || source == null || !source.IsPlaying || GameManager.Instance == null ||
            !PositiveFinite(shotDamage) || !PositiveFinite(shotSpeed) || !PositiveFinite(shotRange) ||
            !PositiveFinite(sizeMultiplier) || shotDirection.sqrMagnitude < 0.001f)
        {
            return false;
        }

        owner = source;
        session = GameManager.Instance;
        direction = shotDirection.normalized;
        damage = shotDamage;
        speed = shotSpeed;
        range = shotRange;
        previewing = false;
        visualTime = 0f;
        ApplySize(sizeMultiplier);
        initialized = true;
        return true;
    }

    private void ApplySize(float sizeMultiplier)
    {
        effectiveHitboxSize = hitboxSize * sizeMultiplier;
        visual.transform.localScale = baseVisualScale * sizeMultiplier;
    }

    // Comprobar también el trayecto hasta la boca evita disparar al otro lado de una pared.
    public void Launch(Vector2 barrelOrigin)
    {
        if (!initialized || launched || discarded)
        {
            return;
        }

        launched = true;
        Vector2 muzzle = transform.position;
        transform.position = new Vector3(barrelOrigin.x, barrelOrigin.y, transform.position.z);
        Vector2 corridor = muzzle - barrelOrigin;
        SweepAndMove(corridor.sqrMagnitude > 0f ? corridor.normalized : direction, corridor.magnitude);
        travelled = 0f;
    }

    private bool OwnerIsPlaying => owner != null && session != null && session.IsPlaying(owner);

    private void FixedUpdate()
    {
        if (!launched || impacting || discarded)
        {
            return;
        }

        if (!OwnerIsPlaying)
        {
            Discard();
            return;
        }
        float distance = Mathf.Min(speed * Time.fixedDeltaTime, range - travelled);
        SweepAndMove(direction, distance);
        travelled += distance;
        if (!impacting && travelled >= range)
        {
            Discard();
        }
    }

    // Compara el primer obstáculo físico con el primer volumen lógico de daño para resolver el impacto más cercano.
    private void SweepAndMove(Vector2 travelDirection, float distance)
    {
        Vector2 origin = transform.position;
        float wallDistance = float.PositiveInfinity;
        Vector2 wallPoint = origin;
        Physics2D.OverlapBox(origin, effectiveHitboxSize, 0f, worldFilter, worldOverlaps);
        if (worldOverlaps.Count > 0)
        {
            wallDistance = 0f;
        }
        else if (distance > 0f)
        {
            Physics2D.BoxCast(origin, effectiveHitboxSize, 0f, travelDirection, worldFilter, worldHits, distance);
            foreach (RaycastHit2D hit in worldHits)
            {
                if (hit.distance < wallDistance)
                {
                    wallDistance = hit.distance;
                    wallPoint = hit.point + hit.normal * 0.02f;
                }
            }
        }
        bool hitEnemy = CombatTargets.TryFindFirstHit(origin, effectiveHitboxSize, travelDirection, distance,
            damageLayers, owner, out MonoBehaviour target, out float enemyDistance);
        if (wallDistance <= distance && (!hitEnemy || wallDistance <= enemyDistance))
        {
            Impact(null, wallPoint);
            return;
        }
        if (hitEnemy)
        {
            Vector2 center = origin + travelDirection * enemyDistance;
            Vector2 point = ((IDamageable)target).DamageBounds.ClosestPoint(center);
            Impact(target, point);
            return;
        }
        Vector2 end = origin + travelDirection * distance;
        transform.position = new Vector3(end.x, end.y, transform.position.z);
    }

    // Aplica daño una sola vez y cambia a una explosión visual que puede conservar su cola de audio.
    private void Impact(MonoBehaviour target, Vector2 point)
    {
        impacting = true;
        visualTime = 0f;
        transform.position = new Vector3(point.x, point.y, transform.position.z);
        visual.sprite = impactFrames[0];
        if (target != null && target.isActiveAndEnabled)
        {
            ((IDamageable)target).TryTakeDamage(damage);
        }

        if (impactAudioSource != null && impactAudioSource.isActiveAndEnabled && impactSound != null)
        {
            impactAudioSource.PlayOneShot(impactSound, impactVolume);
        }
    }

    private void Update()
    {
        if (previewing && !discarded)
        {
            visualTime += Time.deltaTime;
            visual.sprite = flightFrames[Mathf.FloorToInt(visualTime / flightFrameDuration) % flightFrames.Length];
            return;
        }

        if (!initialized || !launched || discarded)
        {
            return;
        }

        if (!OwnerIsPlaying)
        {
            Discard();
            return;
        }
        visualTime += Time.deltaTime;
        if (!impacting)
        {
            visual.sprite = flightFrames[Mathf.FloorToInt(visualTime / flightFrameDuration) % flightFrames.Length];
            return;
        }
        int frame = Mathf.FloorToInt(visualTime / impactFrameDuration);
        if (frame < impactFrames.Length)
        {
            visual.sprite = impactFrames[frame];
        }
        else
        {
            visual.enabled = false;
            // La cola de audio termina aunque la explosión ya no sea visible.
            if (impactAudioSource == null || !impactAudioSource.isPlaying)
            {
                Discard();
            }
        }
    }

    public void Discard()
    {
        discarded = true;
        if (visual != null)
        {
            visual.enabled = false;
        }

        Destroy(gameObject);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.green;
        Vector2 previewSize = initialized ? effectiveHitboxSize : hitboxSize;
        Gizmos.DrawWireCube(transform.position, new Vector3(previewSize.x, previewSize.y, 0f));
    }
}
