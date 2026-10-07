using System.Collections;
using UnityEngine;

/// <summary>
/// 3D zombie sounds: random groans while alive, a sound on each attack, on each hit and on death.
/// Pitch varies a little so a horde does not sound like copies.
/// </summary>
public class ZombieAudio : MonoBehaviour
{
    public AudioClip[] idleClips;
    public AudioClip[] attackClips;
    public AudioClip[] hitClips;
    public AudioClip[] deathClips;
    [Tooltip("Played shortly after the death sound (body falling).")]
    public AudioClip fallClip;

    public Vector2 idleInterval = new Vector2(3f, 8f);
    public Vector2 pitchRange = new Vector2(0.85f, 1.1f);
    [Range(0f, 1f)] public float volume = 0.9f;
    public float maxDistance = 18f;

    AudioSource source;
    ZombieHealth health;
    ZombieAI ai;
    float nextIdle;

    void Awake()
    {
        source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 1f;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.minDistance = 1f;
        source.maxDistance = maxDistance;
        source.dopplerLevel = 0f;
        source.volume = volume;

        health = GetComponent<ZombieHealth>();
        ai = GetComponent<ZombieAI>();
        nextIdle = Time.time + Random.Range(0.5f, idleInterval.y);
    }

    void OnEnable()
    {
        if (health)
        {
            health.onHit.AddListener(OnHit);
            health.onDeath.AddListener(OnDeath);
        }
        if (ai) ai.AttackStarted += OnAttack;
    }

    void OnDisable()
    {
        if (health)
        {
            health.onHit.RemoveListener(OnHit);
            health.onDeath.RemoveListener(OnDeath);
        }
        if (ai) ai.AttackStarted -= OnAttack;
    }

    void Update()
    {
        if (health != null && health.IsDead) return;
        if (Time.time >= nextIdle)
        {
            nextIdle = Time.time + Random.Range(idleInterval.x, idleInterval.y);
            if (!source.isPlaying) Play(idleClips, 0.8f);
        }
    }

    void OnAttack() => Play(attackClips, 1f);

    void OnHit() => Play(hitClips, 1f);

    void OnDeath()
    {
        source.Stop();
        Play(deathClips, 1f);
        if (fallClip) StartCoroutine(PlayLater(fallClip, 0.45f));
    }

    IEnumerator PlayLater(AudioClip clip, float delay)
    {
        yield return new WaitForSeconds(delay);
        source.PlayOneShot(clip, volume);
    }

    void Play(AudioClip[] clips, float scale)
    {
        if (clips == null || clips.Length == 0) return;
        var clip = clips[Random.Range(0, clips.Length)];
        if (clip == null) return;
        source.pitch = Random.Range(pitchRange.x, pitchRange.y);
        source.PlayOneShot(clip, volume * scale);
    }
}
