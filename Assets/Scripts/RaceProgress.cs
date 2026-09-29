using System;

/// <summary>Ordered race progress, independent of scene objects and vehicle physics.</summary>
public sealed class RaceProgress
{
    public int CheckpointCount { get; }
    public int TotalLaps { get; }
    public int CompletedLaps { get; private set; }
    public int CurrentLap => Math.Min(CompletedLaps + 1, TotalLaps);
    public int NextExpectedCheckpoint { get; private set; }
    public int LastValidCheckpointIndex { get; private set; } = -1;
    public bool HasStarted { get; private set; }
    public bool IsFinished => CompletedLaps >= TotalLaps;

    public RaceProgress(int checkpointCount, int totalLaps)
    {
        if (checkpointCount < 2) throw new ArgumentOutOfRangeException(nameof(checkpointCount));
        if (totalLaps < 1) throw new ArgumentOutOfRangeException(nameof(totalLaps));
        CheckpointCount = checkpointCount;
        TotalLaps = totalLaps;
    }

    public bool TryPass(int index)
    {
        if (IsFinished || index < 0 || index >= CheckpointCount ||
            index != NextExpectedCheckpoint) return false;

        LastValidCheckpointIndex = index;
        if (index == 0)
        {
            if (HasStarted) CompletedLaps++;
            else HasStarted = true;
            NextExpectedCheckpoint = IsFinished ? -1 : 1;
        }
        else
        {
            NextExpectedCheckpoint = (index + 1) % CheckpointCount;
        }
        return true;
    }
}
