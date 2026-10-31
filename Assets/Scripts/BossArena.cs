using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using Unity.Cinemachine;

// Coordina el inicio, reinicio y cierre del combate del jefe junto con puerta, cámara y barra de vida.
public class BossArena : MonoBehaviour
{
    public GolemBossController boss;
    [SerializeField] private BossDoor door;

    [Header("Cámaras")]
    [SerializeField] private CinemachineCamera arenaCamera;
    [SerializeField] private CinemachineCamera explorationCamera;
    [SerializeField, Min(1)] private int combatPriorityOffset = 10;
    [SerializeField, Min(0f)] private float deathCameraHoldDuration = 1.5f;

    [Header("Barra")]
    [SerializeField] private GameObject bossHealthBar;
    [SerializeField] private Image healthFill;
    [SerializeField] private Image slowHealthFill;

    private GameManager gameManager;
    private BossHealthBar bar;
    private Coroutine defeatUIRoutine;
    private bool playerReady;

    public bool IsFighting { get; private set; }
    public bool AwaitingRespawn { get; private set; }


    private void Awake()
    {
        bar = new BossHealthBar(bossHealthBar, healthFill, slowHealthFill);
        bar.Hide();

        if (boss == null || door == null)
        {
            Debug.LogError("[ERROR] BossArena necesita un jefe y una barrera.", this);
            enabled = false;
            return;
        }

        door.AssignArena(this);
    }

    private void OnEnable()
    {
        if (boss != null)
        {
            boss.OnDied.AddListener(BossDefeated);
        }
    }

    private void Start()
    {
        gameManager = GameManager.Instance;

        if (door != null)
        {
            door.Open();
        }
    }

    private void LateUpdate()
    {
        if (AwaitingRespawn)
        {
            TryFinishReset();
            return;
        }

        if (IsFighting)
        {
            float health;

            if (boss != null && boss.maxHealth > 0f)
            {
                health = Mathf.Clamp01(boss.CurrentHealth / boss.maxHealth);
            }
            else
            {
                health = 0f;
            }

            bar.Tick(health);
        }
    }

    // Inicia el combate una sola vez cuando el jugador activo entra en el recinto.
    private void OnTriggerEnter2D(Collider2D other)
    {
        if (IsFighting)
        {
            return;
        }

        if (boss == null || boss.IsDead)
        {
            return;
        }

        if (door == null)
        {
            return;
        }

        PlayerController player = other.GetComponentInParent<PlayerController>();

        if (gameManager == null || !gameManager.IsPlaying(player))
        {
            return;
        }

        if (gameManager.ActiveArena != null && gameManager.ActiveArena != this)
        {
            return;
        }

        gameManager.ActiveArena = this;

        IsFighting = true;

        door.Close();
        boss.BeginFight();

        float health;

        if (boss != null && boss.maxHealth > 0f)
        {
            health = Mathf.Clamp01(boss.CurrentHealth / boss.maxHealth);
        }
        else
        {
            health = 0f;
        }

        bar.Show(health);

        SetCamera(true);
    }

    private void SetCamera(bool active)
    {
        GameManager.Instance?.SetArenaCamera(arenaCamera, explorationCamera, combatPriorityOffset, active);
    }

    public void BeginPlayerDeath()
    {
        if (!IsFighting)
        {
            return;
        }

        playerReady = false;

        if (AwaitingRespawn)
        {
            return;
        }

        AwaitingRespawn = true;
        StopDefeatFeedback();

        if (boss != null)
        {
            boss.BeginReturnHome();
        }
    }

    public void NotifyPlayerRespawned()
    {
        if (!AwaitingRespawn)
        {
            return;
        }

        playerReady = true;
        bar.Hide();

        SetCamera(false);
        TryFinishReset();
    }

    // Reinicia solo cuando el nuevo jugador está listo y el jefe ya recuperó su posición inicial.
    private void TryFinishReset()
    {
        if (!playerReady || gameManager == null || !gameManager.IsPlaying(gameManager.CurrentPlayer))
        {
            return;
        }

        if (boss != null && boss.isActiveAndEnabled && !boss.HasReturnedHome)
        {
            return;
        }

        ResetAfterPlayerDeath(null);
    }

    // Limpieza inmediata solo para retirada del jugador/arena o regreso ya completado.
    public void ResetAfterPlayerDeath(PlayerController corpse)
    {
        if (!IsFighting)
        {
            return;
        }

        IsFighting = false;
        AwaitingRespawn = false;
        playerReady = false;

        StopDefeatFeedback();

        bar.Hide();

        SetCamera(false);

        if (boss != null)
        {
            boss.ResetCombat();
        }

        if (door != null)
        {
            door.Open();
        }

        if (gameManager != null && gameManager.ActiveArena == this)
        {
            gameManager.ActiveArena = null;
        }
    }

    // Abre la salida, entrega la llave y conserva brevemente la cámara de la arena.
    private void BossDefeated()
    {
        if (!IsFighting)
        {
            return;
        }

        IsFighting = false;
        door.Open();
        bar.Hide();

        if (gameManager != null)
        {
            gameManager.GrantFinalKey();

            if (gameManager.ActiveArena == this)
            {
                gameManager.ActiveArena = null;
            }
        }

        StopDefeatFeedback();
        defeatUIRoutine = StartCoroutine(EndDefeatUI());
    }

    private IEnumerator EndDefeatUI()
    {
        yield return new WaitForSeconds(deathCameraHoldDuration);
        SetCamera(false);
        defeatUIRoutine = null;
    }

    private void StopDefeatFeedback()
    {
        if (defeatUIRoutine != null)
        {
            StopCoroutine(defeatUIRoutine);
        }

        defeatUIRoutine = null;
    }

    private void OnDisable()
    {
        if (boss != null)
        {
            boss.OnDied.RemoveListener(BossDefeated);
        }

        if (IsFighting)
        {
            ResetAfterPlayerDeath(null);
        }

        StopDefeatFeedback();

        bar?.Hide();

        SetCamera(false);
    }
}
