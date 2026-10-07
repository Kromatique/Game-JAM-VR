using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Chases the VR player (XR Origin camera, else main camera) on the NavMesh and attacks when close.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class ZombieAI : MonoBehaviour
{
    [Tooltip("Left empty: uses the XR Origin camera (the VR head), else Camera.main.")]
    public Transform target;

    [Header("Movement")]
    public float walkSpeed = 1.2f;
    public float runSpeed = 3f;
    [Range(0f, 1f)] public float runnerChance = 0.3f;
    public float repathInterval = 0.25f;

    [Header("Attack")]
    public float attackRange = 1.5f;
    [Tooltip("Extra reach allowed when the agent cannot get closer (path ends near the player).")]
    public float blockedExtraRange = 0.8f;
    public float attackDamage = 10f;
    public float attackCooldown = 1.6f;
    [Tooltip("Delay between the start of the attack animation and the hit.")]
    public float attackHitDelay = 0.6f;

    /// <summary>Raised when an attack starts (for sounds).</summary>
    public event System.Action AttackStarted;

    static readonly int SpeedHash = Animator.StringToHash("Speed");
    static readonly int AttackHash = Animator.StringToHash("Attack");

    NavMeshAgent agent;
    Animator animator;
    PlayerHealth playerHealth;
    float nextRepath;
    float nextAttack;
    float pendingHitTime = -1f;

    float staggerUntil;
    Vector3 staggerVelocity;

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        animator = GetComponentInChildren<Animator>();
        agent.stoppingDistance = attackRange * 0.7f;
        RollSpeed(1f);
    }

    void RollSpeed(float multiplier)
    {
        agent.speed = (Random.value < runnerChance ? runSpeed : walkSpeed) * multiplier;
        // Small variation so zombies do not move in lockstep.
        agent.speed *= Random.Range(0.9f, 1.1f);
    }

    /// <summary>Wave difficulty: more runners and a speed multiplier.</summary>
    public void ApplyDifficulty(float extraRunnerChance, float speedMultiplier)
    {
        runnerChance = Mathf.Clamp01(runnerChance + extraRunnerChance);
        RollSpeed(speedMultiplier);
    }

    /// <summary>Pushes the zombie back and stops it briefly (melee hit).</summary>
    public void Stagger(Vector3 push, float duration)
    {
        push.y = 0f;
        staggerVelocity = push / Mathf.Max(duration, 0.01f);
        staggerUntil = Time.time + duration;
        pendingHitTime = -1f;
        nextAttack = Mathf.Max(nextAttack, staggerUntil + 0.3f);
    }

    void Start()
    {
        FindTarget();
    }

    void FindTarget()
    {
        if (target == null || !target.gameObject.activeInHierarchy)
        {
            var origin = FindAnyObjectByType<XROrigin>();
            if (origin != null && origin.Camera != null) target = origin.Camera.transform;
            else if (Camera.main != null) target = Camera.main.transform;
        }
        if (target != null && playerHealth == null)
        {
            playerHealth = target.GetComponentInParent<PlayerHealth>();
            if (playerHealth == null) playerHealth = FindAnyObjectByType<PlayerHealth>();
        }
    }

    void Update()
    {
        if (target == null || playerHealth == null)
        {
            FindTarget();
            if (target == null) return;
        }

        if (animator) animator.SetFloat(SpeedHash, agent.velocity.magnitude);

        if (!agent.isOnNavMesh) return;
        if (playerHealth != null && playerHealth.IsDead)
        {
            agent.isStopped = true;
            return;
        }
        if (Time.time < staggerUntil)
        {
            agent.isStopped = true;
            agent.Move(staggerVelocity * Time.deltaTime);
            staggerVelocity = Vector3.Lerp(staggerVelocity, Vector3.zero, Time.deltaTime * 6f);
            return;
        }
        agent.isStopped = false;

        // Aim for the player's feet: the head is often closer to a ceiling than to the floor,
        // and SetDestination snaps to the nearest NavMesh point.
        Vector3 targetPos = ZombieSpawner.PlayerFeet(target);
        Vector3 flatDelta = targetPos - transform.position;
        flatDelta.y = 0f;
        float distance = flatDelta.magnitude;

        if (Time.time >= nextRepath)
        {
            nextRepath = Time.time + repathInterval;
            agent.SetDestination(targetPos);
        }

        if (InReach(distance))
        {
            // Face the player while attacking.
            if (flatDelta.sqrMagnitude > 0.001f)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(flatDelta), Time.deltaTime * 8f);

            if (Time.time >= nextAttack)
            {
                nextAttack = Time.time + attackCooldown;
                pendingHitTime = Time.time + attackHitDelay;
                if (animator) animator.SetTrigger(AttackHash);
                AttackStarted?.Invoke();
            }
        }

        if (pendingHitTime > 0f && Time.time >= pendingHitTime)
        {
            pendingHitTime = -1f;
            // Player can dodge: hit lands only if still in reach.
            if (InReach(distance * 0.85f) && playerHealth != null)
                playerHealth.TakeDamage(attackDamage);
        }
    }

    bool InReach(float distance)
    {
        if (distance <= attackRange) return true;
        // Path ends before the player (player off the NavMesh, on furniture...): attack from the end of the path.
        bool arrived = !agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.3f;
        return arrived && distance <= attackRange + blockedExtraRange;
    }

    void OnDisable()
    {
        pendingHitTime = -1f;
    }
}
