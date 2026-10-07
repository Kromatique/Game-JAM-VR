using System;
using System.Collections;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using Random = UnityEngine.Random;

/// <summary>
/// Hitscan pistol with a magazine. Trigger shoots, A (right primary button) reloads,
/// and an empty magazine reloads on the next trigger pull.
/// Damages ZombieHealth, with a bonus for headshots.
/// </summary>
[RequireComponent(typeof(XRGrabInteractable))]
public class Gun : MonoBehaviour
{
    [Tooltip("Bullets leave from here along its forward (blue) axis.")]
    public Transform muzzle;

    [Header("Shooting")]
    public float damage = 35f;
    public float headshotMultiplier = 3f;
    [Tooltip("Hits higher than this above the zombie's feet count as headshots.")]
    public float headshotHeight = 1.5f;
    public float fireCooldown = 0.18f;
    public float range = 80f;
    public LayerMask hitMask = ~0;
    public float impactForce = 4f;

    [Header("Ammo")]
    public int magazineSize = 8;
    [Tooltip("Length of the reload animation.")]
    public float reloadTime = 1.05f;

    [Header("Animation")]
    [Tooltip("Animator with the states Idle, Fire, FireLast, EmptyIdle, Reload.")]
    public Animator animator;
    [Tooltip("Extra procedural kick of the gun model on each shot, in degrees.")]
    public float recoilAngle = 8f;
    public float recoilBack = 0.02f;
    public float recoilRecover = 14f;

    [Header("Feedback")]
    public Material tracerMaterial;
    public float tracerDuration = 0.04f;
    public float hapticAmplitude = 0.6f;
    public float hapticDuration = 0.08f;
    public AudioClip shotClip;
    public AudioClip emptyClip;
    public AudioClip reloadClip;

    [Header("Equip in hand at start")]
    [Tooltip("Puts the gun in the right controller at start (no grab needed). Trigger shoots.")]
    public bool equipOnStart = true;
    [Tooltip("Left empty: the XR Origin child named \"Right Controller\".")]
    public Transform hand;
    [Tooltip("Point of the gun placed on the controller (the grip). Uses the XRGrabInteractable attach if empty.")]
    public Transform gripPoint;
    public bool hideControllerModel = true;

    /// <summary>Raised when ammo or reload state changes (for the HUD).</summary>
    public event Action AmmoChanged;

    public int Ammo { get; private set; }
    public bool IsReloading { get; private set; }
    public bool IsEquipped => equipped;

    static readonly int IdleState = Animator.StringToHash("Idle");
    static readonly int FireState = Animator.StringToHash("Fire");
    static readonly int FireLastState = Animator.StringToHash("FireLast");
    static readonly int ReloadState = Animator.StringToHash("Reload");

    XRGrabInteractable grab;
    InputAction triggerAction;
    InputAction reloadAction;
    bool equipped;
    AudioSource audioSource;
    LineRenderer tracer;
    Light flash;
    Collider[] ownColliders;
    XRNode heldBy = XRNode.RightHand;
    float nextShot;
    Coroutine fxRoutine;
    Transform visual;
    Vector3 visualBasePos;
    Quaternion visualBaseRot;
    float recoil;

    void Awake()
    {
        grab = GetComponent<XRGrabInteractable>();
        ownColliders = GetComponentsInChildren<Collider>();
        if (muzzle == null) muzzle = transform;
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (animator != null)
        {
            visual = animator.transform;
            visualBasePos = visual.localPosition;
            visualBaseRot = visual.localRotation;
        }
        Ammo = magazineSize;

        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 1f;
        if (shotClip == null) shotClip = CreateShotClip();
        if (emptyClip == null) emptyClip = CreateClickClip("Empty", 1, 0.06f, 2500f);
        if (reloadClip == null) reloadClip = CreateClickClip("Reload", 3, 0.32f, 1400f);

        var tracerGo = new GameObject("Tracer");
        tracerGo.transform.SetParent(transform, false);
        tracer = tracerGo.AddComponent<LineRenderer>();
        tracer.useWorldSpace = true;
        tracer.positionCount = 2;
        tracer.startWidth = 0.01f;
        tracer.endWidth = 0.004f;
        tracer.material = tracerMaterial;
        tracer.startColor = new Color(1f, 0.9f, 0.5f);
        tracer.endColor = new Color(1f, 0.6f, 0.2f, 0.3f);
        tracer.enabled = false;

        var flashGo = new GameObject("MuzzleFlash");
        flashGo.transform.SetParent(muzzle, false);
        flash = flashGo.AddComponent<Light>();
        flash.type = LightType.Point;
        flash.color = new Color(1f, 0.75f, 0.35f);
        flash.range = 4f;
        flash.intensity = 6f;
        flash.enabled = false;
    }

    void OnEnable()
    {
        grab.activated.AddListener(OnActivated);
        grab.selectEntered.AddListener(OnGrabbed);
    }

    void OnDisable()
    {
        grab.activated.RemoveListener(OnActivated);
        grab.selectEntered.RemoveListener(OnGrabbed);
        triggerAction?.Disable();
        reloadAction?.Disable();
        IsReloading = false;
    }

    void OnDestroy()
    {
        triggerAction?.Dispose();
        reloadAction?.Dispose();
    }

    void Start()
    {
        if (equipOnStart) Equip();
        AmmoChanged?.Invoke();
    }

    void Update()
    {
        if (visual == null) return;
        // Procedural kick on top of the animation, recovering quickly.
        recoil = Mathf.MoveTowards(recoil, 0f, Time.deltaTime * recoilRecover * Mathf.Max(recoil, 0.1f));
        visual.localRotation = visualBaseRot * Quaternion.Euler(-recoilAngle * recoil, 0f, 0f);
        visual.localPosition = visualBasePos - Vector3.forward * (recoilBack * recoil);
    }

    /// <summary>Parents the gun to the controller so it is always in hand, and reads the buttons directly.</summary>
    public void Equip()
    {
        if (hand == null)
        {
            var origin = FindAnyObjectByType<XROrigin>();
            if (origin != null)
                foreach (var t in origin.GetComponentsInChildren<Transform>(true))
                    if (t.name == "Right Controller") { hand = t; break; }
        }
        if (hand == null)
        {
            Debug.LogWarning("[Gun] Right Controller not found, the gun stays grabbable.");
            return;
        }

        grab.enabled = false;
        var rb = GetComponent<Rigidbody>();
        if (rb) rb.isKinematic = true;

        Transform grip = gripPoint != null ? gripPoint : grab.attachTransform;
        transform.SetParent(hand, false);
        if (grip != null && grip != transform)
        {
            // Place the gun so its grip point sits on the controller origin, same orientation.
            Quaternion gripRot = Quaternion.Inverse(transform.rotation) * grip.rotation;
            Vector3 gripPos = transform.InverseTransformPoint(grip.position);
            transform.localRotation = Quaternion.Inverse(gripRot);
            transform.localPosition = -(transform.localRotation * gripPos);
        }
        else
        {
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
        }

        if (hideControllerModel)
            foreach (Transform t in hand)
                if (t.name.Contains("Controller Visual")) t.gameObject.SetActive(false);

        triggerAction = new InputAction("GunTrigger", InputActionType.Button);
        triggerAction.AddBinding("<XRController>{RightHand}/{TriggerButton}");
        triggerAction.AddBinding("<XRController>{RightHand}/triggerPressed");
        triggerAction.performed += _ => Fire();
        triggerAction.Enable();

        reloadAction = new InputAction("GunReload", InputActionType.Button);
        reloadAction.AddBinding("<XRController>{RightHand}/{PrimaryButton}");
        reloadAction.AddBinding("<XRController>{RightHand}/primaryButton");
        reloadAction.performed += _ => Reload();
        reloadAction.Enable();

        heldBy = XRNode.RightHand;
        equipped = true;
    }

    void OnGrabbed(SelectEnterEventArgs args)
    {
        if (args.interactorObject is IXRInteractor interactor)
            heldBy = interactor.handedness == InteractorHandedness.Left ? XRNode.LeftHand : XRNode.RightHand;
    }

    void OnActivated(ActivateEventArgs args) => Fire();

    public void Fire()
    {
        if (IsReloading || Time.time < nextShot) return;
        nextShot = Time.time + fireCooldown;

        if (Ammo <= 0)
        {
            audioSource.PlayOneShot(emptyClip);
            Reload();
            return;
        }

        Ammo--;
        Vector3 origin = muzzle.position;
        Vector3 dir = muzzle.forward;
        Vector3 end = origin + dir * range;

        if (TryRaycast(origin, dir, out RaycastHit hit))
        {
            end = hit.point;
            var zombie = hit.collider.GetComponentInParent<ZombieHealth>();
            if (zombie != null)
            {
                bool headshot = hit.point.y - zombie.transform.position.y >= headshotHeight;
                zombie.TakeDamage(headshot ? damage * headshotMultiplier : damage);
            }
            else if (hit.rigidbody != null && !hit.rigidbody.isKinematic)
            {
                hit.rigidbody.AddForceAtPosition(dir * impactForce, hit.point, ForceMode.Impulse);
            }
        }

        if (animator) animator.Play(Ammo > 0 ? FireState : FireLastState, 0, 0f);
        recoil = 1f;
        audioSource.pitch = Random.Range(0.95f, 1.05f);
        audioSource.PlayOneShot(shotClip);
        InputDevices.GetDeviceAtXRNode(heldBy).SendHapticImpulse(0, hapticAmplitude, hapticDuration);
        if (fxRoutine != null) StopCoroutine(fxRoutine);
        fxRoutine = StartCoroutine(ShotFx(origin, end));
        AmmoChanged?.Invoke();
    }

    public void Reload()
    {
        if (IsReloading || Ammo >= magazineSize || !isActiveAndEnabled) return;
        StartCoroutine(ReloadRoutine());
    }

    IEnumerator ReloadRoutine()
    {
        IsReloading = true;
        AmmoChanged?.Invoke();
        if (animator) animator.Play(ReloadState, 0, 0f);
        audioSource.pitch = 1f;
        audioSource.PlayOneShot(reloadClip);
        yield return new WaitForSeconds(reloadTime);
        Ammo = magazineSize;
        IsReloading = false;
        if (animator) animator.Play(IdleState, 0, 0f);
        InputDevices.GetDeviceAtXRNode(heldBy).SendHapticImpulse(0, 0.3f, 0.05f);
        AmmoChanged?.Invoke();
    }

    /// <summary>Where a bullet fired now would hit (used by the crosshair).</summary>
    public bool TryGetAimHit(out RaycastHit hit) => TryRaycast(muzzle.position, muzzle.forward, out hit);

    bool TryRaycast(Vector3 origin, Vector3 dir, out RaycastHit closest)
    {
        closest = default;
        float best = float.MaxValue;
        bool found = false;
        foreach (var h in Physics.RaycastAll(origin, dir, range, hitMask, QueryTriggerInteraction.Ignore))
        {
            if (h.distance >= best || Array.IndexOf(ownColliders, h.collider) >= 0) continue;
            // Ignore the player's own body and hands.
            if (h.collider.GetComponentInParent<PlayerHealth>() != null) continue;
            best = h.distance;
            closest = h;
            found = true;
        }
        return found;
    }

    IEnumerator ShotFx(Vector3 from, Vector3 to)
    {
        tracer.SetPosition(0, from);
        tracer.SetPosition(1, to);
        tracer.enabled = tracerMaterial != null;
        flash.enabled = true;
        yield return new WaitForSeconds(tracerDuration);
        tracer.enabled = false;
        flash.enabled = false;
    }

    /// <summary>Procedural gunshot (noise burst with fast decay), so no audio file is needed.</summary>
    static AudioClip CreateShotClip()
    {
        const int rate = 44100;
        int samples = (int)(rate * 0.25f);
        var data = new float[samples];
        var rng = new System.Random(7);
        float low = 0f;
        for (int i = 0; i < samples; i++)
        {
            float t = (float)i / rate;
            float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
            low = Mathf.Lerp(low, noise, 0.25f);
            float env = Mathf.Exp(-t * 28f);
            float thump = Mathf.Sin(2f * Mathf.PI * 90f * t) * Mathf.Exp(-t * 40f);
            data[i] = Mathf.Clamp((low * 0.9f + thump * 0.6f) * env, -1f, 1f);
        }
        var clip = AudioClip.Create("Shot", samples, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    /// <summary>Metallic clicks spread over the given duration (empty trigger, magazine, slide).</summary>
    static AudioClip CreateClickClip(string name, int clicks, float duration, float freq)
    {
        const int rate = 44100;
        int samples = (int)(rate * (duration + 0.05f));
        var data = new float[samples];
        var rng = new System.Random(3);
        for (int c = 0; c < clicks; c++)
        {
            int start = clicks == 1 ? 0 : (int)(rate * duration * c / (clicks - 1));
            for (int i = 0; i < rate * 0.04f && start + i < samples; i++)
            {
                float t = (float)i / rate;
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                data[start + i] += (Mathf.Sin(2f * Mathf.PI * freq * t) * 0.6f + noise * 0.4f) * Mathf.Exp(-t * 120f) * 0.7f;
            }
        }
        var clip = AudioClip.Create(name, samples, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
