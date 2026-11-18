using UnityEngine;
using UnityEngine.Events;

// Fires when the player (or an NPC) comes close, the player looks at or away from this spot, or after a delay
[AddComponentMenu("Game/Puzzle/Game Trigger")]
public class GameTrigger : MonoBehaviour
{
    // Saved as these numbers, so new options always go on the end with the next number
    public enum When
    {
        [InspectorName("Player Comes Close")] PlayerComesClose = 0,
        [InspectorName("Player Looks At This")] PlayerLooksAt = 1,
        [InspectorName("Player Looks Away From This")] PlayerLooksAway = 2,
        [InspectorName("Player Moves Away")] PlayerMovesAway = 3,
        [InspectorName("After A Delay")] AfterDelay = 4,
        [InspectorName("Only When Fire Is Called")] Manually = 5,
        [InspectorName("Someone Else Comes Close")] SomeoneComesClose = 6
    }

    [Header("When")]
    [DisplayAs("Fires When")]
    [SerializeField] private When when = When.PlayerComesClose;

    [DisplayAs("Distance (meters)", Min = 0f)]
    [SerializeField] private float distance = 3f;

    // Only for Someone Else Comes Close, e.g. an NPC walking past the fire
    [DisplayAs("Who (for Someone Else Comes Close)")]
    [SerializeField] private Transform who;

    [DisplayAs("Delay (seconds)", Min = 0f)]
    [SerializeField] private float delay = 10f;

    // Off, it waits for Arm
    [DisplayAs("Start Armed")]
    [SerializeField] private bool startArmed = true;

    [Header("Then")]
    [DisplayAs("Salem Thinks (optional)", Lines = 2)]
    [SerializeField] private string thought = "";

    [Foldout("Advanced")]
    [DisplayAs("Looking Angle (degrees)", Min = 1f, Max = 90f)]
    [SerializeField] private float lookAngle = 15f;

    [DisplayAs("For How Long (seconds)", Min = 0f)]
    [SerializeField] private float holdTime = 0.5f;

    [DisplayAs("Only Fire Once")]
    [SerializeField] private bool onlyOnce = true;

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

    private void Update()
    {
        if (!armed || when == When.Manually) return;

        // Nothing counts mid-conversation, during a cutscene, or while the camera is still easing back,
        // so a delay starts once the player can actually see the scene
        if (IntroCutscene.IsPlaying || (dialogue != null && dialogue.IsBusy)) return;

        bool condition = when == When.AfterDelay || ConditionMet();
        timer = condition ? timer + Time.deltaTime : 0f;

        float needed = when == When.AfterDelay ? delay : holdTime;
        if (condition && timer >= needed) Fire();
    }

    // Starts watching and restarts the timer
    public void Arm()
    {
        if (onlyOnce && fired) return;
        armed = true;
        timer = 0f;
    }

    public void Disarm() => armed = false;

    [ContextMenu("Fire Now")]
    public void Fire()
    {
        if (onlyOnce && fired) return;

        fired = true;
        armed = !onlyOnce;
        timer = 0f;

        if (!string.IsNullOrWhiteSpace(thought)) DetectiveThoughts.Get().Say(thought);
        onTriggered?.Invoke();
    }

    private bool ConditionMet()
    {
        // Measured along the floor, so an NPC's pivot height doesn't matter
        if (when == When.SomeoneComesClose)
        {
            if (who == null || !who.gameObject.activeInHierarchy) return false;
            Vector3 offset = transform.position - who.position;
            offset.y = 0f;
            return offset.sqrMagnitude <= distance * distance;
        }
        if (viewer == null) return false;

        Vector3 toThis = transform.position - viewer.position;
        bool close = toThis.sqrMagnitude <= distance * distance;

        switch (when)
        {
            case When.PlayerComesClose: return close;
            case When.PlayerLooksAt: return close && IsLookedAt(toThis);
            case When.PlayerLooksAway: return !IsLookedAt(toThis);
            case When.PlayerMovesAway: return !close;
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