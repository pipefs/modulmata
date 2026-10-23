using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }
    public PlayerController CurrentPlayer { get; private set; }
    public event Action<PlayerController> PlayerChanged;

    [SerializeField] private PlayerController playerPrefab;
    [SerializeField] private BatteryUI batteryUI;
    [SerializeField] private CinemachineCamera playerCamera;

    [Header("A3")]
    [SerializeField] private CinemachineCamera finalCamera;
    [SerializeField] private CinemachineBrain cameraBrain;
    [SerializeField, Min(1)] private int finalCameraPriorityOffset = 20;

    public bool IsDead { get; private set; }
    public bool HasWon { get; private set; }
    public bool HasA3Key { get; private set; }
    public BossArena ActiveArena { get; set; }

    private readonly List<ModuleData> internalModules = new List<ModuleData>();
    private ScreenFadeTransition screenFade;
    private Vector3 initialSpawn;
    private PrioritySettings finalCameraDefaultPriority;
    private CinemachineBlendDefinition normalCameraBlend;
    private Coroutine restoreBlendRoutine;
    private Coroutine doorTransitionRoutine;

    private void Awake()
    {
        // Singleton
        if (Instance != null && Instance != this)
        {
            enabled = false;
            return;
        }

        Instance = this;

        if (finalCamera != null)
            finalCameraDefaultPriority = finalCamera.Priority;

        if (cameraBrain != null)
            normalCameraBlend = cameraBrain.DefaultBlend;

        if (screenFade == null)
            screenFade = GetComponentInChildren<ScreenFadeTransition>();
    }

    private void Start()
    {
        PlayerController player = FindAnyObjectByType<PlayerController>();

        if (player == null && playerPrefab != null)
            player = Instantiate(playerPrefab);

        if (player == null)
            return;

        initialSpawn = player.transform.position;
        RegisterPlayer(player);
    }

    private void RegisterPlayer(PlayerController player)
    {
        CurrentPlayer = player;

        PlayerModuleManager modules = player.GetComponent<PlayerModuleManager>();

        foreach (ModuleData module in internalModules)
            modules.UnlockInternalModule(new ModuleData(module));

        if (batteryUI != null)
            batteryUI.Vincular(player);

        if (playerCamera != null)
        {
            playerCamera.Follow = player.cameraTarget != null ? player.cameraTarget : player.transform;
            playerCamera.PreviousStateIsValid = false;
        }

        IsDead = false;
        PlayerChanged?.Invoke(player);
    }

    public void HandlePlayerDeath(PlayerController player)
    {
        if (IsDead || HasWon || player == null || player != CurrentPlayer)
            return;

        if (playerPrefab == null)
            return;

        IsDead = true;
        CancelDoorTransition();
        SetFinalCameraActive(false);
        Vector3 deathPosition = player.transform.position;

        internalModules.Clear();

        foreach (ModuleData module in player.GetComponent<PlayerModuleManager>().internalModules)
        {
            if (module != null)
                internalModules.Add(new ModuleData(module));
        }

        if (batteryUI != null)
            batteryUI.Desvincular();

        CurrentPlayer = null;
        PlayerChanged?.Invoke(null);

        if (playerCamera != null)
            playerCamera.Follow = null;

        if (ActiveArena != null)
            ActiveArena.ResetAfterPlayerDeath(player);

        GameObject physicalRemains = CreatePhysicalRemains(player);
        player.gameObject.SetActive(false);

        StartCoroutine(Respawn(deathPosition, player.gameObject, physicalRemains));
    }

    private GameObject CreatePhysicalRemains(PlayerController player)
    {
        var remainsRoot = new GameObject("Restos del jugador");
        var renderers = player.GetComponentsInChildren<SpriteRenderer>();
        int remainsLayer = LayerMask.NameToLayer("Cuerpo");

        if (remainsLayer < 0)
            remainsLayer = player.gameObject.layer;

        foreach (var source in renderers)
        {
            if (!source.enabled || source.sprite == null)
                continue;

            var part = new GameObject($"Resto - {source.gameObject.name}");
            part.layer = remainsLayer;

            var partTransform = part.transform;
            partTransform.SetParent(remainsRoot.transform);
            partTransform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
            partTransform.localScale = source.transform.lossyScale;

            var sprite = part.AddComponent<SpriteRenderer>();
            sprite.sprite = source.sprite;
            sprite.color = source.color;
            sprite.flipX = source.flipX;
            sprite.flipY = source.flipY;
            sprite.sortingLayerID = source.sortingLayerID;
            sprite.sortingOrder = source.sortingOrder;
            sprite.maskInteraction = source.maskInteraction;
            sprite.sharedMaterial = source.sharedMaterial;

            var body = part.AddComponent<Rigidbody2D>();
            body.gravityScale = 1.5f;
            body.linearVelocity = new Vector2(
                UnityEngine.Random.Range(-1.5f, 1.5f),
                UnityEngine.Random.Range(1.5f, 3f));
            body.angularVelocity = UnityEngine.Random.Range(-180f, 180f);

            var collider = part.AddComponent<BoxCollider2D>();
            collider.size = source.sprite.bounds.size;
            collider.offset = source.sprite.bounds.center;
        }

        return remainsRoot;
    }

    private IEnumerator Respawn(Vector3 deathPosition, GameObject defeatedPlayer, GameObject physicalRemains)
    {
        yield return new WaitForSeconds(2);

        if (defeatedPlayer != null)
            Destroy(defeatedPlayer);

        Vector3 spawn = initialSpawn;
        PuntoReensamblaje nearest = PuntoReensamblaje.FindNearest(deathPosition);

        if (nearest != null)
            spawn = nearest.spawnPoint.position;

        PlayerController player = Instantiate(playerPrefab, spawn, playerPrefab.transform.rotation);
        player.GetComponent<PlayerModuleManager>().LoseExternalModule();

        RegisterPlayer(player);
        
        if (physicalRemains != null)
            Destroy(physicalRemains);
    }

    public void Win()
    {
        if (HasWon || IsDead || CurrentPlayer == null)
            return;

        HasWon = true;

        CurrentPlayer.StopControls();
        CurrentPlayer.GetComponent<BatterySystem>().enabled = false;
        CurrentPlayer.GetComponent<PlayerModuleManager>().enabled = false;
    }

    public void GrantA3Key()
    {
        HasA3Key = true;
    }

    public void TeleportCurrentPlayer(Vector3 position)
    {
        if (IsDead || HasWon || CurrentPlayer == null)
            return;

        CurrentPlayer.transform.position = position;

        Rigidbody2D body = CurrentPlayer.GetComponent<Rigidbody2D>();
        if (body != null)
            body.linearVelocity = Vector2.zero;

        if (playerCamera != null)
            playerCamera.PreviousStateIsValid = false;
    }

    public void VictoryFadeIn()
    {
        if (IsDead || !HasWon || CurrentPlayer == null)
            return;

        StartCoroutine(VictoryTransition());
    }

    private IEnumerator VictoryTransition()
    {
        yield return screenFade.FadeTo(1f, 1f);

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    public bool BeginDoorTransition(Vector3 destination, bool activateFinalCamera)
    {
        if (IsDead || HasWon || CurrentPlayer == null || doorTransitionRoutine != null)
            return false;

        doorTransitionRoutine = StartCoroutine(TransitionThroughDoor(destination, activateFinalCamera));
        return true;
    }

    private IEnumerator TransitionThroughDoor(Vector3 destination, bool activateFinalCamera)
    {
        CurrentPlayer.SetControlLock(true);

        yield return screenFade.FadeTo(1f, screenFade.FadeOutDuration);

        TeleportCurrentPlayer(destination);
        SetFinalCameraActive(activateFinalCamera);

        yield return new WaitForEndOfFrame();

        if (screenFade.BlackHoldDuration > 0f)
            yield return new WaitForSecondsRealtime(screenFade.BlackHoldDuration);

        yield return screenFade.FadeTo(0f, screenFade.FadeInDuration);

        if (!IsDead && !HasWon && CurrentPlayer != null)
            CurrentPlayer.SetControlLock(false);

        doorTransitionRoutine = null;
    }

    private void CancelDoorTransition()
    {
        if (doorTransitionRoutine != null)
            StopCoroutine(doorTransitionRoutine);

        doorTransitionRoutine = null;

        if (screenFade != null)
            screenFade.SetAlpha(0f);
    }

    public void SetFinalCameraActive(bool active)
    {
        if (finalCamera == null)
            return;

        if (active)
        {
            int playerPriority = playerCamera != null ? playerCamera.Priority.Value : 0;
            finalCamera.Priority = Mathf.Max(playerPriority, finalCameraDefaultPriority.Value) + finalCameraPriorityOffset;
        }
        else
        {
            finalCamera.Priority = finalCameraDefaultPriority;
        }

        finalCamera.PreviousStateIsValid = false;

        if (playerCamera != null)
            playerCamera.PreviousStateIsValid = false;

        ForceNextCameraChangeToCut();
    }

    private void ForceNextCameraChangeToCut()
    {
        if (cameraBrain == null)
            return;

        if (restoreBlendRoutine != null)
            StopCoroutine(restoreBlendRoutine);

        cameraBrain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.Cut, 0f);
        restoreBlendRoutine = StartCoroutine(RestoreNormalCameraBlend());
    }

    private IEnumerator RestoreNormalCameraBlend()
    {
        yield return null;
        yield return new WaitForEndOfFrame();

        if (cameraBrain != null)
            cameraBrain.DefaultBlend = normalCameraBlend;

        restoreBlendRoutine = null;
    }

    private void OnDestroy()
    {
        if (batteryUI != null)
            batteryUI.Desvincular();

        if (Instance == this)
            Instance = null;
    }
}
