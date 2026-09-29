using UnityEngine;

/// <summary>
/// Arcade-style third-person follow camera inspired by Trackmania.
/// Smoothly follows behind and above the target in LateUpdate, looking slightly ahead.
/// </summary>
public class CarCamera : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("The car root Transform to follow (e.g. PlayerCar).")]
    [SerializeField] private Transform target;

    [Header("Camera Offset")]
    [Tooltip("Distance behind the target car along its backward vector.")]
    [SerializeField] private float distance = 6f;

    [Tooltip("Height above the target car.")]
    [SerializeField] private float height = 2.5f;

    [Tooltip("Distance ahead of the target along its forward vector to focus on.")]
    [SerializeField] private float lookAheadDistance = 4f;

    [Tooltip("Vertical offset for the look target point.")]
    [SerializeField] private float lookUpOffset = 0.5f;

    [Header("Smoothing")]
    [Tooltip("Time (in seconds) to smoothly reach the desired camera position.")]
    [SerializeField] private float positionSmoothing = 0.1f;

    [Tooltip("Speed multiplier for smoothly rotating toward the look target.")]
    [SerializeField] private float rotationSmoothing = 10f;

    private Vector3 currentVelocity;

    private void Start()
    {
        if (target != null)
        {
            // Initialize camera position and rotation immediately on start to prevent startup snap
            Vector3 desiredPosition = target.position - target.forward * distance + Vector3.up * height;
            transform.position = desiredPosition;

            Vector3 lookTarget = target.position + target.forward * lookAheadDistance + Vector3.up * lookUpOffset;
            Vector3 lookDirection = lookTarget - transform.position;
            if (lookDirection.sqrMagnitude > 0.001f)
            {
                transform.rotation = Quaternion.LookRotation(lookDirection, Vector3.up);
            }
        }
    }

    private void LateUpdate()
    {
        if (target == null) return;

        // Calculate desired camera position behind and above the target
        Vector3 targetForward = target.forward;
        Vector3 desiredPosition = target.position - targetForward * distance + Vector3.up * height;

        // Smooth position interpolation
        transform.position = Vector3.SmoothDamp(transform.position, desiredPosition, ref currentVelocity, positionSmoothing);

        // Calculate look target ahead of the car
        Vector3 lookTarget = target.position + targetForward * lookAheadDistance + Vector3.up * lookUpOffset;
        Vector3 lookDirection = lookTarget - transform.position;

        if (lookDirection.sqrMagnitude > 0.001f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(lookDirection, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSmoothing * Time.deltaTime);
        }
    }

    /// <summary>
    /// Assigns a new target for the camera to follow.
    /// </summary>
    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
    }
}
