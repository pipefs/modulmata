using System.Collections.Generic;
using UnityEngine;

// Contrato común para objetivos cuyo volumen de daño puede diferir de su collider físico.
public interface IDamageable
{
    // Puede interceptar un ataque aunque rechace su daño; los cadáveres ya no son objetivos.
    bool IsDamageTarget { get; }
    Bounds DamageBounds { get; }
    bool TryTakeDamage(float amount);
}

// Las hurtboxes pueden ser distintas del collider físico (Gollux).
public static class CombatTargets
{
    private static readonly HashSet<MonoBehaviour> targets = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Clear()
    {
        targets.Clear();
    }

    public static void Register(MonoBehaviour target)
    {
        if (target is IDamageable)
        {
            targets.Add(target);
        }
    }

    public static void Unregister(MonoBehaviour target)
    {
        targets.Remove(target);
    }

    public static bool Hit(Bounds area, LayerMask layers, float damage)
    {
        bool connected = false;
        // El daño puede desactivar o destruir receptores y modificar el registro.
        var candidates = new List<MonoBehaviour>(targets);
        foreach (var candidate in candidates)
        {
            if (candidate == null || !candidate.isActiveAndEnabled ||
                (layers.value & (1 << candidate.gameObject.layer)) == 0)
            {
                continue;
            }

            var receiver = (IDamageable)candidate;
            if (receiver.IsDamageTarget && area.Intersects(receiver.DamageBounds) && receiver.TryTakeDamage(damage))
            {
                connected = true;
            }
        }
        return connected;
    }

    // Barrido de una caja contra las hurtboxes reales, incluida la propia de Gollux.
    // Esta consulta no aplica daño ni modifica el registro durante su recorrido.
    public static bool TryFindFirstHit(Vector2 origin, Vector2 size, Vector2 direction, float distance,
        LayerMask layers, MonoBehaviour ignored, out MonoBehaviour target, out float hitDistance)
    {
        target = null;
        hitDistance = distance;
        foreach (MonoBehaviour candidate in targets)
        {
            if (candidate == null || candidate == ignored || !candidate.isActiveAndEnabled ||
                (layers.value & (1 << candidate.gameObject.layer)) == 0)
            {
                continue;
            }

            var receiver = (IDamageable)candidate;
            if (!receiver.IsDamageTarget)
            {
                continue;
            }

            Bounds bounds = receiver.DamageBounds;
            if (bounds.size.x <= 0f || bounds.size.y <= 0f)
            {
                continue;
            }

            bounds.Expand(new Vector3(size.x, size.y, 0f));
            Vector3 start = new Vector3(origin.x, origin.y, bounds.center.z);
            float entry = 0f;
            if (!bounds.Contains(start) && !bounds.IntersectRay(new Ray(start, direction), out entry))
            {
                continue;
            }

            if (entry < 0f || entry > hitDistance)
            {
                continue;
            }

            target = candidate;
            hitDistance = entry;
        }
        return target != null;
    }
}
