using UnityEngine;
using UnityEngine.UI;

// Presentación opcional del menú; no procesa la imagen de la cámara.
public sealed class MenuFallEffect : MonoBehaviour
{
    [SerializeField, Tooltip("Image sin sprite, a pantalla completa, con material MODULMATA/Menu Fall y Canvas Screen Space - Overlay.")]
    private Image overlay;
    private static readonly int TimeId = Shader.PropertyToID("_EffectTime");
    private static readonly int AspectId = Shader.PropertyToID("_Aspect");
    private static readonly int FocusId = Shader.PropertyToID("_Focus");
    private static readonly int VisibilityId = Shader.PropertyToID("_Visibility");
    private Material originalMaterial;
    private Material runtimeMaterial;
    private Canvas canvas;
    [SerializeField, Tooltip("Opcional: robot decorativo alrededor del cual se despejan las líneas.")]
    private Transform focusTarget;
    [SerializeField, Tooltip("Cámara real del menú, para localizar el robot en pantalla.")]
    private Camera worldCamera;
    private float elapsed;

    private void Awake()
    {
        canvas = overlay != null ? overlay.GetComponentInParent<Canvas>() : null;
        if (overlay == null || canvas == null || canvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay)
        {
            Fail("MenuFallEffect necesita un Image dentro de un Canvas Screen Space - Overlay.");
            return;
        }
        originalMaterial = overlay.material;
        var shader = originalMaterial != null ? originalMaterial.shader : null;
        if (shader == null || !shader.isSupported || shader.name != "MODULMATA/Menu Fall")
        {
            Fail("MenuFallEffect necesita el material MODULMATA/Menu Fall asignado al Image.");
            return;
        }
        runtimeMaterial = new Material(originalMaterial);
        overlay.material = runtimeMaterial;
        overlay.color = Color.white;
        overlay.raycastTarget = false;
        ApplyVisibility();
    }

    private void OnEnable()
    {
        ApplyVisibility();
    }

    private void LateUpdate()
    {
        if (overlay == null || canvas == null || runtimeMaterial == null || overlay.material != runtimeMaterial)
        {
            Fail("MenuFallEffect perdió su Image, Canvas o material. Se omite el efecto.");
            return;
        }
        if (!canvas.isActiveAndEnabled || !overlay.gameObject.activeInHierarchy)
        {
            return;
        }

        elapsed += Time.deltaTime;
        Rect rect = overlay.rectTransform.rect;
        runtimeMaterial.SetFloat(TimeId, elapsed);
        runtimeMaterial.SetFloat(AspectId, Mathf.Max(0.01f, rect.width / Mathf.Max(0.01f, rect.height)));

        Vector2 focus = new Vector2(0.5f, 0.5f);
        if (focusTarget != null && worldCamera != null)
        {
            Vector3 screen = worldCamera.WorldToScreenPoint(focusTarget.position);
            if (screen.z > 0f && RectTransformUtility.ScreenPointToLocalPointInRectangle(
                overlay.rectTransform, screen, null, out var local))
            {
                focus = new Vector2((local.x - rect.xMin) / Mathf.Max(0.01f, rect.width),
                    (local.y - rect.yMin) / Mathf.Max(0.01f, rect.height));
            }
            else
            {
                focus = new Vector2(-2f, -2f);
            }
        }
        runtimeMaterial.SetVector(FocusId, new Vector4(focus.x, focus.y, 0f, 0f));
    }

    private void ApplyVisibility()
    {
        if (runtimeMaterial == null || overlay == null)
        {
            return;
        }

        runtimeMaterial.SetFloat(VisibilityId, 1f);
        overlay.enabled = true;
    }

    private void Fail(string message)
    {
        Debug.LogError(message, this);
        enabled = false;
        if (overlay != null)
        {
            overlay.enabled = false;
        }
    }

    private void OnDisable()
    {
        if (runtimeMaterial != null)
        {
            runtimeMaterial.SetFloat(VisibilityId, 0f);
        }

        if (overlay != null)
        {
            overlay.enabled = false;
        }
    }

    private void OnDestroy()
    {
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
