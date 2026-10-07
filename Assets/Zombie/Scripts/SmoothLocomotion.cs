using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Turning;

/// <summary>
/// Smooth joystick movement: left stick moves relative to the head with acceleration and
/// deceleration (no instant start/stop), right stick turns continuously and smoothly.
/// Replaces the template's move/snap-turn providers at runtime.
/// </summary>
[DefaultExecutionOrder(-50)]
public class SmoothLocomotion : MonoBehaviour
{
    [Header("Move (left stick)")]
    public float moveSpeed = 2.5f;
    public float acceleration = 9f;
    public float deceleration = 14f;
    [Range(0f, 0.5f)] public float deadZone = 0.15f;

    [Header("Turn (right stick)")]
    public bool smoothTurn = true;
    public float turnSpeed = 110f;
    public float turnAcceleration = 600f;

    XROrigin origin;
    CharacterController body;
    Transform head;
    InputAction moveAction;
    InputAction turnAction;
    Vector3 velocity;
    float turnVelocity;

    void Awake()
    {
        origin = FindAnyObjectByType<XROrigin>();
        if (origin == null) { enabled = false; return; }
        body = origin.GetComponent<CharacterController>();
        head = origin.Camera != null ? origin.Camera.transform : Camera.main.transform;

        moveAction = new InputAction("SmoothMove", InputActionType.Value, expectedControlType: "Vector2");
        moveAction.AddBinding("<XRController>{LeftHand}/{Primary2DAxis}");
        moveAction.AddBinding("<XRController>{LeftHand}/thumbstick");
        turnAction = new InputAction("SmoothTurn", InputActionType.Value, expectedControlType: "Vector2");
        turnAction.AddBinding("<XRController>{RightHand}/{Primary2DAxis}");
        turnAction.AddBinding("<XRController>{RightHand}/thumbstick");
    }

    void Start()
    {
        // The template providers move in steps (snap turn) or start/stop instantly; this script takes over.
        foreach (var p in origin.GetComponentsInChildren<ContinuousMoveProvider>(true)) p.enabled = false;
        if (smoothTurn)
        {
            foreach (var p in origin.GetComponentsInChildren<SnapTurnProvider>(true)) p.enabled = false;
            foreach (var p in origin.GetComponentsInChildren<ContinuousTurnProvider>(true)) p.enabled = false;
        }
    }

    void OnEnable()
    {
        moveAction?.Enable();
        turnAction?.Enable();
    }

    void OnDisable()
    {
        moveAction?.Disable();
        turnAction?.Disable();
    }

    void OnDestroy()
    {
        moveAction?.Dispose();
        turnAction?.Dispose();
    }

    void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        // Move
        Vector2 stick = ApplyDeadZone(moveAction.ReadValue<Vector2>());
        Vector3 forward = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
        Vector3 right = Vector3.ProjectOnPlane(head.right, Vector3.up).normalized;
        Vector3 target = Vector3.ClampMagnitude(forward * stick.y + right * stick.x, 1f) * moveSpeed;
        float rate = target.sqrMagnitude > velocity.sqrMagnitude ? acceleration : deceleration;
        velocity = Vector3.MoveTowards(velocity, target, rate * dt);

        if (velocity.sqrMagnitude > 1e-6f)
        {
            Vector3 step = velocity * dt;
            if (body != null && body.enabled) body.Move(step);
            else origin.transform.position += step;
        }

        // Turn around the head so the view does not swing sideways.
        if (smoothTurn)
        {
            float x = ApplyDeadZone(turnAction.ReadValue<Vector2>()).x;
            turnVelocity = Mathf.MoveTowards(turnVelocity, x * turnSpeed, turnAcceleration * dt);
            if (Mathf.Abs(turnVelocity) > 0.01f)
                origin.RotateAroundCameraUsingOriginUp(turnVelocity * dt);
        }
    }

    Vector2 ApplyDeadZone(Vector2 v)
    {
        float m = v.magnitude;
        if (m < deadZone) return Vector2.zero;
        return v / m * Mathf.Clamp01((m - deadZone) / (1f - deadZone));
    }
}
