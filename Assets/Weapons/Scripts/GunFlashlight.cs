using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

/// <summary>
/// Flashlight under the barrel: a spot light read by the PS1 shader. B (right secondary button) toggles it.
/// </summary>
[RequireComponent(typeof(Gun))]
public class GunFlashlight : MonoBehaviour
{
    public bool startOn = true;
    public float intensity = 2.5f;
    public float range = 14f;
    public float spotAngle = 50f;
    public float innerSpotAngle = 25f;
    public Color color = new Color(1f, 0.93f, 0.8f);
    public Vector3 localOffset = new Vector3(0f, -0.01f, 0.1f);

    Light spot;
    InputAction toggleAction;
    AudioSource click;
    AudioClip clickClip;

    void Awake()
    {
        var go = new GameObject("Flashlight");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = localOffset;
        spot = go.AddComponent<Light>();
        spot.type = LightType.Spot;
        spot.range = range;
        spot.spotAngle = spotAngle;
        spot.innerSpotAngle = innerSpotAngle;
        spot.color = color;
        spot.intensity = intensity;
        spot.shadows = LightShadows.None;
        spot.enabled = startOn;

        click = gameObject.AddComponent<AudioSource>();
        click.spatialBlend = 1f;
        click.playOnAwake = false;
        clickClip = CreateClick();
    }

    void OnEnable()
    {
        toggleAction = new InputAction("Flashlight", InputActionType.Button);
        toggleAction.AddBinding("<XRController>{RightHand}/{SecondaryButton}");
        toggleAction.AddBinding("<XRController>{RightHand}/secondaryButton");
        toggleAction.performed += _ => Toggle();
        toggleAction.Enable();
    }

    void OnDisable()
    {
        toggleAction?.Disable();
        toggleAction?.Dispose();
        toggleAction = null;
    }

    public void Toggle()
    {
        spot.enabled = !spot.enabled;
        click.PlayOneShot(clickClip, 0.6f);
        InputDevices.GetDeviceAtXRNode(XRNode.RightHand).SendHapticImpulse(0, 0.2f, 0.03f);
    }

    static AudioClip CreateClick()
    {
        const int rate = 44100;
        int n = rate / 20;
        var data = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / rate;
            data[i] = Mathf.Sin(2f * Mathf.PI * 3200f * t) * Mathf.Exp(-t * 180f) * 0.6f;
        }
        var clip = AudioClip.Create("Click", n, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
