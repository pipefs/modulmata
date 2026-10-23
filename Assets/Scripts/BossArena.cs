using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using Unity.Cinemachine;

public class BossArena : MonoBehaviour
{
    public GolemBossController boss;
    [SerializeField] private BossDoor door;
    [SerializeField] private Transform outsideDeathPoint;

    [Header("Camaras")]
    [SerializeField] private CinemachineCamera arenaCamera;
    [SerializeField] private CinemachineCamera explorationCamera;
    [SerializeField, Min(1)] private int combatPriorityOffset = 10;
    [SerializeField, Min(0f)] private float deathCameraHoldDuration = 1.5f;

    [Header("Barra Fefe")]
    [SerializeField] private GameObject bossHealthBar;
    [SerializeField] private Image healthFill;
    [SerializeField] private Image slowHealthFill;

    private PrioritySettings originalArenaPriority;
    private Coroutine defeatUIRoutine;
    private Coroutine slowDrainRoutine;
    private float previousHealthTarget;
    private bool healthUIInitialized;

    public bool IsFighting { get; private set; }

    private void Awake()
    {
        if (arenaCamera != null)
            originalArenaPriority = arenaCamera.Priority;

        SetCombatUI(false);
    }

    private void OnEnable()
    {
        SetCombatUI(IsFighting);
    }

    private void LateUpdate()
    {
        if (IsFighting)
            UpdateHealthUI();
    }

    private float GetHealthTarget()
    {
        return boss != null && boss.maxHealth > 0f ? Mathf.Clamp01(boss.CurrentHealth / boss.maxHealth) : 0f;
    }

    private void ResetHealthUI()
    {
        float target = GetHealthTarget();
        previousHealthTarget = target;
        healthUIInitialized = true;

        if (healthFill != null)
            healthFill.fillAmount = target;

        if (slowHealthFill != null)
            slowHealthFill.fillAmount = target;

    }

    private void UpdateHealthUI()
    {
        if (!healthUIInitialized)
            ResetHealthUI();

        float target = GetHealthTarget();

        if (target < previousHealthTarget - 0.0001f)
            PlayDamageFeedback(target);
        else if (target > previousHealthTarget + 0.0001f && slowHealthFill != null)
        {
            if (slowDrainRoutine != null)
                StopCoroutine(slowDrainRoutine);

            slowDrainRoutine = null;
            slowHealthFill.fillAmount = target;
        }

        previousHealthTarget = target;

        if (healthFill != null)
            healthFill.fillAmount = SmoothLerp(healthFill.fillAmount, target, 12f);
    }

    private float SmoothLerp(float current, float target, float speed)
    {
        float factor = 1f - Mathf.Exp(-speed * Time.unscaledDeltaTime);
        return Mathf.Lerp(current, target, factor);
    }

    private void PlayDamageFeedback(float target)
    {
        if (slowDrainRoutine != null)
            StopCoroutine(slowDrainRoutine);

        slowDrainRoutine = StartCoroutine(DrainSlowFill(target));
    }

    private IEnumerator DrainSlowFill(float target)
    {
        yield return new WaitForSecondsRealtime(0.4f);

        while (slowHealthFill != null && Mathf.Abs(slowHealthFill.fillAmount - target) > 0.0001f)
        {
            slowHealthFill.fillAmount = SmoothLerp(slowHealthFill.fillAmount, target, 6f);
            yield return null;
        }

        if (slowHealthFill != null)
            slowHealthFill.fillAmount = target;

        slowDrainRoutine = null;
    }

    private void SetCombatUI(bool fighting)
    {
        if (arenaCamera != null)
        {
            if (fighting)
            {
                int explorationPriority = explorationCamera != null ? explorationCamera.Priority.Value : 0;
                arenaCamera.Priority = Mathf.Max(originalArenaPriority.Value, explorationPriority)
                    + combatPriorityOffset;
            }
            else
                arenaCamera.Priority = originalArenaPriority;
        }

        if (fighting)
            ResetHealthUI();

        if (bossHealthBar != null)
            bossHealthBar.SetActive(fighting);
    }

    private void OnDisable()
    {
        SetCombatUI(false);
        StopHealthFeedback();
    }

    private void StopHealthFeedback()
    {
        if (slowDrainRoutine != null)
            StopCoroutine(slowDrainRoutine);

        slowDrainRoutine = null;
    }

    private void Start()
    {
        if (boss == null || door == null)
        {
            enabled = false;
            return;
        }

        boss.onDied.AddListener(BossDefeated);
        door.Open();
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        PlayerController player = collision.GetComponentInParent<PlayerController>();
        TryStartFight(player);
    }

    private void TryStartFight(PlayerController player)
    {
        GameManager manager = GameManager.Instance;

        if (!enabled || player == null || IsFighting || boss == null || boss.IsDead)
            return;

        if (manager == null || manager.IsDead || manager.HasWon || player != manager.CurrentPlayer)
            return;

        manager.ActiveArena = this;
        IsFighting = true;

        door.Close();
        boss.BeginFight();
        SetCombatUI(true);
    }

    public void ResetAfterPlayerDeath(PlayerController corpse)
    {
        if (!IsFighting)
            return;

        IsFighting = false;
        SetCombatUI(false);

        if (outsideDeathPoint != null)
        {
            corpse.transform.position = outsideDeathPoint.position;

            Rigidbody2D body = corpse.GetComponent<Rigidbody2D>();
            if (body != null)
                body.linearVelocity = Vector2.zero;
        }

        boss.ResetCombat();
        door.Open();

        TriggerEvent oldTrigger = GetComponent<TriggerEvent>();
        if (oldTrigger != null)
            oldTrigger.ResetTrigger();

        if (GameManager.Instance != null)
            GameManager.Instance.ActiveArena = null;
    }

    private void BossDefeated()
    {
        IsFighting = false;
        door.Open();
        UpdateHealthUI();

        if (GameManager.Instance != null)
        {
            GameManager.Instance.GrantA3Key();
            GameManager.Instance.ActiveArena = null;
        }

        if (defeatUIRoutine == null)
            defeatUIRoutine = StartCoroutine(EndDefeatUI());
    }

    private IEnumerator EndDefeatUI()
    {
        if (bossHealthBar != null)
            bossHealthBar.SetActive(false);

        yield return new WaitForSeconds(deathCameraHoldDuration);

        SetCombatUI(false);
        defeatUIRoutine = null;
    }

    private void OnDestroy()
    {
        if (boss != null)
            boss.onDied.RemoveListener(BossDefeated);

        StopHealthFeedback();
    }
}
