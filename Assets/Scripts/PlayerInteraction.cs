using System.Collections.Generic;
using UnityEngine;

// Contrato para objetos del mundo que pueden competir por la interacción del jugador.
public interface IPlayerInteractable
{
    bool CanInteract(PlayerController player);
    void SetSelected(bool selected);
    bool TryInteract(PlayerController player);
}

// Los triggers existentes registran contactos; solo el jugador lee la pulsación.
public sealed class PlayerInteraction
{
    private readonly Dictionary<MonoBehaviour, HashSet<Collider2D>> contacts = new();
    private readonly List<MonoBehaviour> stale = new();
    private MonoBehaviour selected;

    public void Enter(MonoBehaviour target, Collider2D contact)
    {
        if (target == null || contact == null || !(target is IPlayerInteractable))
        {
            return;
        }

        if (!contacts.TryGetValue(target, out var colliders))
        {
            contacts[target] = colliders = new HashSet<Collider2D>();
        }

        colliders.Add(contact);
    }

    public void Exit(MonoBehaviour target, Collider2D contact)
    {
        if (!contacts.TryGetValue(target, out var colliders))
        {
            return;
        }

        colliders.Remove(contact);
        if (colliders.Count == 0)
        {
            contacts.Remove(target);
        }
    }

    public void Tick(PlayerController player, bool pressed)
    {
        MonoBehaviour best = null;
        float bestDistance = float.PositiveInfinity;
        stale.Clear();
        foreach (var pair in contacts)
        {
            var target = pair.Key;
            pair.Value.RemoveWhere(c => c == null || !c.enabled || !c.gameObject.activeInHierarchy);
            if (target == null || !target.isActiveAndEnabled || pair.Value.Count == 0)
            {
                stale.Add(target);
                continue;
            }
            if (!((IPlayerInteractable)target).CanInteract(player))
            {
                continue;
            }

            float distance = (target.transform.position - player.transform.position).sqrMagnitude;
            if (distance < bestDistance || (distance == bestDistance &&
                (best == null || target.GetInstanceID() < best.GetInstanceID())))
            {
                best = target;
                bestDistance = distance;
            }
        }
        foreach (var target in stale)
        {
            contacts.Remove(target);
        }

        Select(best);
        if (pressed && selected != null)
        {
            var target = selected;
            if (((IPlayerInteractable)target).TryInteract(player))
            {
                Clear();
            }
        }
    }

    private void Select(MonoBehaviour target)
    {
        if (selected == target)
        {
            return;
        }

        if (selected != null)
        {
            ((IPlayerInteractable)selected).SetSelected(false);
        }

        selected = target;
        if (selected != null)
        {
            ((IPlayerInteractable)selected).SetSelected(true);
        }
    }

    public void Clear()
    {
        Select(null);
        contacts.Clear();
    }
}
