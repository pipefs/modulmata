using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEditor;
using UnityEngine;

// Dirige el bucle principal del juego: jugador activo, inicio, muerte, reaparición, puertas y victoria.
[DefaultExecutionOrder(-100)]
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }
    public PlayerController CurrentPlayer { get; private set; }
    public event Action<PlayerController> PlayerChanged;

    [SerializeField] private PlayerController playerPrefab;
    [SerializeField] private AudioSource generalAudioSource;

    [Header("Inicio")]
    [SerializeField] private PlayerController initialPlayer;
    [SerializeField] private CinemachineBrain cameraBrain;
    [SerializeField] private CinemachineCamera playerCamera;
    [SerializeField] private Transform deathCameraTarget;

    [Header("Muerte")]
    [SerializeField] private TVOffTransition tvOffTransition;
    [SerializeField] private Color respawnFadeOutColor = Color.black;
    [SerializeField] private AudioClip tvOffClip;

    [Header("Respawn")]
    [SerializeField] private AudioClip respawnClip;

    [Header("Final")]
    [SerializeField] private CinemachineCamera finalCamera;
    [SerializeField, Min(1)] private int finalCameraPriorityOffset = 20;

    // Estados
    public bool IsDead { get; private set; }
    public bool HasWon { get; private set; }
    public bool HasFinalKey { get; private set; }
    public bool HasStarted { get; private set; }

    public BossArena ActiveArena { get; set; }
    public IReadOnlyList<ModuleData> InternalModules => internalModules.AsReadOnly();

    // Impide que dos secuencias globales intenten controlar al jugador y la transición al mismo tiempo.
    private enum Operation
    {
        None,
        Respawn,
        Door,
        Victory
    }

    private Operation operation;
    private readonly List<ModuleData> internalModules = new();
    private TransitionManager transitions;
    private SceneReady sceneReady;
    private LevelIntroController levelIntro;
    private GameCameras cameras;
    private Vector3 initialSpawn;
    private Coroutine doorTransitionRoutine;
    private Coroutine respawnRoutine;
    private Coroutine victoryRoutine;
    private GameObject defeatedBody;
    private GameObject remains;
    private MusicManager deathMusic;
    private EnemySpawnZone[] enemySpawnZones = Array.Empty<EnemySpawnZone>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            enabled = false;
            return;
        }

        Instance = this;
        sceneReady = GetComponent<SceneReady>();
        levelIntro = GetComponent<LevelIntroController>();

        if (playerCamera == null || playerPrefab == null)
        {
            Debug.LogError("[ERROR] GameManager necesita Player Camera y Player Prefab asignados.");
            enabled = false;
            return;
        }
    }

    // Prepara jugador, cámaras e introducción bajo la cobertura antes de declarar la escena 'Main' jugable.
    private IEnumerator Start()
    {
        transitions = TransitionManager.Instance;

        if (transitions == null || !transitions.IsReady || SceneLoader.Instance == null || sceneReady == null)
        {
            Debug.LogError("[ERROR] GameManager necesita TransitionManager, SceneLoader y SceneReady en su mismo objeto.");
            enabled = false;
            yield break;
        }

        if (initialPlayer == null || !initialPlayer.IsInitialized)
        {
            Debug.LogError("[ERROR] GameManager necesita Initial Player.");
            enabled = false;
            yield break;
        }

        if (cameraBrain == null)
        {
            Debug.LogError("[ERROR] GameManager necesita Cinemachine Brain de la camara principal.");
            enabled = false;
            yield break;
        }

        if (levelIntro == null || !levelIntro.Prepare(initialPlayer))
        {
            Debug.LogError("[ERROR] GameManager necesita un LevelIntroController en el mismo objeto.");
            enabled = false;
            yield break;
        }

        cameras = new GameCameras(this, playerCamera, finalCamera, cameraBrain, finalCameraPriorityOffset, deathCameraTarget);
        RegisterPlayer(initialPlayer, false);
        InitializeEnemySpawnZones(initialPlayer);

        cameraBrain.ResetState();

        yield return null;
        yield return new WaitForEndOfFrame();

        sceneReady.MarkReady();

        while (!sceneReady.IsRevealed && !sceneReady.HasFailed)
        {
            yield return null;
        }

        if (CurrentPlayer != initialPlayer || CurrentPlayer == null || sceneReady.HasFailed || transitions == null || !transitions.IsReady)
        {
            Debug.LogError("[ERROR] No se pudo completar la presentación inicial.");
            enabled = false;
            yield break;
        }

        yield return levelIntro.Play(() => cameras.Follow(initialPlayer));

        if (levelIntro == null || !levelIntro.IsComplete || CurrentPlayer != initialPlayer || initialPlayer == null)
        {
            Debug.LogError("[ERROR] No se pudo completar la caída inicial.");
            enabled = false;
            yield break;
        }

        initialSpawn = initialPlayer.transform.position;
        HasStarted = true;
    }

    public bool IsPlaying(PlayerController player)
    {
        return HasStarted && player != null && SceneLoader.Instance != null && !SceneLoader.Instance.IsLoading && player.IsInitialized && player == CurrentPlayer && !IsDead && !HasWon;
    }

    // Adopta una instancia como jugador actual y le restaura los módulos internos persistentes de la partida.
    private void RegisterPlayer(PlayerController player, bool followImmediately = true)
    {
        if (player == null || !player.IsInitialized)
        {
            Debug.LogError("[ERROR] No se puede registrar un jugador sin inicializar.");
            enabled = false;
            return;
        }

        var modules = player.GetComponent<PlayerModuleManager>();
        var battery = player.GetComponent<BatterySystem>();

        if (modules == null || battery == null)
        {
            Debug.LogError("[ERROR] El jugador debe tener PlayerModuleManager y BatterySystem.");
            enabled = false;
            return;
        }

        foreach (var module in modules.InternalModules)
        {
            UnlockInternalModule(module);
        }

        modules.RestoreInternalModules(internalModules);
        CurrentPlayer = player;

        IsDead = operation == Operation.Respawn;
        cameras.Follow(followImmediately ? player : null);

        if (ActiveArena != null)
        {
            ActiveArena.NotifyPlayerRespawned();
        }

        PlayerChanged?.Invoke(player);
    }

    public bool UnlockInternalModule(ModuleData module)
    {
        if (module == null || module.type != ModuleType.Internal || module.ability == AbilityType.None || module.ability == AbilityType.EnergyCannon || internalModules.Exists(m => m.ability == module.ability))
        {
            return false;
        }

        internalModules.Add(new ModuleData(module));
        return true;
    }

    public bool HasInternalAbility(AbilityType ability)
    {
        return ability != AbilityType.None && internalModules.Exists(m => m.ability == ability);
    }

    public void UnregisterPlayer(PlayerController player)
    {
        if (!ReferenceEquals(CurrentPlayer, player))
        {
            return;
        }

        if (operation == Operation.Respawn)
        {
            CancelRespawn();
        }

        CancelDoorTransition();

        if (operation == Operation.Victory)
        {
            if (victoryRoutine != null)
            {
                StopCoroutine(victoryRoutine);
            }

            victoryRoutine = null;
            HasWon = false;
            operation = Operation.None;

            if (transitions != null)
            {
                transitions.Release(this);
            }
        }

        if (ActiveArena != null)
        {
            ActiveArena.ResetAfterPlayerDeath(player);
        }

        CurrentPlayer = null;
        cameras?.Follow(null);
        PlayerChanged?.Invoke(null);
    }

    // Retira al jugador actual, crea sus restos y comienza la presentación completa de reaparición.
    public void HandlePlayerDeath(PlayerController player)
    {
        if (!IsPlaying(player))
        {
            return;
        }

        if (playerPrefab == null)
        {
            Debug.LogError("[ERROR] No se puede reaparecer: falta 'Player Prefab'.", this);
            player.StopControls();
            enabled = false;
            return;
        }

        IsDead = true;
        CancelDoorTransition();

        if (transitions == null || !transitions.TryAcquire(this))
        {
            Debug.LogError("[ERROR] No se pudo reservar la transición de muerte.", this);
            enabled = false;
            return;

        }

        operation = Operation.Respawn;
        Vector3 deathPosition = player.transform.position;

        // Capturar la pose visible antes de desactivar el cuerpo o notificar su retirada.
        player.GetComponent<SpriteFlash>()?.StopAndRestore();
        remains = CreatePhysicalRemains(player, out var detachedHead);

        Sprite poweredOffHead = player.PoweredOffHeadSprite;
        if (detachedHead == null || poweredOffHead == null)
        {
            Debug.LogError("[ERROR] Falta 'Death Head Renderer' o 'Powered Off Head Sprite' en el jugador.", player);
        }

        cameras.HoldPlayer();
        defeatedBody = player.gameObject;
        player.gameObject.SetActive(false);
        CurrentPlayer = null;

        if (ActiveArena != null)
        {
            ActiveArena.BeginPlayerDeath();
        }

        PlayerChanged?.Invoke(null);
        respawnRoutine = StartCoroutine(Respawn(deathPosition, detachedHead, poweredOffHead));
    }

    // Separa los sprites visibles en cuerpos físicos para que la muerte conserve la pose del último frame.
    private GameObject CreatePhysicalRemains(PlayerController player, out SpriteRenderer detachedHead)
    {
        detachedHead = null;

        GameObject remainsRoot = new GameObject("Restos del jugador");
        SpriteRenderer[] renderers = player.GetComponentsInChildren<SpriteRenderer>();
        int remainsLayer = LayerMask.NameToLayer("Cuerpo");

        if (remainsLayer < 0)
        {
            remainsLayer = player.gameObject.layer;
        }

        foreach (var source in renderers)
        {
            if (!source.enabled || source.sprite == null)
            {
                continue;
            }

            GameObject part = new GameObject($"Resto - {source.gameObject.name}");
            part.layer = remainsLayer;

            Transform partTransform = part.transform;
            partTransform.SetParent(remainsRoot.transform);
            partTransform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
            partTransform.localScale = source.transform.lossyScale;

            SpriteRenderer sprite = part.AddComponent<SpriteRenderer>();
            sprite.sprite = source.sprite;
            sprite.color = source.color;
            sprite.flipX = source.flipX;
            sprite.flipY = source.flipY;
            sprite.sortingLayerID = source.sortingLayerID;
            sprite.sortingOrder = source.sortingOrder;
            sprite.maskInteraction = source.maskInteraction;
            sprite.sharedMaterial = source.sharedMaterial;
            if (source == player.DeathHeadRenderer)
            {
                detachedHead = sprite;
            }

            Rigidbody2D body = part.AddComponent<Rigidbody2D>();
            body.gravityScale = 1.5f;
            body.linearVelocity = new Vector2(
                UnityEngine.Random.Range(-1.5f, 1.5f),
                UnityEngine.Random.Range(1.5f, 3f));
            body.angularVelocity = UnityEngine.Random.Range(-180f, 180f);

            BoxCollider2D collider = part.AddComponent<BoxCollider2D>();
            collider.size = source.sprite.bounds.size;
            collider.offset = source.sprite.bounds.center;
        }

        if (remainsRoot.transform.childCount == 0)
        {
            Debug.LogWarning("No se encontraron sprites visibles para el desarme del jugador.");
        }

        return remainsRoot;
    }

    // Sincroniza restos, música, TV off, creación del jugador y revelado de la nueva posición.
    private IEnumerator Respawn(Vector3 deathPosition, SpriteRenderer detachedHead, Sprite poweredOffHead)
    {
        deathMusic = MusicManager.Instance;

        if (deathMusic != null)
        {
            deathMusic.BeginDeathEffect();
        }

        float elapsed = 0f;
        while (elapsed < 1.3f)
        {
            yield return null;
            elapsed += Time.unscaledDeltaTime;
            if (deathMusic != null)
            {
                deathMusic.SetDeathProgress(elapsed / 1.3f);
            }
        }

        if (deathMusic != null)
        {
            deathMusic.SilenceUntilRespawn();
        }

        yield return new WaitForSecondsRealtime(0.2f);

        if (tvOffTransition != null && tvOffTransition.IsReady)
        {
            yield return tvOffTransition.PowerOff();
        }
        else
        {
            Debug.LogError("[ERROR] No hay 'TV Off Transition' asignado. La reaparición continúa sin la transicion.");
        }

        if (playerPrefab == null)
        {
            Debug.LogError("No se puede reaparecer: se perdió 'Player Prefab' durante la espera.");
            respawnRoutine = null;
            enabled = false;
            yield break;
        }

        if (defeatedBody != null)
        {
            Destroy(defeatedBody);
        }

        defeatedBody = null;

        Vector3 spawn = initialSpawn;
        PuntoReensamblaje nearest = PuntoReensamblaje.FindNearest(deathPosition);

        if (nearest != null)
        {
            spawn = nearest.spawnPoint.position;
        }

        PlayerController player = Instantiate(playerPrefab, spawn, playerPrefab.transform.rotation);
        BatterySystem bs = player.GetComponent<BatterySystem>();

        if (bs != null)
        {
            bs.ReduceDurability(50f);
            bs.ReduceBattery(25f);
        }

        PlayerModuleManager modules = player != null ? player.GetComponent<PlayerModuleManager>() : null;
        Rigidbody2D playerBody = player != null ? player.GetComponent<Rigidbody2D>() : null;

        if (player == null || !player.IsInitialized || modules == null || playerBody == null)
        {
            Debug.LogError("[ERROR] El prefab de reaparición no pudo inicializar un jugador válido.");
            respawnRoutine = null;
            enabled = false;
            yield break;
        }

        modules.LoseExternalModule();

        cameras.SetFinal(false);
        RegisterPlayer(player);
        ResetEnemySpawnZones(player);

        if (deathMusic != null)
        {
            deathMusic.ResumeVolumeAfterRespawn();
        }

        if (remains != null)
        {
            Destroy(remains);
        }

        remains = null;

        // Permitir actualizar el encuadre sin revelar la posición anterior.
        yield return null;
        yield return null;

        if (tvOffTransition != null && tvOffTransition.IsBlack)
        {
            yield return new WaitForSecondsRealtime(0.5f);

            bool canFade = transitions != null && transitions.SetCovered(this, respawnFadeOutColor);
            if (!canFade)
            {
                Debug.LogError("No se pudo preparar el Fade de la reaparición.");
            }

            tvOffTransition.ResetVisuals();

            if (canFade)
            {
                generalAudioSource.PlayOneShot(respawnClip);

                yield return transitions.Reveal(this, 0.3f, progress =>
                {
                    if (deathMusic != null)
                    {
                        deathMusic.SetRevealProgress(progress);
                    }
                });
            }
        }


        if (player == null || CurrentPlayer != player)
        {
            CancelRespawn();
            yield break;
        }

        operation = Operation.None;
        IsDead = false;

        RestoreDeathMusic();

        if (transitions != null)
        {
            transitions.Release(this);
        }

        respawnRoutine = null;
    }

    // Descubre únicamente zonas de Main y resuelve su población antes de revelar la escena.
    private void InitializeEnemySpawnZones(PlayerController player)
    {
        EnemySpawnZone[] foundZones = FindObjectsByType<EnemySpawnZone>(FindObjectsSortMode.None);
        var sceneZones = new List<EnemySpawnZone>(foundZones.Length);

        foreach (EnemySpawnZone zone in foundZones)
        {
            if (zone != null && zone.isActiveAndEnabled && zone.gameObject.scene == gameObject.scene)
            {
                sceneZones.Add(zone);
            }
        }

        enemySpawnZones = sceneZones.ToArray();
        EnemySpawnZone.BeginPlacementPass();
        Physics2D.SyncTransforms();

        Camera outputCamera = cameraBrain.OutputCamera;

        foreach (EnemySpawnZone zone in enemySpawnZones)
        {
            zone.InitializePopulation(player, outputCamera);
        }
    }

    // Reinicia todas las zonas durante la cobertura de muerte y vuelve a aplicar la población inicial.
    private void ResetEnemySpawnZones(PlayerController player)
    {
        foreach (EnemySpawnZone zone in enemySpawnZones)
        {
            if (zone != null)
            {
                zone.PrepareForReset();
            }
        }

        Physics2D.SyncTransforms();
        EnemySpawnZone.BeginPlacementPass();
        Camera outputCamera = cameraBrain.OutputCamera;

        foreach (EnemySpawnZone zone in enemySpawnZones)
        {
            if (zone != null && zone.isActiveAndEnabled)
            {
                zone.ResetPopulation(player, outputCamera);
            }
        }
    }

    private void RestoreDeathMusic()
    {
        if (deathMusic != null)
        {
            deathMusic.EndDeathEffect();
        }

        deathMusic = null;
    }

    private void CancelRespawn()
    {
        if (respawnRoutine != null)
        {
            StopCoroutine(respawnRoutine);
        }

        respawnRoutine = null;
        RestoreDeathMusic();

        if (defeatedBody != null)
        {
            Destroy(defeatedBody);
        }

        if (remains != null)
        {
            Destroy(remains);
        }

        defeatedBody = remains = null;

        if (operation == Operation.Respawn)
        {
            operation = Operation.None;
            IsDead = false;
        }

        if (tvOffTransition != null)
        {
            tvOffTransition.ResetVisuals();
        }

        if (transitions != null)
        {
            transitions.Release(this);
        }
    }

    public void GrantFinalKey()
    {
        HasFinalKey = true;
    }

    public void TeleportCurrentPlayer(Vector3 position)
    {
        if (!IsPlaying(CurrentPlayer))
        {
            return;
        }

        CurrentPlayer.ClearInteractions();
        CurrentPlayer.transform.position = position;
        CurrentPlayer.GetComponent<Rigidbody2D>().linearVelocity = Vector2.zero;
        cameras.Invalidate();
    }

    public bool BeginDoorTransition(Vector3 destination, bool activateFinalCamera)
    {
        if (!IsPlaying(CurrentPlayer) || operation != Operation.None)
        {
            return false;
        }

        if (transitions == null || !transitions.TryAcquire(this))
        {
            Debug.LogError("[ERROR] La puerta necesita el TransitionManager.", this);
            return false;
        }

        operation = Operation.Door;
        doorTransitionRoutine = StartCoroutine(TransitionThroughDoor(CurrentPlayer, destination, activateFinalCamera));
        return true;
    }

    // Cubre la pantalla antes de teletransportar para que posición y cámara cambien fuera de vista.
    private IEnumerator TransitionThroughDoor(PlayerController player, Vector3 destination, bool activateFinalCamera)
    {
        player.SetControlLock(PlayerControlLock.Transition, true);

        yield return transitions.Cover(this, Color.black, transitions.DoorCoverDuration);

        if (!IsPlaying(player))
        {
            CancelDoorTransition();
            yield break;
        }

        TeleportCurrentPlayer(destination);
        cameras.SetFinal(activateFinalCamera);

        yield return new WaitForEndOfFrame();

        if (transitions.DoorHoldDuration > 0f)
        {
            yield return new WaitForSecondsRealtime(transitions.DoorHoldDuration);
        }

        yield return transitions.Reveal(this, transitions.DoorRevealDuration);
        transitions.Release(this);

        if (player != null)
        {
            player.SetControlLock(PlayerControlLock.Transition, false);
        }

        doorTransitionRoutine = null;

        if (operation == Operation.Door)
        {
            operation = Operation.None;
        }
    }

    private void CancelDoorTransition()
    {
        if (operation != Operation.Door)
        {
            return;
        }

        if (doorTransitionRoutine != null)
        {
            StopCoroutine(doorTransitionRoutine);
        }

        doorTransitionRoutine = null;

        if (CurrentPlayer != null)
        {
            CurrentPlayer.SetControlLock(PlayerControlLock.Transition, false);
        }

        if (transitions != null)
        {
            transitions.Release(this);
        }

        operation = Operation.None;
    }

    // Reserva la transición global y bloquea al jugador antes de que VictorySequence inicie el ascenso.
    public bool TryBeginVictory(PlayerController player)
    {
        if (!IsPlaying(player) || operation != Operation.None)
        {
            return false;
        }

        if (transitions == null || !transitions.TryAcquire(this))
        {
            Debug.LogError("[ERROR] La victoria necesita un Fade configurado.");
            return false;
        }

        operation = Operation.Victory;
        HasWon = true;

        player.SetControlLock(PlayerControlLock.Victory, true);

        return true;
    }

    // Punto de entrada conservado para conexiones existentes.
    public void Win()
    {
        TryBeginVictory(CurrentPlayer);
    }

    public void CancelVictory(PlayerController player)
    {
        if (operation != Operation.Victory || !ReferenceEquals(CurrentPlayer, player))
        {
            return;
        }

        if (victoryRoutine != null)
        {
            StopCoroutine(victoryRoutine);
        }

        victoryRoutine = null;
        HasWon = false;
        operation = Operation.None;

        if (transitions != null)
        {
            transitions.Release(this);
        }

        if (player != null)
        {
            player.SetControlLock(PlayerControlLock.Victory, false);
        }
    }

    public void VictoryFadeIn()
    {
        if (operation != Operation.Victory || !HasWon || CurrentPlayer == null || victoryRoutine != null)
        {
            return;
        }

        victoryRoutine = StartCoroutine(VictoryTransition());
    }

    private IEnumerator VictoryTransition()
    {
        yield return transitions.Cover(this, Color.black, transitions.VictoryCoverDuration);
        
#if UNITY_EDITOR
        EditorApplication.isPlaying = false;
#endif
        Application.Quit();
    }

    public void SetFinalCameraActive(bool active)
    {
        cameras?.SetFinal(active);
    }

    public void SetArenaCamera(CinemachineCamera arena, CinemachineCamera exploration, int offset, bool active)
    {
        cameras?.SetArena(arena, exploration, offset, active);
    }

    private void OnDisable()
    {
        if (Instance != this)
        {
            return;
        }

        if (sceneReady != null && !sceneReady.IsRevealed)
        {
            Debug.LogError("[ERROR] GameManager se desactivó durante la preparación del nivel.");
        }

        StopAllCoroutines();

        if (levelIntro != null)
        {
            levelIntro.Cancel();
        }

        HasStarted = false;

        CancelDoorTransition();
        CancelVictory(CurrentPlayer);
        CancelRespawn();

        if (ActiveArena != null)
        {
            ActiveArena.ResetAfterPlayerDeath(null);
        }

        cameras?.Dispose();
    }

    private void OnDestroy()
    {
        if (Instance != this)
        {
            return;
        }

        CurrentPlayer = null;
        PlayerChanged?.Invoke(null);
        PlayerChanged = null;
        Instance = null;
    }
}
