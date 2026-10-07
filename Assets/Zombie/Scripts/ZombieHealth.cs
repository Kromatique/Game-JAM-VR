using System;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;

/// <summary>
/// Zombie health. Call TakeDamage from weapons (raycast or projectile).
/// Fast rigidbodies hitting the zombie (thrown objects, physical bullets) also deal damage.
/// </summary>
public class ZombieHealth : MonoBehaviour
{
    public float maxHealth = 100f;
    [Tooltip("Seconds before the body is removed after death.")]
    public float corpseLifetime = 4f;

    [Header("Collision damage")]
    public bool damageFromCollisions = true;
    public float minImpactSpeed = 4f;
    public float damagePerImpactSpeed = 10f;

    public UnityEvent onHit;
    public UnityEvent onDeath;

    /// <summary>Raised once when the zombie dies (used by the spawner).</summary>
    public event Action<ZombieHealth> Died;

    public float CurrentHealth { get; private set; }
    public bool IsDead { get; private set; }

    static readonly int HitHash = Animator.StringToHash("Hit");
    static readonly int DieHash = Animator.StringToHash("Die");

    Animator animator;

    void Awake()
    {
        CurrentHealth = maxHealth;
        animator = GetComponentInChildren<Animator>();
    }

    public void TakeDamage(float amount)
    {
        if (IsDead || amount <= 0f) return;
        CurrentHealth -= amount;
        if (CurrentHealth <= 0f)
        {
            Die();
            return;
        }
        if (animator) animator.SetTrigger(HitHash);
        onHit?.Invoke();
    }

    public void Kill() => TakeDamage(CurrentHealth);

    /// <summary>Wave scaling: sets max health and refills.</summary>
    public void SetMaxHealth(float value)
    {
        maxHealth = value;
        CurrentHealth = value;
    }

    void Die()
    {
        IsDead = true;
        CurrentHealth = 0f;

        var ai = GetComponent<ZombieAI>();
        if (ai) ai.enabled = false;
        var agent = GetComponent<NavMeshAgent>();
        if (agent && agent.isOnNavMesh)
        {
            agent.isStopped = true;
            agent.enabled = false;
        }
        foreach (var col in GetComponentsInChildren<Collider>()) col.enabled = false;

        if (animator)
        {
            animator.ResetTrigger(HitHash);
            animator.SetTrigger(DieHash);
        }

        onDeath?.Invoke();
        Died?.Invoke(this);
        Destroy(gameObject, corpseLifetime);
    }

    void OnCollisionEnter(Collision collision)
    {
        if (!damageFromCollisions || collision.rigidbody == null) return;
        float speed = collision.relativeVelocity.magnitude;
        if (speed >= minImpactSpeed) TakeDamage(speed * damagePerImpactSpeed);
    }
}
