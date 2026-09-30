using UnityEngine;
using System.Collections.Generic;
using TMPro;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>Coordinates countdown, ordered race progress, elapsed times and checkpoint respawn.</summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-100)]
public class RaceManager : MonoBehaviour
{
    [SerializeField] private Rigidbody player;
    [Tooltip("Array order must match each Checkpoint index. Element zero is start/finish.")]
    [SerializeField] private Checkpoint[] checkpoints;
    [SerializeField, Min(1)] private int totalLaps = 3;
    [Tooltip("Car root height above the checkpoint road marker when resetting.")]
    [SerializeField, Min(0.1f)] private float resetHeight = 0.6f;

    [Header("Temporary Countdown Display")]
    [SerializeField] private TMP_Text countdownText;
    [SerializeField, Min(0f)] private float goDisplaySeconds = 0.75f;

    public event System.Action RaceStarted;
    public event System.Action<int> ValidStartFinishCrossed;

    public enum RaceState { Countdown, Racing, Finished }
    public RaceState State { get; private set; } = RaceState.Countdown;
    public bool IsRacing => State == RaceState.Racing;
    public float TotalRaceTime { get; private set; }
    public float CurrentLapTime { get; private set; }
    public float LastLapTime { get; private set; }
    public float BestLapTime { get; private set; }
    public IReadOnlyList<float> CompletedLapTimes => completedLapTimes;

    private readonly List<float> completedLapTimes = new List<float>();
    private RaceProgress progress;
    private CarController playerController;
    private float countdownRemaining;
    private float goVisibleRemaining;

    public int CurrentLap => progress != null ? progress.CurrentLap : 1;
    public int CompletedLaps => progress != null ? progress.CompletedLaps : 0;
    public int TotalLaps => progress != null ? progress.TotalLaps : totalLaps;
    public int NextExpectedCheckpoint => progress != null ? progress.NextExpectedCheckpoint : 0;
    public bool IsFinished => State == RaceState.Finished;
    public Checkpoint LastValidCheckpoint =>
        progress != null && progress.LastValidCheckpointIndex >= 0
            ? checkpoints[progress.LastValidCheckpointIndex] : null;

    private void Awake()
    {
        if (!InitializeRace()) enabled = false;
    }

    public bool InitializeRace()
    {
        if (player == null || checkpoints == null || checkpoints.Length < 2 || totalLaps < 1)
        {
            Debug.LogError("RaceManager needs a player Rigidbody, at least two checkpoints, and a positive lap count.", this);
            return false;
        }
        for (int i = 0; i < checkpoints.Length; i++)
        {
            if (checkpoints[i] == null || checkpoints[i].Index != i || checkpoints[i].Manager != this ||
                !checkpoints[i].GetComponent<BoxCollider>().isTrigger)
            {
                Debug.LogError("RaceManager checkpoint array, indices, manager references and triggers must agree.", this);
                return false;
            }
        }
        playerController = player.GetComponent<CarController>();
        if (playerController == null)
        {
            Debug.LogError("RaceManager player needs a CarController for the countdown control lock.", this);
            return false;
        }
        progress = new RaceProgress(checkpoints.Length, totalLaps);
        completedLapTimes.Clear();
        TotalRaceTime = CurrentLapTime = LastLapTime = BestLapTime = 0f;
        countdownRemaining = 3f;
        goVisibleRemaining = 0f;
        State = RaceState.Countdown;
        playerController.SetControlsLocked(true);
        ShowCountdown("3");
        return true;
    }

    private void Update()
    {
        AdvanceRaceClock(Time.deltaTime);
        bool resetPressed = false;
#if ENABLE_INPUT_SYSTEM
        resetPressed = Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
        resetPressed = Input.GetKeyDown(KeyCode.R);
#endif
        if (resetPressed) ResetPlayerToLastCheckpoint();
    }

    private void AdvanceRaceClock(float deltaTime)
    {
        if (progress == null || deltaTime < 0f || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime))
            return;

        if (State == RaceState.Countdown)
        {
            float countdownStep = Mathf.Min(deltaTime, countdownRemaining);
            countdownRemaining -= countdownStep;
            deltaTime -= countdownStep;
            if (countdownRemaining > 0f)
            {
                ShowCountdown(Mathf.CeilToInt(countdownRemaining).ToString());
                return;
            }

            State = RaceState.Racing;
            goVisibleRemaining = goDisplaySeconds;
            ShowCountdown("GO!");
            playerController.SetControlsLocked(false);
            RaceStarted?.Invoke();
        }

        // Only the part of this frame after GO contributes; countdown is never included.
        if (IsRacing)
        {
            TotalRaceTime += deltaTime;
            CurrentLapTime += deltaTime;
        }
        if (goVisibleRemaining > 0f)
        {
            goVisibleRemaining = Mathf.Max(0f, goVisibleRemaining - deltaTime);
            if (goVisibleRemaining == 0f) ShowCountdown("");
        }
        else if (IsRacing)
        {
            ShowCountdown("");
        }
    }

    private void ShowCountdown(string message)
    {
        if (countdownText == null) return;
        countdownText.text = message;
        countdownText.enabled = message.Length > 0;
    }

    private void OnDisable()
    {
        if (playerController != null && playerController.ControlsLocked)
            playerController.SetControlsLocked(false);
        ShowCountdown("");
    }

    public bool TryPassCheckpoint(Checkpoint checkpoint, Rigidbody entrant)
    {
        if (!isActiveAndEnabled || !IsRacing || progress == null || entrant != player || checkpoint == null)
            return false;
        int index = checkpoint.Index;
        if (index < 0 || index >= checkpoints.Length || checkpoints[index] != checkpoint)
            return false;
        int previousCompletedLaps = progress.CompletedLaps;
        if (!progress.TryPass(index)) return false;
        // The initial start/finish pass changes HasStarted, not CompletedLaps.
        if (progress.CompletedLaps > previousCompletedLaps)
        {
            LastLapTime = CurrentLapTime;
            completedLapTimes.Add(LastLapTime);
            if (completedLapTimes.Count == 1 || LastLapTime < BestLapTime)
                BestLapTime = LastLapTime;
            CurrentLapTime = 0f;
            if (progress.IsFinished)
            {
                State = RaceState.Finished;
                goVisibleRemaining = 0f;
                ShowCountdown("");
            }
        }
        if (index == 0) ValidStartFinishCrossed?.Invoke(progress.CompletedLaps);
        return true;
    }

    public void ResetPlayerToLastCheckpoint()
    {
        if (progress == null || player == null) return;
        Checkpoint checkpoint = LastValidCheckpoint != null ? LastValidCheckpoint : checkpoints[0];
        player.position = checkpoint.transform.position + checkpoint.transform.up * resetHeight;
        player.rotation = checkpoint.transform.rotation;
        player.linearVelocity = Vector3.zero;
        player.angularVelocity = Vector3.zero;
        player.WakeUp();
        // Keep race progress. Re-entering the last valid gate cannot award it twice.
    }
}
