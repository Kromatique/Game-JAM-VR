using UnityEngine;

/// <summary>
/// The zombie clips move the hips forward (root motion baked in the Bip01 bone), so the body
/// slides away from the NavMeshAgent during a walk cycle. This keeps the hips horizontally
/// on their bind position under the model, after the Animator ran. Vertical bob is kept.
/// </summary>
[DefaultExecutionOrder(1000)]
public class ZombieRootLock : MonoBehaviour
{
    [Tooltip("Hips bone moved by the animations (Bip01).")]
    public Transform hips;

    Vector3 bindOffset;

    void Awake()
    {
        if (hips == null)
            foreach (var t in GetComponentsInChildren<Transform>())
                if (t.name == "Bip01") { hips = t; break; }
        if (hips != null) bindOffset = transform.InverseTransformPoint(hips.position);
    }

    void LateUpdate()
    {
        if (hips == null) return;
        Vector3 target = transform.TransformPoint(bindOffset);
        Vector3 p = hips.position;
        p.x = target.x;
        p.z = target.z;
        hips.position = p;
    }
}
