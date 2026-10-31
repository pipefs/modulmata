using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Aplica temporalmente un material de destello a todos los sprites y restaura su presentación original.
public class SpriteFlash : MonoBehaviour
{
    [SerializeField, Tooltip("Material con shader MODULMATA/Sprite Flash. Sin asignación se omite el efecto.")]
    private Material sourceMaterial;
    private const float InitialAlpha = 0.9f;
    private static readonly int FlashColorId = Shader.PropertyToID("_FlashColor");
    private readonly Dictionary<SpriteRenderer, Color> originalColors = new();
    private readonly Dictionary<SpriteRenderer, Material> originalMaterials = new();
    private Coroutine flashRoutine;
    private Material flashMaterial;

    public bool IsFlashing => flashRoutine != null;
    public bool IsConfigured => sourceMaterial != null && sourceMaterial.HasProperty(FlashColorId);

    // Reinicia el efecto desde su intensidad máxima sin perder los materiales originales.
    public void Play(Color flashColor, float duration)
    {
        StopAndRestore();
        if (!isActiveAndEnabled || sourceMaterial == null || !(duration > 0f) || float.IsInfinity(duration))
        {
            return;
        }

        if (!sourceMaterial.HasProperty(FlashColorId))
        {
            Debug.LogError("SpriteFlash necesita un Source Material con la propiedad _FlashColor.", this);
            enabled = false;
            return;
        }
        // Instancia propia para no cambiar el color de otros personajes ni el asset.
        if (flashMaterial == null)
        {
            flashMaterial = new Material(sourceMaterial);
        }

        flashColor.a = InitialAlpha;
        flashMaterial.SetColor(FlashColorId, flashColor);
        flashRoutine = StartCoroutine(Flash(flashColor, duration));
    }

    public void StopAndRestore()
    {
        if (flashRoutine != null)
        {
            StopCoroutine(flashRoutine);
        }

        RestoreColors();
        flashRoutine = null;
    }

    // Reduce el alpha del color de flash y deja que el shader mezcle gradualmente el sprite original.
    private IEnumerator Flash(Color flashColor, float duration)
    {
        originalColors.Clear();

        foreach (var sprite in GetComponentsInChildren<SpriteRenderer>(true))
        {
            if (sprite == null)
            {
                continue;
            }

            originalColors[sprite] = sprite.color;
            originalMaterials[sprite] = sprite.sharedMaterial;
            sprite.sharedMaterial = flashMaterial;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            yield return null;
            elapsed += Time.deltaTime;
            flashColor.a = InitialAlpha * (1f - Mathf.Clamp01(elapsed / duration));
            flashMaterial.SetColor(FlashColorId, flashColor);
        }

        RestoreColors();
        flashRoutine = null;
    }

    private void RestoreColors()
    {
        foreach (var pair in originalColors)
        {
            if (pair.Key != null)
            {
                pair.Key.color = pair.Value;
            }
        }

        originalColors.Clear();

        foreach (var pair in originalMaterials)
        {
            if (pair.Key != null)
            {
                pair.Key.sharedMaterial = pair.Value;
            }
        }

        originalMaterials.Clear();
    }

    private void OnDisable()
    {
        StopAndRestore();
    }

    private void OnDestroy()
    {
        if (flashMaterial != null)
        {
            Destroy(flashMaterial);
        }
    }
}
