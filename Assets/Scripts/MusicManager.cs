using System.Collections;
using UnityEngine;

// Mantiene la música global, mezcla cambios de pista y aplica el filtrado asociado a la muerte.
public class MusicManager : MonoBehaviour
{
    public static MusicManager Instance { get; private set; }
    public bool CriticalEffectActive { get; set; }


    [SerializeField, Min(0f)] private float defaultFadeDuration = 1.5f;
    [SerializeField] private AudioSource currentSource;
    [SerializeField] private AudioSource standbySource;
    
    private Coroutine transitionRoutine;
    private AudioClip requestedClip;
    private float requestedVolume;
    private float currentVolume;
    private float standbyVolume;
    private const float MinimumCutoff = 10f;
    private const float MaximumCutoff = 22000f;
    private bool musicMuted;
    private bool deathEffectActive;
    private bool filtersCaptured;
    private readonly AudioLowPassFilter[] deathFilters = new AudioLowPassFilter[2];
    private readonly float[] originalCutoff = new float[2];
    private readonly bool[] originalFilterEnabled = new bool[2];

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogError("Solo puede haber un MusicManager activo.", this);
            enabled = false;
            return;
        }

        Instance = this;
        
        if (!ValidateSources())
        {
            return;
        }

        currentVolume = currentSource.volume;
        standbyVolume = standbySource.volume;
    }

    // Solicita la pista de una zona y reutiliza el fundido actual cuando la petición cambia a mitad de mezcla.
    public void PlayZoneMusic(AudioClip clip, float volume = 1f, float fadeDuration = -1f)
    {
        if (!isActiveAndEnabled || clip == null || !ValidateSources())
        {
            return;
        }

        volume = Mathf.Clamp01(volume);
        
        if (requestedClip == clip && Mathf.Approximately(requestedVolume, volume))
        {
            return;
        }

        if (transitionRoutine != null)
        {
            StopCoroutine(transitionRoutine);
        }

        transitionRoutine = null;
        requestedClip = clip;
        requestedVolume = volume;
        
        if (currentSource.clip == clip)
        {
            if (!standbySource.isPlaying)
            {
                currentVolume = volume;
                ApplyVolumes();
                
                if (!currentSource.isPlaying)
                {
                    currentSource.Play();
                }

                return;
            }
            
            // Al volver a la zona anterior, invertir el fundido desde los volúmenes actuales.
            SwapSources();
        }

        transitionRoutine = StartCoroutine(Crossfade(clip, volume,
            fadeDuration >= 0f ? fadeDuration : defaultFadeDuration));
    }

    // Mezcla dos AudioSource y convierte el de espera en principal al completar la transición.
    private IEnumerator Crossfade(AudioClip clip, float volume, float duration)
    {
        if (standbySource.clip != clip || !standbySource.isPlaying)
        {
            standbySource.Stop();
            standbySource.clip = clip;
            standbyVolume = 0f;
            ApplyVolumes();
            standbySource.Play();
        }
        
        float outgoing = currentVolume;
        float incoming = standbyVolume;
        float elapsed = 0f;
        
        do
        {
            if (!ValidateSources())
            {
                yield break;
            }

            elapsed += Time.unscaledDeltaTime;
            float progress = duration > 0f ? Mathf.Clamp01(elapsed / duration) : 1f;
            
            currentVolume = Mathf.Lerp(outgoing, 0f, progress);
            standbyVolume = Mathf.Lerp(incoming, volume, progress);
            
            ApplyVolumes();
            
            yield return null;
        } while (elapsed < duration);
        
        if (!ValidateSources())
        {
            yield break;
        }

        currentSource.Stop();
        currentVolume = 0f;
        standbyVolume = volume;
        ApplyVolumes();
        SwapSources();
        transitionRoutine = null;
    }

    private void SwapSources()
    {
        (currentSource, standbySource) = (standbySource, currentSource);
        (currentVolume, standbyVolume) = (standbyVolume, currentVolume);
    }

    private void ApplyVolumes()
    {
        // Conservar los volúmenes de zona incluso mientras la muerte los silencia.
        if (currentSource != null)
        {
            currentSource.volume = musicMuted ? 0f : currentVolume;
        }

        if (standbySource != null)
        {
            standbySource.volume = musicMuted ? 0f : standbyVolume;
        }
    }

    // Aplica un filtro de paso bajo hasta un valor de corte específico (targetCutoff)
    public void BeginCriticalStateEffect(float targetCutoff)
    {
        if (!isActiveAndEnabled || !ValidateSources() || deathEffectActive)
        {
            return;
        }

        // Si ya había otro efecto crítico activo, restauramos primero para tomar los valores limpios
        if (CriticalEffectActive)
        {
            EndCriticalStateEffect();
        }

        deathFilters[0] = currentSource.GetComponent<AudioLowPassFilter>();
        deathFilters[1] = standbySource.GetComponent<AudioLowPassFilter>();

        if (deathFilters[0] == null || deathFilters[1] == null)
        {
            Debug.LogError("Para filtrar la música, añade un Audio Low Pass Filter al objeto de cada AudioSource de música.", this);
        }

        if (deathFilters[1] == deathFilters[0])
        {
            deathFilters[1] = null;
        }

        for (int i = 0; i < deathFilters.Length; i++)
        {
            var filter = deathFilters[i];
            if (filter == null)
            {
                continue;
            }

            originalCutoff[i] = filter.cutoffFrequency;
            originalFilterEnabled[i] = filter.enabled;
        }

        filtersCaptured = true;
        CriticalEffectActive = true;

        float clampedTarget = Mathf.Clamp(targetCutoff, MinimumCutoff, MaximumCutoff);

        for (int i = 0; i < deathFilters.Length; i++)
        {
            var filter = deathFilters[i];
            if (filter == null)
            {
                continue;
            }

            filter.cutoffFrequency = clampedTarget;
            filter.enabled = true;
        }
    }

    // Restaura los filtros modificados por BeginCriticalStateEffect
    public void EndCriticalStateEffect()
    {
        if (!CriticalEffectActive)
        {
            return;
        }

        RestoreFilters();
        CriticalEffectActive = false;
    }

    // Guarda volumen y filtros para que la presentación de muerte pueda alterarlos de forma reversible.
    public void BeginDeathEffect()
    {
        if (!isActiveAndEnabled || !ValidateSources())
        {
            return;
        }

        EndDeathEffect();
        
        deathFilters[0] = currentSource.GetComponent<AudioLowPassFilter>();
        deathFilters[1] = standbySource.GetComponent<AudioLowPassFilter>();
        
        if (deathFilters[0] == null || deathFilters[1] == null)
        {
            Debug.LogError("Para filtrar la música al morir, añade un Audio Low Pass Filter al objeto de cada AudioSource de música. Sin filtro solo se aplica el silencio al apagarse la cabeza.", this);
        }

        if (deathFilters[1] == deathFilters[0])
        {
            deathFilters[1] = null;
        }

        for (int i = 0; i < deathFilters.Length; i++)
        {
            var filter = deathFilters[i];
            if (filter == null)
            {
                continue;
            }

            originalCutoff[i] = filter.cutoffFrequency;
            originalFilterEnabled[i] = filter.enabled;
        }
        
        filtersCaptured = true;
        deathEffectActive = true;
        SetDeathProgress(0f);
    }

    // Cierra el filtro en escala logarítmica mientras caen las piezas del jugador.
    public void SetDeathProgress(float progress)
    {
        if (!deathEffectActive || !isActiveAndEnabled)
        {
            return;
        }

        progress = Mathf.Clamp01(progress);
        
        if (!filtersCaptured)
        {
            return;
        }

        for (int i = 0; i < deathFilters.Length; i++)
        {
            var filter = deathFilters[i];
            
            if (filter == null)
            {
                continue;
            }

            float initial = NormalCutoff(i);
            filter.cutoffFrequency = InterpolateCutoff(initial, MinimumCutoff, progress);
            filter.enabled = true;
        }
    }

    public void SilenceUntilRespawn()
    {
        if (!deathEffectActive)
        {
            return;
        }

        SetDeathProgress(1f);
       
        musicMuted = true;
        ApplyVolumes();
    }

    // Recupera el volumen de golpe, pero deja el filtro cerrado hasta que comience el revelado.
    public void ResumeVolumeAfterRespawn()
    {
        if (!deathEffectActive)
        {
            return;
        }
        
        // El filtro sigue en 10 Hz hasta que empiece el fundido visual.
        musicMuted = false;
        ApplyVolumes();
    }

    public void SetRevealProgress(float progress)
    {
        if (!deathEffectActive || !filtersCaptured || !isActiveAndEnabled)
        {
            return;
        }

        for (int i = 0; i < deathFilters.Length; i++)
        {
            var filter = deathFilters[i];
            
            if (filter != null)
            {
                filter.cutoffFrequency = InterpolateCutoff(MinimumCutoff, NormalCutoff(i), progress);
            }
        }
    }

    private float NormalCutoff(int index)
    {
        return originalFilterEnabled[index] ? Mathf.Clamp(originalCutoff[index], MinimumCutoff, MaximumCutoff) : MaximumCutoff;
    }

    private static float InterpolateCutoff(float from, float to, float progress)
    {
        if (progress <= 0f)
        {
            return from;
        }

        if (progress >= 1f)
        {
            return to;
        }

        return Mathf.Exp(Mathf.Lerp(Mathf.Log(from), Mathf.Log(to), progress));
    }

    // Restaura la configuración previa y finaliza cualquier estado temporal de muerte.
    public void EndDeathEffect()
    {
        if (!deathEffectActive)
        {
            return;
        }

        RestoreFilters();
        deathEffectActive = false;
        musicMuted = false;
        ApplyVolumes();
    }

    private void RestoreFilters()
    {
        if (!filtersCaptured)
        {
            return;
        }

        for (int i = 0; i < deathFilters.Length; i++)
        {
            var filter = deathFilters[i];
            if (filter == null)
            {
                continue;
            }

            filter.enabled = originalFilterEnabled[i];
            filter.cutoffFrequency = originalCutoff[i];
        }

        filtersCaptured = false;
    }

    private bool ValidateSources()
    {
        // También se valida desde Awake, antes de que las fuentes reciban OnEnable.
        if (currentSource != null && standbySource != null && currentSource != standbySource &&
            currentSource.enabled && currentSource.gameObject.activeInHierarchy &&
            standbySource.enabled && standbySource.gameObject.activeInHierarchy)
        {
            return true;
        }

        Debug.LogError("MusicManager necesita dos AudioSource distintos y activos, asignados a Current Source y Standby Source.", this);
        enabled = false;
        return false;
    }

    private void OnDisable()
    {
        if (Instance != this)
        {
            return;
        }

        EndDeathEffect();
        
        if (transitionRoutine != null)
        {
            StopCoroutine(transitionRoutine);
        }

        transitionRoutine = null;
        
        if (currentSource != null)
        {
            currentSource.Stop();
        }

        if (standbySource != null)
        {
            standbySource.Stop();
        }

        requestedClip = null;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }

        EndDeathEffect();
        EndCriticalStateEffect();
    }
}
