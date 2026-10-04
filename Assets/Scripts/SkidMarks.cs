using UnityEngine;

/// <summary>
/// Leaves dark rear-tyre marks while a rear wheel is clearly sliding or handbraking.
/// Reads wheel state only; it never changes handling or physics.
/// </summary>
[DisallowMultipleComponent]
public class SkidMarks : MonoBehaviour
{
    [SerializeField] private CarController car;
    [SerializeField] private Rigidbody body;
    [SerializeField] private WheelCollider[] rearWheels;
    [Tooltip("Dark semi-transparent material asset (kept as an asset so it is included in builds).")]
    [SerializeField] private Material markMaterial;

    [Tooltip("Rear |sidewaysSlip| above this leaves marks. Normal cornering stays below ~0.2.")]
    [SerializeField, Min(0f)] private float sidewaysSlipThreshold = 0.3f;
    [SerializeField, Min(0f)] private float handbrakeMinSpeed = 3f;
    [SerializeField, Min(0.01f)] private float markWidth = 0.22f;
    [SerializeField, Min(0.1f)] private float markLifetime = 7f;
    [Tooltip("Lift along the surface normal so marks don't flicker into the road.")]
    [SerializeField, Min(0f)] private float surfaceOffset = 0.02f;

    private TrailRenderer[] activeTrails;

    public bool IsMarking(int wheel) => activeTrails != null && activeTrails[wheel] != null;

    private void Awake()
    {
        if (car == null) car = GetComponent<CarController>();
        if (body == null) body = GetComponent<Rigidbody>();
        activeTrails = new TrailRenderer[rearWheels != null ? rearWheels.Length : 0];
    }

    private void LateUpdate()
    {
        for (int i = 0; i < activeTrails.Length; i++)
        {
            WheelCollider wheel = rearWheels[i];
            if (wheel == null || markMaterial == null || !wheel.GetGroundHit(out WheelHit hit) || !IsSkidding(hit))
            {
                EndTrail(i);
                continue;
            }

            Vector3 point = hit.point + hit.normal * surfaceOffset;
            // A big jump (e.g. R reset) must not draw a line across the track.
            if (activeTrails[i] != null && (activeTrails[i].transform.position - point).sqrMagnitude > 4f) EndTrail(i);
            // TransformZ alignment: the ribbon faces its Z axis, so pointing Z along the normal lays it flat.
            Quaternion flat = Quaternion.LookRotation(hit.normal, transform.forward);
            if (activeTrails[i] == null) activeTrails[i] = StartTrail(point, flat);
            else activeTrails[i].transform.SetPositionAndRotation(point, flat);
        }
    }

    private bool IsSkidding(WheelHit hit)
    {
        if (Mathf.Abs(hit.sidewaysSlip) > sidewaysSlipThreshold) return true;
        return car != null && car.IsHandbraking && body != null && body.linearVelocity.magnitude > handbrakeMinSpeed;
    }

    // Each skid gets its own trail, so separate slides are never joined by a straight line.
    private TrailRenderer StartTrail(Vector3 point, Quaternion rotation)
    {
        var mark = new GameObject("SkidMark");
        mark.transform.SetPositionAndRotation(point, rotation);
        var trail = mark.AddComponent<TrailRenderer>();
        trail.sharedMaterial = markMaterial;
        trail.alignment = LineAlignment.TransformZ;
        trail.time = markLifetime;
        trail.widthMultiplier = markWidth;
        trail.minVertexDistance = 0.1f;
        trail.numCapVertices = 0;
        trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        trail.receiveShadows = false;
        trail.autodestruct = false;
        return trail;
    }

    private void EndTrail(int i)
    {
        if (activeTrails[i] == null) return;
        activeTrails[i].emitting = false;
        Destroy(activeTrails[i].gameObject, markLifetime + 0.5f);
        activeTrails[i] = null;
    }

    private void OnDisable()
    {
        if (activeTrails == null) return;
        for (int i = 0; i < activeTrails.Length; i++) EndTrail(i);
    }
}
