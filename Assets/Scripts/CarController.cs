using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>Simple arcade WheelCollider controller with balanced all-wheel drive. PlayerCar local +Z is forward.</summary>
[RequireComponent(typeof(Rigidbody))]
public class CarController : MonoBehaviour
{
    [Header("Drive")]
    [SerializeField, Min(1f)] private float maxForwardSpeed = 24f;
    [SerializeField, Min(1f)] private float maxReverseSpeed = 7f;
    [Tooltip("Total engine torque shared across all four wheels, in Nm.")]
    [SerializeField, Min(0f)] private float driveTorque = 1000f;
    [SerializeField, Range(0f, 1f)] private float frontDriveShare = 0.4f;
    [Tooltip("Normal brake torque per wheel, in Nm.")]
    [SerializeField, Min(0f)] private float brakeTorque = 450f;
    [Tooltip("Coasting brake torque per rear wheel; front wheels free-roll.")]
    [SerializeField, Min(0f)] private float coastBrakeTorque = 70f;
    [SerializeField, Min(0f)] private float handbrakeTorque = 900f;

    [Header("Steering")]
    [SerializeField, Range(1f, 45f)] private float maxSteerAngle = 38f;
    [Tooltip("Speed-dependent steering budget in m/s². Keeps large steering angles from scrubbing at speed.")]
    [SerializeField, Min(1f)] private float corneringAcceleration = 24f;
    [SerializeField, Min(1f)] private float steeringResponse = 500f;

    [Header("Input and Stability")]
    [SerializeField, Range(0f, 0.9f)] private float gamepadDeadzone = 0.2f;
    [Tooltip("Explicit center of mass in PlayerCar local meters, independent of previous runtime offsets.")]
    [SerializeField] private Vector3 centerOfMass = new Vector3(0f, 0.2f, -0.128f);

    [Header("Wheel Colliders")]
    [SerializeField] private WheelCollider frontLeftCollider;
    [SerializeField] private WheelCollider frontRightCollider;
    [SerializeField] private WheelCollider backLeftCollider;
    [SerializeField] private WheelCollider backRightCollider;

    [Header("Visual Wheel Pivots")]
    [SerializeField] private Transform wheelFrontLeft;
    [SerializeField] private Transform wheelFrontRight;
    [SerializeField] private Transform wheelBackLeft;
    [SerializeField] private Transform wheelBackRight;

    private Rigidbody rb;
    private WheelCollider[] wheels;
    private Transform[] visuals;
    private float throttleInput;
    private float steerInput;
    private float steeringAngle;
    private bool handbrakeInput;
    private float wheelbase;
    private readonly WheelFrictionCurve[] normalSidewaysFriction = new WheelFrictionCurve[4];

    public float CurrentSpeed => rb == null ? 0f : Vector3.Dot(rb.linearVelocity, transform.forward);
    public float CurrentSpeedKmh => Mathf.Abs(CurrentSpeed) * 3.6f;
    public float MaxForwardSpeed => maxForwardSpeed;
    public bool IsHandbraking => handbrakeInput;
    public bool ControlsLocked { get; private set; }
    private bool holdBrakesWhileLocked = true;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        AutoAssignWheelReferences();
        wheels = new[] { frontLeftCollider, frontRightCollider, backLeftCollider, backRightCollider };
        visuals = new[] { wheelFrontLeft, wheelFrontRight, wheelBackLeft, wheelBackRight };

        for (int i = 0; i < wheels.Length; i++)
        {
            if (wheels[i] == null || visuals[i] == null)
            {
                Debug.LogError("CarController needs all four WheelColliders and visible wheels assigned.", this);
                enabled = false;
                return;
            }
            normalSidewaysFriction[i] = wheels[i].sidewaysFriction;
        }

        wheelbase = Mathf.Abs(Vector3.Dot(
            (frontLeftCollider.transform.position + frontRightCollider.transform.position
            - backLeftCollider.transform.position - backRightCollider.transform.position) * 0.5f,
            transform.forward));
        rb.centerOfMass = centerOfMass;
        rb.maxAngularVelocity = 4f;
        frontLeftCollider.ConfigureVehicleSubsteps(5f, 5, 3);
        if (ControlsLocked && holdBrakesWhileLocked) ApplyControlLock();
    }

    public void AutoAssignWheelReferences()
    {
        if (frontLeftCollider == null) frontLeftCollider = FindWheelCollider("WheelCollider_FL");
        if (frontRightCollider == null) frontRightCollider = FindWheelCollider("WheelCollider_FR");
        if (backLeftCollider == null) backLeftCollider = FindWheelCollider("WheelCollider_BL");
        if (backRightCollider == null) backRightCollider = FindWheelCollider("WheelCollider_BR");
        if (wheelFrontLeft == null) wheelFrontLeft = transform.Find("raceCarRed/VisualWheelPivot_FL");
        if (wheelFrontRight == null) wheelFrontRight = transform.Find("raceCarRed/VisualWheelPivot_FR");
        if (wheelBackLeft == null) wheelBackLeft = transform.Find("raceCarRed/VisualWheelPivot_BL");
        if (wheelBackRight == null) wheelBackRight = transform.Find("raceCarRed/VisualWheelPivot_BR");
    }

    private WheelCollider FindWheelCollider(string childName)
    {
        Transform child = transform.Find(childName);
        return child == null ? null : child.GetComponent<WheelCollider>();
    }

    /// <param name="holdBrakes">True holds the car still (countdown). False only ignores input,
    /// so the car coasts exactly as if the player released all keys (finish roll-out).</param>
    public void SetControlsLocked(bool locked, bool holdBrakes = true)
    {
        ControlsLocked = locked;
        holdBrakesWhileLocked = holdBrakes;
        if (locked)
        {
            throttleInput = 0f;
            steerInput = 0f;
            handbrakeInput = false;
            if (!holdBrakes) return;
            steeringAngle = 0f;
            ApplyControlLock();
        }
        else if (wheels != null)
        {
            // Release the countdown hold immediately; normal controls resume next physics tick.
            foreach (var wheel in wheels)
                if (wheel != null) wheel.brakeTorque = 0f;
        }
    }

    private void ApplyControlLock()
    {
        if (wheels == null) return; // RaceManager may lock before this component's Awake.
        for (int i = 0; i < wheels.Length; i++)
        {
            if (wheels[i] == null) continue;
            wheels[i].motorTorque = 0f;
            wheels[i].steerAngle = 0f;
            wheels[i].brakeTorque = brakeTorque;
            if (normalSidewaysFriction[i].extremumSlip > 0f)
                wheels[i].sidewaysFriction = normalSidewaysFriction[i];
        }
    }

    private void Update() => ReadInput();

    private void ReadInput()
    {
        throttleInput = 0f;
        steerInput = 0f;
        handbrakeInput = false;
        if (ControlsLocked) return;
#if ENABLE_INPUT_SYSTEM
        bool keyboardThrottleHeld = false;
        bool keyboardSteeringHeld = false;
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null)
        {
            throttleInput = (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed ? 1f : 0f)
                - (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed ? 1f : 0f);
            steerInput = (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed ? 1f : 0f)
                - (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed ? 1f : 0f);
            keyboardThrottleHeld = keyboard.wKey.isPressed || keyboard.sKey.isPressed
                || keyboard.upArrowKey.isPressed || keyboard.downArrowKey.isPressed;
            keyboardSteeringHeld = keyboard.aKey.isPressed || keyboard.dKey.isPressed
                || keyboard.leftArrowKey.isPressed || keyboard.rightArrowKey.isPressed;
            handbrakeInput = keyboard.spaceKey.isPressed;
        }
        Gamepad pad = Gamepad.current;
        if (pad != null)
        {
            float forwardTrigger = ApplyGamepadDeadzone(pad.rightTrigger.ReadValue());
            float reverseTrigger = ApplyGamepadDeadzone(pad.leftTrigger.ReadValue());
            float padThrottle = ApplyGamepadDeadzone(forwardTrigger - reverseTrigger);
            float padSteer = ApplyGamepadDeadzone(pad.leftStick.x.ReadValue());
            if (!keyboardThrottleHeld && padThrottle != 0f) throttleInput = padThrottle;
            if (!keyboardSteeringHeld && padSteer != 0f) steerInput = padSteer;
            handbrakeInput |= pad.buttonSouth.isPressed;
        }
#elif ENABLE_LEGACY_INPUT_MANAGER
        throttleInput = (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow) ? 1f : 0f)
            - (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow) ? 1f : 0f);
        steerInput = (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow) ? 1f : 0f)
            - (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow) ? 1f : 0f);
        handbrakeInput = Input.GetKey(KeyCode.Space);
#endif
    }

    private float ApplyGamepadDeadzone(float value)
    {
        return Mathf.Abs(value) <= gamepadDeadzone ? 0f : value;
    }

    private void FixedUpdate()
    {
        ReadInput();
        ApplyControls();
    }

    private void ApplyControls()
    {
        if (ControlsLocked && holdBrakesWhileLocked)
        {
            ApplyControlLock();
            return;
        }
        float signedSpeed = CurrentSpeed;
        float speedSquared = Vector3.ProjectOnPlane(rb.linearVelocity, transform.up).sqrMagnitude;

        // Bicycle steering geometry: request a turn the tires can support instead of huge
        // front-wheel angles at racing speed. Throttle never enters this calculation.
        float speedAngle = Mathf.Atan(wheelbase * corneringAcceleration /
            Mathf.Max(0.01f, speedSquared)) * Mathf.Rad2Deg;
        float targetAngle = steerInput * Mathf.Min(maxSteerAngle, speedAngle);
        steeringAngle = Mathf.MoveTowards(steeringAngle, targetAngle, steeringResponse * Time.fixedDeltaTime);

        float totalDrive = 0f;
        float frontBrake = 0f;
        float rearBrake = 0f;
        if (handbrakeInput)
        {
            rearBrake = handbrakeTorque;
        }
        else if (throttleInput == 0f)
        {
            rearBrake = coastBrakeTorque;
        }
        else if (signedSpeed * Mathf.Sign(throttleInput) < -0.5f)
        {
            // Opposite input brakes first. The same held input drives through zero into reverse.
            frontBrake = rearBrake = brakeTorque;
        }
        else
        {
            float direction = Mathf.Sign(throttleInput);
            float limit = direction > 0f ? maxForwardSpeed : maxReverseSpeed;
            float limitFactor = Mathf.Clamp01((limit - signedSpeed * direction) / 2f);
            totalDrive = throttleInput * driveTorque * limitFactor;
        }

        for (int i = 0; i < wheels.Length; i++)
        {
            bool front = i < 2;
            wheels[i].steerAngle = front ? steeringAngle : 0f;
            wheels[i].motorTorque = totalDrive * (front ? frontDriveShare : 1f - frontDriveShare) * 0.5f;
            wheels[i].brakeTorque = front ? frontBrake : rearBrake;
            var grip = normalSidewaysFriction[i];
            if (!front && handbrakeInput) grip.stiffness *= 0.65f;
            wheels[i].sidewaysFriction = grip;
        }
    }

    private void LateUpdate()
    {
        for (int i = 0; i < wheels.Length; i++)
        {
            wheels[i].GetWorldPose(out Vector3 position, out Quaternion rotation);
            // Centered pivots carry the pose; imported mesh orientation stays on their children.
            visuals[i].SetPositionAndRotation(position, rotation);
        }
    }

    private void OnDisable()
    {
        if (wheels == null) return;
        for (int i = 0; i < wheels.Length; i++)
        {
            if (wheels[i] == null) continue;
            wheels[i].motorTorque = 0f;
            wheels[i].brakeTorque = 0f;
            if (normalSidewaysFriction[i].extremumSlip > 0f)
                wheels[i].sidewaysFriction = normalSidewaysFriction[i];
        }
    }
}
