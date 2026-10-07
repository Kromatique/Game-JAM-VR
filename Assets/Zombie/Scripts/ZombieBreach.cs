using System.Collections;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// A zombie spawned outside a Barricade: queues at the window, tears the planks off,
/// climbs through, then switches back to normal NavMesh chasing.
/// </summary>
public class ZombieBreach : MonoBehaviour
{
    public float hitInterval = 2.2f;
    public float climbTime = 1.1f;
    public float shuffleSpeed = 0.6f;

    static readonly int AttackHash = Animator.StringToHash("Attack");
    static readonly int SpeedHash = Animator.StringToHash("Speed");

    Barricade barricade;
    ZombieHealth health;

    public void Begin(Barricade b)
    {
        barricade = b;
        StartCoroutine(Run());
    }

    void OnDestroy()
    {
        if (barricade != null) barricade.Queue.Remove(this);
    }

    bool Dead => health != null && health.IsDead;

    IEnumerator Run()
    {
        var b = barricade;
        var agent = GetComponent<NavMeshAgent>();
        var ai = GetComponent<ZombieAI>();
        health = GetComponent<ZombieHealth>();
        var animator = GetComponentInChildren<Animator>();
        if (ai) ai.enabled = false;
        if (agent) agent.enabled = false;

        b.Queue.Add(this);
        Quaternion facing = Quaternion.LookRotation(b.transform.forward);
        transform.SetPositionAndRotation(b.QueuePoint(b.Queue.IndexOf(this)), facing);

        while (!Dead && b.HasPlanks)
        {
            int index = b.Queue.IndexOf(this);
            Vector3 target = b.QueuePoint(index);
            if ((transform.position - target).sqrMagnitude > 0.0025f)
            {
                // Shuffle up the queue towards the window.
                transform.position = Vector3.MoveTowards(transform.position, target, shuffleSpeed * Time.deltaTime);
                if (animator) animator.SetFloat(SpeedHash, shuffleSpeed);
                yield return null;
                continue;
            }
            if (animator) animator.SetFloat(SpeedHash, 0f);
            if (index != 0)
            {
                yield return null;
                continue;
            }

            // First in line: rip a plank off.
            if (animator) animator.SetTrigger(AttackHash);
            yield return new WaitForSeconds(hitInterval * 0.45f);
            if (Dead) break;
            b.BreakPlank();
            yield return new WaitForSeconds(hitInterval * 0.55f);
        }
        b.Queue.Remove(this);
        if (Dead) yield break;

        // Climb in through the opening.
        Vector3 from = transform.position, to = b.InsidePoint;
        if (NavMesh.SamplePosition(to, out NavMeshHit hit, 1.5f, NavMesh.AllAreas)) to = hit.position;
        float t = 0f;
        while (t < 1f)
        {
            if (Dead) yield break;
            t += Time.deltaTime / climbTime;
            transform.position = Vector3.Lerp(from, to, t) + Vector3.up * Mathf.Sin(t * Mathf.PI) * 0.5f;
            if (animator) animator.SetFloat(SpeedHash, 1f);
            yield return null;
        }

        if (agent)
        {
            agent.enabled = true;
            agent.Warp(to);
        }
        if (ai) ai.enabled = true;
        barricade = null;
        Destroy(this);
    }
}
