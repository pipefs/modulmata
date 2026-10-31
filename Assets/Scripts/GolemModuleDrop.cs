using System;
using UnityEngine;

// Añade a un gólem común una tirada ponderada de módulo externo cuando muere.
[DisallowMultipleComponent]
[RequireComponent(typeof(GolemController))]
public sealed class GolemModuleDrop : MonoBehaviour
{
    [Serializable]
    private sealed class ModuleOption
    {
        public ModulePickup prefab;
        [Min(0f)] public float weight = 1f;
    }

    [SerializeField, Range(0f, 100f)] private float dropPercentage = 80f;
    [SerializeField] private ModuleOption[] modules;

    private GolemController golem;
    private float totalWeight;
    private bool subscribed;

    private void Awake()
    {
        golem = GetComponent<GolemController>();

        if (!ValidateConfiguration())
        {
            enabled = false;
        }
    }

    private bool ValidateConfiguration()
    {
        if (golem == null || golem is GolemBossController)
        {
            return Fail("GolemModuleDrop necesita un GolemController común en el mismo objeto.");
        }

        if (dropPercentage < 0f || dropPercentage > 100f || float.IsNaN(dropPercentage))
        {
            return Fail("La probabilidad de GolemModuleDrop debe estar entre 0 y 100.");
        }

        if (modules == null || modules.Length == 0)
        {
            return Fail("GolemModuleDrop necesita al menos un prefab de módulo externo.");
        }

        totalWeight = 0f;

        for (int i = 0; i < modules.Length; i++)
        {
            ModuleOption option = modules[i];

            if (option == null || option.prefab == null || option.prefab.data == null)
            {
                return Fail($"La opción de módulo {i} está incompleta.");
            }

            ModuleData data = option.prefab.data;
            if (data.type != ModuleType.External || data.ability == AbilityType.None || data.maxUses <= 0)
            {
                return Fail($"El prefab '{option.prefab.name}' debe contener un módulo externo válido y con usos.");
            }

            if (option.weight < 0f || float.IsInfinity(option.weight) || float.IsNaN(option.weight))
            {
                return Fail($"El peso de '{option.prefab.name}' no es válido.");
            }

            totalWeight += option.weight;
        }

        if (!(totalWeight > 0f) || float.IsInfinity(totalWeight))
        {
            return Fail("GolemModuleDrop necesita al menos una opción con peso mayor que cero.");
        }

        return true;
    }

    private void OnEnable()
    {
        if (golem == null || subscribed)
        {
            return;
        }

        golem.Died += HandleGolemDeath;
        subscribed = true;
    }

    // Cada muerte realiza una tirada nueva; una tirada exitosa genera como máximo un módulo.
    private void HandleGolemDeath(Vector3 dropPosition)
    {
        if (!isActiveAndEnabled || !RollPercentage(dropPercentage))
        {
            return;
        }

        ModulePickup prefab = ChooseModule();
        if (prefab == null)
        {
            return;
        }

        ModulePickup pickup = Instantiate(prefab, dropPosition, prefab.transform.rotation);
        golem.ReportSpawnedReward(pickup.gameObject);
    }

    private ModulePickup ChooseModule()
    {
        float choice = UnityEngine.Random.value * totalWeight;
        ModulePickup fallback = null;

        foreach (ModuleOption option in modules)
        {
            if (option == null || option.prefab == null || option.weight <= 0f)
            {
                continue;
            }

            fallback = option.prefab;
            choice -= option.weight;

            if (choice <= 0f)
            {
                return option.prefab;
            }
        }

        return fallback;
    }

    private static bool RollPercentage(float percentage)
    {
        return percentage >= 100f || (percentage > 0f && UnityEngine.Random.value * 100f < percentage);
    }

    private bool Fail(string message)
    {
        Debug.LogError(message, this);
        return false;
    }

    private void OnDisable()
    {
        if (golem != null && subscribed)
        {
            golem.Died -= HandleGolemDeath;
        }

        subscribed = false;
    }
}
