using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Loads a baked NavMeshData at runtime. The project has no AI Navigation package,
/// so the bake is done from this component (context menu "Bake NavMesh") and saved as an asset.
/// </summary>
public class ZombieNavMesh : MonoBehaviour
{
    public NavMeshData navMeshData;

    [Header("Bake settings")]
    [Tooltip("Geometry used for the bake (the imported map).")]
    public Transform geometryRoot;
    public float agentRadius = 0.3f;
    public float agentHeight = 1.8f;
    public float agentClimb = 0.4f;
    public float agentSlope = 45f;
    [Tooltip("Bake only inside this world-space box (e.g. the building interior). Otherwise uses the map bounds.")]
    public bool useBakeBox;
    public Vector3 bakeCenter;
    public Vector3 bakeSize = new Vector3(20f, 3f, 20f);

    NavMeshDataInstance instance;

    void OnEnable()
    {
        if (navMeshData != null) instance = NavMesh.AddNavMeshData(navMeshData);
    }

    void OnDisable()
    {
        instance.Remove();
    }

    public NavMeshBuildSettings BuildSettings()
    {
        var settings = NavMesh.GetSettingsByID(0);
        settings.agentRadius = agentRadius;
        settings.agentHeight = agentHeight;
        settings.agentClimb = agentClimb;
        settings.agentSlope = agentSlope;
        return settings;
    }

#if UNITY_EDITOR
    [ContextMenu("Bake NavMesh")]
    public void Bake()
    {
        Transform root = geometryRoot != null ? geometryRoot : transform;
        var sources = new List<NavMeshBuildSource>();
        NavMeshBuilder.CollectSources(root, ~0, NavMeshCollectGeometry.RenderMeshes, 0,
            new List<NavMeshBuildMarkup>(), sources);

        var renderers = root.GetComponentsInChildren<Renderer>();
        var bounds = renderers.Length > 0 ? renderers[0].bounds : new Bounds(root.position, Vector3.one * 50f);
        foreach (var r in renderers) bounds.Encapsulate(r.bounds);
        bounds.Expand(2f);
        if (useBakeBox)
        {
            bounds = new Bounds(bakeCenter, bakeSize);
            // Drop geometry entirely above the box (ceiling, roof), otherwise its top becomes walkable.
            float top = bounds.max.y;
            sources.RemoveAll(s => s.sourceObject is Mesh m && WorldMinY(m.bounds, s.transform) > top);
        }

        var data = NavMeshBuilder.BuildNavMeshData(BuildSettings(), sources, bounds, Vector3.zero, Quaternion.identity);
        data.name = "ZombieNavMesh";

        const string path = "Assets/Zombie/ZombieNavMesh.asset";
        UnityEditor.AssetDatabase.DeleteAsset(path);
        UnityEditor.AssetDatabase.CreateAsset(data, path);
        UnityEditor.AssetDatabase.SaveAssets();

        UnityEditor.Undo.RecordObject(this, "Bake NavMesh");
        navMeshData = data;
        UnityEditor.EditorUtility.SetDirty(this);
        if (isActiveAndEnabled)
        {
            instance.Remove();
            instance = NavMesh.AddNavMeshData(navMeshData);
        }
        Debug.Log($"[ZombieNavMesh] Bake fini : {sources.Count} sources, bounds {bounds}");
    }

    static float WorldMinY(Bounds b, Matrix4x4 m)
    {
        float min = float.MaxValue;
        for (int i = 0; i < 8; i++)
        {
            var corner = new Vector3(
                (i & 1) == 0 ? b.min.x : b.max.x,
                (i & 2) == 0 ? b.min.y : b.max.y,
                (i & 4) == 0 ? b.min.z : b.max.z);
            min = Mathf.Min(min, m.MultiplyPoint3x4(corner).y);
        }
        return min;
    }
#endif
}
