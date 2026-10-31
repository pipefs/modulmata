using UnityEngine;

// Mueve secciones ya pintadas; no crea objetos ni modifica las celdas del Tilemap.
public sealed class LoopingTilemap : MonoBehaviour
{
    [SerializeField] private Transform[] sections;
    [SerializeField, Min(0.01f)] private float sectionHeight = 24f;
    [SerializeField] private float speed = 8f;
    private Vector3[] initialPositions;
    private float offset;
    private float bottom;

    private void Awake()
    {
        initialPositions = new Vector3[sections.Length];
        bottom = float.PositiveInfinity;

        for (int i = 0; i < sections.Length; i++)
        {
            initialPositions[i] = sections[i].localPosition;
            bottom = Mathf.Min(bottom, initialPositions[i].y);
        }

        // Cada origen ocupa una altura exacta dentro del ciclo, independientemente del orden del array.
        for (int i = 0; i < sections.Length; i++)
        {
            float slot = (initialPositions[i].y - bottom) / sectionHeight;
        }

    }

    private void Update()
    {
        float span = sectionHeight * sections.Length;
        offset = Mathf.Repeat(offset + speed * Time.deltaTime, span);
        for (int i = 0; i < sections.Length; i++)
        {
            if (sections[i] == null)
            {
                Fail("LoopingTilemap perdió una sección.");
                return;
            }
            Vector3 position = initialPositions[i];
            position.y = bottom + Mathf.Repeat(position.y - bottom + offset, span);
            sections[i].localPosition = position;
        }
    }

    private void Fail(string message)
    {
        Debug.LogError(message, this);
        enabled = false;
    }
}
