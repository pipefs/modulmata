using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SpriteFlash : MonoBehaviour
{
    private readonly Dictionary<SpriteRenderer, Color> originalColors = new();
    private readonly Dictionary<SpriteRenderer, Material> originalMaterials = new();
    private Coroutine flashRoutine;
    private Material flashMaterial;

    public bool IsFlashing => flashRoutine != null;

    public void Play(Color flashColor, float duration)
    {
        StopAndRestore();
        flashRoutine = StartCoroutine(Flash(flashColor, duration));
    }

    public void StopAndRestore()
    {
        if (flashRoutine != null)
            StopCoroutine(flashRoutine);

        RestoreColors();
        flashRoutine = null;
    }

    private IEnumerator Flash(Color flashColor, float duration)
    {
        originalColors.Clear();

        foreach (var sprite in GetComponentsInChildren<SpriteRenderer>(true))
        {
            if (sprite == null)
                continue;

            originalColors[sprite] = sprite.color;
            originalMaterials[sprite] = sprite.sharedMaterial;
            sprite.sharedMaterial = GetFlashMaterial(flashColor);
        }

        yield return new WaitForSeconds(duration);

        RestoreColors();
        flashRoutine = null;
    }

    private void RestoreColors()
    {
        foreach (var pair in originalColors)
        {
            if (pair.Key != null)
                pair.Key.color = pair.Value;
        }

        originalColors.Clear();

        foreach (var pair in originalMaterials)
        {
            if (pair.Key != null)
                pair.Key.sharedMaterial = pair.Value;
        }

        originalMaterials.Clear();
    }

    private Material GetFlashMaterial(Color flashColor)
    {
        if (flashMaterial == null)
        {
            Shader shader = Shader.Find("MODULMATA/Sprite Flash");

            if (shader == null)
                shader = Shader.Find("Sprites/Default");

            flashMaterial = new Material(shader);
        }

        if (flashMaterial.HasProperty("_FlashColor"))
            flashMaterial.SetColor("_FlashColor", flashColor);
        else
            flashMaterial.color = flashColor;

        return flashMaterial;
    }

    private void OnDisable()
    {
        StopAndRestore();
    }

    private void OnDestroy()
    {
        if (flashMaterial != null)
            Destroy(flashMaterial);
    }
}
