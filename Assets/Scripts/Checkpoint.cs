using UnityEngine;

/// <summary>A movable road gate. Index zero is always start/finish.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCollider))]
public class Checkpoint : MonoBehaviour
{
    [SerializeField] private RaceManager raceManager;
    [SerializeField, Min(0)] private int index;

    public int Index => index;
    public RaceManager Manager => raceManager;

    private void Reset()
    {
        var trigger = GetComponent<BoxCollider>();
        trigger.isTrigger = true;
        trigger.size = new Vector3(12f, 6f, 2f);
        trigger.center = new Vector3(0f, 2.5f, 0f);
    }

    private void OnValidate()
    {
        index = Mathf.Max(0, index);
        GetComponent<BoxCollider>().isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (raceManager != null && other.attachedRigidbody != null)
            raceManager.TryPassCheckpoint(this, other.attachedRigidbody);
    }

    // A car may already overlap the start gate when the countdown unlocks progression.
    private void OnTriggerStay(Collider other) => OnTriggerEnter(other);

    private void OnDrawGizmos()
    {
        var trigger = GetComponent<BoxCollider>();
        if (trigger == null) return;
        Color color = index == 0 ? Color.green : Color.cyan;
        if (Application.isPlaying && raceManager != null &&
            raceManager.NextExpectedCheckpoint == index) color = Color.yellow;
        Matrix4x4 previousMatrix = Gizmos.matrix;
        Color previousColor = Gizmos.color;
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = color;
        Gizmos.DrawWireCube(trigger.center, trigger.size);
        Vector3 origin = Vector3.up * 0.3f;
        Vector3 tip = origin + Vector3.forward * 3f;
        Gizmos.DrawLine(origin, tip);
        Gizmos.DrawLine(tip, tip + new Vector3(-0.6f, 0f, -0.8f));
        Gizmos.DrawLine(tip, tip + new Vector3(0.6f, 0f, -0.8f));
        Gizmos.matrix = previousMatrix;
        Gizmos.color = previousColor;
#if UNITY_EDITOR
        UnityEditor.Handles.Label(transform.TransformPoint(trigger.center + Vector3.up * trigger.size.y * 0.5f),
            index == 0 ? "0: Start / Finish" : "Checkpoint " + index);
#endif
    }
}
