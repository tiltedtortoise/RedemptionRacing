using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Racing/Replay Lap Data")]
public sealed class ReplayLapData : ScriptableObject
{
    [SerializeField] private List<ReplayFrame> frames = new List<ReplayFrame>();
    [SerializeField] private int formatVersion;
    [SerializeField] private float lapStartTime = -1f;
    [SerializeField] private int lapStartFrameIndex = -1;

    public IReadOnlyList<ReplayFrame> Frames => frames;
    public float Duration => frames.Count == 0 ? 0f : frames[frames.Count - 1].timestamp;
    public float LapStartTime => lapStartTime;
    public int LapStartFrameIndex => lapStartFrameIndex;
    public bool HasGridStart => formatVersion >= 2 && lapStartFrameIndex >= 0;

    public void SetFrames(IEnumerable<ReplayFrame> source, float startTime, int startFrameIndex)
    {
        frames = new List<ReplayFrame>(source);
        lapStartTime = startTime;
        lapStartFrameIndex = startFrameIndex;
        formatVersion = 2;
    }
}
