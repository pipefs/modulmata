using UnityEngine;

public class KeyPickup : MonoBehaviour
{
    [SerializeField] private AudioClip pickupSound;

    private void OnTriggerEnter2D(Collider2D collision)
    {
        GameManager gm = GameManager.Instance;
        PlayerController player = collision.GetComponentInParent<PlayerController>();

        if (player != null && gm != null)
        {
            player.PlayActionSound(pickupSound);
            gm.GrantFinalKey();
            Destroy(gameObject);
        }
    }
}
