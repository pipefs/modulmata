using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;


public class PlayerModuleManager : MonoBehaviour
{
    private BatterySystem batterySystem;

    // Modulos Internos
    public List<ModuleData> internalModules = new();

    // Modulo Externo
    public ModuleData activeExternalModule;

    // Comprobar si hay un modulo activo en reemplazo del problema con la clase C# sin MonoBehaviour.
    public bool HasActiveModule => activeExternalModule != null && activeExternalModule.ability != AbilityType.None;

    private void Awake()
    {
        batterySystem = GetComponent<BatterySystem>();
    }

    private void Update()
    {
        GameManager manager = GameManager.Instance;

        if (manager == null || manager.IsDead || manager.HasWon || manager.CurrentPlayer != GetComponent<PlayerController>())
            return;

        if (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)
        {
            SacrificeCurrentExternalModule();
        }
    }

    // Los internos se guardan sin duplicar una habilidad que ya está desbloqueada.
    public void UnlockInternalModule(ModuleData newModule)
    {
        if (newModule == null || newModule.type != ModuleType.Internal)
        {
            return;
        }

        if (!internalModules.Exists(module => module != null && module.ability == newModule.ability))
        {
            internalModules.Add(newModule);
        }
    }

    public void EquipExternalFromPickup(ModuleData pickup)
    {
        if (pickup == null || pickup.type == ModuleType.Internal)
        {
            return;
        }

        activeExternalModule = pickup;
    }

    public bool HasAbility(AbilityType ability)
    {
        // Revisar si esta en los modulos internos.
        foreach (var mD in internalModules)
        {
            if (mD != null && mD.ability == ability && ability != AbilityType.None)
            {
                return true;
            }
        }

        // Revisar si está en el módulo externo activo y tiene usos.
        if (activeExternalModule != null && activeExternalModule.ability == ability)
        {
            return activeExternalModule.currentUses > 0;
        }

        return false;
    }

    // Los internos no consumen usos. Los externos se rompen cuando llegan a cero.
    public void UseAbility(AbilityType ability)
    {
        foreach (var mD in internalModules)
        {
            if (mD != null && mD.ability == ability)
                return;
        }

        if (activeExternalModule != null && activeExternalModule.ability == ability)
        {
            activeExternalModule.currentUses--;

            if (activeExternalModule.currentUses <= 0)
            {
                BreakExternalModule();
            }
        }
    }

    // Sacrificar el modulo externo activo por bateria.
    public void SacrificeCurrentExternalModule()
    {
        if (activeExternalModule == null ||
            activeExternalModule.currentUses <= 0 ||
            activeExternalModule.maxUses <= 0)
        {
            return;
        }

        // uR / uM * bRA = Bateria Recuperada
        float restoredAmount = activeExternalModule.batteryRestoreAmount * ((float)activeExternalModule.currentUses / activeExternalModule.maxUses);

        if (batterySystem != null)
        {
            batterySystem.AddBattery(restoredAmount);
        }

        BreakExternalModule();
    }

    // Eliminar el modulo.
    private void BreakExternalModule()
    {
        activeExternalModule = null;
    }

    public void LoseExternalModule()
    {
        // TODO: Terminar, la idea es que instancie un modulo usando un prefab, modificando el sprite y ModulePickup.cs del prefab.

        // Por ahora solo borrara el modulo.
        BreakExternalModule();
    }
}
