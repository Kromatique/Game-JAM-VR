using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Night horror mood: near-black ambient and fog, a faint cold moonlight, black sky,
/// a low procedural drone and distant zombie noises from time to time.
/// </summary>
[DefaultExecutionOrder(-150)]
public class DarkAmbience : MonoBehaviour
{
    [Header("Light")]
    public Light moonLight;
    public float moonIntensity = 0.08f;
    public Color moonColor = new Color(0.55f, 0.65f, 0.9f);
    public Color ambientColor = new Color(0.025f, 0.028f, 0.035f);
    [Tooltip("Murky grey so the fog is visible against the dark, instead of plain black.")]
    public Color fogColor = new Color(0.045f, 0.05f, 0.052f);
    [Tooltip("Linear: clear up to fogStart, opaque at fogEnd (hard view limit).")]
    public FogMode fogMode = FogMode.Linear;
    public float fogStart = 1.5f;
    public float fogEnd = 9f;
    public float fogDensity = 0.16f;

    [Header("Sound")]
    [Range(0f, 1f)] public float droneVolume = 0.35f;
    public AudioClip[] distantClips;
    public Vector2 distantInterval = new Vector2(8f, 20f);
    [Range(0f, 1f)] public float distantVolume = 0.25f;

    AudioSource drone;
    AudioSource distant;

    void Awake()
    {
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = ambientColor;
        RenderSettings.fog = true;
        RenderSettings.fogMode = fogMode;
        RenderSettings.fogColor = fogColor;
        RenderSettings.fogDensity = fogDensity;
        RenderSettings.fogStartDistance = fogStart;
        RenderSettings.fogEndDistance = fogEnd;
        RenderSettings.skybox = null;
        DynamicGI.UpdateEnvironment();
        Shader.SetGlobalColor("_PS1Ambient", ambientColor);

        if (moonLight == null)
            foreach (var l in FindObjectsByType<Light>())
                if (l.type == LightType.Directional) { moonLight = l; break; }
        if (moonLight != null)
        {
            moonLight.intensity = moonIntensity;
            moonLight.color = moonColor;
        }

        var ps1 = FindAnyObjectByType<PS1Style>();
        if (ps1 != null) ps1.setFog = false;
    }

    void Start()
    {
        foreach (var cam in FindObjectsByType<Camera>())
        {
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = fogColor;
        }

        drone = gameObject.AddComponent<AudioSource>();
        drone.clip = CreateDrone();
        drone.loop = true;
        drone.spatialBlend = 0f;
        drone.volume = droneVolume;
        drone.Play();

        distant = gameObject.AddComponent<AudioSource>();
        distant.spatialBlend = 0f;
        if (distantClips != null && distantClips.Length > 0) StartCoroutine(DistantNoises());
    }

    IEnumerator DistantNoises()
    {
        while (true)
        {
            yield return new WaitForSeconds(Random.Range(distantInterval.x, distantInterval.y));
            var clip = distantClips[Random.Range(0, distantClips.Length)];
            if (clip == null) continue;
            distant.pitch = Random.Range(0.6f, 0.8f);
            distant.panStereo = Random.Range(-0.8f, 0.8f);
            distant.PlayOneShot(clip, distantVolume);
        }
    }

    /// <summary>Seamless 8 s loop: filtered brown noise + two detuned low tones that beat slowly.</summary>
    static AudioClip CreateDrone()
    {
        const int rate = 22050;
        const float seconds = 8f;
        int n = (int)(rate * seconds);
        var data = new float[n];
        var rng = new System.Random(11);
        float brown = 0f, low = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / rate;
            brown = Mathf.Clamp(brown + ((float)rng.NextDouble() * 2f - 1f) * 0.02f, -1f, 1f);
            low = Mathf.Lerp(low, brown, 0.05f);
            // Whole number of cycles in 8 s so the loop is seamless.
            float tone = Mathf.Sin(2f * Mathf.PI * 41f * t) * 0.35f + Mathf.Sin(2f * Mathf.PI * 41.5f * t) * 0.3f
                         + Mathf.Sin(2f * Mathf.PI * 55f * t) * 0.12f;
            float swell = 0.75f + 0.25f * Mathf.Sin(2f * Mathf.PI * t / seconds);
            data[i] = (low * 1.6f + tone * 0.5f) * swell * 0.6f;
        }
        // Fade the noise seam.
        int fade = rate / 4;
        for (int i = 0; i < fade; i++)
        {
            float k = (float)i / fade;
            data[i] = data[i] * k + data[n - fade + i] * (1f - k);
        }
        var clip = AudioClip.Create("Drone", n - fade, 1, rate, false);
        var trimmed = new float[n - fade];
        System.Array.Copy(data, trimmed, n - fade);
        clip.SetData(trimmed, 0);
        return clip;
    }
}
