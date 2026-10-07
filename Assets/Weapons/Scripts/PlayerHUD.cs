using TMPro;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Small VR HUD in the lower part of the view: health bar, ammo and kills.
/// Built at runtime as a world-space canvas that follows the head.
/// </summary>
public class PlayerHUD : MonoBehaviour
{
    public PlayerHealth playerHealth;
    public Gun gun;
    public ZombieSpawner spawner;

    [Header("Placement (relative to the head)")]
    public Vector3 offset = new Vector3(0f, -0.28f, 0.9f);
    [Tooltip("Tilt so the HUD faces the eyes when it sits below the view.")]
    public float tilt = 15f;
    [Tooltip("How fast the HUD follows head rotation (0 = locked to the head).")]
    public float followSpeed = 0f;

    [Tooltip("Hidden while the start menu is shown.")]
    public bool visible = true;

    public Color healthColor = new Color(0.85f, 0.15f, 0.15f);
    public Color lowAmmoColor = new Color(1f, 0.55f, 0.1f);

    Transform head;
    Transform root;
    Image healthFill;
    TextMeshProUGUI healthText;
    TextMeshProUGUI ammoText;
    TextMeshProUGUI killsText;
    TextMeshProUGUI waveText;
    TextMeshProUGUI announceText;
    WaveManager waves;
    float announceUntil;
    const float AnnounceDuration = 3f;

    void Start()
    {
        if (playerHealth == null) playerHealth = FindAnyObjectByType<PlayerHealth>();
        if (gun == null) gun = FindAnyObjectByType<Gun>(FindObjectsInactive.Include);
        if (spawner == null) spawner = FindAnyObjectByType<ZombieSpawner>();
        waves = FindAnyObjectByType<WaveManager>();
        if (waves != null)
        {
            waves.WaveStarted += w => Announce($"VAGUE {w}");
            waves.WaveCleared += w => Announce($"<size=60%>VAGUE {w} TERMINEE</size>");
        }

        var origin = FindAnyObjectByType<XROrigin>();
        head = origin != null && origin.Camera != null ? origin.Camera.transform : Camera.main != null ? Camera.main.transform : null;
        if (head == null) return;
        Build();
    }

    void Announce(string text)
    {
        if (announceText == null) return;
        announceText.text = text;
        announceUntil = Time.time + AnnounceDuration;
    }

    void LateUpdate()
    {
        if (root == null) return;
        if (root.gameObject.activeSelf != visible) root.gameObject.SetActive(visible);
        if (!visible) return;

        if (playerHealth != null)
        {
            float ratio = playerHealth.CurrentHealth / playerHealth.maxHealth;
            healthFill.fillAmount = ratio;
            healthFill.color = Color.Lerp(Color.white * 0.6f, healthColor, 0.4f + 0.6f * (1f - ratio));
            healthText.text = Mathf.CeilToInt(playerHealth.CurrentHealth).ToString();
        }

        if (gun != null)
        {
            if (gun.IsReloading)
            {
                ammoText.text = "<size=60%>RECHARGEMENT</size>";
                ammoText.color = lowAmmoColor;
            }
            else
            {
                ammoText.text = $"{gun.Ammo}<size=55%> / {gun.magazineSize}</size>";
                ammoText.color = gun.Ammo <= 2 ? lowAmmoColor : Color.white;
            }
        }

        if (spawner != null) killsText.text = $"KILLS {spawner.Kills}";
        if (waves != null && waves.Wave > 0)
        {
            waveText.text = waves.InBreak ? $"VAGUE {waves.Wave + 1}<size=50%>  ...</size>" : $"VAGUE {waves.Wave}<size=50%>  RESTANTS {waves.RemainingInWave}</size>";
        }

        if (announceText != null)
        {
            float left = announceUntil - Time.time;
            var c = announceText.color;
            c.a = Mathf.Clamp01(left / 0.8f);
            announceText.color = c;
        }

        if (followSpeed > 0f)
        {
            // Smoothly follow the head instead of being rigidly attached.
            Vector3 targetPos = head.TransformPoint(offset);
            Quaternion targetRot = head.rotation * Quaternion.Euler(tilt, 0f, 0f);
            float k = 1f - Mathf.Exp(-followSpeed * Time.deltaTime);
            root.SetPositionAndRotation(Vector3.Lerp(root.position, targetPos, k), Quaternion.Slerp(root.rotation, targetRot, k));
        }
    }

    void Build()
    {
        var canvasGo = new GameObject("HUD", typeof(Canvas));
        root = canvasGo.transform;
        if (followSpeed > 0f)
        {
            root.SetPositionAndRotation(head.TransformPoint(offset), head.rotation * Quaternion.Euler(tilt, 0f, 0f));
        }
        else
        {
            root.SetParent(head, false);
            root.localPosition = offset;
            root.localRotation = Quaternion.Euler(tilt, 0f, 0f);
        }
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 500;
        var rt = (RectTransform)root;
        rt.sizeDelta = new Vector2(600f, 120f);
        root.localScale = Vector3.one * 0.0008f;

        // Health: bar + number on the left.
        var bg = NewImage("HealthBack", root, new Color(0f, 0f, 0f, 0.55f));
        Place(bg.rectTransform, new Vector2(0f, 0.3f), new Vector2(0.48f, 0.7f));
        healthFill = NewImage("HealthFill", bg.transform, healthColor);
        Place(healthFill.rectTransform, Vector2.zero, Vector2.one, 4f);
        healthFill.type = Image.Type.Filled;
        healthFill.fillMethod = Image.FillMethod.Horizontal;
        healthFill.sprite = WhiteSprite();
        healthText = NewText("HealthText", bg.transform, 34f, TextAlignmentOptions.Center);
        Place(healthText.rectTransform, Vector2.zero, Vector2.one);

        // Ammo on the right.
        var ammoBg = NewImage("AmmoBack", root, new Color(0f, 0f, 0f, 0.55f));
        Place(ammoBg.rectTransform, new Vector2(0.62f, 0.15f), new Vector2(1f, 0.85f));
        ammoText = NewText("AmmoText", ammoBg.transform, 56f, TextAlignmentOptions.Center);
        Place(ammoText.rectTransform, Vector2.zero, Vector2.one);

        // Kills under the health bar.
        killsText = NewText("KillsText", root, 24f, TextAlignmentOptions.Left);
        Place(killsText.rectTransform, new Vector2(0f, 0f), new Vector2(0.48f, 0.28f));

        // Wave above the health bar.
        waveText = NewText("WaveText", root, 30f, TextAlignmentOptions.Left);
        waveText.color = new Color(0.85f, 0.1f, 0.05f);
        Place(waveText.rectTransform, new Vector2(0f, 0.72f), new Vector2(0.6f, 1.05f));

        // Big wave announcement in the middle of the view.
        var announce = new GameObject("WaveAnnounce", typeof(RectTransform), typeof(TextMeshProUGUI));
        announce.transform.SetParent(root, false);
        announceText = announce.GetComponent<TextMeshProUGUI>();
        announceText.fontSize = 110f;
        announceText.fontStyle = FontStyles.Bold;
        announceText.alignment = TextAlignmentOptions.Center;
        announceText.textWrappingMode = TextWrappingModes.NoWrap;
        announceText.raycastTarget = false;
        announceText.color = new Color(0.85f, 0.08f, 0.05f, 0f);
        var art = announceText.rectTransform;
        art.anchorMin = art.anchorMax = new Vector2(0.5f, 0.5f);
        art.sizeDelta = new Vector2(1000f, 160f);
        art.anchoredPosition = new Vector2(0f, 420f);
    }

    static Image NewImage(string name, Transform parent, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    static TextMeshProUGUI NewText(string name, Transform parent, float size, TextAlignmentOptions align)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var text = go.GetComponent<TextMeshProUGUI>();
        text.fontSize = size;
        text.alignment = align;
        text.color = Color.white;
        text.fontStyle = FontStyles.Bold;
        text.raycastTarget = false;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        return text;
    }

    static void Place(RectTransform rt, Vector2 min, Vector2 max, float inset = 0f)
    {
        rt.anchorMin = min;
        rt.anchorMax = max;
        rt.offsetMin = new Vector2(inset, inset);
        rt.offsetMax = new Vector2(-inset, -inset);
    }

    static Sprite whiteSprite;

    static Sprite WhiteSprite()
    {
        if (whiteSprite == null)
            whiteSprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
        return whiteSprite;
    }
}
