using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DashAfterImage : MonoBehaviour
{
    private Material ghostMaterial;
    private float nextGhostTime;

    public void Begin(float duration, float interval, float lifetime, Color color, int sortingOrderOffset = -1)
    {
        Stop();
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
        var ghostRoot = new GameObject("Eco de Dash");
        var ghostParts = new List<SpriteRenderer>();

        foreach (var source in GetComponentsInChildren<SpriteRenderer>())
        {
            if (!source.enabled || source.sprite == null)
                continue;

            var part = new GameObject(source.gameObject.name);
            part.transform.SetParent(ghostRoot.transform, false);
            part.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
            part.transform.localScale = source.transform.lossyScale;

            var renderer = part.AddComponent<SpriteRenderer>();
            renderer.sprite = source.sprite;
            renderer.flipX = source.flipX;
            renderer.flipY = source.flipY;
            renderer.sortingLayerID = source.sortingLayerID;
            renderer.sortingOrder = source.sortingOrder + sortingOrderOffset;
            renderer.color = color;
            renderer.sharedMaterial = GetGhostMaterial();
            ghostParts.Add(renderer);
        }

        var fade = ghostRoot.AddComponent<DashGhostFade>();
        fade.Initialize(ghostParts, color, lifetime);
    }

    private Material GetGhostMaterial()
    {
        if (ghostMaterial != null)
            return ghostMaterial;

        Shader shader = Shader.Find("MODULMATA/DashAfterImage");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");

        ghostMaterial = new Material(shader);
        return ghostMaterial;
    }

    private void OnDestroy()
    {
        if (ghostMaterial != null)
            Destroy(ghostMaterial);
    }
}

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
        age += Time.deltaTime;
        float alpha = Mathf.Lerp(baseColor.a, 0f, age / lifetime);

        foreach (var renderer in renderers)
        {
            if (renderer != null)
                renderer.color = new Color(baseColor.r, baseColor.g, baseColor.b, alpha);
        }

        if (age >= lifetime)
            Destroy(gameObject);
    }
}
