using System;
using UnityEngine;

/// <summary>One world-space pose, with seconds measured from the starting finish-line crossing.</summary>
[Serializable]
public struct ReplayFrame
{
    public float timestamp;
    public Vector3 worldPosition;
    public Quaternion worldRotation;

    public ReplayFrame(float timestamp, Vector3 worldPosition, Quaternion worldRotation)
    {
        this.timestamp = timestamp;
        this.worldPosition = worldPosition;
        this.worldRotation = worldRotation;
    }
}
