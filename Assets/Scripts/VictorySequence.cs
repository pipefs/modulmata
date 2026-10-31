using System.Collections;
using TMPro;
using UnityEngine;

// Convierte la salida final en una interacción y conduce al jugador hasta el fundido de victoria.
[RequireComponent(typeof(BoxCollider2D))]
public class VictorySequence : MonoBehaviour, IPlayerInteractable
{
    [SerializeField] private TextMeshPro prompt;
    [SerializeField, Min(0f)] private float promptFadeDuration = 0.15f;
    [SerializeField, Min(0f)] private float promptPulseScale = 1.06f;
    [SerializeField, Min(0.01f)] private float promptPulseDuration = 0.65f;
    [SerializeField] private Transform boardingPoint;
    [SerializeField, Min(0f)] private float ascentHeight = 8f;
    [SerializeField, Min(0.01f)] private float ascentDuration = 2.5f;
    [SerializeField] private AnimationCurve ascentCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    private InteractionPrompt indicator;
    private PlayerController passenger;
    private Rigidbody2D body;
    private GameManager manager;
    private Vector3 originalPosition;
    private bool originalSimulation;
    private Coroutine ascentRoutine;
    private bool sequenceStarted;

    private void Awake()
    {
        GetComponent<BoxCollider2D>().isTrigger = true;
        if (prompt == null)
        {
            prompt = GetComponentInChildren<TextMeshPro>(true);
        }

        indicator = new InteractionPrompt(this, prompt, promptFadeDuration, promptPulseScale, promptPulseDuration);
        indicator.SetText("<sprite name=\"Z\"> ASCENDER");
        if (boardingPoint == null)
        {
            Debug.LogError("VictorySequence necesita boardingPoint antes de iniciar la victoria.", this);
            enabled = false;
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        Register(other);
    }

    private void OnTriggerStay2D(Collider2D other)
    {
        Register(other);
    }

    private void Register(Collider2D other)
    {
        var player = other.GetComponentInParent<PlayerController>();
        if (!sequenceStarted && player != null && player.IsPlaying)
        {
            player.RegisterInteraction(this, other);
        }
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        other.GetComponentInParent<PlayerController>()?.UnregisterInteraction(this, other);
    }

    public bool CanInteract(PlayerController player)
    {
        return isActiveAndEnabled && !sequenceStarted &&
        boardingPoint != null && player != null && player.CanAct;
    }

    public void SetSelected(bool selected)
    {
        if (selected)
        {
            indicator.Show();
        }
        else
        {
            indicator.Hide();
        }
    }

    public bool TryInteract(PlayerController player)
    {
        if (!CanInteract(player))
        {
            return false;
        }

        manager = GameManager.Instance;
        body = player.GetComponent<Rigidbody2D>();
        if (body == null || !manager.TryBeginVictory(player))
        {
            return false;
        }

        sequenceStarted = true;
        passenger = player;
        originalPosition = player.transform.position;
        originalSimulation = body.simulated;
        ascentRoutine = StartCoroutine(Ascend());
        return true;
    }
    // Retira la simulación física, mueve al jugador con la curva configurada y solicita el fundido final.
    private IEnumerator Ascend()
    {
        if (passenger.animator != null)
        {
            passenger.animator.Play("JumpMax");
        }

        body.linearVelocity = Vector2.zero;
        body.simulated = false;
        Vector3 start = boardingPoint.position;
        Vector3 end = start + Vector3.up * ascentHeight;
        passenger.transform.position = start;
        float elapsed = 0f;
        do
        {
            if (passenger == null || manager == null || manager.CurrentPlayer != passenger || !manager.HasWon)
            {
                CancelSequence();
                yield break;
            }
            elapsed += Time.deltaTime;
            float progress = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, ascentDuration));
            passenger.transform.position = Vector3.LerpUnclamped(start, end,
                ascentCurve != null ? ascentCurve.Evaluate(progress) : progress);
            yield return null;
        } while (elapsed < ascentDuration);
        passenger.transform.position = end;
        ascentRoutine = null;
        manager.VictoryFadeIn();
    }
    // Devuelve posición, física y estado global si el ascenso se interrumpe.
    private void CancelSequence()
    {
        if (!sequenceStarted)
        {
            return;
        }

        sequenceStarted = false;
        if (body != null)
        {
            body.simulated = originalSimulation;
            body.linearVelocity = Vector2.zero;
        }
        if (passenger != null)
        {
            passenger.transform.position = originalPosition;
        }

        if (manager != null)
        {
            manager.CancelVictory(passenger);
        }

        passenger = null;
    }

    private void OnDisable()
    {
        if (ascentRoutine != null)
        {
            StopCoroutine(ascentRoutine);
        }

        ascentRoutine = null;
        CancelSequence();
        indicator?.HideImmediate();
    }
}
