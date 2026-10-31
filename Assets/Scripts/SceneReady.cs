using UnityEngine;

// Comunica a SceneLoader cuándo una escena terminó de prepararse y cuándo ya fue revelada.
[DisallowMultipleComponent]
public sealed class SceneReady : MonoBehaviour
{
    public bool IsReady { get; private set; }
    public bool IsRevealed { get; private set; }
    public bool HasFailed { get; private set; }

    public void MarkReady()
    {
        if (!HasFailed)
        {
            IsReady = true;
        }
    }

    internal void MarkRevealed()
    {
        IsRevealed = true;
    }

    private void OnDisable()
    {
        if (!IsRevealed)
        {
            Debug.LogError("[ERROR] SceneReady se desactivó antes de completar la entrada.");
        }
    }
}
