using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>Opt-in recording of the whole race (GO on the grid to the final finish crossing); never changes race state or vehicle physics.</summary>
[DisallowMultipleComponent]
public sealed class ReplayRecorder : MonoBehaviour
{
    [SerializeField] private RaceManager race;
    [SerializeField] private Rigidbody player;
    [SerializeField] private TMP_Text ghostStatusText;

    public const int SamplesPerSecond = 25;
    public const string AssetPath = "Assets/Replays/PlayerBestLap.asset";
    private enum RecordingState { Waiting, Armed, Recording, Completed }
    private RecordingState state;
    private readonly List<ReplayFrame> frames = new List<ReplayFrame>();
    private double startedAt, nextSampleAt;
    private float lapStartTime = -1f;
    private int lapStartFrameIndex = -1;
    private float savedMessageUntil;
    private bool saveSucceeded;
    public bool IsArmed => state == RecordingState.Armed;
    public bool IsRecording => state == RecordingState.Recording;
    public bool HasCompleted => state == RecordingState.Completed;
    public IReadOnlyList<ReplayFrame> Frames => frames;

    private void OnEnable()
    {
#if UNITY_EDITOR
        if (race == null || player == null) return;
        race.RaceStarted += BeginRecording;
        race.ValidStartFinishCrossed += OnStartFinish;
        RefreshStatus();
#else
        // Saving a ghost needs the AssetDatabase, so builds only play back the included ghost:
        // no G prompt, no recording, no status messages.
        if (ghostStatusText != null) ghostStatusText.enabled = false;
        enabled = false;
#endif
    }

    private void OnDisable()
    {
        if (race != null)
        {
            race.RaceStarted -= BeginRecording;
            race.ValidStartFinishCrossed -= OnStartFinish;
        }
        if (ghostStatusText != null) ghostStatusText.enabled = false;
    }

    private void Update()
    {
        bool pressed = false;
#if ENABLE_INPUT_SYSTEM
        pressed = Keyboard.current != null && Keyboard.current.gKey.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
        pressed = Input.GetKeyDown(KeyCode.G);
#endif
        if (pressed) ToggleRecordingDuringCountdown();
        RefreshStatus();
    }

    public void ToggleRecordingDuringCountdown()
    {
        if (race == null || race.State != RaceManager.RaceState.Countdown ||
            (state != RecordingState.Waiting && state != RecordingState.Armed)) return;
        state = IsArmed ? RecordingState.Waiting : RecordingState.Armed;
        Debug.Log(IsArmed ? "Replay recorder armed" : "Replay recording cancelled", this);
        // A recording run races without the current ghost, so it can never end the race before
        // the full recording is saved. Cancelling during the countdown brings it back.
        if (race.Opponent != null) race.Opponent.gameObject.SetActive(!IsArmed);
        RefreshStatus();
    }

    private void BeginRecording()
    {
        if (!IsArmed) { RefreshStatus(); return; }
        frames.Clear();
        lapStartTime = -1f;
        lapStartFrameIndex = -1;
        startedAt = Time.timeAsDouble;
        nextSampleAt = startedAt + 1d / SamplesPerSecond;
        state = RecordingState.Recording;
        Capture(0f);
        Debug.Log("Replay recording started", this);
        RefreshStatus();
    }

    private void OnStartFinish(int completedLaps)
    {
        if (!IsRecording) return;
        float timestamp = Mathf.Max(0f, (float)(Time.timeAsDouble - startedAt));
        if (completedLaps == 0 && lapStartFrameIndex < 0)
        {
            Capture(timestamp);
            lapStartTime = timestamp;
            lapStartFrameIndex = frames.Count - 1;
        }
        else if (completedLaps > 0 && completedLaps < race.TotalLaps)
        {
            Capture(timestamp); // Keep the exact intermediate finish-line crossing; recording continues.
        }
        else if (completedLaps == race.TotalLaps && lapStartFrameIndex >= 0)
        {
            Capture(timestamp);
            state = RecordingState.Completed;
            Debug.Log($"Replay recording completed; frame count: {frames.Count}; duration: {timestamp:F3} s", this);
            saveSucceeded = SaveLap();
            savedMessageUntil = Time.unscaledTime + 3f;
            RefreshStatus();
        }
    }

    private void FixedUpdate()
    {
        if (!IsRecording || Time.timeAsDouble + .000001d < nextSampleAt) return;
        Capture((float)(Time.timeAsDouble - startedAt));
        do { nextSampleAt += 1d / SamplesPerSecond; }
        while (nextSampleAt <= Time.timeAsDouble + .000001d);
    }

    private void Capture(float timestamp)
    {
        var frame = new ReplayFrame(timestamp, player.position, player.rotation);
        if (frames.Count > 0 && timestamp <= frames[frames.Count - 1].timestamp)
            frames[frames.Count - 1] = frame;
        else frames.Add(frame);
    }

    private void RefreshStatus()
    {
        if (ghostStatusText == null) return;
        string message = "";
        if (race != null && race.State == RaceManager.RaceState.Countdown)
            message = IsArmed ? "Ghost Recording: ON (G to cancel)" : "G - Record New Ghost";
        else if (IsRecording) message = "Recording Ghost...";
        else if (HasCompleted && Time.unscaledTime < savedMessageUntil)
            message = saveSucceeded ? "Ghost Saved!" : "Ghost could not be saved";
        ghostStatusText.text = message;
        ghostStatusText.enabled = message.Length > 0;
    }

    private bool SaveLap()
    {
#if UNITY_EDITOR
        try
        {
            if (!AssetDatabase.IsValidFolder("Assets/Replays"))
                AssetDatabase.CreateFolder("Assets", "Replays");
            var data = AssetDatabase.LoadAssetAtPath<ReplayLapData>(AssetPath);
            if (data == null)
            {
                data = ScriptableObject.CreateInstance<ReplayLapData>();
                data.SetFrames(frames, lapStartTime, lapStartFrameIndex);
                AssetDatabase.CreateAsset(data, AssetPath);
            }
            else
            {
                data.SetFrames(frames, lapStartTime, lapStartFrameIndex);
                EditorUtility.SetDirty(data);
            }
            AssetDatabase.SaveAssetIfDirty(data);
            Debug.Log($"Replay saved asset path: {AssetPath}", this);
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogError($"Could not save replay to {AssetPath}: {exception.Message}", this);
        }
#else
        Debug.Log("Replay captured in memory; saving a .asset requires the Unity Editor.", this);
#endif
        return false;
    }
}
