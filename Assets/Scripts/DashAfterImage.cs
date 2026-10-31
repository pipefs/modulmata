using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Captura los sprites actuales del personaje para producir la estela temporal de un dash.
public class DashAfterImage : MonoBehaviour
{
    [SerializeField]
    private Material ghostMaterial;
    private float nextGhostTime;
    private SpriteRenderer[] sourceRenderers;

    public void Begin(float duration, float interval, float lifetime, Color color, int sortingOrderOffset = -1)
    {
        Stop();

        if (ghostMaterial == null || !(duration > 0f) || !(interval > 0f) || !(lifetime > 0f))
        {
            return;
        }

        // Renovar en cada dash permite cambiar la representación entre ejecuciones.
        sourceRenderers = GetComponentsInChildren<SpriteRenderer>(true);
        nextGhostTime = Time.time;
        StartCoroutine(EmitGhosts(duration, interval, lifetime, color, sortingOrderOffset));
    }

    public void Stop()
    {
        StopAllCoroutines();
    }

    private IEnumerator EmitGhosts(float duration, float interval, float lifetime, Color color, int sortingOrderOffset)
    {
        float endsAt = Time.time + duration;

        while (Time.time < endsAt)
        {
            if (Time.time >= nextGhostTime)
            {
                CreateGhost(lifetime, color, sortingOrderOffset);
                nextGhostTime = Time.time + interval;
            }

            yield return null;
        }
    }

    private void CreateGhost(float lifetime, Color color, int sortingOrderOffset)
    {
        if (ghostMaterial == null)
        {
            return;
        }

        GameObject ghostRoot = new GameObject("Eco de Dash");
        List<SpriteRenderer> ghostParts = new List<SpriteRenderer>();

        foreach (SpriteRenderer source in sourceRenderers)
        {
            if (source == null || !source.gameObject.activeInHierarchy || !source.enabled || source.sprite == null)
            {
                continue;
            }

            GameObject part = new GameObject(source.gameObject.name);
            part.transform.SetParent(ghostRoot.transform, false);
            part.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
            part.transform.localScale = source.transform.lossyScale;

            SpriteRenderer renderer = part.AddComponent<SpriteRenderer>();
            renderer.sprite = source.sprite;
            renderer.flipX = source.flipX;
            renderer.flipY = source.flipY;
            renderer.sortingLayerID = source.sortingLayerID;
            renderer.sortingOrder = source.sortingOrder + sortingOrderOffset;
            renderer.color = color;
            renderer.sharedMaterial = ghostMaterial;
            ghostParts.Add(renderer);
        }

        DashGhostFade fade = ghostRoot.AddComponent<DashGhostFade>();
        fade.Initialize(ghostParts, color, Mathf.Max(0.01f, lifetime));
    }

    private void OnDisable()
    {
        Stop();
    }
}

// Desvanece una copia visual independiente y la destruye al terminar su vida útil.
public class DashGhostFade : MonoBehaviour
{
    private List<SpriteRenderer> renderers;
    private Color baseColor;
    private float lifetime;
    private float age;

    public void Initialize(List<SpriteRenderer> ghostRenderers, Color color, float duration)
    {
        renderers = ghostRenderers;
        baseColor = color;
        lifetime = duration;
    }

    private void Update()
    {
        if (renderers == null)
        {
            return;
        }

        age += Time.deltaTime;
        float alpha = Mathf.Lerp(baseColor.a, 0f, age / lifetime);

        foreach (SpriteRenderer renderer in renderers)
        {
            if (renderer != null)
            {
                renderer.color = new Color(baseColor.r, baseColor.g, baseColor.b, alpha);
            }
        }

        if (age >= lifetime)
        {
            Destroy(gameObject);
        }
    }
}
