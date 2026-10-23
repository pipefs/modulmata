using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class BatteryUI : MonoBehaviour
{
    public Image batteryBar;
    public Image durabilityBar;

    public GameObject modulePanel;
    public Image moduleIcon;
    public Image usesBar;
    public TMP_Text usesText;
    public Sprite[] abilityIcons;

    private BatterySystem battery;
    private PlayerModuleManager modules;

    AbilityType lastActiveExternalModule;

    public void Vincular(PlayerController player)
    {
        Desvincular();

        battery = player.GetComponent<BatterySystem>();
        modules = player.GetComponent<PlayerModuleManager>();

        if (modules.HasActiveModule)
        {
            lastActiveExternalModule = modules.activeExternalModule.ability;
            UpdateModuleDisplay(modules.activeExternalModule.ability);
            UpdateUsesDisplay(modules.activeExternalModule.currentUses, modules.activeExternalModule.maxUses);
        }
        else
        {
            OnModuleBroken();
        }
    }

    public void Desvincular()
    {
        battery = null;
        modules = null;
        lastActiveExternalModule = AbilityType.None;
    }

    private void LateUpdate()
    {
        if (battery != null)
        {
            UpdateBatteryDisplay(battery.CurrentBattery, battery.MaxBattery);
            UpdateDurabilityDisplay(battery.CurrentDurability, battery.MaxDurability);
        }

        if (modules != null && modules.HasActiveModule)
        {
            UpdateUsesDisplay(modules.activeExternalModule.currentUses, modules.activeExternalModule.maxUses);

            if (lastActiveExternalModule != modules.activeExternalModule.ability)
            {
                UpdateModuleDisplay(modules.activeExternalModule.ability);
            }

            lastActiveExternalModule = modules.activeExternalModule.ability;
        }
        else
        {
            OnModuleBroken();
            lastActiveExternalModule = AbilityType.None;
        }
    }

    public void UpdateBatteryDisplay(float current, float max)
    {
        if (batteryBar != null)
            batteryBar.fillAmount = max > 0f ? current / max : 0f;
    }

    public void UpdateDurabilityDisplay(float current, float max)
    {
        if (durabilityBar != null)
            durabilityBar.fillAmount = max > 0f ? current / max : 0f;
    }

    public void UpdateUsesDisplay(int current, int max)
    {
        if (usesBar != null)
            usesBar.fillAmount = max > 0 ? Mathf.Clamp01((float)current / max) : 0f;

        if (usesText != null)
            usesText.text = current + "/" + max;
    }

    public void UpdateModuleDisplay(AbilityType type)
    {
        if (modulePanel != null)
            modulePanel.SetActive(type != AbilityType.None);

        // TODO: Mejorar este sistema.
        if (type == AbilityType.DoubleJump)
            moduleIcon.sprite = abilityIcons[0];
    }

    public void OnModuleBroken()
    {
        if (modulePanel != null)
            modulePanel.SetActive(false);
    }

    private void OnDestroy() => Desvincular();
}
