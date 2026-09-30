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
        startedAt = Time.timeAsDouble;
        playing = true;
    }

    private void LateUpdate()
    {
        if (!Mathf.Approximately(appliedOpacity, ghostOpacity)) ApplyOpacity();
        if (!playing) return;
        Vector3 previousPosition = transform.position;
        PlaybackTime = Mathf.Min((float)(Time.timeAsDouble - startedAt), replay.Duration);
        ApplyPose(PlaybackTime);

        // Cosmetic rolling only; centered pivots prevent imported wheel meshes from wobbling.
        float distance = Vector3.Distance(previousPosition, transform.position);
        float sign = Vector3.Dot(transform.position - previousPosition, transform.forward) >= 0f ? 1f : -1f;
        float angle = -sign * distance / Mathf.Max(0.01f, visualWheelRadius) * Mathf.Rad2Deg;
        if (visualWheelPivots != null)
            foreach (Transform wheel in visualWheelPivots)
                if (wheel != null) wheel.Rotate(Vector3.right, angle, Space.Self);

        if (PlaybackTime >= replay.Duration)
        {
            playing = false;
            HasFinished = true;
        }
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
