using System.Collections;
using UnityEngine;
using UnityEngine.UI;

// Presentación opcional. GameManager conserva el control de la reaparición.
public sealed class TVOffTransition : MonoBehaviour
{
    [SerializeField, Tooltip("Image de pantalla completa, sin sprite, en un Canvas por encima del HUD. Asignar el material MODULMATA/TV Off en el Image.")]
    private Image overlay;
    [SerializeField, Min(0.01f), Tooltip("Duración del cierre y contracción de la línea, sin contar la pausa y el desvanecimiento del punto.")]
    private float powerOffDuration = 0.4f;
    [SerializeField, Min(0f)] private float pointHoldDuration = 0.12f;
    [SerializeField, Min(0.01f)] private float pointFadeDuration = 0.16f;

    private static readonly int ProgressId = Shader.PropertyToID("_Progress");
    private static readonly int AspectId = Shader.PropertyToID("_Aspect");
    private Material originalMaterial;
    private Material runtimeMaterial;
    private Canvas canvas;
    private int animationVersion;
    private float progress;

    public bool IsReady => isActiveAndEnabled && overlay != null && overlay.gameObject.activeInHierarchy &&
        canvas != null && canvas.isActiveAndEnabled && runtimeMaterial != null &&
        overlay.material == runtimeMaterial;
    public bool IsBlack => IsReady && overlay.enabled && progress >= 1f;

    private void Awake()
    {
        canvas = overlay != null ? overlay.GetComponentInParent<Canvas>() : null;
        originalMaterial = overlay != null ? overlay.material : null;
        if (overlay == null || canvas == null)
        {
            Debug.LogError("TVOffTransition necesita un Overlay Image dentro de un Canvas. Se omite el efecto.", this);
            enabled = false;
            return;
        }
        // El shader propio define Progress y Aspect; su identidad garantiza esas propiedades.
        var shader = originalMaterial != null ? originalMaterial.shader : null;
        if (shader == null || !shader.isSupported || shader.name != "MODULMATA/TV Off")
        {
            Debug.LogError("TVOffTransition necesita un material compatible con MODULMATA/TV Off asignado al Image. Se omite el efecto.", this);
            enabled = false;
            return;
        }
        // Parámetros propios de esta transición; nunca modificar el material del proyecto.
        runtimeMaterial = new Material(originalMaterial);
        overlay.material = runtimeMaterial;
        overlay.color = Color.white;
        overlay.raycastTarget = false;
        ResetVisuals();
    }

    public IEnumerator PowerOff()
    {
        return Animate(powerOffDuration, pointHoldDuration);
    }

    // Recorre cierre, pausa del punto final y desvanecimiento antes de dejar la pantalla negra.
    private IEnumerator Animate(float duration, float pointHold)
    {
        if (!IsReady)
        {
            FailPlayback();
            yield break;
        }
        int version = ++animationVersion;
        overlay.enabled = true;
        SetProgress(0f);
        float elapsed = 0f;
        duration = Mathf.Max(0.01f, duration);
        pointHold = Mathf.Max(0f, pointHold);
        // Coincide con el instante en que TVOff.shader termina de contraer la línea.
        float pointTime = duration;
        float fadeDuration = Mathf.Max(0.01f, pointFadeDuration);
        while (elapsed < pointTime + pointHold + fadeDuration)
        {
            yield return null;
            if (version != animationVersion)
            {
                yield break;
            }

            if (!IsReady)
            {
                FailPlayback();
                yield break;
            }
            elapsed += Time.unscaledDeltaTime;
            float value = elapsed <= pointTime ? 0.84f * Mathf.Clamp01(elapsed / pointTime) :
                elapsed <= pointTime + pointHold ? 0.84f :
                Mathf.Lerp(0.84f, 1f, Mathf.Clamp01((elapsed - pointTime - pointHold) / fadeDuration));
            SetProgress(value);
        }
        SetProgress(1f);
    }

    private void SetProgress(float value)
    {
        progress = value;
        runtimeMaterial.SetFloat(ProgressId, value);
        runtimeMaterial.SetFloat(AspectId, (float)Screen.width / Mathf.Max(1, Screen.height));
    }

    private void FailPlayback()
    {
        Debug.LogError("TVOffTransition perdió su configuración o está inactivo. Se omite la presentación.", this);
        ResetVisuals();
        enabled = false;
    }

    public void ResetVisuals()
    {
        animationVersion++;
        progress = 0f;
        if (runtimeMaterial != null)
        {
            runtimeMaterial.SetFloat(ProgressId, 0f);
        }

        if (overlay != null)
        {
            overlay.enabled = false;
        }
    }

    private void OnDisable()
    {
        ResetVisuals();
    }

    private void OnDestroy()
    {
        ResetVisuals();
        if (overlay != null && runtimeMaterial != null && overlay.material == runtimeMaterial)
        {
            overlay.material = originalMaterial;
        }

        if (runtimeMaterial != null)
        {
            Destroy(runtimeMaterial);
        }
    }
}
