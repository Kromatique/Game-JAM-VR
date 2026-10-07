using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR;

/// <summary>
/// Hand-held melee weapon (knife): attached to a controller at start, hits zombies when swung fast.
/// Swing speed is measured relative to the player rig, so walking or turning does not trigger it.
/// </summary>
public class MeleeWeapon : MonoBehaviour
{
    [Header("Hand")]
    [Tooltip("XR Origin child the weapon is attached to.")]
    public string handName = "Left Controller";
    public XRNode hapticNode = XRNode.LeftHand;
    [Tooltip("Point of the weapon placed on the controller (the grip).")]
    public Transform gripPoint;
    [Tooltip("Point that must touch the zombie (blade).")]
    public Transform hitPoint;
    public bool hideControllerModel = true;

    [Header("Hit")]
    public float damage = 75f;
    public float minSwingSpeed = 1.6f;
    public float radius = 0.18f;
    public float cooldown = 0.35f;
    public float pushDistance = 0.5f;
    public float staggerTime = 0.4f;
    public AudioClip hitClip;
    public float hapticAmplitude = 0.9f;
    public float hapticDuration = 0.12f;

    Transform rig;
    Vector3 lastLocalPos;
    bool hasLast;
    float nextHit;
    AudioSource source;
    readonly Collider[] hits = new Collider[16];

    void Awake()
    {
        source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 1f;
        // The knife must not push zombies or the player physically.
        foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
    }

    void Start()
    {
        var origin = FindAnyObjectByType<XROrigin>();
        if (origin == null) return;
        rig = origin.transform;
        Transform hand = null;
        foreach (var t in origin.GetComponentsInChildren<Transform>(true))
            if (t.name == handName) { hand = t; break; }
        if (hand == null) return;

        transform.SetParent(hand, false);
        if (gripPoint != null && gripPoint != transform)
        {
            Quaternion gripRot = Quaternion.Inverse(transform.rotation) * gripPoint.rotation;
            Vector3 gripPos = transform.InverseTransformPoint(gripPoint.position);
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
    }

    void OnEnable() => hasLast = false;

    void Update()
    {
        if (rig == null) return;
        Vector3 center = hitPoint != null ? hitPoint.position : transform.position;
        Vector3 local = rig.InverseTransformPoint(center);
        float speed = hasLast && Time.deltaTime > 0f ? (local - lastLocalPos).magnitude / Time.deltaTime : 0f;
        Vector3 swingDir = rig.TransformDirection(local - lastLocalPos).normalized;
        lastLocalPos = local;
        hasLast = true;

        if (speed < minSwingSpeed || Time.time < nextHit) return;

        int n = Physics.OverlapSphereNonAlloc(center, radius, hits, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            var zombie = hits[i].GetComponentInParent<ZombieHealth>();
            if (zombie == null || zombie.IsDead) continue;

            nextHit = Time.time + cooldown;
            Vector3 away = zombie.transform.position - rig.position;
            away.y = 0f;
            Vector3 push = (away.normalized + swingDir * 0.5f).normalized * pushDistance;
            var ai = zombie.GetComponent<ZombieAI>();
            if (ai) ai.Stagger(push, staggerTime);
            zombie.TakeDamage(damage);

            if (hitClip) source.PlayOneShot(hitClip);
            InputDevices.GetDeviceAtXRNode(hapticNode).SendHapticImpulse(0, hapticAmplitude, hapticDuration);
            break;
        }
    }
}
