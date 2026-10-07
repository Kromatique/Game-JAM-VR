using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Call of Duty Zombies style rounds: each wave has a set number of zombies to kill,
/// zombies get tougher and faster, new types unlock, and there is a short break between waves.
/// Uses the ZombieSpawner only for spawn points and bookkeeping (its auto spawn is turned off).
/// </summary>
public class WaveManager : MonoBehaviour
{
    [Serializable]
    public class ZombieType
    {
        public GameObject prefab;
        [Tooltip("First wave where this type can appear.")]
        public int fromWave = 1;
        [Tooltip("Relative chance to be picked.")]
        public float weight = 1f;
        [Tooltip("Extra weight added each wave after it unlocks.")]
        public float weightPerWave = 0f;
    }

    public ZombieSpawner spawner;
    public ZombieType[] types;

    [Header("Waves")]
    [Tooltip("Zombies to kill in the first waves; after that +zombiesPerWave each wave.")]
    public int[] firstWaveCounts = { 6, 8, 13, 18, 24 };
    public int zombiesPerWave = 5;
    [Tooltip("Max zombies alive at once (performance on Quest).")]
    public int maxAlive = 10;
    public float timeBetweenWaves = 8f;
    public float firstWaveDelay = 3f;

    [Header("Scaling per wave")]
    [Tooltip("+% health per wave (0.15 = +15%).")]
    public float healthPerWave = 0.15f;
    [Tooltip("Extra chance to be a runner per wave.")]
    public float runnerChancePerWave = 0.08f;
    public float speedPerWave = 0.03f;
    public float maxSpeedMultiplier = 1.4f;
    public float spawnInterval = 2f;
    public float minSpawnInterval = 0.6f;

    public event Action<int> WaveStarted;
    public event Action<int> WaveCleared;

    public int Wave { get; private set; }
    public int RemainingInWave => Mathf.Max(0, toKill - killedThisWave);
    public bool InBreak { get; private set; }
    public int TotalKills => spawner != null ? spawner.Kills : 0;

    int toKill;
    int spawnedThisWave;
    int killedThisWave;
    int killsAtWaveStart;
    float nextSpawn;
    bool running;

    void Awake()
    {
        if (spawner == null) spawner = GetComponent<ZombieSpawner>();
        if (spawner == null) spawner = FindAnyObjectByType<ZombieSpawner>();
    }

    public void StartWaves()
    {
        if (running) return;
        running = true;
        spawner.autoSpawn = false;
        spawner.enabled = true;
        StartCoroutine(Run());
    }

    IEnumerator Run()
    {
        yield return new WaitForSeconds(firstWaveDelay);
        while (true)
        {
            Wave++;
            toKill = Wave <= firstWaveCounts.Length ? firstWaveCounts[Wave - 1] : firstWaveCounts[firstWaveCounts.Length - 1] + (Wave - firstWaveCounts.Length) * zombiesPerWave;
            spawnedThisWave = 0;
            killedThisWave = 0;
            killsAtWaveStart = spawner.Kills;
            InBreak = false;
            WaveStarted?.Invoke(Wave);

            float interval = Mathf.Max(minSpawnInterval, spawnInterval * Mathf.Pow(0.92f, Wave - 1));
            nextSpawn = Time.time + 1f;
            while (killedThisWave < toKill)
            {
                killedThisWave = spawner.Kills - killsAtWaveStart;
                if (spawnedThisWave < toKill && Time.time >= nextSpawn && spawner.AliveCount < maxAlive)
                {
                    nextSpawn = Time.time + interval;
                    if (SpawnOne()) spawnedThisWave++;
                }
                yield return null;
            }

            InBreak = true;
            WaveCleared?.Invoke(Wave);
            yield return new WaitForSeconds(timeBetweenWaves);
        }
    }

    bool SpawnOne()
    {
        var type = PickType();
        if (type == null || type.prefab == null) return false;
        // Zombies come in through the boarded windows when the map has some.
        var go = Barricade.All.Count > 0
            ? spawner.SpawnAtBarricade(Barricade.All[UnityEngine.Random.Range(0, Barricade.All.Count)], type.prefab)
            : spawner.TrySpawn(type.prefab);
        if (go == null) return false;

        int w = Wave - 1;
        var health = go.GetComponent<ZombieHealth>();
        if (health) health.SetMaxHealth(health.maxHealth * (1f + healthPerWave * w));
        var ai = go.GetComponent<ZombieAI>();
        if (ai) ai.ApplyDifficulty(runnerChancePerWave * w, Mathf.Min(maxSpeedMultiplier, 1f + speedPerWave * w));
        return true;
    }

    ZombieType PickType()
    {
        float total = 0f;
        foreach (var t in types)
            if (t.prefab != null && Wave >= t.fromWave) total += Weight(t);
        if (total <= 0f) return null;
        float r = UnityEngine.Random.value * total;
        foreach (var t in types)
        {
            if (t.prefab == null || Wave < t.fromWave) continue;
            r -= Weight(t);
            if (r <= 0f) return t;
        }
        return types[0];
    }

    float Weight(ZombieType t) => t.weight + t.weightPerWave * (Wave - t.fromWave);
}
