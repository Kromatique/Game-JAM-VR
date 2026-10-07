using UnityEngine;

/// <summary>
/// Ambient particles around the player: dust floating in the air, and slow fog banks
/// creeping on the floor. Both follow the player so they cost little on Quest.
/// </summary>
public class AtmosphereFX : MonoBehaviour
{
    public Material dustMaterial;
    public Material fogMaterial;

    [Header("Dust")]
    public int dustCount = 250;
    public Vector3 dustArea = new Vector3(8f, 2.6f, 8f);
    public Color dustColor = new Color(0.75f, 0.75f, 0.7f, 0.35f);

    [Header("Ground fog")]
    public int fogCount = 30;
    public Vector3 fogArea = new Vector3(16f, 0.3f, 16f);
    public float fogHeight = 0.25f;
    public Vector2 fogSize = new Vector2(2.5f, 5f);
    public Color fogColor = new Color(0.6f, 0.65f, 0.62f, 0.09f);

    Transform head;
    Transform dust;
    Transform fog;
    float floorY;

    void Start()
    {
        var origin = FindAnyObjectByType<Unity.XR.CoreUtils.XROrigin>();
        head = origin != null && origin.Camera != null ? origin.Camera.transform : Camera.main.transform;
        floorY = origin != null ? origin.transform.position.y : 0f;
        if (dustMaterial != null) dust = CreateDust().transform;
        if (fogMaterial != null) fog = CreateFog().transform;
    }

    void LateUpdate()
    {
        if (head == null) return;
        // Shapes follow the player; particles are simulated in world space so they do not slide with the head.
        if (dust != null) dust.position = head.position;
        if (fog != null) fog.position = new Vector3(head.position.x, floorY + fogHeight, head.position.z);
    }

    GameObject CreateDust()
    {
        var go = new GameObject("Dust");
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true;
        main.prewarm = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(6f, 10f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.01f, 0.05f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.006f, 0.016f);
        main.startColor = dustColor;
        main.maxParticles = dustCount;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var emission = ps.emission;
        emission.rateOverTime = dustCount / 8f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = dustArea;
        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.05f;
        noise.frequency = 0.3f;
        noise.scrollSpeed = 0.1f;
        var fade = ps.colorOverLifetime;
        fade.enabled = true;
        fade.color = FadeInOut(dustColor);
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = dustMaterial;
        ps.Play();
        return go;
    }

    GameObject CreateFog()
    {
        var go = new GameObject("Ground Fog");
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true;
        main.prewarm = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(10f, 16f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.02f, 0.12f);
        main.startSize = new ParticleSystem.MinMaxCurve(fogSize.x, fogSize.y);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = fogColor;
        main.maxParticles = fogCount;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var emission = ps.emission;
        emission.rateOverTime = fogCount / 13f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = fogArea;
        var rot = ps.rotationOverLifetime;
        rot.enabled = true;
        rot.z = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f);
        var fade = ps.colorOverLifetime;
        fade.enabled = true;
        fade.color = FadeInOut(fogColor);
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = fogMaterial;
        // Lying flat on the floor, seen from above like a carpet of mist.
        r.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
        r.sortingFudge = 10f;
        ps.Play();
        return go;
    }

    static ParticleSystem.MinMaxGradient FadeInOut(Color c)
    {
        var g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.25f), new GradientAlphaKey(1f, 0.75f), new GradientAlphaKey(0f, 1f) });
        return new ParticleSystem.MinMaxGradient(g);
    }
}
