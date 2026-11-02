using UnityEngine;
using UnityEngine.Events;

// Fires an event when something happens: the player walks close, looks at this spot, looks away from it,
// or a timer runs out. Use it for timed sequences, Salem's comments, and NPCs acting behind the player's back.
[AddComponentMenu("Game/Puzzle/Game Trigger")]
public class GameTrigger : MonoBehaviour
{
    public enum When
    {
        [InspectorName("Player Comes Close")] PlayerComesClose,
        [InspectorName("Player Looks At This")] PlayerLooksAt,
        [InspectorName("Player Looks Away From This")] PlayerLooksAway,
        [InspectorName("After A Delay")] AfterDelay,
        [InspectorName("Only When Fire Is Called")] Manually
    }

    [Header("When")]
    [DisplayAs("Fires When")]
    [SerializeField] private When when = When.PlayerComesClose;

    [DisplayAs("Distance (meters)", Min = 0f)]
    [SerializeField] private float distance = 3f;

    [DisplayAs("Looking Angle (degrees)", Min = 1f, Max = 90f)]
    [SerializeField] private float lookAngle = 15f;

    [DisplayAs("For How Long (seconds)", Min = 0f)]
    [SerializeField] private float holdTime = 0.5f;

    [DisplayAs("Delay (seconds)", Min = 0f)]
    [SerializeField] private float delay = 10f;

    [DisplayAs("Start Armed")]
    [SerializeField] private bool startArmed = true;

    [DisplayAs("Only Fire Once")]
    [SerializeField] private bool onlyOnce = true;

    [Header("Then")]
    [DisplayAs("Salem Thinks (optional)", Lines = 2)]
    [SerializeField] private string thought = "";

    [Foldout("Events")]
    public UnityEvent onTriggered;

    private Transform viewer;
    private PlayerDialogue dialogue;
    private float timer;
    private bool armed;
    private bool fired;

    private void Start()
    {
        var player = FindAnyObjectByType<FirstPersonController>();
        if (player != null) viewer = player.CameraTransform != null ? player.CameraTransform : player.transform;
        dialogue = FindAnyObjectByType<PlayerDialogue>();
        if (startArmed) Arm();
    }

    // Starts watching (and restarts the timer). Not armed at start, a trigger waits for this, e.g. from another event.
    public void Arm()
    {
        if (onlyOnce && fired) return;
        armed = true;
        timer = 0f;
    }

    public void Disarm() => armed = false;

    public void Fire()
    {
        if (onlyOnce && fired) return;

        fired = true;
        armed = !onlyOnce;
        timer = 0f;

        if (!string.IsNullOrWhiteSpace(thought)) DetectiveThoughts.Get().Say(thought);
        onTriggered?.Invoke();
    }

    private void Update()
    {
        if (!armed || when == When.Manually) return;

        // Paused menus stop the clock, and nothing fires mid-conversation or during a cutscene
        if (IntroCutscene.IsPlaying || (dialogue != null && dialogue.IsTalking)) return;

        bool condition = when == When.AfterDelay || ConditionMet();
        timer = condition ? timer + Time.deltaTime : 0f;

        float needed = when == When.AfterDelay ? delay : holdTime;
        if (condition && timer >= needed) Fire();
    }

    private bool ConditionMet()
    {
        if (viewer == null) return false;

        Vector3 toThis = transform.position - viewer.position;
        bool close = toThis.sqrMagnitude <= distance * distance;

        switch (when)
        {
            case When.PlayerComesClose: return close;
            case When.PlayerLooksAt: return close && IsLookedAt(toThis);
            case When.PlayerLooksAway: return !IsLookedAt(toThis);
            default: return false;
        }
    }

    private bool IsLookedAt(Vector3 toThis) =>
        toThis.sqrMagnitude > 0.0001f && Vector3.Angle(viewer.forward, toThis) <= lookAngle;

    private void OnDrawGizmosSelected()
    {
        if (when == When.AfterDelay || when == When.Manually) return;
        Gizmos.color = new Color(1f, 0.5f, 0.9f, 0.8f);
        Gizmos.DrawWireSphere(transform.position, distance);
    }
}
