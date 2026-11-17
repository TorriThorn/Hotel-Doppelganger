using System.Collections;
using UnityEngine;
using UnityEngine.Events;

// Clicking this does something before it can be picked up, like opening a tin, or instead of picking it up,
// like flipping a light switch. Needs a Rigidbody (kinematic is fine for things fixed to a wall)
[AddComponentMenu("Game/Interaction/Usable")]
[RequireComponent(typeof(Rigidbody))]
public class Usable : MonoBehaviour
{
    [Header("Clicking It")]
    [DisplayAs("Salem Thinks (optional)", Lines = 2)]
    [SerializeField] private string thought = "";

    [DisplayAs("Sound (optional)")]
    [SerializeField] private AudioClip sound;

    [DisplayAs("Can Pick It Up Afterwards")]
    [SerializeField] private bool canPickUpAfter = true;

    // Off, it can only be used once
    [DisplayAs("Can Use Again", HideIf = nameof(canPickUpAfter))]
    [SerializeField] private bool reusable;

    [Foldout("Opening (optional)")]
    [DisplayAs("Lid To Swing Open")]
    [SerializeField] private Transform lid;

    [DisplayAs("Lid Open Rotation (degrees)")]
    [SerializeField] private Vector3 lidOpenRotation = new Vector3(-110f, 0f, 0f);

    [DisplayAs("Opening Time (seconds)", Min = 0f)]
    [SerializeField] private float openTime = 0.35f;

    [DisplayAs("Show When Used, e.g. the powder")]
    [SerializeField] private GameObject showWhenUsed;

    [DisplayAs("Hide When Used, e.g. a closed model")]
    [SerializeField] private GameObject hideWhenUsed;

    [Foldout("Events")]
    [DisplayAs("On Used")]
    public UnityEvent onUsed;

    private Quaternion lidClosed;
    private int uses;

    public bool WasUsed => uses > 0;

    // Whether a click should use it rather than pick it up
    public bool WantsClick => isActiveAndEnabled && (uses == 0 || (reusable && !canPickUpAfter));

    public bool CanPickUp => canPickUpAfter && WasUsed;

    private void Awake()
    {
        if (lid != null) lidClosed = lid.localRotation;
        if (showWhenUsed != null) showWhenUsed.SetActive(false);
    }

    public void Use()
    {
        uses++;

        if (sound != null) AudioSource.PlayClipAtPoint(sound, transform.position, 1f);
        if (!string.IsNullOrWhiteSpace(thought) && uses == 1) DetectiveThoughts.Get().Say(thought);

        // A reusable switch flips back and forth
        bool on = !reusable || uses % 2 == 1;
        if (lid != null) StartCoroutine(SwingLid(on));
        if (showWhenUsed != null) showWhenUsed.SetActive(on);
        if (hideWhenUsed != null) hideWhenUsed.SetActive(!on);

        onUsed?.Invoke();
    }

    private IEnumerator SwingLid(bool open)
    {
        Quaternion from = lid.localRotation;
        Quaternion to = open ? lidClosed * Quaternion.Euler(lidOpenRotation) : lidClosed;

        for (float t = 0f; t < 1f;)
        {
            t = openTime > 0f ? Mathf.Min(1f, t + Time.deltaTime / openTime) : 1f;
            lid.localRotation = Quaternion.Slerp(from, to, Mathf.SmoothStep(0f, 1f, t));
            yield return null;
        }
    }
}