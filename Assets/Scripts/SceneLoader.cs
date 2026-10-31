using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

// Ejecuta cambios de escena bajo una cobertura y espera la preparación explícita de la escena nueva.
public sealed class SceneLoader : MonoBehaviour
{
    public static SceneLoader Instance { get; private set; }
    [SerializeField] private TransitionManager transitions;
    public bool IsLoading { get; private set; }
    private Coroutine routine;
    private bool initialReveal;
    private bool blocked;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            if (Instance.transform.root != transform.root)
            {
                Destroy(transform.root.gameObject);
            }
            else
            {
                Debug.LogError("Solo puede haber un SceneLoader en PersistentSystems.", this);
            }

            enabled = false;
            return;
        }
        if (transitions == null || transitions != TransitionManager.Instance ||
            transitions.transform.root != transform.root || !transitions.TryAcquire(this))
        {
            Debug.LogError("SceneLoader necesita el TransitionManager activo de su misma raíz persistente.", this);
            enabled = false;
            return;
        }
        Instance = this;
        initialReveal = true;
        transitions.SetCovered(this, transitions.SceneColor);
    }

    // La primera escena también usa el mismo protocolo de preparación y revelado que una carga posterior.
    private void Start()
    {
        if (initialReveal)
        {
            routine = StartCoroutine(EnterScene(SceneManager.GetActiveScene(), true));
        }
    }

    // Acepta una sola solicitud de carga y conserva callbacks únicamente mientras la escena actual existe.
    public bool Load(string sceneName, Action<float> coverProgress = null, Action failed = null)
    {
        if (!isActiveAndEnabled || IsLoading || blocked || string.IsNullOrWhiteSpace(sceneName) ||
            sceneName == SceneManager.GetActiveScene().name || !Application.CanStreamedLevelBeLoaded(sceneName))
        {
            Debug.LogError("No se puede cargar la escena solicitada: " + sceneName + ". Comprueba Build Profiles y que no haya otra carga activa.", this);
            return false;
        }
        if (!transitions.TryAcquire(this))
        {
            return false;
        }

        if (initialReveal && routine != null)
        {
            StopCoroutine(routine);
        }

        initialReveal = false;
        IsLoading = true;
        routine = StartCoroutine(ChangeScene(sceneName, coverProgress, failed));
        return true;
    }

    // Cubre, sustituye la escena y transfiere la decisión de revelado a EnterScene.
    private IEnumerator ChangeScene(string sceneName, Action<float> progress, Action failed)
    {
        yield return transitions.Cover(this, transitions.SceneColor, transitions.SceneCoverDuration, progress);
        progress = null;
        if (transitions == null || !transitions.IsReady)
        {
            failed?.Invoke();
            Block("La transición dejó de estar disponible antes de cargar " + sceneName + ".");
            yield break;
        }
        AsyncOperation operation = TryLoad(sceneName);
        if (operation == null)
        {
            yield return transitions.Reveal(this, transitions.SceneRevealDuration);
            transitions.Release(this);
            IsLoading = false;
            routine = null;
            failed?.Invoke();
            yield break;
        }
        yield return operation;
        // No conservar callbacks del menú descargado durante la preparación de Main.
        failed = null;
        yield return EnterScene(SceneManager.GetActiveScene(), false);
    }

    private AsyncOperation TryLoad(string sceneName)
    {
        try
        {
            AsyncOperation operation = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
            if (operation == null)
            {
                Debug.LogError("Unity no pudo iniciar la carga de " + sceneName, this);
            }

            return operation;
        }
        catch (Exception error)
        {
            Debug.LogError("No se pudo cargar " + sceneName + ": " + error.Message, this);
            return null;
        }
    }

    // Espera SceneReady cuando existe; una escena sin marcador se revela automáticamente tras inicializarse.
    private IEnumerator EnterScene(Scene scene, bool initial)
    {
        // Permitir que se ejecuten los Start de la escena antes de decidir su revelación.
        yield return null;
        SceneReady ready = null;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (SceneReady candidate in root.GetComponentsInChildren<SceneReady>(true))
            {
                if (ready != null)
                {
                    Block("La escena " + scene.name + " debe tener como máximo un SceneReady.");
                    yield break;
                }
                ready = candidate;
            }
        }

        bool expectsReady = ready != null;
        if (expectsReady)
        {
            while (ready != null && ready.isActiveAndEnabled && !ready.HasFailed && !ready.IsReady)
            {
                yield return null;
            }

            if (ready == null || !ready.isActiveAndEnabled || ready.HasFailed)
            {
                Block("La preparación de " + scene.name + " falló. Se conserva la pantalla cubierta.");
                yield break;
            }
        }
        yield return new WaitForEndOfFrame();
        yield return transitions.Reveal(this, initial ? transitions.InitialRevealDuration : transitions.SceneRevealDuration);
        if (transitions == null || !transitions.IsReady ||
            (expectsReady && (ready == null || !ready.isActiveAndEnabled || ready.HasFailed)))
        {
            Block("No se pudo completar la revelación de " + scene.name + ".");
            yield break;
        }
        if (ready != null)
        {
            ready.MarkRevealed();
        }

        transitions.Release(this);
        initialReveal = false;
        IsLoading = false;
        routine = null;
    }

    // Conserva la pantalla cubierta cuando la escena no puede garantizar una entrada válida.
    private void Block(string message)
    {
        blocked = true;
        initialReveal = false;
        if (transitions != null && transitions.IsReady)
        {
            transitions.SetCovered(this, transitions.SceneColor);
        }

        Debug.LogError(message, this);
    }

    private void OnDisable()
    {
        if (Instance != this)
        {
            return;
        }

        StopAllCoroutines();
        if (IsLoading)
        {
            Debug.LogError("SceneLoader se desactivó durante una carga; la operación de Unity no se puede cancelar.", this);
        }

        if (transitions != null)
        {
            transitions.Release(this, !IsLoading && !blocked);
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }
}
