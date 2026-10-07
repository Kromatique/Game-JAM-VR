using System.Collections;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Small jump scare: now and then a black cat meows and darts across the floor
/// right behind the player, then runs off and disappears.
/// </summary>
public class CatScare : MonoBehaviour
{
    public GameObject catPrefab;
    public AudioClip meow;
    [Tooltip("Random delay between two cat runs, in seconds.")]
    public Vector2 interval = new Vector2(45f, 100f);
    public float firstDelay = 25f;
    public float runSpeed = 3.2f;
    [Tooltip("Distance behind the player where the cat crosses.")]
    public float behindDistance = 1.4f;
    [Tooltip("Half length of the run, each side of the player.")]
    public float halfRun = 2.5f;
    [Tooltip("Only while a game is running (after JOUER).")]
    public bool onlyDuringGame = true;

    Transform head;
    XROrigin origin;
    WaveManager waves;
    bool running;

    void Start()
    {
        origin = FindAnyObjectByType<XROrigin>();
        head = origin != null && origin.Camera != null ? origin.Camera.transform : Camera.main.transform;
        waves = FindAnyObjectByType<WaveManager>();
        StartCoroutine(Loop());
    }

    IEnumerator Loop()
    {
        yield return new WaitForSeconds(firstDelay);
        while (true)
        {
            bool inGame = !onlyDuringGame || (waves != null && waves.Wave > 0);
            if (inGame && !running && TryRun()) yield return new WaitForSeconds(Random.Range(interval.x, interval.y));
            else yield return new WaitForSeconds(3f);
        }
    }

    /// <summary>Starts a run now if there is room behind the player. Returns false otherwise.</summary>
    public bool TryRun()
    {
        if (catPrefab == null || head == null) return false;
        float floor = origin != null ? origin.transform.position.y : 0f;
        Vector3 fwd = head.forward;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.01f) return false;
        fwd.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, fwd);
        Vector3 feet = new Vector3(head.position.x, floor, head.position.z);

        // Try a few distances behind and both directions; the run must be on the floor and clear of walls.
        foreach (float back in new[] { behindDistance, behindDistance * 0.7f, behindDistance * 1.5f })
        {
            int dir = Random.value < 0.5f ? 1 : -1;
            for (int k = 0; k < 2; k++, dir = -dir)
            {
                Vector3 mid = feet - fwd * back;
                Vector3 from = mid - right * dir * halfRun;
                Vector3 to = mid + right * dir * halfRun;
                if (!OnFloor(ref from) || !OnFloor(ref to)) continue;
                Vector3 up = Vector3.up * 0.2f;
                if (Physics.Linecast(from + up, to + up, ~(1 << 2), QueryTriggerInteraction.Ignore)) continue;
                StartCoroutine(Run(from, to));
                return true;
            }
        }
        return false;
    }

    static bool OnFloor(ref Vector3 p)
    {
        if (!NavMesh.SamplePosition(p, out NavMeshHit hit, 0.4f, NavMesh.AllAreas)) return false;
        p = hit.position;
        return true;
    }

    IEnumerator Run(Vector3 from, Vector3 to)
    {
        running = true;
        Vector3 dir = (to - from).normalized;
        var cat = Instantiate(catPrefab, from, Quaternion.LookRotation(dir));
        PS1Style.Apply(cat);
        var anim = cat.GetComponentInChildren<Animation>();
        if (anim != null && anim.clip != null)
        {
            anim[anim.clip.name].speed = 1.4f;
            anim.Play();
        }

        var src = cat.AddComponent<AudioSource>();
        src.spatialBlend = 1f;
        src.minDistance = 1.5f;
        src.maxDistance = 15f;
        src.rolloffMode = AudioRolloffMode.Linear;
        src.pitch = Random.Range(0.9f, 1.1f);
        if (meow != null) src.PlayOneShot(meow, 1f);

        // Dash past behind the player, then keep running a bit further and vanish in the dark.
        float total = Vector3.Distance(from, to) + 4f;
        float travelled = 0f;
        Vector3 pos = from;
        while (travelled < total)
        {
            float step = runSpeed * Time.deltaTime;
            Vector3 next = pos + dir * step;
            if (travelled > Vector3.Distance(from, to))
            {
                // Past the end point: stop early at walls or NavMesh edges.
                if (!NavMesh.SamplePosition(next, out NavMeshHit h, 0.3f, NavMesh.AllAreas)) break;
                next.y = h.position.y;
            }
            pos = next;
            travelled += step;
            cat.transform.position = pos;
            yield return null;
        }

        // Gone into the dark: hide the model now, keep the object until the meow has finished.
        foreach (var r in cat.GetComponentsInChildren<Renderer>()) r.enabled = false;
        while (src.isPlaying) yield return null;
        Destroy(cat);
        running = false;
    }
}
