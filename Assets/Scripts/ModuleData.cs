public enum AbilityType
{
    None,
    DoubleJump,
}

public enum ModuleType
{
    External, // Usos limitados
    Internal, // Usos ilimitados
}

// ? Info relevante para el funcionamiento de los modulos power-up/habilidades.

[System.Serializable]
public class ModuleData
{
    // No se muestra en inspector y es publico, con posibilidad de obtener y cambiar su valor.
    public int currentUses { get; set; }

    public AbilityType ability;
    public ModuleType type;
    public int maxUses = 4;
    public float batteryRestoreAmount = 30;

    public ModuleData(ModuleType type, AbilityType ability, int maxUses, int batteryRestoreAmount)
    {
        this.type = type;
        this.ability = ability;
        this.maxUses = (type == ModuleType.Internal) ? -1 : maxUses;
        currentUses = (type == ModuleType.Internal) ? -1 : maxUses;
        this.batteryRestoreAmount = (type == ModuleType.Internal) ? 0 : batteryRestoreAmount;
    }

    // Constructor por copia para que ModulePickup mande una copia a PlayerModuleManager
    // ya que queremos solo clonar la data.
    public ModuleData(ModuleData other)
    {
        type = other.type;
        ability = other.ability;
        maxUses = other.maxUses;
        currentUses = (other.type == ModuleType.Internal) ? -1 : (other.currentUses > 0 ? other.currentUses : other.maxUses);
        batteryRestoreAmount = other.batteryRestoreAmount;
    }
}
