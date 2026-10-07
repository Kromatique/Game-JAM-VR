using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR;

/// <summary>
/// Player health with VR feedback: red flash in front of the eyes and controller vibration on each hit.
/// Put it on the XR Origin; zombies find it from the camera with GetComponentInParent.
/// </summary>
public class PlayerHealth : MonoBehaviour
{
    public float maxHealth = 100f;
    [Tooltip("Health regenerated per second after regenDelay without damage.")]
    public float regenPerSecond = 5f;
    public float regenDelay = 4f;

    [Header("Feedback")]
    public Color flashColor = new Color(0.8f, 0f, 0f, 0.55f);
    public float flashDuration = 0.35f;
    public float hapticAmplitude = 0.8f;
    public float hapticDuration = 0.2f;

    [Header("Death")]
    public bool restartSceneOnDeath = true;
    public float restartDelay = 4f;

    public UnityEvent<float> onDamaged;
    public UnityEvent onDeath;

    public float CurrentHealth { get; private set; }
    public bool IsDead => CurrentHealth <= 0f;

    Image overlay;
    float flashTimer;
    float lastDamageTime = -999f;

    void Awake()
    {
        CurrentHealth = maxHealth;
    }

    void Start()
    {
        CreateOverlay();
    }

    void Update()
    {
        if (!IsDead && CurrentHealth < maxHealth && Time.time - lastDamageTime > regenDelay)
            CurrentHealth = Mathf.Min(maxHealth, CurrentHealth + regenPerSecond * Time.deltaTime);

        if (overlay == null) return;
        // Permanent tint when low on health, plus a flash on each hit.
        float lowHealth = 1f - CurrentHealth / maxHealth;
        float alpha = IsDead ? 0.8f : Mathf.Max(lowHealth * 0.35f, flashTimer / flashDuration * flashColor.a);
        flashTimer = Mathf.Max(0f, flashTimer - Time.deltaTime);
        var c = flashColor;
        c.a = alpha;
        overlay.color = c;
    }

    public void TakeDamage(float amount)
    {
        if (IsDead) return;
        CurrentHealth = Mathf.Max(0f, CurrentHealth - amount);
        lastDamageTime = Time.time;
        flashTimer = flashDuration;
        Vibrate(hapticAmplitude, hapticDuration);
        onDamaged?.Invoke(CurrentHealth);
        Debug.Log($"[PlayerHealth] -{amount} HP, reste {CurrentHealth}");
        if (IsDead)
        {
            Debug.Log("[PlayerHealth] Joueur mort");
            Vibrate(1f, 0.6f);
            onDeath?.Invoke();
            if (restartSceneOnDeath) StartCoroutine(RestartAfterDelay());
        }
    }

    public void ResetHealth()
    {
        CurrentHealth = maxHealth;
    }

    IEnumerator RestartAfterDelay()
    {
        yield return new WaitForSeconds(restartDelay);
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    public static void Vibrate(float amplitude, float duration)
    {
        var devices = new List<InputDevice>();
        InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.Controller, devices);
        foreach (var d in devices) d.SendHapticImpulse(0, amplitude, duration);
    }

    void CreateOverlay()
    {
        var cam = Camera.main;
        if (cam == null)
        {
            var origin = FindAnyObjectByType<Unity.XR.CoreUtils.XROrigin>();
            if (origin != null) cam = origin.Camera;
        }
        if (cam == null) return;

        // World-space canvas just in front of the eyes (screen-space overlays do not render in VR).
        var go = new GameObject("DamageOverlay", typeof(Canvas), typeof(Image));
        go.transform.SetParent(cam.transform, false);
        go.transform.localPosition = new Vector3(0f, 0f, cam.nearClipPlane + 0.05f);
        go.transform.localRotation = Quaternion.identity;
        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 1000;
        var rt = (RectTransform)go.transform;
        rt.sizeDelta = new Vector2(1f, 1f);
        go.transform.localScale = Vector3.one;
        overlay = go.GetComponent<Image>();
        overlay.raycastTarget = false;
        overlay.color = Color.clear;
    }
}
