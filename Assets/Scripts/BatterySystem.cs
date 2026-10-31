using System;
using UnityEngine;

// Mantiene las reservas de batería y durabilidad del jugador y notifica cuando la energía se agota.
public class BatterySystem : MonoBehaviour
{
    public float MaxBattery { get; } = 100f;
    public float MaxDurability { get; } = 100f;
    public float CurrentBattery { get; private set; }
    public float CurrentDurability { get; private set; }
    public bool IsDepleted => CurrentBattery <= 0f;

    public AudioSource criticalStateSource;


    public event Action BatteryDepleted;
    private MusicManager musicManager;

    private void Awake()
    {
        CurrentBattery = MaxBattery;
        CurrentDurability = MaxDurability;
    }

    private void Start()
    {
        musicManager = MusicManager.Instance;
    }

    private void LateUpdate()
    {
        if (CurrentDurability <= 0f && musicManager.CriticalEffectActive == false)
        {
            musicManager.BeginCriticalStateEffect(1000);
            criticalStateSource.Play();
        }

        if (CurrentDurability > 0f && musicManager.CriticalEffectActive == true)
        {
            criticalStateSource.Stop();
            musicManager.EndCriticalStateEffect();
        }
    }

    public void ConsumeBattery(float deltaTime)
    {
        if (!CanChange(deltaTime))
        {
            return;
        }

        float multiplier = 1.111f;

        if (CurrentDurability <= 0f) // 1m 30s
        {
            multiplier = 11.111f;
        }
        else if (CurrentDurability <= MaxDurability * 0.25f) // 5m
        {
            multiplier = 5.556f;
        }
        else if (CurrentDurability <= MaxDurability * 0.50f) // 10m
        {
            multiplier = 3.333f;
        }
        else if (CurrentDurability <= MaxDurability * 0.75f) // 15m
        {
            multiplier = 1.667f;
        }

        ReduceBattery(0.1f * multiplier * deltaTime);
    }

    // El excedente de un golpe a durabilidad no se transfiere a batería.
    public float ApplyDamage(float amount)
    {
        if (!CanChange(amount))
        {
            return 0f;
        }

        return CurrentDurability > 0f ? ReduceDurability(amount) : ReduceBattery(amount);
    }

    public float AddDurability(float amount)
    {
        if (!CanChange(amount))
        {
            return 0f;
        }

        float restored = Mathf.Min(amount, MaxDurability - CurrentDurability);
        CurrentDurability += restored;

        return restored;
    }

    public float ReduceDurability(float amount)
    {
        if (!CanChange(amount))
        {
            return 0f;
        }

        float reduced = Mathf.Min(amount, CurrentDurability);
        CurrentDurability -= reduced;

        return reduced;
    }

    public float AddBattery(float amount)
    {
        if (!CanChange(amount))
        {
            return 0f;
        }

        float restored = Mathf.Min(amount, MaxBattery - CurrentBattery);
        CurrentBattery += restored;

        return restored;
    }

    public float ReduceBattery(float amount)
    {
        if (!CanChange(amount))
        {
            return 0f;
        }

        float reduced = Mathf.Min(amount, CurrentBattery);
        CurrentBattery -= reduced;

        // Una vez agotado, CanChange impide volver a emitir el evento o revivir el cuerpo.
        if (IsDepleted)
        {
            BatteryDepleted?.Invoke();
        }

        return reduced;
    }

    public void ResetBattery()
    {
        AddBattery(MaxBattery);
    }

    private bool CanChange(float amount)
    {
        return isActiveAndEnabled && !IsDepleted && amount > 0f && !float.IsInfinity(amount);
    }
}
