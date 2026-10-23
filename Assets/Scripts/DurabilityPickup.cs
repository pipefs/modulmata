using UnityEngine;

public class DurabilityPickup : MonoBehaviour
{
    [Min(0f)] public float restoreAmount = 20f;

    private bool collected;

    private void OnTriggerStay2D(Collider2D other)
    {
        TryCollect(other);
    }

    private void TryCollect(Collider2D other)
    {
        GameManager manager = GameManager.Instance;
        PlayerController player = other.GetComponentInParent<PlayerController>();

        if (collected || manager == null || manager.IsDead || manager.HasWon || player == null || player != manager.CurrentPlayer || restoreAmount <= 0f)
            return;

        BatterySystem battery = player.GetComponent<BatterySystem>();

        if (battery == null || !battery.enabled || battery.CurrentDurability >= battery.MaxDurability)
            return;

        collected = true;
        battery.AddDurabilty(restoreAmount);
        player.PlayFlash(Color.cyan, 0.1f);
        Destroy(gameObject);
    }
}
