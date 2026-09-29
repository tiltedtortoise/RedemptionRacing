using UnityEngine;

/// <summary>Loops the engine sound and smoothly raises pitch with the controller's speed.</summary>
[RequireComponent(typeof(AudioSource))]
public class CarEngineAudio : MonoBehaviour
{
    [SerializeField] private CarController carController;
    [SerializeField] private AudioSource engineSource;
    [SerializeField, Range(0.1f, 3f)] private float idlePitch = 0.6f;
    [SerializeField, Range(0.1f, 3f)] private float maximumPitch = 2f;
    [SerializeField, Min(0f)] private float pitchResponse = 5f;

    private void Awake()
    {
        if (carController == null) carController = GetComponent<CarController>();
        if (engineSource == null) engineSource = GetComponent<AudioSource>();
        engineSource.pitch = idlePitch;
    }

    private void Update()
    {
        if (carController == null || engineSource == null) return;

        float speedFraction = Mathf.Clamp01(
            Mathf.Abs(carController.CurrentSpeed) / Mathf.Max(0.01f, carController.MaxForwardSpeed));
        float targetPitch = Mathf.Lerp(idlePitch, maximumPitch, speedFraction);
        // Exponential interpolation gives the same response at different frame rates.
        float blend = 1f - Mathf.Exp(-pitchResponse * Time.deltaTime);
        engineSource.pitch = Mathf.Lerp(engineSource.pitch, targetPitch, blend);
    }
}
