using System;
using System.Collections.Generic;
using UnityEngine;

// Formas disponibles para colocar la población de una zona.
public enum EnemyPlacementMode
{
    RandomArea,
    FixedPoints
}

// Genera encuentros de golems, limita los élites y repuebla una zona despejada cuando queda fuera de cámara.
[DisallowMultipleComponent]
public sealed class EnemySpawnZone : MonoBehaviour
{
    private enum EnemyKind
    {
        Normal,
        Elite
    }

    // El estado impide repetir sorteos mientras la zona permanece fuera de cámara.
    private enum PopulationState
    {
        Active,
        Cleared,
        WaitingForExit,
        AttemptConsumed
    }

    [Serializable]
    private sealed class EnemyOption
    {
        public GolemController prefab;
        public EnemyKind kind;
        [Min(0f)] public float weight = 1f;
    }

    private sealed class SpawnSlot
    {
        public GolemController Prefab;
        public Vector3 Position;
        public Quaternion Rotation;
        public bool IsAlive;
        public GolemController Instance;
        public Action<Vector3> DeathHandler;
        public Action<GameObject> RewardHandler;
    }

    private readonly struct BodyShape
    {
        public readonly Vector2 Size;
        public readonly Vector2 Offset;

        public BodyShape(Vector2 size, Vector2 offset)
        {
            Size = size;
            Offset = offset;
        }
    }

    [Header("Área de aparición")]
    [SerializeField] private Vector2 areaCenter;
    [SerializeField] private Vector2 areaSize = new Vector2(8f, 4f);
    [SerializeField] private EnemyPlacementMode placementMode;
    [SerializeField] private Transform[] fixedPoints;

    [Header("Población")]
    [Tooltip("Solo se utiliza en Random Area. Fixed Points ocupa todos sus puntos asignados.")]
    [SerializeField, Min(1)] private int maximumSlots = 3;
    [SerializeField] private EnemyOption[] enemies;
    [SerializeField, Range(0f, 100f)] private float initialElitePercentage = 20f;

    [Header("Repoblación")]
    [SerializeField, Range(0f, 100f)] private float respawnElitePercentage = 5f;
    [SerializeField, Range(0f, 100f)] private float baseRepopulationPercentage = 50f;
    [Tooltip("Porcentaje de la probabilidad anterior que se conserva después de cada repoblación exitosa.")]
    [SerializeField, Range(0f, 100f)] private float repopulationDecayPercentage = 50f;
    [SerializeField, Min(0f)] private float offscreenMargin = 2f;

    [Header("Colocación física")]
    [SerializeField] private LayerMask groundLayers = 1 << 3;
    [SerializeField] private LayerMask obstacleLayers = (1 << 3) | (1 << 6) | (1 << 7);
    [SerializeField, Min(1)] private int randomAttemptsPerSlot = 30;
    [SerializeField, Min(0f)] private float enemySeparation = 0.15f;
    [SerializeField, Min(0f)] private float playerSeparation = 2f;
    [SerializeField, Min(0f)] private float groundClearance = 0.02f;
    [SerializeField, Range(0f, 89f)] private float maximumGroundSlope = 15f;
    [SerializeField, Min(0f)] private float maximumSupportHeightDifference = 0.1f;

    private static readonly List<Bounds> SharedReservations = new();

    private readonly List<SpawnSlot> slots = new();
    private readonly List<GameObject> spawnedRewards = new();
    private readonly List<Collider2D> deathZoneColliders = new();
    private readonly List<Collider2D> overlapResults = new(16);
    private readonly RaycastHit2D[] groundHits = new RaycastHit2D[16];

    private ContactFilter2D groundFilter;
    private ContactFilter2D obstacleFilter;
    private PopulationState populationState;
    private PlayerController currentPlayer;
    private Camera outputCamera;
    private float totalNormalWeight;
    private float totalEliteWeight;
    private int successfulRepopulations;
    private bool configurationValid;
    private bool initialized;

    private Bounds AreaBounds => new Bounds(
        transform.position + (Vector3)areaCenter,
        new Vector3(areaSize.x, areaSize.y, 0.1f));

    private void Awake()
    {
        configurationValid = ValidateConfiguration();

        if (!configurationValid)
        {
            enabled = false;
            return;
        }

        groundFilter = new ContactFilter2D();
        groundFilter.SetLayerMask(groundLayers);
        groundFilter.useTriggers = false;

        obstacleFilter = new ContactFilter2D();
        obstacleFilter.SetLayerMask(obstacleLayers);
        obstacleFilter.useTriggers = false;
    }

    // Solo evalúa visibilidad después de despejar la población; las zonas activas no pagan este coste.
    private void Update()
    {
        if (!initialized || populationState == PopulationState.Active ||
            currentPlayer == null || !currentPlayer.isActiveAndEnabled)
        {
            return;
        }

        bool isVisible = IsAreaVisible();

        switch (populationState)
        {
            case PopulationState.Cleared:
                if (isVisible)
                {
                    populationState = PopulationState.WaitingForExit;
                }

                break;

            case PopulationState.WaitingForExit:
                if (!isVisible)
                {
                    TryRepopulate();
                }

                break;

            case PopulationState.AttemptConsumed:
                if (isVisible)
                {
                    populationState = PopulationState.WaitingForExit;
                }

                break;
        }
    }

    private bool ValidateConfiguration()
    {
        if (Quaternion.Angle(transform.rotation, Quaternion.identity) > 0.01f ||
            !ApproximatelyOne(transform.lossyScale.x) || !ApproximatelyOne(transform.lossyScale.y) ||
            !ApproximatelyOne(transform.lossyScale.z))
        {
            return Fail("EnemySpawnZone necesita rotación cero y escala uno en su raíz.");
        }

        if (!PositiveFinite(areaSize.x) || !PositiveFinite(areaSize.y))
        {
            return Fail("El área de EnemySpawnZone debe tener ancho y alto mayores que cero.");
        }

        if ((placementMode == EnemyPlacementMode.RandomArea && maximumSlots <= 0) ||
            randomAttemptsPerSlot <= 0 || groundLayers.value == 0 || obstacleLayers.value == 0)
        {
            return Fail("EnemySpawnZone necesita plazas, intentos y capas físicas válidas.");
        }

        if (!ValidPercentage(initialElitePercentage) || !ValidPercentage(respawnElitePercentage) ||
            !ValidPercentage(baseRepopulationPercentage) || !ValidPercentage(repopulationDecayPercentage) ||
            !NonNegativeFinite(offscreenMargin) || !NonNegativeFinite(enemySeparation) ||
            !NonNegativeFinite(playerSeparation) || !NonNegativeFinite(groundClearance) ||
            !NonNegativeFinite(maximumSupportHeightDifference))
        {
            return Fail("EnemySpawnZone contiene probabilidades o distancias no válidas.");
        }

        if (enemies == null || enemies.Length == 0)
        {
            return Fail("EnemySpawnZone necesita al menos un prefab de enemigo.");
        }

        totalNormalWeight = 0f;
        totalEliteWeight = 0f;

        for (int i = 0; i < enemies.Length; i++)
        {
            EnemyOption option = enemies[i];

            if (option == null || option.prefab == null)
            {
                return Fail($"La opción de enemigo {i} está incompleta.");
            }

            if (!ValidateEnemyPrefab(option.prefab))
            {
                return false;
            }

            if (!NonNegativeFinite(option.weight))
            {
                return Fail($"El peso de '{option.prefab.name}' no es válido.");
            }

            if (option.kind == EnemyKind.Elite)
            {
                totalEliteWeight += option.weight;
            }
            else
            {
                totalNormalWeight += option.weight;
            }
        }

        if (!(totalNormalWeight > 0f) || float.IsInfinity(totalNormalWeight))
        {
            return Fail("EnemySpawnZone necesita al menos un enemigo Normal con peso mayor que cero.");
        }

        if (placementMode == EnemyPlacementMode.FixedPoints && !ValidateFixedPoints())
        {
            return false;
        }

        return true;
    }

    private bool ValidateEnemyPrefab(GolemController prefab)
    {
        BoxCollider2D bodyCollider = prefab.GetComponent<BoxCollider2D>();
        Collider2D[] colliders = prefab.GetComponentsInChildren<Collider2D>(true);
        Vector3 scale = prefab.transform.localScale;

        if (prefab is GolemBossController)
        {
            return Fail("Las zonas de aparición comunes no aceptan el prefab del jefe.");
        }

        if (!prefab.enabled || bodyCollider == null || !bodyCollider.enabled || bodyCollider.isTrigger ||
            prefab.GetComponent<Rigidbody2D>() == null || colliders.Length != 1)
        {
            return Fail($"'{prefab.name}' necesita GolemController activo, Rigidbody2D y un único BoxCollider2D sólido en la raíz.");
        }

        if (Quaternion.Angle(prefab.transform.rotation, Quaternion.identity) > 0.01f ||
            !(Mathf.Abs(scale.x) > 0f) || !(Mathf.Abs(scale.y) > 0f) ||
            !Mathf.Approximately(Mathf.Abs(scale.x), Mathf.Abs(scale.y)))
        {
            return Fail($"'{prefab.name}' necesita rotación cero y la misma magnitud de escala en X e Y.");
        }

        return true;
    }

    private bool ValidateFixedPoints()
    {
        if (fixedPoints == null || fixedPoints.Length == 0)
        {
            return Fail("El modo Fixed Points necesita al menos un punto asignado.");
        }

        Bounds area = AreaBounds;
        var uniquePoints = new HashSet<Transform>();

        for (int i = 0; i < fixedPoints.Length; i++)
        {
            Transform point = fixedPoints[i];

            if (point == null || point == transform || !point.IsChildOf(transform) ||
                !ContainsPoint2D(area, point.position))
            {
                return Fail($"El punto fijo {i} debe ser un hijo asignado y estar dentro del área.");
            }

            if (!uniquePoints.Add(point))
            {
                return Fail($"El punto fijo '{point.name}' está asignado más de una vez.");
            }
        }

        return true;
    }

    // GameManager limpia las reservas antes de una generación coordinada de todas las zonas.
    internal static void BeginPlacementPass()
    {
        SharedReservations.Clear();
    }

    internal bool InitializePopulation(PlayerController player, Camera camera)
    {
        if (!configurationValid || !isActiveAndEnabled || initialized)
        {
            return false;
        }

        if (!SetRuntimeDependencies(player, camera))
        {
            return false;
        }

        initialized = true;
        successfulRepopulations = 0;
        bool populated = SpawnPopulation(initialElitePercentage);
        UpdatePopulationState();
        return populated;
    }

    // Primera fase del reinicio: elimina todas las instancias para que ninguna bloquee las demás zonas.
    internal void PrepareForReset()
    {
        if (!initialized)
        {
            return;
        }

        CleanupAllRewards();
        RemoveAllInstances();
        slots.Clear();
        successfulRepopulations = 0;
        populationState = PopulationState.Cleared;
    }

    // Segunda fase del reinicio: vuelve a sortear una población inicial completa.
    internal bool ResetPopulation(PlayerController player, Camera camera)
    {
        if (!initialized || !configurationValid || !isActiveAndEnabled ||
            !SetRuntimeDependencies(player, camera))
        {
            return false;
        }

        bool populated = SpawnPopulation(initialElitePercentage);
        UpdatePopulationState();
        return populated;
    }

    private bool SetRuntimeDependencies(PlayerController player, Camera camera)
    {
        if (player == null || camera == null)
        {
            Debug.LogError("EnemySpawnZone necesita el jugador activo y la cámara de salida de Cinemachine.", this);
            enabled = false;
            return false;
        }

        currentPlayer = player;
        outputCamera = camera;
        return true;
    }

    // Construye una población nueva. Fixed Points usa todos sus puntos y Random Area sortea entre uno y el máximo.
    private bool SpawnPopulation(float elitePercentage)
    {
        CacheDeathZones();
        Physics2D.SyncTransforms();

        int targetCount = placementMode == EnemyPlacementMode.FixedPoints
            ? fixedPoints.Length
            : UnityEngine.Random.Range(1, maximumSlots + 1);
        bool elitePending = totalEliteWeight > 0f && RollPercentage(elitePercentage);
        List<int> slotOrder = CreateSlotOrder(targetCount, elitePending);
        int spawnedCount = 0;

        foreach (int slotIndex in slotOrder)
        {
            bool tryElite = elitePending;
            GolemController prefab = ChooseEnemy(tryElite ? EnemyKind.Elite : EnemyKind.Normal);
            bool spawned = TryCreateEnemy(prefab, slotIndex);

            if (spawned && tryElite)
            {
                elitePending = false;
            }
            else if (!spawned && tryElite)
            {
                // Un élite más grande no debe dejar vacía una plaza que sí admite un golem normal.
                spawned = TryCreateEnemy(ChooseEnemy(EnemyKind.Normal), slotIndex);
            }

            if (spawned)
            {
                spawnedCount++;
            }
        }

        ReportPopulationResult(targetCount, spawnedCount);
        return spawnedCount > 0;
    }

    // Empieza por la plaza elegida para el élite; si no cabe, las plazas siguientes todavía pueden recibirlo.
    private static List<int> CreateSlotOrder(int count, bool prioritizeElite)
    {
        var order = new List<int>(count);
        int first = prioritizeElite ? UnityEngine.Random.Range(0, count) : 0;

        for (int offset = 0; offset < count; offset++)
        {
            order.Add((first + offset) % count);
        }

        return order;
    }

    private GolemController ChooseEnemy(EnemyKind kind)
    {
        float totalWeight = kind == EnemyKind.Elite ? totalEliteWeight : totalNormalWeight;
        float choice = UnityEngine.Random.value * totalWeight;
        GolemController fallback = null;

        foreach (EnemyOption option in enemies)
        {
            if (option == null || option.prefab == null || option.kind != kind || option.weight <= 0f)
            {
                continue;
            }

            fallback = option.prefab;
            choice -= option.weight;

            if (choice <= 0f)
            {
                return option.prefab;
            }
        }

        return fallback;
    }

    private bool TryCreateEnemy(GolemController prefab, int slotIndex)
    {
        if (prefab == null)
        {
            return false;
        }

        Vector3 position;
        Bounds bodyBounds;
        bool foundPlacement;

        if (placementMode == EnemyPlacementMode.FixedPoints)
        {
            // Un punto fijo representa la posición raíz exacta elegida manualmente en la escena.
            position = fixedPoints[slotIndex].position;
            bodyBounds = GetBodyBounds(position, prefab);
            foundPlacement = true;
        }
        else
        {
            foundPlacement = TryRandomPlacement(prefab, currentPlayer, out position, out bodyBounds);
        }

        if (!foundPlacement)
        {
            return false;
        }

        var slot = new SpawnSlot
        {
            Prefab = prefab,
            Position = position,
            Rotation = prefab.transform.rotation,
            IsAlive = true
        };

        if (!SpawnIntoSlot(slot, bodyBounds))
        {
            return false;
        }

        slots.Add(slot);
        return true;
    }

    private void ReportPopulationResult(int targetCount, int spawnedCount)
    {
        if (spawnedCount == 0)
        {
            string reason = placementMode == EnemyPlacementMode.FixedPoints
                ? "no pudo instanciar enemigos en sus puntos fijos"
                : "no encontró una posición física válida dentro del área aleatoria";
            Debug.LogError($"EnemySpawnZone '{name}' {reason}.", this);
            return;
        }

        if (spawnedCount < targetCount)
        {
            string reason = placementMode == EnemyPlacementMode.FixedPoints
                ? "algunos prefabs no pudieron instanciarse"
                : "las plazas restantes no tenían una posición física válida";
            Debug.LogWarning(
                $"EnemySpawnZone '{name}' generó {spawnedCount} de {targetCount} enemigo(s); {reason}.",
                this);
        }
    }

    private bool TryRandomPlacement(GolemController prefab, PlayerController player,
        out Vector3 position, out Bounds bodyBounds)
    {
        BodyShape shape = GetBodyShape(prefab);
        Bounds area = AreaBounds;
        float minimumX = area.min.x + shape.Size.x * 0.5f;
        float maximumX = area.max.x - shape.Size.x * 0.5f;

        if (minimumX > maximumX)
        {
            return FailPlacement(out position, out bodyBounds);
        }

        for (int attempt = 0; attempt < randomAttemptsPerSlot; attempt++)
        {
            float bodyCenterX = UnityEngine.Random.Range(minimumX, maximumX);
            Vector2 rayOrigin = new Vector2(bodyCenterX, area.max.y + 0.05f);

            if (TryBuildPlacement(prefab, player, bodyCenterX, rayOrigin,
                area.size.y + 0.1f, transform.position.z, out position, out bodyBounds))
            {
                return true;
            }
        }

        return FailPlacement(out position, out bodyBounds);
    }

    // Busca tres apoyos compatibles y eleva el cuerpo lo suficiente para no solaparlo con el suelo.
    private bool TryBuildPlacement(GolemController prefab, PlayerController player, float bodyCenterX,
        Vector2 rayOrigin, float rayDistance, float rootZ, out Vector3 position, out Bounds bodyBounds)
    {
        BodyShape shape = GetBodyShape(prefab);

        if (!TryFindGround(rayOrigin, rayDistance, out RaycastHit2D centerGround))
        {
            return FailPlacement(out position, out bodyBounds);
        }

        float sideOffset = shape.Size.x * 0.45f;
        Vector2 sideOrigin = new Vector2(bodyCenterX, centerGround.point.y + 0.25f);

        if (!TryFindGround(sideOrigin + Vector2.left * sideOffset, 0.5f, out RaycastHit2D leftGround) ||
            !TryFindGround(sideOrigin + Vector2.right * sideOffset, 0.5f, out RaycastHit2D rightGround))
        {
            return FailPlacement(out position, out bodyBounds);
        }

        float lowestSupport = Mathf.Min(centerGround.point.y, leftGround.point.y, rightGround.point.y);
        float highestSupport = Mathf.Max(centerGround.point.y, leftGround.point.y, rightGround.point.y);

        if (highestSupport - lowestSupport > maximumSupportHeightDifference)
        {
            return FailPlacement(out position, out bodyBounds);
        }

        Vector2 bodyCenter = new Vector2(
            bodyCenterX,
            highestSupport + groundClearance + shape.Size.y * 0.5f);

        position = new Vector3(
            bodyCenter.x - shape.Offset.x,
            bodyCenter.y - shape.Offset.y,
            rootZ);
        bodyBounds = CreateBodyBounds(bodyCenter, shape.Size);

        return CanOccupy(bodyBounds, player);
    }

    private bool TryFindGround(Vector2 origin, float distance, out RaycastHit2D bestHit)
    {
        int count = Physics2D.Raycast(origin, Vector2.down, groundFilter, groundHits, distance);
        float closestDistance = float.PositiveInfinity;
        bestHit = default;

        for (int i = 0; i < count; i++)
        {
            RaycastHit2D hit = groundHits[i];

            if (hit.collider == null || hit.distance >= closestDistance ||
                Vector2.Angle(hit.normal, Vector2.up) > maximumGroundSlope ||
                hit.collider.GetComponentInParent<OneWayPlatform>() != null ||
                hit.collider.GetComponentInParent<PlatformEffector2D>() != null)
            {
                continue;
            }

            closestDistance = hit.distance;
            bestHit = hit;
        }

        return bestHit.collider != null;
    }

    private bool CanOccupy(Bounds bodyBounds, PlayerController player)
    {
        if (!ContainsBounds2D(AreaBounds, bodyBounds) || IntersectsReservation(bodyBounds) ||
            IntersectsDeathZone(bodyBounds) || TooCloseToPlayer(bodyBounds, player))
        {
            return false;
        }

        overlapResults.Clear();
        Vector2 expandedSize = new Vector2(
            bodyBounds.size.x + enemySeparation * 2f,
            bodyBounds.size.y);
        Physics2D.OverlapBox(bodyBounds.center, expandedSize, 0f, obstacleFilter, overlapResults);

        return overlapResults.Count == 0;
    }

    private bool IntersectsReservation(Bounds bodyBounds)
    {
        Bounds separated = bodyBounds;
        separated.Expand(new Vector3(enemySeparation * 2f, enemySeparation * 2f, 0f));

        foreach (Bounds reservation in SharedReservations)
        {
            if (Intersects2D(separated, reservation))
            {
                return true;
            }
        }

        return false;
    }

    private bool IntersectsDeathZone(Bounds bodyBounds)
    {
        foreach (Collider2D deathCollider in deathZoneColliders)
        {
            if (deathCollider != null && deathCollider.enabled && Intersects2D(bodyBounds, deathCollider.bounds))
            {
                return true;
            }
        }

        return false;
    }

    private bool TooCloseToPlayer(Bounds bodyBounds, PlayerController player)
    {
        if (player == null || !player.isActiveAndEnabled)
        {
            return false;
        }

        Bounds clearance = bodyBounds;
        clearance.Expand(new Vector3(playerSeparation * 2f, playerSeparation * 2f, 0f));
        return Intersects2D(clearance, player.DamageBounds);
    }

    private bool SpawnIntoSlot(SpawnSlot slot, Bounds bodyBounds)
    {
        GolemController instance = Instantiate(slot.Prefab, slot.Position, slot.Rotation);

        if (instance == null || !instance.isActiveAndEnabled)
        {
            Debug.LogError($"EnemySpawnZone no pudo inicializar '{slot.Prefab.name}'.", this);

            if (instance != null)
            {
                instance.gameObject.SetActive(false);
                Destroy(instance.gameObject);
            }

            return false;
        }

        slot.Instance = instance;
        slot.DeathHandler = _ => MarkSlotDead(slot, instance);
        slot.RewardHandler = RegisterReward;
        instance.Died += slot.DeathHandler;
        instance.RewardSpawned += slot.RewardHandler;

        Physics2D.SyncTransforms();
        SharedReservations.Add(bodyBounds);
        return true;
    }

    private void MarkSlotDead(SpawnSlot slot, GolemController source)
    {
        if (slot.Instance != source || !slot.IsAlive)
        {
            return;
        }

        slot.IsAlive = false;

        foreach (SpawnSlot current in slots)
        {
            if (current.IsAlive)
            {
                return;
            }
        }

        populationState = IsAreaVisible()
            ? PopulationState.WaitingForExit
            : PopulationState.Cleared;
    }

    private void RegisterReward(GameObject reward)
    {
        if (reward != null && !spawnedRewards.Contains(reward))
        {
            spawnedRewards.Add(reward);
        }
    }

    // Realiza un solo sorteo por salida de cámara y conserva el valor si el intento falla.
    private void TryRepopulate()
    {
        populationState = PopulationState.AttemptConsumed;
        PruneMissingRewards();

        if (HasPendingModule())
        {
            return;
        }

        float chancePercentage = baseRepopulationPercentage *
            Mathf.Pow(repopulationDecayPercentage / 100f, successfulRepopulations);

        if (!RollPercentage(chancePercentage))
        {
            return;
        }

        RemoveAllInstances();
        slots.Clear();
        BeginPlacementPass();

        if (!SpawnPopulation(respawnElitePercentage))
        {
            return;
        }

        CleanupConsumableRewards();
        successfulRepopulations++;
        populationState = PopulationState.Active;
    }

    private bool IsAreaVisible()
    {
        if (outputCamera == null || !outputCamera.isActiveAndEnabled ||
            outputCamera.pixelWidth <= 0 || outputCamera.pixelHeight <= 0)
        {
            return true;
        }

        Bounds area = AreaBounds;
        float planeDistance = Mathf.Abs(area.center.z - outputCamera.transform.position.z);
        Vector3 bottomLeft = outputCamera.ViewportToWorldPoint(new Vector3(0f, 0f, planeDistance));
        Vector3 topRight = outputCamera.ViewportToWorldPoint(new Vector3(1f, 1f, planeDistance));
        Vector2 visibleMin = Vector2.Min(bottomLeft, topRight);
        Vector2 visibleMax = Vector2.Max(bottomLeft, topRight);
        Bounds visibleBounds = new Bounds(
            (visibleMin + visibleMax) * 0.5f,
            new Vector3(visibleMax.x - visibleMin.x, visibleMax.y - visibleMin.y, 0.1f));
        visibleBounds.Expand(new Vector3(offscreenMargin * 2f, offscreenMargin * 2f, 0f));

        return Intersects2D(area, visibleBounds);
    }

    private bool HasPendingModule()
    {
        foreach (GameObject reward in spawnedRewards)
        {
            if (reward != null && reward.GetComponent<ModulePickup>() != null)
            {
                return true;
            }
        }

        return false;
    }

    private void CleanupConsumableRewards()
    {
        for (int i = spawnedRewards.Count - 1; i >= 0; i--)
        {
            GameObject reward = spawnedRewards[i];

            if (reward == null)
            {
                spawnedRewards.RemoveAt(i);
                continue;
            }

            if (reward.GetComponent<DurabilityPickup>() == null)
            {
                continue;
            }

            reward.SetActive(false);
            Destroy(reward);
            spawnedRewards.RemoveAt(i);
        }
    }

    private void CleanupAllRewards()
    {
        foreach (GameObject reward in spawnedRewards)
        {
            if (reward == null)
            {
                continue;
            }

            reward.SetActive(false);
            Destroy(reward);
        }

        spawnedRewards.Clear();
    }

    private void PruneMissingRewards()
    {
        for (int i = spawnedRewards.Count - 1; i >= 0; i--)
        {
            if (spawnedRewards[i] == null)
            {
                spawnedRewards.RemoveAt(i);
            }
        }
    }

    private void RemoveAllInstances()
    {
        foreach (SpawnSlot slot in slots)
        {
            RemoveInstance(slot);
        }
    }

    private void RemoveInstance(SpawnSlot slot)
    {
        GolemController instance = slot.Instance;

        if (instance == null)
        {
            slot.Instance = null;
            return;
        }

        if (slot.DeathHandler != null)
        {
            instance.Died -= slot.DeathHandler;
        }

        if (slot.RewardHandler != null)
        {
            instance.RewardSpawned -= slot.RewardHandler;
        }

        slot.DeathHandler = null;
        slot.RewardHandler = null;
        slot.Instance = null;
        instance.gameObject.SetActive(false);
        Destroy(instance.gameObject);
    }

    private void UpdatePopulationState()
    {
        foreach (SpawnSlot slot in slots)
        {
            if (slot.IsAlive)
            {
                populationState = PopulationState.Active;
                return;
            }
        }

        populationState = IsAreaVisible()
            ? PopulationState.WaitingForExit
            : PopulationState.Cleared;
    }

    private void CacheDeathZones()
    {
        deathZoneColliders.Clear();
        DeathZone[] deathZones = FindObjectsByType<DeathZone>(FindObjectsSortMode.None);

        foreach (DeathZone deathZone in deathZones)
        {
            if (deathZone == null || deathZone.gameObject.scene != gameObject.scene)
            {
                continue;
            }

            foreach (Collider2D deathCollider in deathZone.GetComponentsInChildren<Collider2D>())
            {
                if (deathCollider != null && deathCollider.enabled)
                {
                    deathZoneColliders.Add(deathCollider);
                }
            }
        }
    }

    private BodyShape GetBodyShape(GolemController prefab)
    {
        BoxCollider2D bodyCollider = prefab.GetComponent<BoxCollider2D>();
        Vector3 scale = prefab.transform.localScale;
        Vector2 size = new Vector2(
            bodyCollider.size.x * Mathf.Abs(scale.x),
            bodyCollider.size.y * Mathf.Abs(scale.y));
        Vector2 offset = new Vector2(
            bodyCollider.offset.x * scale.x,
            bodyCollider.offset.y * scale.y);

        return new BodyShape(size, offset);
    }

    private Bounds GetBodyBounds(Vector3 rootPosition, GolemController prefab)
    {
        BodyShape shape = GetBodyShape(prefab);
        Vector2 center = (Vector2)rootPosition + shape.Offset;
        return CreateBodyBounds(center, shape.Size);
    }

    private static Bounds CreateBodyBounds(Vector2 center, Vector2 size)
    {
        return new Bounds(center, new Vector3(size.x, size.y, 0.1f));
    }

    private static bool FailPlacement(out Vector3 position, out Bounds bodyBounds)
    {
        position = default;
        bodyBounds = default;
        return false;
    }

    private static bool RollPercentage(float percentage)
    {
        return percentage >= 100f || (percentage > 0f && UnityEngine.Random.value * 100f < percentage);
    }

    private static bool ContainsPoint2D(Bounds area, Vector3 point)
    {
        return point.x >= area.min.x && point.x <= area.max.x &&
        point.y >= area.min.y && point.y <= area.max.y;
    }

    private static bool ContainsBounds2D(Bounds area, Bounds body)
    {
        return body.min.x >= area.min.x && body.max.x <= area.max.x &&
        body.min.y >= area.min.y && body.max.y <= area.max.y;
    }

    private static bool Intersects2D(Bounds first, Bounds second)
    {
        return first.min.x < second.max.x && first.max.x > second.min.x &&
        first.min.y < second.max.y && first.max.y > second.min.y;
    }

    private static bool PositiveFinite(float value)
    {
        return value > 0f && !float.IsInfinity(value) && !float.IsNaN(value);
    }

    private static bool NonNegativeFinite(float value)
    {
        return value >= 0f && !float.IsInfinity(value) && !float.IsNaN(value);
    }

    private static bool ValidPercentage(float value)
    {
        return value >= 0f && value <= 100f && !float.IsNaN(value);
    }

    private static bool ApproximatelyOne(float value)
    {
        return Mathf.Abs(value - 1f) <= 0.001f;
    }

    private bool Fail(string message)
    {
        Debug.LogError(message, this);
        return false;
    }

    private void OnDestroy()
    {
        foreach (SpawnSlot slot in slots)
        {
            GolemController instance = slot.Instance;
            if (instance == null)
            {
                continue;
            }

            if (slot.DeathHandler != null)
            {
                instance.Died -= slot.DeathHandler;
            }

            if (slot.RewardHandler != null)
            {
                instance.RewardSpawned -= slot.RewardHandler;
            }
        }
    }

    private void OnDrawGizmosSelected()
    {
        Bounds area = AreaBounds;
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireCube(area.center, area.size);

        if (fixedPoints != null)
        {
            Gizmos.color = Color.cyan;

            foreach (Transform point in fixedPoints)
            {
                if (point != null)
                {
                    Gizmos.DrawWireSphere(point.position, 0.12f);
                }
            }
        }

        foreach (SpawnSlot slot in slots)
        {
            if (slot.Prefab == null)
            {
                continue;
            }

            Gizmos.color = slot.IsAlive ? Color.green : Color.red;
            BodyShape shape = GetBodyShape(slot.Prefab);
            Vector2 center = (Vector2)slot.Position + shape.Offset;
            Gizmos.DrawWireCube(center, shape.Size);
        }
    }
}
