using UnityEngine;

/// <summary>
/// Failing neon / emergency light: random dropouts and buzzing, or a slow pulse.
/// Also dims an optional emissive renderer (the tube) with the light.
/// </summary>
[RequireComponent(typeof(Light))]
public class FlickerLight : MonoBehaviour
{
    public enum Mode { BrokenNeon, Pulse }

    public Mode mode = Mode.BrokenNeon;
    public float baseIntensity = 1.2f;
    [Tooltip("Chance per second to start a dropout burst (neon).")]
    public float dropoutRate = 0.35f;
    public float pulseSpeed = 1.2f;
    public Renderer tube;
    public AudioSource buzz;

    Light lightSource;
    float burstUntil;
    float nextToggle;
    bool on = true;
    Color tubeColor;
    MaterialPropertyBlock block;
    static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

    [Tooltip("Adds a quiet 3D electric hum if no AudioSource is given.")]
    public bool makeBuzz = true;

    static AudioClip buzzClip;

    void Awake()
    {
        lightSource = GetComponent<Light>();
        if (buzz == null && makeBuzz)
        {
            if (buzzClip == null) buzzClip = CreateBuzz();
            buzz = gameObject.AddComponent<AudioSource>();
            buzz.clip = buzzClip;
            buzz.loop = true;
            buzz.spatialBlend = 1f;
            buzz.rolloffMode = AudioRolloffMode.Linear;
            buzz.minDistance = 0.5f;
            buzz.maxDistance = 6f;
            buzz.Play();
        }
        if (tube != null)
        {
            block = new MaterialPropertyBlock();
            tubeColor = tube.sharedMaterial.HasProperty(BaseColor) ? tube.sharedMaterial.GetColor(BaseColor) : Color.white;
        }
        // Different lights do not flicker in sync.
        nextToggle = Time.time + Random.Range(0f, 2f);
    }

    bool dead;

    /// <summary>Blows the neon: white flash, sparks falling, pop, then it stays off.</summary>
    public void Explode(Material sparkMaterial, AudioClip pop)
    {
        if (dead) return;
        dead = true;
        StartCoroutine(ExplodeRoutine(sparkMaterial, pop));
    }

    System.Collections.IEnumerator ExplodeRoutine(Material sparkMaterial, AudioClip pop)
    {
        if (buzz != null) buzz.Stop();
        if (pop != null)
        {
            var src = gameObject.AddComponent<AudioSource>();
            src.spatialBlend = 1f;
            src.maxDistance = 20f;
            src.PlayOneShot(pop);
        }
        if (sparkMaterial != null) SpawnSparks(sparkMaterial);
        lightSource.intensity = baseIntensity * 5f;
        SetTube(2f);
        yield return new WaitForSeconds(0.07f);
        lightSource.intensity = 0f;
        SetTube(0.05f);
        yield return new WaitForSeconds(0.12f);
        lightSource.intensity = baseIntensity * 2f;
        SetTube(1f);
        yield return new WaitForSeconds(0.05f);
        lightSource.enabled = false;
        SetTube(0.03f);
    }

    void SetTube(float k)
    {
        if (tube == null) return;
        block.SetColor(BaseColor, tubeColor * k);
        tube.SetPropertyBlock(block);
    }

    void SpawnSparks(Material mat)
    {
        var go = new GameObject("Sparks");
        go.transform.SetPositionAndRotation(transform.position, Quaternion.identity);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.duration = 0.5f;
        main.loop = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.4f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 4f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.015f, 0.035f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.9f, 0.5f), new Color(1f, 0.5f, 0.15f));
        main.gravityModifier = 1.2f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.stopAction = ParticleSystemStopAction.Destroy;
        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 60), new ParticleSystem.Burst(0.15f, 25) });
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.15f;
        var col = ps.collision;
        col.enabled = true;
        col.type = ParticleSystemCollisionType.World;
        col.dampen = 0.5f;
        col.bounce = 0.3f;
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = mat;
        r.renderMode = ParticleSystemRenderMode.Stretch;
        r.velocityScale = 0.04f;
        ps.Play();
    }

    void Update()
    {
        if (dead) return;
        float k;
        if (mode == Mode.Pulse)
        {
            k = 0.35f + 0.65f * Mathf.Pow(0.5f + 0.5f * Mathf.Sin(Time.time * pulseSpeed * Mathf.PI * 2f), 2f);
        }
        else
        {
            if (Time.time > burstUntil && Random.value < dropoutRate * Time.deltaTime)
                burstUntil = Time.time + Random.Range(0.15f, 1.2f);

            if (Time.time < burstUntil)
            {
                if (Time.time >= nextToggle)
                {
                    on = !on;
                    nextToggle = Time.time + Random.Range(0.02f, 0.12f);
                }
            }
            else on = true;
            k = on ? Random.Range(0.92f, 1f) : Random.Range(0f, 0.08f);
        }

        lightSource.intensity = baseIntensity * k;
        if (buzz != null) buzz.volume = 0.15f * k;
        if (tube != null)
        {
            block.SetColor(BaseColor, tubeColor * Mathf.Lerp(0.15f, 1f, k));
            tube.SetPropertyBlock(block);
        }
    }

    /// <summary>1 s loop of 100 Hz mains hum with harmonics (whole cycles, seamless).</summary>
    static AudioClip CreateBuzz()
    {
        const int rate = 22050;
        var data = new float[rate];
        for (int i = 0; i < rate; i++)
        {
            float t = (float)i / rate;
            data[i] = (Mathf.Sin(2f * Mathf.PI * 100f * t) * 0.5f + Mathf.Sin(2f * Mathf.PI * 200f * t) * 0.25f
                       + Mathf.Sign(Mathf.Sin(2f * Mathf.PI * 300f * t)) * 0.08f) * 0.5f;
        }
        var clip = AudioClip.Create("NeonBuzz", rate, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
