using UnityEngine;

// Puente entre el Animator del hijo visual y el controlador de la raíz.
public class GolluxAnimationEvents : MonoBehaviour
{
    private GolemBossController boss;

    private void Awake() => boss = GetComponentInParent<GolemBossController>();

    public void TriggerHitbox() => boss?.TriggerHitbox();

    public void DisableHitbox() => boss?.DisableHitbox();

    public void FinishAttack() => boss?.FinishAttack();
}
