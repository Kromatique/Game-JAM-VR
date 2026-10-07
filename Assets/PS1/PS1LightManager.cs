using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Feeds point/spot Lights of the scene to the PS1/Lit shader (URP additional lights are off
/// in this project for Quest performance). The nearest enabled point lights to the player are
/// sent each frame (max 8), plus the brightest spot light (the gun flashlight).
/// </summary>
[DefaultExecutionOrder(500)]
public class PS1LightManager : MonoBehaviour
{
    const int MaxPoints = 8;

    [Tooltip("How often the list of lights in the scene is refreshed (s).")]
    public float rescanInterval = 0.5f;

    static readonly int PointPos = Shader.PropertyToID("_PS1PointPos");
    static readonly int PointColor = Shader.PropertyToID("_PS1PointColor");
    static readonly int PointCount = Shader.PropertyToID("_PS1PointCount");
    static readonly int SpotPos = Shader.PropertyToID("_PS1SpotPos");
    static readonly int SpotDir = Shader.PropertyToID("_PS1SpotDir");
    static readonly int SpotColor = Shader.PropertyToID("_PS1SpotColor");
    static readonly int SpotCone = Shader.PropertyToID("_PS1SpotCone");

    readonly Vector4[] pos = new Vector4[MaxPoints];
    readonly Vector4[] col = new Vector4[MaxPoints];
    readonly List<Light> lights = new List<Light>();
    readonly List<Light> sorted = new List<Light>();
    float nextScan;
    Transform viewer;

    void OnDisable()
    {
        Shader.SetGlobalFloat(PointCount, 0);
        Shader.SetGlobalVector(SpotDir, Vector4.zero);
    }

    void LateUpdate()
    {
        if (Time.time >= nextScan)
        {
            nextScan = Time.time + rescanInterval;
            lights.Clear();
            foreach (var l in FindObjectsByType<Light>())
                if (l.type == LightType.Point || l.type == LightType.Spot) lights.Add(l);
            if (Camera.main != null) viewer = Camera.main.transform;
        }

        Vector3 eye = viewer != null ? viewer.position : Vector3.zero;
        sorted.Clear();
        Light spot = null;
        float spotPower = 0f;
        foreach (var l in lights)
        {
            if (l == null || !l.isActiveAndEnabled || l.intensity <= 0.001f) continue;
            if (l.type == LightType.Spot)
            {
                if (l.intensity > spotPower) { spot = l; spotPower = l.intensity; }
            }
            else sorted.Add(l);
        }
        sorted.Sort((a, b) => (a.transform.position - eye).sqrMagnitude.CompareTo((b.transform.position - eye).sqrMagnitude));

        int n = Mathf.Min(MaxPoints, sorted.Count);
        for (int i = 0; i < n; i++)
        {
            var l = sorted[i];
            Vector3 p = l.transform.position;
            pos[i] = new Vector4(p.x, p.y, p.z, l.range);
            Color c = l.color.linear * l.intensity;
            col[i] = new Vector4(c.r, c.g, c.b, 0f);
        }
        Shader.SetGlobalVectorArray(PointPos, pos);
        Shader.SetGlobalVectorArray(PointColor, col);
        Shader.SetGlobalFloat(PointCount, n);

        if (spot != null)
        {
            Vector3 p = spot.transform.position;
            Vector3 d = spot.transform.forward;
            Color c = spot.color.linear * spot.intensity;
            Shader.SetGlobalVector(SpotPos, new Vector4(p.x, p.y, p.z, spot.range));
            Shader.SetGlobalVector(SpotDir, new Vector4(d.x, d.y, d.z, 1f));
            Shader.SetGlobalVector(SpotColor, new Vector4(c.r, c.g, c.b, 0f));
            Shader.SetGlobalVector(SpotCone, new Vector4(
                Mathf.Cos(spot.spotAngle * 0.5f * Mathf.Deg2Rad),
                Mathf.Cos(spot.innerSpotAngle * 0.5f * Mathf.Deg2Rad), 0f, 0f));
        }
        else Shader.SetGlobalVector(SpotDir, Vector4.zero);
    }
}
