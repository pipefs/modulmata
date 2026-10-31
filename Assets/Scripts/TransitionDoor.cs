using TMPro;
using UnityEngine;

// Ofrece una interacción entre puertas enlazadas y delega el traslado cubierto al GameManager.
[RequireComponent(typeof(BoxCollider2D))]
public class TransitionDoor : MonoBehaviour, IPlayerInteractable
{
    [SerializeField] private TransitionDoor destination;
    [SerializeField] private Vector2 exitOffset = new Vector2(2f, 0f);
    [SerializeField] private bool requiresSurfaceKey;
    [SerializeField] private bool activatesFinalCamera;
    [Header("Interacción")]
    [SerializeField] private TextMeshPro prompt;
    [SerializeField, Min(0f)] private float promptFadeDuration = 0.15f;
    [SerializeField, Min(0f)] private float promptPulseScale = 1.06f;
    [SerializeField, Min(0.01f)] private float promptPulseDuration = 0.65f;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip lockedSound;
    [SerializeField] private AudioClip transitionSound;
    private InteractionPrompt indicator;
    private bool selected;

    private void Awake()
    {
        var trigger = GetComponent<BoxCollider2D>();
        if (trigger == null || destination == null)
        {
            Debug.LogError("TransitionDoor necesita un BoxCollider2D y una puerta destino.", this);
            enabled = false;
            return;
        }
        trigger.isTrigger = true;
        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
        }

        if (prompt == null)
        {
            prompt = GetComponentInChildren<TextMeshPro>(true);
        }

        indicator = new InteractionPrompt(this, prompt, promptFadeDuration, promptPulseScale, promptPulseDuration);
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
        if (!isActiveAndEnabled)
        {
            return;
        }

        var player = other.GetComponentInParent<PlayerController>();
        if (player != null && player.IsPlaying)
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
        return isActiveAndEnabled && player != null && player.CanAct;
    }

    public void SetSelected(bool value)
    {
        if (!isActiveAndEnabled || indicator == null)
        {
            return;
        }

        selected = value;
        if (value)
        {
            UpdatePrompt();
            indicator.Show();
        }
        else
        {
            indicator.Hide();
        }
    }

    private void LateUpdate()
    {
        if (selected)
        {
            UpdatePrompt();
        }
    }

    private void UpdatePrompt()
    {
        var manager = GameManager.Instance;
        bool locked = requiresSurfaceKey && (manager == null || !manager.HasFinalKey);
        indicator.SetText(locked ? "<sprite name=\"Z\"> LLAVE REQUERIDA" : "<sprite name=\"Z\"> ENTRAR");
    }

    public bool TryInteract(PlayerController player)
    {
        if (!CanInteract(player) || destination == null || !destination.isActiveAndEnabled)
        {
            return false;
        }

        var manager = GameManager.Instance;
        if (requiresSurfaceKey && !manager.HasFinalKey)
        {
            if (audioSource != null && lockedSound != null)
            {
                audioSource.PlayOneShot(lockedSound);
            }

            return false;
        }
        if (!manager.BeginDoorTransition(destination.transform.position + (Vector3)destination.exitOffset, activatesFinalCamera))
        {
            return false;
        }

        if (audioSource != null && transitionSound != null)
        {
            audioSource.PlayOneShot(transitionSound);
        }

        return true;
    }

    private void OnDisable()
    {
        selected = false;
        indicator?.HideImmediate();
    }
}
