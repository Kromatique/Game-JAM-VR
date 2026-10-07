using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR;

/// <summary>
/// Pistol-whip: swing the gun hand fast into a zombie to hit it (damage + push back).
/// Speed is measured relative to the player rig, so walking does not trigger it.
/// </summary>
[RequireComponent(typeof(Gun))]
public class GunMelee : MonoBehaviour
{
    public float damage = 60f;
    [Tooltip("Hand speed needed for a hit (m/s).")]
    public float minSwingSpeed = 1.8f;
    [Tooltip("Hit zone radius around the gun.")]
    public float radius = 0.3f;
    public float cooldown = 0.5f;
    public float pushDistance = 0.9f;
    public float staggerTime = 0.6f;
    public AudioClip hitClip;
    public float hapticAmplitude = 1f;
    public float hapticDuration = 0.15f;

    Gun gun;
    Transform rig;
    Vector3 lastLocalPos;
    bool hasLast;
    float nextHit;
    AudioSource source;
    readonly Collider[] hits = new Collider[16];

    void Awake()
    {
        gun = GetComponent<Gun>();
        source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 1f;
    }

    void OnEnable() => hasLast = false;

    void Update()
    {
        if (!gun.IsEquipped) return;
        if (rig == null)
        {
            var origin = FindAnyObjectByType<XROrigin>();
            if (origin == null) return;
            rig = origin.transform;
        }

        // Swing speed in rig space: ignores locomotion and turning of the rig.
        Vector3 center = transform.TransformPoint(Vector3.forward * 0.06f);
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
            InputDevices.GetDeviceAtXRNode(XRNode.RightHand).SendHapticImpulse(0, hapticAmplitude, hapticDuration);
            break;
        }
    }
}
