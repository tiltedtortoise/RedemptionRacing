using UnityEngine;

/// <summary>Collision-free playback of a recorded race, starting at GO. A full multi-lap recording plays
/// straight through; an older one-lap recording loops its lap section as a fallback.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public sealed class ReplayOpponent : MonoBehaviour
{
    [SerializeField] private RaceManager race;
    [SerializeField] private ReplayLapData replay;
    [SerializeField] private Transform[] visualWheelPivots;
    [SerializeField, Min(0.01f)] private float visualWheelRadius = 0.28f;

    [SerializeField, Range(0f, 1f)] private float ghostOpacity = 0.5f;
    [Tooltip("Transparent URP/Lit material asset. Referencing it keeps the transparent shader variant in builds.")]
    [SerializeField] private Material ghostMaterialTemplate;
    private readonly System.Collections.Generic.List<Material> ghostMaterials = new System.Collections.Generic.List<Material>();
    private readonly System.Collections.Generic.Dictionary<Renderer, Material[]> originalMaterials = new System.Collections.Generic.Dictionary<Renderer, Material[]>();
    private float appliedOpacity = -1f;
    private ReplayLapData playbackSnapshot;


    [SerializeField, Range(0.8f, 1.2f)] private float replaySpeedMultiplier = 1f;
    private RaceProgress progress;
    private Vector3 probeCenter, probeExtents;
    private Vector3 loopPositionOffset;
    private Quaternion loopRotationOffset = Quaternion.identity;
    private bool looping;
    private int playbackLap = 1;
    public bool IsAvailable => ready && isActiveAndEnabled;
    public int CurrentLap => progress != null ? progress.CurrentLap : 1;
    public int CompletedLaps => progress != null ? progress.CompletedLaps : 0;
    public int NextExpectedCheckpoint => progress != null ? progress.NextExpectedCheckpoint : 0;
    public int LastCheckpointIndex => progress != null ? progress.LastValidCheckpointIndex : -1;
    public double FinishRaceTime { get; private set; }

    private void AdvancePlayback(float seconds)
    {
        float remaining = Mathf.Max(0f, seconds) * Mathf.Clamp(replaySpeedMultiplier, .8f, 1.2f);
        while (playing && remaining > 0f)
        {
            float nextFrameTime = replay.Duration;
            if (frameIndex < replay.Frames.Count - 1)
                nextFrameTime = replay.Frames[frameIndex + 1].timestamp;
            float step = Mathf.Min(remaining, Mathf.Max(0.000001f, nextFrameTime - PlaybackTime));
            step = Mathf.Min(step, replay.Duration - PlaybackTime);
            Vector3 previous = transform.position;
            Quaternion previousRotation = transform.rotation;
            PlaybackTime += step;
            remaining -= step;
            ApplyPose(PlaybackTime);
            if (looping)
            {
                float blend = Mathf.Clamp01((PlaybackTime - replay.LapStartTime) / .25f);
                transform.position += Vector3.Lerp(loopPositionOffset, Vector3.zero, blend);
                transform.rotation = Quaternion.Slerp(loopRotationOffset, Quaternion.identity, blend) * transform.rotation;
            }
            CheckMovement(progress, previous, previousRotation, transform.position, transform.rotation);
            if (progress.IsFinished)
            {
                HasFinished = true;
                playing = false;
                FinishRaceTime = Time.timeAsDouble - startedAt;
                ApplyPose(replay.Duration);
                break;
            }
            if (PlaybackTime >= replay.Duration)
            {
                if (progress.CompletedLaps != playbackLap)
                {
                    playing = false;
                    Debug.LogWarning("Ghost replay did not pass the current ordered checkpoint layout. Record a new ghost for this track.", this);
                    break;
                }
                playbackLap++;
                looping = true;
                var start = replay.Frames[replay.LapStartFrameIndex];
                loopPositionOffset = transform.position - start.worldPosition;
                loopRotationOffset = transform.rotation * Quaternion.Inverse(start.worldRotation);
                PlaybackTime = replay.LapStartTime;
                frameIndex = replay.LapStartFrameIndex;
                // Hold the finish pose here; the next samples smoothly remove the small loop offset.
            }
        }
    }

    /// <summary>Advances the given progress through any gates the movement passes; returns how many were passed.</summary>
    private int CheckMovement(RaceProgress progress, Vector3 from, Quaternion fromRotation, Vector3 to, Quaternion toRotation)
    {
        int passed = 0;
        // Only the next expected gate can advance this independent instance of RaceProgress.
        for (int attempt = 0; attempt < race.Checkpoints.Count && !progress.IsFinished; attempt++)
        {
            int index = progress.NextExpectedCheckpoint;
            var checkpoint = race.Checkpoints[index];
            var box = checkpoint.GetComponent<BoxCollider>();
            if (!checkpoint.isActiveAndEnabled || !box.enabled || !box.isTrigger) return passed;
            var frame = box.transform;
            Vector3 a = frame.InverseTransformPoint(from + fromRotation * probeCenter) - box.center;
            Vector3 b = frame.InverseTransformPoint(to + toRotation * probeCenter) - box.center;
            Vector3 padding = Vector3.Max(ProjectExtents(frame, fromRotation), ProjectExtents(frame, toRotation));
            var bounds = new Bounds(Vector3.zero, box.size + padding * 2f);
            Vector3 delta = b - a;
            bool hit = bounds.Contains(a) || bounds.Contains(b);
            if (!hit && delta.sqrMagnitude > .0000001f)
                hit = bounds.IntersectRay(new Ray(a, delta.normalized), out float distance) && distance <= delta.magnitude;
            if (!hit || !progress.TryPass(index)) return passed;
            passed++;
        }
        return passed;
    }

    /// <summary>
    /// Recorded race time (already divided by replaySpeedMultiplier) at which the ghost made its
    /// n-th valid checkpoint pass. Pass 0 is the first start-line crossing; the order matches the player's.
    /// </summary>
    public bool TryGetSplitReference(int passOrdinal, out float raceTime)
    {
        raceTime = 0f;
        if (passOrdinal < 0 || passOrdinal >= splitTimes.Count) return false;
        raceTime = splitTimes[passOrdinal] / Mathf.Clamp(replaySpeedMultiplier, .8f, 1.2f);
        return true;
    }

    private readonly System.Collections.Generic.List<float> splitTimes = new System.Collections.Generic.List<float>();

    // Runs the recorded path through the same gate test with its own RaceProgress, so every split is
    // known up front (even for gates the live ghost hasn't reached yet). Live playback is untouched.
    private void BuildSplitReferences()
    {
        splitTimes.Clear();
        var reference = new RaceProgress(race.Checkpoints.Count, race.TotalLaps);
        int index = 0;
        SamplePose(0f, ref index, out Vector3 position, out Quaternion rotation);
        for (int n = CheckMovement(reference, position, rotation, position, rotation); n > 0; n--) splitTimes.Add(0f);
        const float step = 0.01f;
        for (float t = step; !reference.IsFinished; t += step)
        {
            float time = Mathf.Min(t, replay.Duration);
            SamplePose(time, ref index, out Vector3 nextPosition, out Quaternion nextRotation);
            for (int n = CheckMovement(reference, position, rotation, nextPosition, nextRotation); n > 0; n--) splitTimes.Add(time);
            position = nextPosition;
            rotation = nextRotation;
            if (time >= replay.Duration) break;
        }
        // Older one-lap recording: later laps replay the lap section, so their gates follow one lap-section later.
        int perLap = race.Checkpoints.Count;
        if (!reference.IsFinished && splitTimes.Count == 1 + perLap)
        {
            float lapSection = replay.Duration - replay.LapStartTime;
            for (int i = splitTimes.Count; i < 1 + perLap * race.TotalLaps; i++)
                splitTimes.Add(splitTimes[i - perLap] + lapSection);
        }
    }

    private void SamplePose(float time, ref int index, out Vector3 position, out Quaternion rotation)
    {
        var frames = replay.Frames;
        if (time <= frames[0].timestamp) { position = frames[0].worldPosition; rotation = frames[0].worldRotation; return; }
        if (time >= replay.Duration) { var last = frames[frames.Count - 1]; position = last.worldPosition; rotation = last.worldRotation; return; }
        while (index > 0 && frames[index].timestamp > time) index--;
        while (index < frames.Count - 2 && frames[index + 1].timestamp <= time) index++;
        float fraction = Mathf.InverseLerp(frames[index].timestamp, frames[index + 1].timestamp, time);
        position = Vector3.Lerp(frames[index].worldPosition, frames[index + 1].worldPosition, fraction);
        rotation = Quaternion.Slerp(frames[index].worldRotation, frames[index + 1].worldRotation, fraction);
    }

    private Vector3 ProjectExtents(Transform frame, Quaternion rotation)
    {
        Vector3 x = frame.InverseTransformVector(rotation * Vector3.right * probeExtents.x);
        Vector3 y = frame.InverseTransformVector(rotation * Vector3.up * probeExtents.y);
        Vector3 z = frame.InverseTransformVector(rotation * Vector3.forward * probeExtents.z);
        return new Vector3(Mathf.Abs(x.x)+Mathf.Abs(y.x)+Mathf.Abs(z.x),
            Mathf.Abs(x.y)+Mathf.Abs(y.y)+Mathf.Abs(z.y),
            Mathf.Abs(x.z)+Mathf.Abs(y.z)+Mathf.Abs(z.z));
    }

    private bool ready;
    private bool playing;
    private double startedAt;
    private int frameIndex;
    public bool IsPlaying => playing;
    public bool HasFinished { get; private set; }
    public float PlaybackTime { get; private set; }

    private void Awake()
    {
        // detectCollisions is runtime-only, so apply the replay setup on every run.
        var body = GetComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
        body.detectCollisions = false;
#if UNITY_EDITOR
        replay = UnityEditor.AssetDatabase.LoadAssetAtPath<ReplayLapData>(ReplayRecorder.AssetPath);
#endif
        ready = race != null && replay != null && replay.HasGridStart && replay.Frames.Count >= 2;
        if (!ready)
        {
            HideGhost(); // Missing or old line-to-line replay: record a new grid-start ghost with G.
            return;
        }
        for (int i = 1; i < replay.Frames.Count; i++)
            if (replay.Frames[i].timestamp <= replay.Frames[i - 1].timestamp)
            {
                ready = false;
                HideGhost();
                return;
            }
        if (replay.LapStartFrameIndex < 0 || replay.LapStartFrameIndex >= replay.Frames.Count - 1 || replay.Duration <= replay.LapStartTime)
        { ready = false; HideGhost(); return; }
        playbackSnapshot = Instantiate(replay); // Saving a new ghost cannot change this run's playback.
        replay = playbackSnapshot;
        CreateGhostMaterials();
        ApplyPose(0f);
    }

    private void OnEnable()
    {
        if (race != null) race.RaceStarted += BeginPlayback;
    }

    private void OnDisable()
    {
        if (race != null) race.RaceStarted -= BeginPlayback;
    }

    private void BeginPlayback()
    {
        if (!ready || playing || HasFinished) return;
        progress = new RaceProgress(race.Checkpoints.Count, race.TotalLaps);
        var chassis = race.PlayerBody.GetComponent<BoxCollider>();
        probeCenter = Vector3.Scale(chassis.center, chassis.transform.lossyScale);
        probeExtents = Vector3.Scale(chassis.size * .5f, chassis.transform.lossyScale);
        BuildSplitReferences();
        startedAt = Time.timeAsDouble;
        playing = true;
        CheckMovement(progress, transform.position, transform.rotation, transform.position, transform.rotation);
    }

    private void LateUpdate()
    {
        if (!Mathf.Approximately(appliedOpacity, ghostOpacity)) ApplyOpacity();
        if (!playing) return;
        Vector3 previousPosition = transform.position;
        AdvancePlayback(Time.deltaTime);

        // Cosmetic rolling only; centered pivots prevent imported wheel meshes from wobbling.
        float distance = Vector3.Distance(previousPosition, transform.position);
        float sign = Vector3.Dot(transform.position - previousPosition, transform.forward) >= 0f ? 1f : -1f;
        float angle = -sign * distance / Mathf.Max(0.01f, visualWheelRadius) * Mathf.Rad2Deg;
        if (visualWheelPivots != null)
            foreach (Transform wheel in visualWheelPivots)
                if (wheel != null) wheel.Rotate(Vector3.right, angle, Space.Self);


    }

    private void ApplyPose(float time)
    {
        var frames = replay.Frames;
        if (time <= frames[0].timestamp)
        {
            frameIndex = 0;
            transform.SetPositionAndRotation(frames[0].worldPosition, frames[0].worldRotation);
            return;
        }
        if (time >= replay.Duration)
        {
            var last = frames[frames.Count - 1];
            transform.SetPositionAndRotation(last.worldPosition, last.worldRotation);
            return;
        }
        while (frameIndex > 0 && frames[frameIndex].timestamp > time) frameIndex--;
        while (frameIndex < frames.Count - 2 && frames[frameIndex + 1].timestamp <= time) frameIndex++;
        ReplayFrame a = frames[frameIndex];
        ReplayFrame b = frames[frameIndex + 1];
        float fraction = Mathf.InverseLerp(a.timestamp, b.timestamp, time);
        transform.SetPositionAndRotation(Vector3.Lerp(a.worldPosition, b.worldPosition, fraction),
            Quaternion.Slerp(a.worldRotation, b.worldRotation, fraction));
    }

    private void HideGhost()
    {
        foreach (var renderer in GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
    }

    private void CreateGhostMaterials()
    {
        if (ghostMaterialTemplate == null)
        {
            Debug.LogWarning("ReplayOpponent needs a transparent ghostMaterialTemplate; the ghost stays opaque.", this);
            return;
        }
        foreach (var renderer in GetComponentsInChildren<Renderer>(true))
        {
            originalMaterials[renderer] = renderer.sharedMaterials;
            var copies = renderer.sharedMaterials;
            for (int i = 0; i < copies.Length; i++)
            {
                if (copies[i] == null) continue;
                // Start from the transparent template so no shader mode is switched at runtime;
                // only the original surface look is copied over.
                var material = new Material(ghostMaterialTemplate) { name = copies[i].name + " (Ghost Instance)" };
                material.SetColor("_BaseColor", copies[i].GetColor("_BaseColor"));
                material.SetTexture("_BaseMap", copies[i].GetTexture("_BaseMap"));
                material.SetTextureScale("_BaseMap", copies[i].GetTextureScale("_BaseMap"));
                material.SetTextureOffset("_BaseMap", copies[i].GetTextureOffset("_BaseMap"));
                material.SetFloat("_Smoothness", copies[i].GetFloat("_Smoothness"));
                material.SetFloat("_Metallic", copies[i].GetFloat("_Metallic"));
                copies[i] = material;
                ghostMaterials.Add(material);
            }
            renderer.sharedMaterials = copies;
        }
        ApplyOpacity();
    }

    private void ApplyOpacity()
    {
        appliedOpacity = Mathf.Clamp01(ghostOpacity);
        foreach (var material in ghostMaterials)
        {
            Color color = material.GetColor("_BaseColor");
            color.a = appliedOpacity;
            material.SetColor("_BaseColor", color);
        }
    }

    private void OnDestroy()
    {
        foreach (var entry in originalMaterials)
            if (entry.Key != null) entry.Key.sharedMaterials = entry.Value;
        foreach (var material in ghostMaterials) Destroy(material);
        if (playbackSnapshot != null) Destroy(playbackSnapshot);
    }
}
