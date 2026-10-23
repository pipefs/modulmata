using UnityEngine;

public class BatterySystem : MonoBehaviour
{
    private GameManager gameManager;

    public float MaxBattery { get; private set; } = 100f;
    public float MaxDurability { get; private set; } = 100f;
    public float CurrentBattery { get; set; }
    public float CurrentDurability { get; set; }

    private void Awake()
    {
        CurrentBattery = MaxBattery;
        CurrentDurability = MaxDurability;
    }

    private void Start()
    {
        gameManager = GameManager.Instance;
    }

    private void Update()
    {
        if (!enabled || gameManager == null || gameManager.IsDead || gameManager.HasWon)
            return;

        float multiplier = 1.111f;
        if (CurrentDurability <= 0f)
            multiplier = 11.111f;
        else if (CurrentDurability <= MaxDurability * 0.25f)
            multiplier = 5.556f;
        else if (CurrentDurability <= MaxDurability * 0.50f)
            multiplier = 3.333f;
        else if (CurrentDurability <= MaxDurability * 0.75f)
            multiplier = 1.667f;

        ReduceBattery(0.1f * multiplier * Time.deltaTime);
    }

    public void AddDurabilty(float amount)
    {
        if (!enabled || gameManager == null || gameManager.IsDead || gameManager.HasWon)
            return;

        CurrentDurability = Mathf.Clamp(CurrentDurability + Mathf.Max(0f, amount), 0f, MaxDurability);
    }

    public void ReduceDurability(float amount)
    {
        if (!enabled || gameManager == null || gameManager.IsDead || gameManager.HasWon)
            return;

        CurrentDurability = Mathf.Clamp(CurrentDurability - Mathf.Max(0f, amount), 0f, MaxDurability);
    }

    public void AddBattery(float amount)
    {
        if (!enabled || gameManager == null || gameManager.IsDead || gameManager.HasWon)
            return;

        CurrentBattery = Mathf.Clamp(CurrentBattery + Mathf.Max(0f, amount), 0f, MaxBattery);
    }

    public void ReduceBattery(float amount)
    {
        if (!enabled || gameManager == null || gameManager.IsDead || gameManager.HasWon || amount <= 0f)
            return;

        CurrentBattery = Mathf.Max(0f, CurrentBattery - amount);

        if (CurrentBattery <= 0f)
            Kill();
    }

    public void Kill()
    {
        PlayerController player = GetComponent<PlayerController>();

        if (!enabled || gameManager == null || gameManager.IsDead || gameManager.HasWon || player == null || player != gameManager.CurrentPlayer)
            return;

        CurrentBattery = 0f;
        gameManager.HandlePlayerDeath(player);
    }

    public void ResetBattery()
    {
        if (!enabled || gameManager == null || gameManager.IsDead || gameManager.HasWon)
            return;

        CurrentBattery = MaxBattery;
    }
}
