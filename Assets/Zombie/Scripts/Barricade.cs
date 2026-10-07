using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

/// <summary>
/// Call of Duty Zombies style window: a dark opening in an outside wall, boarded with planks.
/// Zombies spawn outside, tear the planks off one by one, then climb in.
/// Between waves the player repairs it: stand close and press a grip button, one plank per press.
/// The object's forward (blue) axis points into the room.
/// </summary>
public class Barricade : MonoBehaviour
{
    public static readonly List<Barricade> All = new List<Barricade>();

    [Header("Shape")]
    public int plankCount = 5;
    public Vector2 openingSize = new Vector2(1.2f, 1.3f);
    public float openingBottom = 0.55f;
    public Material plankMaterial;
    public Material holeMaterial;

    [Header("Alcove")]
    [Tooltip("Depth of the boarded window recess behind the planks (zombies stand in it).")]
    public float alcoveDepth = 1.1f;
    public Material frameMaterial;

    [Header("Zombies")]
    [Tooltip("Where the first zombie stands outside, along -forward.")]
    public float outsideDistance = 0.5f;
    [Tooltip("Spacing of the zombies queuing behind the first one.")]
    public float queueSpacing = 0.7f;
    [Tooltip("Where zombies land inside, along +forward.")]
    public float insideDistance = 0.9f;

    [Header("Repair")]
    public float repairRange = 1.7f;
    public float repairCooldown = 0.8f;
    public bool repairOnlyBetweenWaves = true;

    public int Planks => slots.FindAll(s => s.intact).Count;
    public bool HasPlanks => Planks > 0;
    public Vector3 OutsidePoint => transform.position - transform.forward * outsideDistance;
    public Vector3 InsidePoint => transform.position + transform.forward * insideDistance;
    /// <summary>Zombies waiting at this window, first one is tearing the planks.</summary>
    public readonly List<ZombieBreach> Queue = new List<ZombieBreach>();

    /// <summary>Queue slot: the first right behind the planks, the others hidden past the back of the recess.</summary>
    public Vector3 QueuePoint(int index)
    {
        float d = outsideDistance + (index > 0 ? alcoveDepth - outsideDistance + queueSpacing * index : 0f);
        return transform.position - transform.forward * d + transform.right * (index % 2 == 0 ? 0f : 0.3f);
    }

    class Slot
    {
        public Transform plank;
        public Vector3 localPos;
        public Quaternion localRot;
        public bool intact;
    }

    readonly List<Slot> slots = new List<Slot>();
    AudioSource audioSource;
    static AudioClip crackClip, hammerClip;
    WaveManager waves;
    Transform head;
    TextMeshPro prompt;
    InputAction repairAction;
    float nextRepair;
    bool repairing;

    void OnEnable() => All.Add(this);
    void OnDisable()
    {
        All.Remove(this);
        repairAction?.Disable();
    }

    void OnDestroy() => repairAction?.Dispose();

    void Start()
    {
        waves = FindAnyObjectByType<WaveManager>();
        var origin = FindAnyObjectByType<XROrigin>();
        head = origin != null && origin.Camera != null ? origin.Camera.transform : Camera.main.transform;

        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.spatialBlend = 1f;
        audioSource.maxDistance = 25f;
        audioSource.rolloffMode = AudioRolloffMode.Linear;
        if (crackClip == null) crackClip = CreateCrack();
        if (hammerClip == null) hammerClip = CreateHammer();

        BuildHole();
        BuildPlanks();
        BuildPrompt();

        repairAction = new InputAction("Repair", InputActionType.Button);
        repairAction.AddBinding("<XRController>{LeftHand}/{GripButton}");
        repairAction.AddBinding("<XRController>{RightHand}/{GripButton}");
        repairAction.AddBinding("<XRController>{LeftHand}/gripPressed");
        repairAction.AddBinding("<XRController>{RightHand}/gripPressed");
        repairAction.performed += _ => TryRepair();
        repairAction.Enable();
    }

    void Update()
    {
        if (prompt == null) return;
        bool show = CanRepair();
        if (prompt.gameObject.activeSelf != show) prompt.gameObject.SetActive(show);
        if (show) prompt.transform.rotation = Quaternion.LookRotation(prompt.transform.position - head.position);
    }

    bool PlayerClose()
    {
        if (head == null) return false;
        Vector3 d = head.position - transform.position;
        d.y = 0f;
        return d.magnitude < repairRange;
    }

    bool CanRepair()
    {
        if (repairing || Planks >= plankCount || !PlayerClose()) return false;
        if (repairOnlyBetweenWaves && waves != null && waves.Wave > 0 && !waves.InBreak) return false;
        return true;
    }

    public void TryRepair()
    {
        if (!CanRepair() || Time.time < nextRepair) return;
        nextRepair = Time.time + repairCooldown;
        var slot = slots.Find(s => !s.intact);
        if (slot != null) StartCoroutine(RepairRoutine(slot));
    }

    // ---------- Zombie side ----------

    /// <summary>Called by a zombie hitting the boards: removes one plank with a crack.</summary>
    public void BreakPlank()
    {
        // Top planks first, like boards being torn off from the outside.
        Slot slot = null;
        for (int i = slots.Count - 1; i >= 0; i--) if (slots[i].intact) { slot = slots[i]; break; }
        if (slot == null) return;
        slot.intact = false;
        audioSource.pitch = Random.Range(0.85f, 1.1f);
        audioSource.PlayOneShot(crackClip);

        // The plank flies into the room, then is put back in its slot hidden (ready for repair).
        var p = slot.plank;
        var rb = p.gameObject.AddComponent<Rigidbody>();
        rb.mass = 2f;
        rb.AddForce((transform.forward * 2.5f + Vector3.up * 1.5f + transform.right * Random.Range(-1f, 1f)), ForceMode.VelocityChange);
        rb.AddTorque(Random.insideUnitSphere * 8f, ForceMode.VelocityChange);
        StartCoroutine(HideAfter(slot, 3f));
    }

    IEnumerator HideAfter(Slot slot, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (slot.intact) yield break; // repaired meanwhile
        var rb = slot.plank.GetComponent<Rigidbody>();
        if (rb) Destroy(rb);
        slot.plank.gameObject.SetActive(false);
    }

    IEnumerator RepairRoutine(Slot slot)
    {
        repairing = true;
        slot.intact = true;
        var p = slot.plank;
        var rb = p.GetComponent<Rigidbody>();
        if (rb) Destroy(rb);
        p.gameObject.SetActive(true);

        // Plank rises from the floor in front of the window, spins into place, then gets nailed.
        Vector3 fromPos = transform.InverseTransformPoint(new Vector3(head.position.x, transform.position.y + 0.1f, head.position.z));
        Quaternion fromRot = slot.localRot * Quaternion.Euler(0f, 0f, 70f);
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / 0.45f;
            float e = 1f - (1f - t) * (1f - t);
            p.localPosition = Vector3.Lerp(fromPos, slot.localPos, e) + Vector3.up * Mathf.Sin(e * Mathf.PI) * 0.3f;
            p.localRotation = Quaternion.Slerp(fromRot, slot.localRot, e);
            yield return null;
        }
        p.localPosition = slot.localPos;
        p.localRotation = slot.localRot;
        for (int i = 0; i < 2; i++)
        {
            audioSource.pitch = Random.Range(0.9f, 1.15f);
            audioSource.PlayOneShot(hammerClip);
            InputDevices.GetDeviceAtXRNode(XRNode.LeftHand).SendHapticImpulse(0, 0.5f, 0.05f);
            InputDevices.GetDeviceAtXRNode(XRNode.RightHand).SendHapticImpulse(0, 0.5f, 0.05f);
            p.localPosition = slot.localPos - Vector3.forward * 0.02f;
            yield return new WaitForSeconds(0.06f);
            p.localPosition = slot.localPos;
            yield return new WaitForSeconds(0.12f);
        }
        repairing = false;
    }

    // ---------- Building ----------

    /// <summary>
    /// Boarded window recess built against the wall: dark back panel, side walls, sill and lintel,
    /// with colliders so the player cannot walk in, and a carving obstacle so agents path around it.
    /// </summary>
    void BuildHole()
    {
        float w = openingSize.x, h = openingSize.y, d = alcoveDepth, t = 0.12f;
        float top = openingBottom + h;
        float wallH = top + 0.35f;

        var back = GameObject.CreatePrimitive(PrimitiveType.Quad);
        back.name = "Outside";
        Destroy(back.GetComponent<Collider>());
        back.transform.SetParent(transform, false);
        back.transform.localPosition = new Vector3(0f, openingBottom + h * 0.5f, -d);
        back.transform.localScale = new Vector3(w, h, 1f);
        if (holeMaterial != null) back.GetComponent<Renderer>().sharedMaterial = holeMaterial;

        Box("Side L", new Vector3(-(w + t) * 0.5f, wallH * 0.5f, -d * 0.5f), new Vector3(t, wallH, d));
        Box("Side R", new Vector3((w + t) * 0.5f, wallH * 0.5f, -d * 0.5f), new Vector3(t, wallH, d));
        Box("Sill", new Vector3(0f, openingBottom * 0.5f, -0.15f), new Vector3(w, openingBottom, 0.3f));
        Box("Lintel", new Vector3(0f, (top + wallH) * 0.5f, -d * 0.5f), new Vector3(w, wallH - top, d));
        Box("Frame", new Vector3(0f, top + 0.05f, 0.02f), new Vector3(w + t * 2f + 0.1f, 0.1f, 0.06f));

        var obstacle = gameObject.AddComponent<UnityEngine.AI.NavMeshObstacle>();
        obstacle.carving = true;
        obstacle.center = new Vector3(0f, wallH * 0.5f, -d * 0.5f);
        obstacle.size = new Vector3(w + t * 2f, wallH, d);
    }

    void Box(string name, Vector3 localPos, Vector3 size)
    {
        var b = GameObject.CreatePrimitive(PrimitiveType.Cube);
        b.name = name;
        b.transform.SetParent(transform, false);
        b.transform.localPosition = localPos;
        b.transform.localScale = size;
        if (frameMaterial != null) b.GetComponent<Renderer>().sharedMaterial = frameMaterial;
    }

    void BuildPlanks()
    {
        float step = openingSize.y / plankCount;
        for (int i = 0; i < plankCount; i++)
        {
            var p = GameObject.CreatePrimitive(PrimitiveType.Cube);
            p.name = "Plank " + (i + 1);
            Destroy(p.GetComponent<Collider>());
            p.transform.SetParent(transform, false);
            var slot = new Slot
            {
                plank = p.transform,
                localPos = new Vector3(Random.Range(-0.05f, 0.05f), openingBottom + step * (i + 0.5f), 0.06f + 0.015f * (i % 2)),
                localRot = Quaternion.Euler(0f, 0f, Random.Range(-14f, 14f)),
                intact = true
            };
            p.transform.localPosition = slot.localPos;
            p.transform.localRotation = slot.localRot;
            p.transform.localScale = new Vector3(openingSize.x + 0.35f, step * 0.7f, 0.04f);
            if (plankMaterial != null) p.GetComponent<Renderer>().sharedMaterial = plankMaterial;
            slots.Add(slot);
        }
    }

    void BuildPrompt()
    {
        var go = new GameObject("Repair Prompt", typeof(TextMeshPro));
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, openingBottom + openingSize.y + 0.25f, 0.3f);
        prompt = go.GetComponent<TextMeshPro>();
        prompt.text = "GRIP : REPARER";
        prompt.fontSize = 1.2f;
        prompt.fontStyle = FontStyles.Bold;
        prompt.alignment = TextAlignmentOptions.Center;
        prompt.color = new Color(1f, 0.85f, 0.4f);
        prompt.rectTransform.sizeDelta = new Vector2(2f, 0.4f);
        go.SetActive(false);
    }

    static AudioClip CreateCrack()
    {
        const int rate = 44100;
        int n = (int)(rate * 0.5f);
        var d = new float[n];
        var rng = new System.Random(9);
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / rate;
            float noise = (float)(rng.NextDouble() * 2 - 1);
            float snap = noise * Mathf.Exp(-t * 25f);
            float splinter = (rng.NextDouble() < 0.05 * Mathf.Exp(-t * 6f) ? noise : 0f) * 0.7f;
            float thump = Mathf.Sin(2f * Mathf.PI * 70f * t) * Mathf.Exp(-t * 18f) * 0.6f;
            d[i] = Mathf.Clamp(snap + splinter + thump, -1f, 1f) * 0.9f;
        }
        var c = AudioClip.Create("WoodCrack", n, 1, rate, false);
        c.SetData(d, 0);
        return c;
    }

    static AudioClip CreateHammer()
    {
        const int rate = 44100;
        int n = (int)(rate * 0.18f);
        var d = new float[n];
        var rng = new System.Random(4);
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / rate;
            float knock = Mathf.Sin(2f * Mathf.PI * 180f * t) * Mathf.Exp(-t * 45f);
            float click = (float)(rng.NextDouble() * 2 - 1) * Mathf.Exp(-t * 120f) * 0.6f;
            d[i] = Mathf.Clamp(knock + click, -1f, 1f) * 0.9f;
        }
        var c = AudioClip.Create("Hammer", n, 1, rate, false);
        c.SetData(d, 0);
        return c;
    }
}
