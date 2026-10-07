using System.Collections;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// A zombie spawned behind a Barricade: walks up to the window, tears the planks off
/// (several zombies can attack a wide window side by side), climbs through,
/// then switches back to normal NavMesh chasing.
/// </summary>
public class ZombieBreach : MonoBehaviour
{
    public float hitInterval = 2.2f;
    public float climbTime = 1.1f;

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
        // The chase stopping distance (attack range) would leave the zombie short of the window.
        float stopping = agent ? agent.stoppingDistance : 0f;
        if (agent) agent.stoppingDistance = 0.05f;
        b.Queue.Add(this);
        Quaternion facing = Quaternion.LookRotation(b.transform.forward);
        float nextHit = 0f;

        while (!Dead && b.HasPlanks)
        {
            int index = b.Queue.IndexOf(this);
            Vector3 target = b.QueuePoint(index);
            if (agent && agent.enabled && agent.isOnNavMesh)
            {
                // Walk across the room up to the window on the NavMesh, which stops a bit short of the wall.
                Vector3 navTarget = Barricade.Snap(target);
                Vector3 toNav = navTarget - transform.position;
                toNav.y = 0f;
                if (toNav.magnitude > 0.3f)
                {
                    agent.isStopped = false;
                    agent.SetDestination(navTarget);
                    if (animator) animator.SetFloat(SpeedHash, agent.velocity.magnitude);
                    yield return null;
                    continue;
                }
                agent.enabled = false;
            }

            // Last steps against the boards, and shuffling along the queue, without the agent.
            Vector3 flat = target - transform.position;
            flat.y = 0f;
            if (flat.magnitude > 0.05f)
            {
                transform.position = Vector3.MoveTowards(transform.position, new Vector3(target.x, transform.position.y, target.z), 0.8f * Time.deltaTime);
                if (animator) animator.SetFloat(SpeedHash, 0.8f);
                yield return null;
                continue;
            }

            if (animator) animator.SetFloat(SpeedHash, 0f);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, facing, 360f * Time.deltaTime);
            if (!b.IsAttackSlot(index) || Time.time < nextHit)
            {
                yield return null;
                continue;
            }

            // At the boards: rip one off.
            if (animator) animator.SetTrigger(AttackHash);
            yield return new WaitForSeconds(hitInterval * 0.45f);
            if (Dead) break;
            b.BreakPlank();
            nextHit = Time.time + hitInterval * 0.55f + Random.Range(0f, 0.4f);
        }
        int slot = Mathf.Max(0, b.Queue.IndexOf(this));
        b.Queue.Remove(this);
        if (Dead) yield break;

        // Climb in through the window.
        if (agent) agent.enabled = false;
        Vector3 from = transform.position;
        Vector3 to = b.InsidePointFor(slot);
        if (NavMesh.SamplePosition(to, out NavMeshHit hit, 1.5f, NavMesh.AllAreas)) to = hit.position;
        transform.rotation = facing;
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
            agent.isStopped = false;
            agent.stoppingDistance = stopping;
        }
        if (ai) ai.enabled = true;
        barricade = null;
        Destroy(this);
    }
}
