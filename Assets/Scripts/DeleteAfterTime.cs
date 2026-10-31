using UnityEngine;

public class DeleteAfterTime : MonoBehaviour
{
    public float deleteTime;

    private float timer;

    private void Update()
    {
        timer += Time.deltaTime;

        if (timer >= deleteTime)
        {
            Destroy(gameObject);
        }
    }
}
