using UnityEngine;

// Identifica las habilidades que pueden conceder los módulos del jugador.
public enum AbilityType
{
    None = 0,
    DoubleJump = 1,
    EnergyCannon = 2
}
// Distingue mejoras permanentes de módulos externos con usos limitados.
public enum ModuleType
{
    External,
    Internal
}

[System.Serializable]
// Contiene la configuración y el estado de usos de un módulo sin depender de un objeto de escena.
public class ModuleData
{
    public int currentUses { get; private set; }

    public string name;
    public AbilityType ability;
    public ModuleType type;
    public int maxUses = 4;
    public float batteryRestoreAmount = 30;
    public Sprite icon;

    /*public ModuleData(ModuleType type, AbilityType ability, int maxUses, int batteryRestoreAmount)
    {
        this.type = type;
        this.ability = ability;
        this.maxUses = type == ModuleType.Internal ? -1 : Mathf.Max(0, maxUses);
        currentUses = this.maxUses;
        this.batteryRestoreAmount = type == ModuleType.Internal ? 0f : Mathf.Max(0f, batteryRestoreAmount);
    }*/

    // Copiar estado nunca repone usos agotados.
    public ModuleData(ModuleData other)
    {
        name = other.name;
        type = other.type;
        ability = other.ability;
        maxUses = other.maxUses;
        currentUses = other.currentUses;
        batteryRestoreAmount = other.batteryRestoreAmount;
        icon = other.icon;
    }

    public ModuleData CreatePickupInstance()
    {
        var instance = new ModuleData(this);
        instance.currentUses = type == ModuleType.Internal ? -1 : Mathf.Max(0, maxUses);
        return instance;
    }

    public bool TryUse()
    {
        if (ability == AbilityType.None)
        {
            return false;
        }

        if (type == ModuleType.Internal)
        {
            return true;
        }

        if (currentUses <= 0)
        {
            return false;
        }

        currentUses--;
        return true;
    }
}
