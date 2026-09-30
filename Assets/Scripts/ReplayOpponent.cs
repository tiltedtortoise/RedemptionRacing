using UnityEngine;

/// <summary>One collision-free playback of a recorded world-space lap, starting at GO.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public sealed class ReplayOpponent : MonoBehaviour
{
    [SerializeField] private RaceManager race;
    [SerializeField] private ReplayLapData replay;
    [SerializeField] private Transform[] visualWheelPivots;
    [SerializeField, Min(0.01f)] private float visualWheelRadius = 0.28f;

    [SerializeField, Range(0f, 1f)] private float ghostOpacity = 0.5f;
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
            CheckMovement(previous, previousRotation, transform.position, transform.rotation);
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

    private void CheckMovement(Vector3 from, Quaternion fromRotation, Vector3 to, Quaternion toRotation)
    {
        // Only the next expected gate can advance this independent instance of RaceProgress.
        for (int attempt = 0; attempt < race.Checkpoints.Count && !progress.IsFinished; attempt++)
        {
            int index = progress.NextExpectedCheckpoint;
            var checkpoint = race.Checkpoints[index];
            var box = checkpoint.GetComponent<BoxCollider>();
            if (!checkpoint.isActiveAndEnabled || !box.enabled || !box.isTrigger) return;
            var frame = box.transform;
            Vector3 a = frame.InverseTransformPoint(from + fromRotation * probeCenter) - box.center;
            Vector3 b = frame.InverseTransformPoint(to + toRotation * probeCenter) - box.center;
            Vector3 padding = Vector3.Max(ProjectExtents(frame, fromRotation), ProjectExtents(frame, toRotation));
            var bounds = new Bounds(Vector3.zero, box.size + padding * 2f);
            Vector3 delta = b - a;
            bool hit = bounds.Contains(a) || bounds.Contains(b);
            if (!hit && delta.sqrMagnitude > .0000001f)
                hit = bounds.IntersectRay(new Ray(a, delta.normalized), out float distance) && distance <= delta.magnitude;
            if (!hit || !progress.TryPass(index)) return;
        }
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
        startedAt = Time.timeAsDouble;
        playing = true;
        CheckMovement(transform.position, transform.rotation, transform.position, transform.rotation);
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
        foreach (var renderer in GetComponentsInChildren<Renderer>(true))
        {
            originalMaterials[renderer] = renderer.sharedMaterials;
            var copies = renderer.sharedMaterials;
            for (int i = 0; i < copies.Length; i++)
            {
                if (copies[i] == null) continue;
                var material = new Material(copies[i]) { name = copies[i].name + " (Ghost Instance)" };
                material.SetFloat("_Surface", 1f);
                material.SetFloat("_Blend", 0f);
                material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                if (material.HasProperty("_SrcBlendAlpha")) material.SetFloat("_SrcBlendAlpha", 1f);
                if (material.HasProperty("_DstBlendAlpha")) material.SetFloat("_DstBlendAlpha", 10f);
                material.SetFloat("_ZWrite", 0f);
                material.SetFloat("_AlphaClip", 0f);
                material.SetOverrideTag("RenderType", "Transparent");
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.DisableKeyword("_ALPHATEST_ON");
                material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                material.DisableKeyword("_ALPHAMODULATE_ON");
                material.SetShaderPassEnabled("ShadowCaster", false);
                material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
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
