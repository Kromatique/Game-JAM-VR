using System.Collections;
using UnityEngine;

/// <summary>
/// From a given wave, the ceiling neons blow up one after another (flash, sparks, pop)
/// and stay dead: only the emergency lights and the flashlight are left.
/// </summary>
public class NeonBlackout : MonoBehaviour
{
    public int fromWave = 3;
    public Vector2 delayBetween = new Vector2(0.6f, 2.2f);
    public Material sparkMaterial;
    public AudioClip popClip;

    WaveManager waves;
    bool done;

    void Start()
    {
        waves = FindAnyObjectByType<WaveManager>();
        if (waves != null) waves.WaveStarted += OnWave;
        if (popClip == null) popClip = CreatePop();
    }

    void OnDestroy()
    {
        if (waves != null) waves.WaveStarted -= OnWave;
    }

    void OnWave(int wave)
    {
        if (done || wave < fromWave) return;
        done = true;
        StartCoroutine(BlowAll());
    }

    IEnumerator BlowAll()
    {
        var neons = FindObjectsByType<FlickerLight>();
        // Random order so the blackout travels around the room.
        for (int i = 0; i < neons.Length; i++)
        {
            int j = Random.Range(i, neons.Length);
            (neons[i], neons[j]) = (neons[j], neons[i]);
        }
        yield return new WaitForSeconds(1.5f);
        foreach (var n in neons)
        {
            if (n == null || n.mode != FlickerLight.Mode.BrokenNeon) continue;
            n.Explode(sparkMaterial, popClip);
            yield return new WaitForSeconds(Random.Range(delayBetween.x, delayBetween.y));
        }
    }

    /// <summary>Electric pop: short noise burst with a crackle tail.</summary>
    static AudioClip CreatePop()
    {
        const int rate = 44100;
        int n = (int)(rate * 0.6f);
        var data = new float[n];
        var rng = new System.Random(5);
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / rate;
            float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
            float bang = noise * Mathf.Exp(-t * 35f);
            float crackle = (rng.NextDouble() < 0.02 * Mathf.Exp(-t * 4f) ? noise : 0f) * 0.8f;
            data[i] = Mathf.Clamp((bang + crackle) * 0.9f, -1f, 1f);
        }
        var clip = AudioClip.Create("NeonPop", n, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
