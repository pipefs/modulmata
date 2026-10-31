using System.Collections;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;

// Único propietario de prioridades y del corte temporal de Cinemachine.
public sealed class GameCameras
{
    private readonly MonoBehaviour owner;
    private readonly CinemachineCamera player;
    private readonly CinemachineCamera final;
    private readonly CinemachineBrain brain;
    private readonly int finalOffset;
    private readonly PrioritySettings finalPriority;
    private readonly CinemachineBlendDefinition normalBlend;
    private readonly Dictionary<CinemachineCamera, PrioritySettings> arenaPriorities = new();
    private Coroutine restore;
    private readonly Transform deathTarget;

    public GameCameras(MonoBehaviour owner, CinemachineCamera player, CinemachineCamera final, CinemachineBrain brain, int finalOffset, Transform deathTarget)
    {
        this.owner = owner;
        this.player = player;
        this.final = final;
        this.brain = brain;
        this.finalOffset = finalOffset;
        this.deathTarget = deathTarget;

        if (final != null)
        {
            finalPriority = final.Priority;
        }

        if (brain != null)
        {
            normalBlend = brain.DefaultBlend;
        }
    }

    public void Follow(PlayerController target)
    {
        if (player == null)
        {
            return;
        }

        player.Follow = target == null ? null : target.cameraTarget != null ? target.cameraTarget : target.transform;

        Invalidate();
    }

    // Anula el blend durante un frame para evitar mostrar el recorrido entre objetivos muy alejados.
    private void CutForOneFrame()
    {
        if (brain == null || owner == null || !owner.isActiveAndEnabled)
        {
            return;
        }

        if (restore != null)
        {
            owner.StopCoroutine(restore);
        }

        brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.Cut, 0f);
        restore = owner.StartCoroutine(RestoreBlend());
    }

    public void HoldPlayer()
    {
        if (player == null || player.Follow == null || deathTarget == null)
        {
            return;
        }

        deathTarget.position = player.Follow.position;
        player.Follow = deathTarget;
    }

    public void Invalidate()
    {
        if (player != null)
        {
            player.PreviousStateIsValid = false;
        }

        if (final != null)
        {
            final.PreviousStateIsValid = false;
        }
    }

    public void SetFinal(bool active)
    {
        if (final == null)
        {
            return;
        }

        final.Priority = active
            ? Mathf.Max(player != null ? player.Priority.Value : 0, finalPriority.Value) + finalOffset
            : finalPriority;

        Invalidate();
        CutForOneFrame();
    }

    // Eleva temporalmente la cámara de arena y restaura las prioridades originales al terminar.
    public void SetArena(CinemachineCamera camera, CinemachineCamera exploration, int offset, bool active)
    {
        if (camera == null)
        {
            return;
        }

        if (!arenaPriorities.TryGetValue(camera, out var original))
        {
            arenaPriorities[camera] = original = camera.Priority;
        }

        camera.Priority = active
            ? Mathf.Max(original.Value, exploration != null ? exploration.Priority.Value : 0) + offset
            : original;
    }

    private IEnumerator RestoreBlend()
    {
        yield return null;
        yield return new WaitForEndOfFrame();

        if (brain != null)
        {
            brain.DefaultBlend = normalBlend;
        }

        restore = null;
    }

    public void Dispose()
    {
        if (restore != null && owner != null)
        {
            owner.StopCoroutine(restore);
        }

        restore = null;

        if (brain != null)
        {
            brain.DefaultBlend = normalBlend;
        }

        if (final != null)
        {
            final.Priority = finalPriority;
        }

        foreach (var pair in arenaPriorities)
        {
            if (pair.Key != null)
            {
                pair.Key.Priority = pair.Value;
            }
        }

        arenaPriorities.Clear();
    }
}
