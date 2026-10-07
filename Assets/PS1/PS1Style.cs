using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;

/// <summary>
/// Gives the scene a PS1 look at runtime: swaps the materials of the map, zombies and gun for
/// the "PS1/Lit" shader (keeping texture and color), and sets a dark fog.
/// Disable this component to get the original look back. Original assets are not modified.
/// </summary>
[DefaultExecutionOrder(-200)]
public class PS1Style : MonoBehaviour
{
    public static PS1Style Instance { get; private set; }

    public Shader ps1Shader;
    [Tooltip("Virtual screen resolution vertices snap to. Lower = more wobble.")]
    public Vector2 snapResolution = new Vector2(320, 240);
    [Range(0f, 1f)] public float affineAmount = 0.5f;
    public float colorLevels = 32f;
    [Range(0f, 1f)] public float ditherStrength = 1f;
    [Range(0f, 2f)] public float ambientBoost = 1f;
    [Tooltip("Draw both faces (the map has single-sided ceilings/walls seen from behind).")]
    public bool doubleSided = true;
    [Tooltip("Materials whose name contains one of these get a depth offset (decals).")]
    public string[] decalKeywords = { "Saleté", "Salete", "Sang", "sticker", "stciker", "Papier", "Affiche", "Sérigraphie" };

    [Header("Fog")]
    public bool setFog = true;
    public Color fogColor = new Color(0.08f, 0.09f, 0.08f);
    public float fogDensity = 0.1f;

    [Header("Resolution")]
    [Tooltip("Eye render scale. Below 1 gives a lower resolution, blurrier image (and better performance).")]
    [Range(0.5f, 1f)] public float renderScale = 1f;

    readonly Dictionary<Material, Material> cache = new Dictionary<Material, Material>();

    static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
    static readonly int MainTex = Shader.PropertyToID("_MainTex");
    static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
    static readonly int Color = Shader.PropertyToID("_Color");
    static readonly int Cull = Shader.PropertyToID("_Cull");
    static readonly int Cutoff = Shader.PropertyToID("_Cutoff");
    static readonly int AlphaClip = Shader.PropertyToID("_AlphaClip");

    void Awake()
    {
        Instance = this;
        if (ps1Shader == null) ps1Shader = Shader.Find("PS1/Lit");
        if (ps1Shader == null || !ps1Shader.isSupported)
        {
            Debug.LogWarning("[PS1Style] PS1/Lit shader missing or unsupported.");
            enabled = false;
            return;
        }

        if (setFog)
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = fogColor;
            RenderSettings.fogDensity = fogDensity;
        }
        // Default flat ambient (DarkAmbience overrides it).
        Shader.SetGlobalColor("_PS1Ambient", RenderSettings.ambientMode == UnityEngine.Rendering.AmbientMode.Flat
            ? RenderSettings.ambientLight : RenderSettings.ambientEquatorColor);
        if (renderScale < 1f) UnityEngine.XR.XRSettings.eyeTextureResolutionScale = renderScale;

        // Everything in the scene except the player's rig (hands, controllers, UI).
        var origin = FindAnyObjectByType<XROrigin>();
        foreach (var r in FindObjectsByType<Renderer>(FindObjectsInactive.Include))
        {
            if (!(r is MeshRenderer || r is SkinnedMeshRenderer)) continue;
            if (origin != null && r.transform.IsChildOf(origin.transform) && r.GetComponentInParent<Gun>() == null) continue;
            ApplyTo(r);
        }
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        foreach (var m in cache.Values) Destroy(m);
    }

    /// <summary>Applies the style to every mesh renderer under a spawned object (zombies...).</summary>
    public static void Apply(GameObject go)
    {
        if (Instance == null || !Instance.enabled) return;
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            if (r is MeshRenderer || r is SkinnedMeshRenderer) Instance.ApplyTo(r);
    }

    void ApplyTo(Renderer r)
    {
        var mats = r.sharedMaterials;
        bool changed = false;
        for (int i = 0; i < mats.Length; i++)
        {
            var src = mats[i];
            if (src == null || src.shader == ps1Shader) continue;
            // Unlit materials are light sources (neon tubes): keep them bright.
            if (src.shader.name.Contains("Unlit")) continue;
            // Leave transparent materials (glass, effects) alone.
            if (src.renderQueue >= 2500 && src.renderQueue < 5000 && src.GetTag("RenderType", false) == "Transparent") continue;
            mats[i] = Convert(src);
            changed = true;
        }
        if (changed) r.sharedMaterials = mats;
    }

    Material Convert(Material src)
    {
        if (cache.TryGetValue(src, out var m)) return m;
        m = new Material(ps1Shader) { name = src.name + " (PS1)" };
        Texture tex = src.HasProperty(BaseMap) ? src.GetTexture(BaseMap) : src.HasProperty(MainTex) ? src.GetTexture(MainTex) : null;
        if (tex != null)
        {
            m.SetTexture(BaseMap, tex);
            if (src.HasProperty(BaseMap))
            {
                m.SetTextureScale(BaseMap, src.GetTextureScale(BaseMap));
                m.SetTextureOffset(BaseMap, src.GetTextureOffset(BaseMap));
            }
        }
        var col = src.HasProperty(BaseColor) ? src.GetColor(BaseColor) : src.HasProperty(Color) ? src.GetColor(Color) : UnityEngine.Color.white;
        m.SetColor(BaseColor, col);
        if (doubleSided) m.SetFloat(Cull, 0f);
        else if (src.HasProperty(Cull)) m.SetFloat(Cull, src.GetFloat(Cull));
        bool clip = src.IsKeywordEnabled("_ALPHATEST_ON") || (src.HasProperty(AlphaClip) && src.GetFloat(AlphaClip) > 0.5f);
        m.SetFloat(AlphaClip, clip ? 1f : 0f);
        if (src.HasProperty(Cutoff)) m.SetFloat(Cutoff, src.GetFloat(Cutoff));

        m.SetVector("_SnapResolution", snapResolution);
        m.SetFloat("_AffineAmount", affineAmount);
        m.SetFloat("_ColorDepth", colorLevels);
        m.SetFloat("_DitherStrength", ditherStrength);
        m.SetFloat("_AmbientBoost", ambientBoost);
        // Decals lying on walls/floors (dirt, blood, stickers, papers) are coplanar with them:
        // pull them toward the camera in depth so they do not flicker (z-fighting).
        foreach (var key in decalKeywords)
            if (src.name.IndexOf(key, System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                m.SetFloat("_OffsetFactor", -1f);
                m.SetFloat("_OffsetUnits", -2f);
                break;
            }
        m.enableInstancing = true;
        cache[src] = m;
        return m;
    }
}
