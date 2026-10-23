using System.Collections;
using UnityEngine;

public class MusicManager : MonoBehaviour
{
    public static MusicManager Instance { get; private set; }

    [SerializeField, Min(0f)] private float defaultFadeDuration = 1.5f;

    private AudioSource currentSource;
    private AudioSource standbySource;
    private Coroutine transitionRoutine;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        currentSource = CreateMusicSource("Música A");
        standbySource = CreateMusicSource("Música B");
    }

    public void PlayZoneMusic(AudioClip clip, float volume = 1f, float fadeDuration = -1f)
    {
        if (clip == null || currentSource.clip == clip)
            return;

        if (transitionRoutine != null)
            StopCoroutine(transitionRoutine);

        float duration = fadeDuration >= 0f ? fadeDuration : defaultFadeDuration;
        transitionRoutine = StartCoroutine(Crossfade(clip, Mathf.Clamp01(volume), duration));
    }

    private IEnumerator Crossfade(AudioClip nextClip, float targetVolume, float duration)
    {
        standbySource.Stop();
        standbySource.clip = nextClip;
        standbySource.volume = 0f;
        standbySource.Play();

        float originalVolume = currentSource.volume;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float progress = duration <= 0f ? 1f : elapsed / duration;
            currentSource.volume = Mathf.Lerp(originalVolume, 0f, progress);
            standbySource.volume = Mathf.Lerp(0f, targetVolume, progress);
            yield return null;
        }

        currentSource.Stop();
        currentSource.volume = 0f;
        standbySource.volume = targetVolume;

        (currentSource, standbySource) = (standbySource, currentSource);
        transitionRoutine = null;
    }

    private AudioSource CreateMusicSource(string sourceName)
    {
        var sourceObject = new GameObject(sourceName);
        sourceObject.transform.SetParent(transform, false);

        var source = sourceObject.AddComponent<AudioSource>();
        source.loop = true;
        source.playOnAwake = false;
        source.spatialBlend = 0f;
        return source;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }
}
