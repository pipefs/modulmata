using UnityEngine;

public class DeathZone : MonoBehaviour
{
    private void OnTriggerEnter2D(Collider2D collision)
    {
        GameManager manager = GameManager.Instance;
        PlayerController player = collision.GetComponentInParent<PlayerController>();

        if (manager == null || manager.IsDead || manager.HasWon || player == null || player != manager.CurrentPlayer)
            return;

        player.GetComponent<BatterySystem>().Kill();
    }
}
