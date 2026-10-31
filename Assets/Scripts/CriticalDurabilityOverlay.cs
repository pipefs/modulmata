using UnityEngine;
using UnityEngine.UI;

public sealed class CriticalDurabilityOverlay : MonoBehaviour
{
    [SerializeField] private Image overlay;
    [SerializeField, Min(0f)] private float fadeInDuration = 0.3f;
    [SerializeField, Min(0f)] private float fadeOutDuration = 0.3f;
    [SerializeField, Min(0.01f)] private float pulsePeriod = 1.2f;
    [SerializeField, Range(0f, 1f)] private float minimumPulseIntensity = 0.7f;

    private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
    private static readonly int AspectId = Shader.PropertyToID("_Aspect");

    private GameManager manager;
    private PlayerController player;
    private BatterySystem battery;
    private Canvas canvas;
    private Material originalMaterial;
    private Material runtimeMaterial;
    private float visibility;
    private float pulsePhase;

    private void Awake()
    {
        canvas = overlay != null ? overlay.GetComponentInParent<Canvas>() : null;
        originalMaterial = overlay != null ? overlay.material : null;

        if (overlay == null || canvas == null)
        {
            enabled = false;
            HideImmediately();
            return;
        }

        Shader shader = originalMaterial != null ? originalMaterial.shader : null;

        if (shader == null || !shader.isSupported || shader.name != "MODULMATA/Critical Durability")
        {
            enabled = false;
            HideImmediately();
            return;
        }

        runtimeMaterial = new Material(originalMaterial);
        overlay.material = runtimeMaterial;
        overlay.color = Color.white;
        overlay.raycastTarget = false;
        HideImmediately();
    }

    private void OnEnable()
    {
        Connect();
    }

    private void Connect()
    {
        GameManager current = GameManager.Instance;

        if (ReferenceEquals(manager, current))
        {
            return;
        }

        if (!ReferenceEquals(manager, null))
        {
            manager.PlayerChanged -= BindPlayer;
        }

        manager = current;

        if (manager != null)
        {
            manager.PlayerChanged += BindPlayer;
        }

        BindPlayer(manager != null ? manager.CurrentPlayer : null);
    }

    // Sigue la batería de la instancia vigente para sobrevivir a la sustitución del jugador al reaparecer.
    private void BindPlayer(PlayerController current)
    {
        player = current;
        battery = player != null ? player.GetComponent<BatterySystem>() : null;
        HideImmediately();
    }

    // Funde el material únicamente cuando la durabilidad llega a cero y mantiene su pulso visual.
    private void LateUpdate()
    {
        Connect();

        if (overlay == null || canvas == null || runtimeMaterial == null || overlay.material != runtimeMaterial)
        {
            enabled = false;
            return;
        }

        if (manager == null || !manager.IsPlaying(player))
        {
            HideImmediately();
            return;
        }

        if (battery == null || !battery.isActiveAndEnabled || !overlay.gameObject.activeInHierarchy || !canvas.isActiveAndEnabled)
        {
            HideImmediately();
            return;
        }

        bool critical = battery.CurrentDurability <= 0f;
        float duration = critical ? fadeInDuration : fadeOutDuration;
        float target = critical ? 1f : 0f;

        visibility = duration > 0f ? Mathf.MoveTowards(visibility, target, Time.deltaTime / duration) : target;

        if (visibility <= 0f)
        {
            HideImmediately();
            return;
        }

        pulsePhase = Mathf.Repeat(pulsePhase + Time.deltaTime / Mathf.Max(0.01f, pulsePeriod), 1f);

        float wave = 0.5f + 0.5f * Mathf.Cos(pulsePhase * Mathf.PI * 2f);
        float pulse = Mathf.Lerp(Mathf.Clamp01(minimumPulseIntensity), 1f, wave);
        float intensity = Mathf.SmoothStep(0f, 1f, visibility) * pulse;
        Rect rect = overlay.rectTransform.rect;

        runtimeMaterial.SetFloat(AspectId, Mathf.Max(0.01f, rect.width / Mathf.Max(0.01f, rect.height)));
        runtimeMaterial.SetFloat(IntensityId, intensity);
        overlay.enabled = true;
    }

    private void HideImmediately()
    {
        visibility = 0f;
        pulsePhase = 0f;

        if (runtimeMaterial != null)
        {
            runtimeMaterial.SetFloat(IntensityId, 0f);
        }

        if (overlay != null)
        {
            overlay.enabled = false;
        }
    }

    private void OnDisable()
    {
        if (!ReferenceEquals(manager, null))
        {
            manager.PlayerChanged -= BindPlayer;
        }

        manager = null;
        player = null;
        battery = null;

        HideImmediately();
    }

    private void OnDestroy()
    {
        if (overlay != null && runtimeMaterial != null && overlay.material == runtimeMaterial)
        {
            overlay.material = originalMaterial;
        }

        if (runtimeMaterial != null)
        {
            Destroy(runtimeMaterial);
        }
    }
}
