using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Crosshair placed where the gun's bullet would hit, so aiming follows the hand.
/// Turns red over a zombie. Optional thin laser from the muzzle.
/// </summary>
[RequireComponent(typeof(Gun))]
public class GunCrosshair : MonoBehaviour
{
    [Tooltip("Angular size of the crosshair, so it looks the same at any distance.")]
    public float angularSize = 1.6f;
    public float maxDistance = 30f;
    public Color normalColor = new Color(1f, 1f, 1f, 0.9f);
    public Color enemyColor = new Color(1f, 0.15f, 0.1f, 1f);

    [Header("Laser")]
    public bool showLaser = true;
    public Material laserMaterial;
    public Color laserColor = new Color(1f, 0.2f, 0.1f, 0.35f);

    Gun gun;
    Transform head;
    Transform reticle;
    Image reticleImage;
    LineRenderer laser;

    void Awake()
    {
        gun = GetComponent<Gun>();
    }

    void Start()
    {
        var cam = Camera.main;
        head = cam != null ? cam.transform : null;
        BuildReticle();
        if (showLaser) BuildLaser();
    }

    void OnDisable()
    {
        if (reticle) reticle.gameObject.SetActive(false);
        if (laser) laser.enabled = false;
    }

    void LateUpdate()
    {
        if (reticle == null) return;
        bool active = gun.IsEquipped || transform.parent != null;
        if (!active)
        {
            reticle.gameObject.SetActive(false);
            if (laser) laser.enabled = false;
            return;
        }
        if (head == null && Camera.main != null) head = Camera.main.transform;

        Vector3 origin = gun.muzzle.position;
        Vector3 point;
        bool onEnemy = false;
        if (gun.TryGetAimHit(out RaycastHit hit) && hit.distance <= maxDistance)
        {
            point = hit.point;
            onEnemy = hit.collider.GetComponentInParent<ZombieHealth>() != null;
        }
        else
        {
            point = origin + gun.muzzle.forward * maxDistance;
        }

        // Pull the crosshair slightly toward the eye so walls do not hide it.
        Vector3 eye = head != null ? head.position : origin;
        Vector3 toEye = eye - point;
        float dist = toEye.magnitude;
        Vector3 pos = point + toEye.normalized * Mathf.Min(0.05f, dist * 0.1f);

        reticle.gameObject.SetActive(true);
        reticle.position = pos;
        if (dist > 0.001f) reticle.rotation = Quaternion.LookRotation(-toEye, head != null ? head.up : Vector3.up);
        float size = 2f * dist * Mathf.Tan(angularSize * 0.5f * Mathf.Deg2Rad);
        reticle.localScale = Vector3.one * (size / 100f);
        reticleImage.color = onEnemy ? enemyColor : normalColor;

        if (laser)
        {
            laser.enabled = true;
            laser.SetPosition(0, origin);
            laser.SetPosition(1, point);
        }
    }

    void BuildReticle()
    {
        var go = new GameObject("Crosshair", typeof(Canvas));
        reticle = go.transform;
        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 600;
        ((RectTransform)reticle).sizeDelta = new Vector2(100f, 100f);

        var img = new GameObject("Ring", typeof(RectTransform), typeof(Image));
        img.transform.SetParent(reticle, false);
        var rt = (RectTransform)img.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        reticleImage = img.GetComponent<Image>();
        reticleImage.sprite = CreateCrosshairSprite();
        reticleImage.raycastTarget = false;
        reticleImage.color = normalColor;
        go.SetActive(false);
    }

    void BuildLaser()
    {
        var go = new GameObject("AimLaser");
        go.transform.SetParent(transform, false);
        laser = go.AddComponent<LineRenderer>();
        laser.useWorldSpace = true;
        laser.positionCount = 2;
        laser.startWidth = 0.003f;
        laser.endWidth = 0.0015f;
        laser.material = laserMaterial != null ? laserMaterial : gun.tracerMaterial;
        laser.startColor = laserColor;
        var end = laserColor;
        end.a = 0f;
        laser.endColor = end;
        laser.enabled = false;
        if (laser.sharedMaterial == null) showLaser = false;
    }

    /// <summary>Ring with a center dot and four ticks, drawn into a texture at runtime.</summary>
    static Sprite CreateCrosshairSprite()
    {
        const int n = 128;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[n * n];
        float c = (n - 1) * 0.5f;
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
        {
            float dx = x - c, dy = y - c;
            float r = Mathf.Sqrt(dx * dx + dy * dy);
            float a = 0f;
            a = Mathf.Max(a, Band(r, 40f, 46f));                   // ring
            a = Mathf.Max(a, 1f - Mathf.Clamp01(r - 6f));          // center dot
            bool tickX = Mathf.Abs(dy) < 3f && Mathf.Abs(dx) > 50f && Mathf.Abs(dx) < 62f;
            bool tickY = Mathf.Abs(dx) < 3f && Mathf.Abs(dy) > 50f && Mathf.Abs(dy) < 62f;
            if (tickX || tickY) a = 1f;
            px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255));
        }
        tex.SetPixels32(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
    }

    static float Band(float r, float inner, float outer)
    {
        return Mathf.Clamp01(r - inner + 1f) * Mathf.Clamp01(outer - r + 1f);
    }
}
