using System;
using UnityEngine;

// Resuelve la secuencia temporal de los golpes del jugador con valores oficiales independientes del prefab.
public sealed class PlayerCombat
{
    // Tipos de golpe que el controlador puede solicitar y que las devoluciones de audio identifican.
    public enum Punch
    {
        Jab,
        Cross
    }

    // Agrupa los tiempos y restricciones de movimiento de una acción de combate completa.
    private readonly struct Attack
    {
        public readonly float Damage;
        public readonly float Duration;
        public readonly float HitTime;
        public readonly float Recovery;
        public readonly float MovementMultiplier;
        public Attack(float damage, float duration, float hitTime, float recovery, float movementMultiplier)
        {
            Damage = Mathf.Max(0f, damage);
            Duration = Mathf.Max(0f, duration);
            HitTime = Mathf.Clamp(hitTime, 0f, Duration);
            Recovery = Mathf.Max(0f, recovery);
            MovementMultiplier = Mathf.Clamp01(movementMultiplier);
        }
    }

    private readonly Transform point;
    private readonly Vector2 size;
    private readonly LayerMask layers;
    private static readonly Attack JabAttack = new Attack(8f, 0.3f, 0.1f, 0.2f, 0.65f);
    private static readonly Attack CrossAttack = new Attack(20f, 0.6f, 0.3f, 0.7f, 0.2f);
    private readonly Action<Punch> play;
    private readonly Action<Punch> connected;
    private Punch current;
    private Punch? queued;
    private float elapsed;
    private float nextJab;
    private float nextCross;
    private int jabsUsed;
    private bool hit;
    public bool IsAttacking { get; private set; }
    public float MovementMultiplier => !IsAttacking ? 1f :
        current == Punch.Jab ? JabAttack.MovementMultiplier : CrossAttack.MovementMultiplier;

    public PlayerCombat(Transform point, Vector2 size, LayerMask layers, Action<Punch> play, Action<Punch> connected)
    {
        this.point = point;
        this.size = size;
        this.layers = layers;
        this.play = play;
        this.connected = connected;
    }

    // Inicia un golpe o conserva una única petición para ejecutarla al terminar el actual.
    public void Request(Punch punch)
    {
        if (!CanUse(punch))
        {
            return;
        }

        if (IsAttacking)
        {
            if (!queued.HasValue)
            {
                queued = punch;
            }
        }
        else
        {
            Start(punch);
        }
    }

    private bool CanUse(Punch punch)
    {
        return punch == Punch.Jab
        ? jabsUsed < 2 || Time.time >= nextJab
        : Time.time >= nextCross;
    }

    // Fija los tiempos del golpe y solicita al controlador la animación y el sonido correspondientes.
    private void Start(Punch punch)
    {
        if (punch == Punch.Jab)
        {
            if (Time.time >= nextJab)
            {
                jabsUsed = 0;
            }

            jabsUsed++;
            nextJab = float.PositiveInfinity;
        }
        else
        {
            nextCross = float.PositiveInfinity;
        }

        current = punch;
        queued = null;
        elapsed = 0f;
        hit = false;
        IsAttacking = true;
        play(punch);
    }

    // Abre el impacto una sola vez y encadena la petición pendiente tras ataque y recuperación.
    public void Tick(float deltaTime)
    {
        if (!IsAttacking)
        {
            return;
        }

        elapsed += deltaTime;
        Attack attack = current == Punch.Jab ? JabAttack : CrossAttack;
        if (!hit && elapsed >= attack.HitTime)
        {
            hit = true;
            Punch punch = current;
            if (point != null && CombatTargets.Hit(new Bounds(point.position, new Vector3(size.x, size.y, 1f)), layers, attack.Damage))
            {
                connected?.Invoke(punch);
            }
        }
        if (!IsAttacking || elapsed < attack.Duration)
        {
            return;
        }

        Punch? next = queued;
        Cancel();
        if (next.HasValue && CanUse(next.Value))
        {
            Start(next.Value);
        }
    }

    public void Cancel()
    {
        if (IsAttacking)
        {
            if (current == Punch.Jab)
            {
                nextJab = Time.time + JabAttack.Recovery;
            }
            else
            {
                nextCross = Time.time + CrossAttack.Recovery;
            }
        }
        IsAttacking = false;
        queued = null;
    }
}
