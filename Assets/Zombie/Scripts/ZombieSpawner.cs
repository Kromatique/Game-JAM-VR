using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Spawns zombies at random NavMesh points inside the bounds of a map (or at given spawn points).
/// </summary>
public class ZombieSpawner : MonoBehaviour
{
    public GameObject zombiePrefab;
    [Tooltip("Other zombie types, picked at random with the main prefab.")]
    public GameObject[] variantPrefabs;

    [Tooltip("Spawn area = combined bounds of this object's renderers (the imported map). Ignored if useAreaBox.")]
    public Transform spawnArea;
    [Tooltip("Use the box below (world space) as spawn area, e.g. the inside of the building.")]
    public bool useAreaBox;
    public Vector3 areaCenter;
    public Vector3 areaSize = new Vector3(10f, 1f, 10f);
    [Tooltip("Optional: if set, zombies spawn only near these points.")]
    public Transform[] spawnPoints;

    [Header("Rules")]
    [Tooltip("Off when a WaveManager drives the spawns.")]
    public bool autoSpawn = true;
    public int maxAlive = 8;
    [Tooltip("0 = infinite.")]
    public int totalToSpawn = 0;
    public float spawnInterval = 3f;
    public float initialDelay = 2f;
    public float minDistanceFromPlayer = 6f;
    public int maxTriesPerSpawn = 30;

    [Header("Difficulty")]
    [Tooltip("Spawn interval decreases by this factor after each kill (1 = no change).")]
    public float intervalMultiplierPerKill = 0.97f;
    public float minSpawnInterval = 0.8f;

    public int AliveCount
    {
        get
        {
            alive.RemoveAll(z => z == null || z.IsDead);
            return alive.Count;
        }
    }
    public int Kills { get; private set; }

    readonly List<ZombieHealth> alive = new List<ZombieHealth>();
    Bounds areaBounds;
    bool hasArea;
    float nextSpawn;
    int spawned;
    float currentInterval;

    void Start()
    {
        currentInterval = spawnInterval;
        nextSpawn = Time.time + initialDelay;
        if (useAreaBox)
        {
            areaBounds = new Bounds(areaCenter, areaSize);
            hasArea = true;
        }
        else if (spawnArea != null)
        {
            var renderers = spawnArea.GetComponentsInChildren<Renderer>();
            if (renderers.Length > 0)
            {
                areaBounds = renderers[0].bounds;
                foreach (var r in renderers) areaBounds.Encapsulate(r.bounds);
                hasArea = true;
            }
        }
    }

    void Update()
    {
        if (!autoSpawn || zombiePrefab == null || Time.time < nextSpawn) return;
        if (totalToSpawn > 0 && spawned >= totalToSpawn) return;

        alive.RemoveAll(z => z == null);
        if (alive.Count >= maxAlive) return;

        nextSpawn = Time.time + currentInterval;
        if (TryGetSpawnPosition(out Vector3 pos)) Spawn(pos);
    }

    public GameObject Spawn(Vector3 position)
    {
        var prefab = zombiePrefab;
        int count = 1 + (variantPrefabs != null ? variantPrefabs.Length : 0);
        int pick = Random.Range(0, count);
        if (pick > 0 && variantPrefabs[pick - 1] != null) prefab = variantPrefabs[pick - 1];
        return Spawn(position, prefab);
    }

    /// <summary>Spawns a given zombie type at a random valid point. Returns null if no point was found.</summary>
    public GameObject TrySpawn(GameObject prefab)
    {
        return TryGetSpawnPosition(out Vector3 pos) ? Spawn(pos, prefab) : null;
    }

    public GameObject Spawn(Vector3 position, GameObject prefab)
    {
        Vector3 player = PlayerPosition();
        Vector3 look = player - position;
        look.y = 0f;
        Quaternion rot = look.sqrMagnitude > 0.01f ? Quaternion.LookRotation(look) : Quaternion.identity;

        var go = Instantiate(prefab, position, rot);
        PS1Style.Apply(go);
        var agent = go.GetComponent<NavMeshAgent>();
        if (agent) agent.Warp(position);

        var health = go.GetComponent<ZombieHealth>();
        if (health)
        {
            alive.Add(health);
            health.Died += OnZombieDied;
        }
        spawned++;
        return go;
    }

    /// <summary>Spawns a zombie outside a boarded window; it tears the planks off before coming in.</summary>
    public GameObject SpawnAtBarricade(Barricade barricade, GameObject prefab)
    {
        var go = Instantiate(prefab, barricade.SpawnPoint(), Quaternion.LookRotation(barricade.transform.forward));
        PS1Style.Apply(go);
        var health = go.GetComponent<ZombieHealth>();
        if (health)
        {
            alive.Add(health);
            health.Died += OnZombieDied;
        }
        spawned++;
        go.AddComponent<ZombieBreach>().Begin(barricade);
        return go;
    }

    void OnZombieDied(ZombieHealth z)
    {
        alive.Remove(z);
        Kills++;
        currentInterval = Mathf.Max(minSpawnInterval, currentInterval * intervalMultiplierPerKill);
    }

    bool TryGetSpawnPosition(out Vector3 result)
    {
        Vector3 player = PlayerPosition();
        for (int i = 0; i < maxTriesPerSpawn; i++)
        {
            Vector3 candidate;
            if (spawnPoints != null && spawnPoints.Length > 0)
            {
                var p = spawnPoints[Random.Range(0, spawnPoints.Length)];
                if (p == null) continue;
                Vector2 offset = Random.insideUnitCircle * 1.5f;
                candidate = p.position + new Vector3(offset.x, 0f, offset.y);
            }
            else if (hasArea)
            {
                candidate = new Vector3(
                    Random.Range(areaBounds.min.x, areaBounds.max.x),
                    Random.Range(areaBounds.min.y, areaBounds.max.y),
                    Random.Range(areaBounds.min.z, areaBounds.max.z));
            }
            else
            {
                Vector2 c = Random.insideUnitCircle.normalized * Random.Range(minDistanceFromPlayer, minDistanceFromPlayer * 3f);
                candidate = player + new Vector3(c.x, 0f, c.y);
            }

            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 3f, NavMesh.AllAreas)) continue;
            if (hasArea && !areaBounds.Contains(hit.position)) continue;
            if (Vector3.Distance(hit.position, player) < minDistanceFromPlayer) continue;

            // Only keep points from which the player is reachable.
            var path = new NavMeshPath();
            if (NavMesh.SamplePosition(player, out NavMeshHit playerHit, 3f, NavMesh.AllAreas)
                && (!NavMesh.CalculatePath(hit.position, playerHit.position, NavMesh.AllAreas, path)
                    || path.status != NavMeshPathStatus.PathComplete))
                continue;

            result = hit.position;
            return true;
        }
        result = default;
        return false;
    }

    static Vector3 PlayerPosition()
    {
        var cam = Camera.main;
        return cam != null ? PlayerFeet(cam.transform) : Vector3.zero;
    }

    static XROrigin cachedOrigin;

    /// <summary>Head position projected to the floor of the XR Origin.</summary>
    public static Vector3 PlayerFeet(Transform head)
    {
        if (cachedOrigin == null) cachedOrigin = FindAnyObjectByType<XROrigin>();
        Vector3 p = head.position;
        if (cachedOrigin != null) p.y = cachedOrigin.transform.position.y;
        return p;
    }

    void OnDrawGizmosSelected()
    {
        if (useAreaBox && !Application.isPlaying) areaBounds = new Bounds(areaCenter, areaSize);
        else if (!hasArea) return;
        Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.4f);
        Gizmos.DrawWireCube(areaBounds.center, areaBounds.size);
    }
}
