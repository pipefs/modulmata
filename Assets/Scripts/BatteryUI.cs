using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Refleja en el HUD los recursos y el módulo externo del jugador registrado por GameManager.
public class BatteryUI : MonoBehaviour
{
    public Image batteryBar;
    public Image durabilityBar;
    public GameObject modulePanel;
    public Image moduleIcon;
    public Image usesBar;
    public TMP_Text moduleNameText;

    private GameManager manager;
    private BatterySystem battery;
    private PlayerModuleManager modules;

    private void OnEnable()
    {
        Connect();
    }

    private void Connect()
    {
        var current = GameManager.Instance;

        if (ReferenceEquals(manager, current))
        {
            return;
        }

        if (!ReferenceEquals(manager, null))
        {
            manager.PlayerChanged -= Link;
        }

        manager = current;

        if (manager != null)
        {
            manager.PlayerChanged += Link;
        }

        Link(manager != null ? manager.CurrentPlayer : null);
    }

    // Cambia todas las fuentes del HUD cuando GameManager registra una nueva instancia del jugador.
    public void Link(PlayerController player)
    {
        battery = player != null ? player.GetComponent<BatterySystem>() : null;
        modules = player != null ? player.GetComponent<PlayerModuleManager>() : null;
        Refresh();
    }

    public void Unlink()
    {
        Link(null);
    }

    private void LateUpdate()
    {
        // También cubre una UI creada antes del manager o un manager reemplazado.
        Connect();
        Refresh();
    }

    // Lee el estado actual en un solo punto para mantener barras, icono y usos sincronizados.
    private void Refresh()
    {
        UpdateBatteryDisplay(battery != null ? battery.CurrentBattery : 0f, battery != null ? battery.MaxBattery : 1f);
        UpdateDurabilityDisplay(battery != null ? battery.CurrentDurability : 0f, battery != null ? battery.MaxDurability : 1f);

        ModuleData module = modules != null && modules.HasActiveModule ? modules.ActiveExternalModule : null;
        bool visible = module != null;

        if (modulePanel != null && modulePanel.activeSelf != visible)
        {
            modulePanel.SetActive(visible);
        }

        if (!visible)
        {
            return;
        }

        UpdateUsesDisplay(module.currentUses, module.maxUses, module.name);

        if (moduleIcon != null)
        {
            Sprite icon = module.icon;

            if (icon != null && moduleIcon.sprite != icon)
            {
                moduleIcon.sprite = icon;
            }
        }
    }

    public void UpdateBatteryDisplay(float current, float max)
    {
        if (batteryBar != null)
        {
            batteryBar.fillAmount = max > 0f ? Mathf.Clamp01(current / max) : 0f;
        }
    }

    public void UpdateDurabilityDisplay(float current, float max)
    {
        if (durabilityBar != null)
        {
            durabilityBar.fillAmount = max > 0f ? Mathf.Clamp01(current / max) : 0f;
        }
    }

    public void UpdateUsesDisplay(int current, int max, string moduleName)
    {
        if (usesBar != null)
        {
            usesBar.fillAmount = max > 0 ? Mathf.Clamp01((float)current / max) : 0f;
        }

        if (moduleNameText != null)
        {
            moduleNameText.text = moduleName;
        }
    }

    public void UpdateModuleDisplay(AbilityType type)
    {
        if (modulePanel != null)
        {
            modulePanel.SetActive(type != AbilityType.None);
        }
    }

    public void OnModuleBroken()
    {
        if (modulePanel != null)
        {
            modulePanel.SetActive(false);
        }
    }

    private void OnDisable()
    {
        if (!ReferenceEquals(manager, null))
        {
            manager.PlayerChanged -= Link;
        }

        manager = null;
        Unlink();
    }
}
