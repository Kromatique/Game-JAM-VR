using System.Collections;
using TMPro;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

/// <summary>
/// Game flow with a wrist menu: a small panel on the left controller shows up when the player
/// looks at their wrist. Before a game: PLAY / QUIT and the records. During a game: wave, zombies
/// left, kills, health and a MENU button. When the player dies the scene reloads to the menu.
/// If no left controller is found, a floating panel in front of the player is used instead.
/// </summary>
[DefaultExecutionOrder(-100)]
public class GameFlow : MonoBehaviour
{
    public ZombieSpawner spawner;
    public Gun gun;
    public PlayerHUD hud;
    public PlayerHealth playerHealth;
    public bool showMenuOnStart = true;

    [Header("Wrist menu")]
    [Tooltip("Stats panel on the left wrist during the game (off: more immersive).")]
    public bool useWristMenu = false;
    [Tooltip("Left empty: the XR Origin child named \"Left Controller\".")]
    public Transform wrist;
    public Vector3 wristOffset = new Vector3(0f, 0.035f, -0.16f);
    public Vector3 wristRotation = new Vector3(60f, 0f, 0f);
    public float wristScale = 0.0004f;
    [Tooltip("How much the panel must face the eyes to show (0..1).")]
    [Range(0f, 1f)] public float lookThreshold = 0.35f;
    public float maxLookDistance = 0.8f;

    [Header("Fallback floating menu")]
    public float menuDistance = 2.2f;
    public float menuHeight = 1.5f;

    public string title = "ZOMBIE VR";
    public string subtitle = "MMI GAME JAM";

    const string BestKey = "ZombieVR.BestKills";
    const string BestWaveKey = "ZombieVR.BestWave";
    static int lastKills = -1;
    static int lastWave;

    bool playing;
    Transform head;
    WaveManager waves;

    // Wrist UI
    GameObject wristMenu;
    CanvasGroup wristGroup;
    TextMeshProUGUI statsText;
    GameObject menuButtons;
    GameObject gameButtons;
    GameObject hint;
    GameObject floatingMenu;

    void Awake()
    {
        if (spawner == null) spawner = FindAnyObjectByType<ZombieSpawner>();
        if (gun == null) gun = FindAnyObjectByType<Gun>(FindObjectsInactive.Include);
        if (hud == null) hud = FindAnyObjectByType<PlayerHUD>();
        if (playerHealth == null) playerHealth = FindAnyObjectByType<PlayerHealth>();
        waves = FindAnyObjectByType<WaveManager>();

        if (!showMenuOnStart) return;
        if (spawner) spawner.enabled = false;
        if (gun) gun.gameObject.SetActive(false);
        if (hud) hud.visible = false;
    }

    void Start()
    {
        if (playerHealth) playerHealth.onDeath.AddListener(OnPlayerDeath);
        var origin = FindAnyObjectByType<XROrigin>();
        head = origin != null && origin.Camera != null ? origin.Camera.transform : Camera.main.transform;
        if (wrist == null && origin != null)
            foreach (var t in origin.GetComponentsInChildren<Transform>(true))
                if (t.name == "Left Controller") { wrist = t; break; }

        if (wrist != null && useWristMenu) BuildWristMenu();
        if (showMenuOnStart) StartCoroutine(ShowStartHelp());
        else StartGame();
    }

    IEnumerator ShowStartHelp()
    {
        // Wait a little so the headset pose is known before placing anything in front of it.
        yield return new WaitForSeconds(0.5f);
        if (playing) yield break;
        // Start menu floats in front of the player spawn; the wrist panel keeps the stats.
        BuildFloatingMenu();
    }

    void Update()
    {
        if (wristMenu == null) return;

        // Show the wrist panel only when the player looks at it.
        Vector3 toEye = head.position - wristMenu.transform.position;
        float facing = Vector3.Dot(-wristMenu.transform.forward, toEye.normalized);
        bool show = wrist.gameObject.activeInHierarchy && facing > lookThreshold && toEye.magnitude < maxLookDistance;
        wristGroup.alpha = Mathf.MoveTowards(wristGroup.alpha, show ? 1f : 0f, Time.deltaTime * 6f);
        wristGroup.interactable = wristGroup.blocksRaycasts = wristGroup.alpha > 0.5f;

        if (wristGroup.alpha > 0f) statsText.text = StatsText();
    }

    string StatsText()
    {
        int best = PlayerPrefs.GetInt(BestKey, 0);
        int bestWave = PlayerPrefs.GetInt(BestWaveKey, 0);
        if (!playing)
        {
            string last = lastKills >= 0 ? $"\n<color=#CCCCCC>DERNIERE  VAGUE {lastWave}  -  {lastKills} KILLS</color>" : "";
            return $"RECORD  VAGUE {bestWave}  -  {best} KILLS{last}";
        }
        int wave = waves != null ? waves.Wave : 0;
        int left = waves != null ? waves.RemainingInWave : 0;
        int kills = spawner != null ? spawner.Kills : 0;
        int hp = playerHealth != null ? Mathf.CeilToInt(playerHealth.CurrentHealth) : 0;
        string state = waves != null && waves.InBreak ? "PAUSE" : $"RESTANTS {left}";
        return $"<size=130%><color=#D8140C>VAGUE {wave}</color></size>   {state}\nKILLS {kills}    PV {hp}\n<color=#CCCCCC>RECORD  VAGUE {bestWave}  -  {best} KILLS</color>";
    }

    public void StartGame()
    {
        if (playing) return;
        playing = true;
        if (hint) Destroy(hint);
        if (floatingMenu) Destroy(floatingMenu);
        if (menuButtons) menuButtons.SetActive(false);
        if (gameButtons) gameButtons.SetActive(true);
        if (waves != null && waves.isActiveAndEnabled) waves.StartWaves();
        else if (spawner) spawner.enabled = true;
        if (gun) gun.gameObject.SetActive(true);
        if (hud) hud.visible = true;
    }

    /// <summary>Ends the current game and goes back to the menu (scene reload).</summary>
    public void BackToMenu()
    {
        if (playing) RecordStats();
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    public void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    void OnPlayerDeath() => RecordStats();

    void RecordStats()
    {
        lastKills = spawner ? spawner.Kills : 0;
        lastWave = waves != null ? waves.Wave : 0;
        if (lastKills > PlayerPrefs.GetInt(BestKey, 0)) PlayerPrefs.SetInt(BestKey, lastKills);
        if (lastWave > PlayerPrefs.GetInt(BestWaveKey, 0)) PlayerPrefs.SetInt(BestWaveKey, lastWave);
        PlayerPrefs.Save();
    }

    void BuildWristMenu()
    {
        wristMenu = NewCanvas("Wrist Menu", new Vector2(420, 300));
        wristMenu.transform.SetParent(wrist, false);
        wristMenu.transform.localPosition = wristOffset;
        wristMenu.transform.localRotation = Quaternion.Euler(wristRotation);
        wristMenu.transform.localScale = Vector3.one * wristScale;
        wristGroup = wristMenu.AddComponent<CanvasGroup>();
        wristGroup.alpha = 0f;

        var panel = NewImage("Panel", wristMenu.transform, new Color(0.02f, 0.02f, 0.02f, 0.85f));
        Stretch(panel.rectTransform, Vector2.zero, Vector2.one);
        var border = NewImage("Border", wristMenu.transform, new Color(0.55f, 0.05f, 0.05f, 1f));
        Stretch(border.rectTransform, new Vector2(0f, 0.96f), Vector2.one);

        var t = NewText("Title", wristMenu.transform, title, 46, new Color(0.85f, 0.08f, 0.05f));
        Stretch(t.rectTransform, new Vector2(0f, 0.76f), new Vector2(1f, 0.95f));
        t.characterSpacing = 6;

        statsText = NewText("Stats", wristMenu.transform, "", 20, new Color(0.9f, 0.8f, 0.4f));
        Stretch(statsText.rectTransform, new Vector2(0.03f, 0.36f), new Vector2(0.97f, 0.76f));

        menuButtons = new GameObject("MenuButtons", typeof(RectTransform));
        menuButtons.transform.SetParent(wristMenu.transform, false);
        Stretch((RectTransform)menuButtons.transform, Vector2.zero, Vector2.one);
        NewButton("Play", menuButtons.transform, lastKills >= 0 ? "REJOUER" : "JOUER", new Vector2(0.05f, 0.06f), new Vector2(0.58f, 0.32f), 34, StartGame);
        NewButton("Quit", menuButtons.transform, "QUITTER", new Vector2(0.62f, 0.06f), new Vector2(0.95f, 0.32f), 26, Quit);

        gameButtons = new GameObject("GameButtons", typeof(RectTransform));
        gameButtons.transform.SetParent(wristMenu.transform, false);
        Stretch((RectTransform)gameButtons.transform, Vector2.zero, Vector2.one);
        NewButton("Menu", gameButtons.transform, "MENU", new Vector2(0.05f, 0.06f), new Vector2(0.58f, 0.32f), 30, BackToMenu);
        NewButton("Quit", gameButtons.transform, "QUITTER", new Vector2(0.62f, 0.06f), new Vector2(0.95f, 0.32f), 26, Quit);
        gameButtons.SetActive(playing);
        menuButtons.SetActive(!playing);
    }

    void BuildHint()
    {
        Vector3 forward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
        if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
        forward.Normalize();
        hint = NewCanvas("Wrist Hint", new Vector2(900, 260));
        Vector3 pos = head.position + forward * menuDistance;
        hint.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(forward));
        hint.transform.localScale = Vector3.one * 0.0022f;
        var t = NewText("Title", hint.transform, title, 110, new Color(0.85f, 0.08f, 0.05f));
        Stretch(t.rectTransform, new Vector2(0f, 0.45f), Vector2.one);
        t.characterSpacing = 8;
        var s = NewText("Help", hint.transform, "REGARDE TON POIGNET GAUCHE POUR JOUER", 40, new Color(0.85f, 0.85f, 0.8f));
        Stretch(s.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0.42f));
    }

    void BuildFloatingMenu()
    {
        // In front of the spawn point (XR Origin position and facing), not where the head happens to look.
        var origin = FindAnyObjectByType<XROrigin>();
        Vector3 forward = origin != null ? Vector3.ProjectOnPlane(origin.transform.forward, Vector3.up) : Vector3.ProjectOnPlane(head.forward, Vector3.up);
        if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
        forward.Normalize();
        float floorY = origin != null ? origin.transform.position.y : 0f;
        Vector3 basePos = origin != null ? origin.transform.position : head.position;
        float dist = menuDistance;
        Vector3 eye = new Vector3(basePos.x, floorY + menuHeight, basePos.z);
        // Do not bury the panel in a wall right in front of the spawn.
        if (Physics.Raycast(eye, forward, out RaycastHit hit, menuDistance, ~0, QueryTriggerInteraction.Ignore))
            dist = Mathf.Max(0.8f, hit.distance - 0.25f);
        Vector3 pos = basePos + forward * dist;
        pos.y = floorY + menuHeight;

        floatingMenu = NewCanvas("Start Menu", new Vector2(900, 700));
        floatingMenu.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(forward));
        floatingMenu.transform.localScale = Vector3.one * 0.0022f;
        var panel = NewImage("Panel", floatingMenu.transform, new Color(0.02f, 0.02f, 0.02f, 0.82f));
        Stretch(panel.rectTransform, Vector2.zero, Vector2.one);
        var t = NewText("Title", floatingMenu.transform, title, 120, new Color(0.85f, 0.08f, 0.05f));
        Stretch(t.rectTransform, new Vector2(0f, 0.72f), new Vector2(1f, 0.95f));
        var s = NewText("Subtitle", floatingMenu.transform, subtitle, 38, new Color(0.75f, 0.75f, 0.7f));
        Stretch(s.rectTransform, new Vector2(0f, 0.64f), new Vector2(1f, 0.73f));
        var sc = NewText("Score", floatingMenu.transform, StatsText(), 26, new Color(0.9f, 0.8f, 0.4f));
        Stretch(sc.rectTransform, new Vector2(0f, 0.5f), new Vector2(1f, 0.63f));
        NewButton("Play", floatingMenu.transform, lastKills >= 0 ? "REJOUER" : "JOUER", new Vector2(0.25f, 0.30f), new Vector2(0.75f, 0.47f), 60, StartGame);
        NewButton("Quit", floatingMenu.transform, "QUITTER", new Vector2(0.25f, 0.08f), new Vector2(0.75f, 0.24f), 60, Quit);
    }

    GameObject NewCanvas(string name, Vector2 size)
    {
        var go = new GameObject(name, typeof(Canvas), typeof(TrackedDeviceGraphicRaycaster));
        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = head != null ? head.GetComponent<Camera>() : null;
        canvas.sortingOrder = 700;
        ((RectTransform)go.transform).sizeDelta = size;
        return go;
    }

    static Image NewImage(string name, Transform parent, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.color = color;
        return img;
    }

    static TextMeshProUGUI NewText(string name, Transform parent, string text, float size, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.color = color;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.raycastTarget = false;
        return tmp;
    }

    static void NewButton(string name, Transform parent, string label, Vector2 min, Vector2 max, float fontSize, UnityEngine.Events.UnityAction onClick)
    {
        var img = NewImage(name, parent, Color.white);
        Stretch(img.rectTransform, min, max);
        var button = img.gameObject.AddComponent<Button>();
        var colors = button.colors;
        colors.normalColor = new Color(0.25f, 0.03f, 0.03f, 1f);
        colors.highlightedColor = new Color(0.6f, 0.06f, 0.04f, 1f);
        colors.pressedColor = new Color(0.9f, 0.2f, 0.1f, 1f);
        colors.selectedColor = colors.highlightedColor;
        colors.fadeDuration = 0.05f;
        button.colors = colors;
        button.onClick.AddListener(onClick);
        var text = NewText("Label", img.transform, label, fontSize, Color.white);
        Stretch(text.rectTransform, Vector2.zero, Vector2.one);
    }

    static void Stretch(RectTransform rt, Vector2 min, Vector2 max)
    {
        rt.anchorMin = min;
        rt.anchorMax = max;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }
}
