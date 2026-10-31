using System;
using System.Collections.Generic;
using UnityEngine;

// Conserva módulos internos y el único módulo externo equipado, incluidos sus usos restantes.
[RequireComponent(typeof(BatterySystem))]
public class PlayerModuleManager : MonoBehaviour
{
    private BatterySystem batterySystem;
    // Mantener nombres y serialización del prefab; el exterior solo obtiene lectura.
    [SerializeField] private List<ModuleData> internalModules = new();
    [SerializeField] private ModuleData activeExternalModule;
    public IReadOnlyList<ModuleData> InternalModules => internalModules.AsReadOnly();
    public ModuleData ActiveExternalModule => activeExternalModule;
    public event Action ExternalModuleChanged;
    public bool HasActiveModule => activeExternalModule != null &&
        activeExternalModule.ability != AbilityType.None && activeExternalModule.currentUses > 0;

    private void Awake()
    {
        batterySystem = GetComponent<BatterySystem>();
        // Awake no garantiza que la dependencia ya haya recibido OnEnable.
        if (batterySystem == null || !batterySystem.enabled)
        {
            Debug.LogError("PlayerModuleManager necesita un BatterySystem activo en el mismo objeto.", this);
            enabled = false;
            return;
        }
        if (activeExternalModule != null && activeExternalModule.ability != AbilityType.None)
        {
            activeExternalModule = activeExternalModule.CreatePickupInstance();
        }
        else
        {
            activeExternalModule = null;
        }
    }

    public bool UnlockInternalModule(ModuleData module)
    {
        if (!isActiveAndEnabled || batterySystem == null || module == null || module.type != ModuleType.Internal ||
            module.ability == AbilityType.None || module.ability == AbilityType.EnergyCannon)
        {
            return false;
        }

        bool added = AddInternal(module);
        GameManager.Instance?.UnlockInternalModule(module);
        return added;
    }

    private bool AddInternal(ModuleData module)
    {
        if (module.ability == AbilityType.EnergyCannon || internalModules.Exists(m => m != null && m.ability == module.ability))
        {
            return false;
        }

        internalModules.Add(new ModuleData(module));
        return true;
    }

    public void RestoreInternalModules(IReadOnlyList<ModuleData> modules)
    {
        if (!isActiveAndEnabled || batterySystem == null || modules == null)
        {
            return;
        }

        foreach (var module in modules)
        {
            if (module != null)
            {
                AddInternal(module);
            }
        }
    }

    // Acepta una copia del recogible solo si la ranura está libre y su habilidad tiene soporte activo.
    public bool EquipExternalFromPickup(ModuleData pickup)
    {
        if (!isActiveAndEnabled || batterySystem == null || pickup == null || pickup.type != ModuleType.External ||
            pickup.ability == AbilityType.None || pickup.currentUses <= 0 || HasActiveModule)
        {
            return false;
        }

        if (pickup.ability == AbilityType.EnergyCannon)
        {
            var cannon = GetComponent<PlayerCannon>();
            if (cannon == null || !cannon.IsReady)
            {
                Debug.LogError("No se puede equipar EnergyCannon: configura un PlayerCannon activo en el jugador.", this);
                return false;
            }
        }
        activeExternalModule = new ModuleData(pickup);
        ExternalModuleChanged?.Invoke();
        return true;
    }

    public bool HasAbility(AbilityType ability)
    {
        if (ability == AbilityType.None)
        {
            return false;
        }

        return (ability != AbilityType.EnergyCannon && internalModules.Exists(m => m != null && m.ability == ability)) ||
            (HasActiveModule && activeExternalModule.ability == ability);
    }

    // Las habilidades internas no se consumen; las externas descuentan un uso y se retiran al agotarse.
    public bool TryUseAbility(AbilityType ability)
    {
        if (!isActiveAndEnabled || batterySystem == null)
        {
            return false;
        }

        if (ability == AbilityType.None)
        {
            return false;
        }

        if (ability != AbilityType.EnergyCannon && internalModules.Exists(m => m != null && m.ability == ability))
        {
            return true;
        }

        if (!HasActiveModule || activeExternalModule.ability != ability || !activeExternalModule.TryUse())
        {
            return false;
        }

        if (activeExternalModule.currentUses == 0)
        {
            activeExternalModule = null;
        }

        ExternalModuleChanged?.Invoke();
        return true;
    }

    public void UseAbility(AbilityType ability)
    {
        TryUseAbility(ability);
    }

    // Convierte los usos restantes en batería proporcional y elimina el módulo aunque la carga ya esté llena.
    public void SacrificeCurrentExternalModule()
    {
        if (!isActiveAndEnabled || !HasActiveModule || activeExternalModule.maxUses <= 0 || batterySystem == null)
        {
            return;
        }

        float amount = activeExternalModule.batteryRestoreAmount *
            ((float)activeExternalModule.currentUses / activeExternalModule.maxUses);
        if (batterySystem.IsDepleted)
        {
            return;
        }

        batterySystem.AddBattery(amount);
        // Se conserva la regla actual: sacrificar consume el módulo incluso con batería llena.
        LoseExternalModule();
    }

    public void LoseExternalModule()
    {
        if (activeExternalModule == null)
        {
            return;
        }

        activeExternalModule = null;
        ExternalModuleChanged?.Invoke();
    }
}
