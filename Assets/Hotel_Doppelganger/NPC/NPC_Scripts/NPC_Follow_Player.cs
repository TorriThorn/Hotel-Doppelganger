using UnityEngine;
using UnityEngine.Events;

// Sends an NPC after the player: Stalk stays behind them and freezes when looked at, Follow walks along,
// and Approach walks up and starts talking. Needs NPC Patrol on the NPC
[AddComponentMenu("Game/NPC/NPC Follow Player")]
public class NPCFollowPlayer : MonoBehaviour
{
    public enum Style
    {
        [InspectorName("Stalk (stays behind, out of sight)")] Stalk,
        [InspectorName("Follow (walks along after them)")] Follow,
        [InspectorName("Approach (walks up and starts talking)")] Approach
    }

    private const float RepathInterval = 0.25f;
    private const float SeenAngle = 55f;
    private const float SeenDistance = 30f;

    [Header("Following")]
    [DisplayAs("Style")]
    [SerializeField] private Style style = Style.Stalk;

    // For Approach, how close they come before talking
    [DisplayAs("Distance From The Player (meters)", Min = 0.5f)]
    [SerializeField] private float distance = 5f;

    // Counts play time only. Approach ignores it
    [DisplayAs("Finish After (seconds, 0 = until Stop Following)", Min = 0f)]
    [SerializeField] private float duration = 15f;

    [DisplayAs("Approach: Start Talking At Node (blank = first node)")]
    [SerializeField] private string conversationNode = "";

    [Foldout("Advanced")]
    [DisplayAs("Walk Speed (0 = their usual)", Min = 0f)]
    [SerializeField] private float walkSpeed;

    [DisplayAs("Start When Scene Starts")]
    [SerializeField] private bool startOnSceneStart;

    [Foldout("Events")]
    [DisplayAs("On Finished")]
    public UnityEvent onFinished;

    [Foldout("References (filled in automatically)")]
    [DisplayAs("NPC Patrol (optional)")]
    [SerializeField] private NPCPatrol patrol;

    [DisplayAs("NPC Dialogue (optional)")]
    [SerializeField] private NPCDialogue npc;

    [DisplayAs("Player (optional)")]
    [SerializeField] private FirstPersonController player;

    private PlayerDialogue dialogue;
    private float usualSpeed;
    private float elapsed;
    private float repathTimer;

    public bool IsFollowing { get; private set; }

    private Vector3 PlayerPosition => player.transform.position;

    private Transform View => player.CameraTransform != null ? player.CameraTransform : player.transform;

    private void Start()
    {
        if (patrol == null) patrol = GetComponentInParent<NPCPatrol>();
        if (patrol == null) patrol = GetComponentInChildren<NPCPatrol>();
        if (npc == null) npc = GetComponentInChildren<NPCDialogue>();
        if (npc == null) npc = GetComponentInParent<NPCDialogue>();
        if (player == null) player = FindAnyObjectByType<FirstPersonController>();
        dialogue = FindAnyObjectByType<PlayerDialogue>();

        if (startOnSceneStart) StartFollowing();
    }

    private void Update()
    {
        if (!IsFollowing) return;

        if (IntroCutscene.IsPlaying || (dialogue != null && dialogue.IsTalking) || Journal.Get().IsOpen) return;

        elapsed += Time.deltaTime;
        if (style != Style.Approach && duration > 0f && elapsed >= duration)
        {
            Finish();
            return;
        }

        repathTimer -= Time.deltaTime;
        if (repathTimer > 0f) return;
        repathTimer = RepathInterval;

        switch (style)
        {
            case Style.Stalk:
                Stalk();
                break;
            case Style.Follow:
                patrol.WalkTowards(Beside(distance));
                break;
            default:
                Approach();
                break;
        }
    }

    public void StartFollowing()
    {
        if (IsFollowing || patrol == null || player == null) return;

        IsFollowing = true;
        elapsed = 0f;
        repathTimer = 0f;
        usualSpeed = patrol.WalkSpeed;
        if (walkSpeed > 0f) patrol.WalkSpeed = walkSpeed;
    }

    // Leaves them standing there. NPC Patrol > Resume Patrol sends them back to their route
    public void StopFollowing()
    {
        if (!IsFollowing) return;

        IsFollowing = false;
        patrol.WalkSpeed = usualSpeed;
        patrol.StopPatrol();
    }

    private void Finish()
    {
        StopFollowing();
        onFinished?.Invoke();
    }

    // Keeps behind the player, and stands still whenever they could be seen moving
    private void Stalk()
    {
        if (InPlayersView())
        {
            patrol.StopPatrol();
            return;
        }

        Vector3 behind = PlayerPosition - Flat(View.forward) * distance;
        patrol.WalkTowards(behind);
    }

    private void Approach()
    {
        Vector3 gap = PlayerPosition - transform.position;
        gap.y = 0f;

        if (gap.magnitude > distance)
        {
            patrol.WalkTowards(Beside(distance * 0.8f));
            return;
        }

        // Waits beside the player until a conversation can start
        patrol.StopPatrol();
        if (dialogue == null || npc == null || dialogue.StartConversation(npc, conversationNode)) Finish();
    }

    // A spot this far from the player on the NPC's side
    private Vector3 Beside(float gap)
    {
        Vector3 away = transform.position - PlayerPosition;
        away.y = 0f;
        if (away.sqrMagnitude < 0.01f) away = -Flat(View.forward);
        return PlayerPosition + away.normalized * gap;
    }

    private bool InPlayersView()
    {
        Vector3 toNpc = transform.position - View.position;
        toNpc.y = 0f;
        if (toNpc.sqrMagnitude > SeenDistance * SeenDistance) return false;

        return Vector3.Angle(Flat(View.forward), toNpc) < SeenAngle;
    }

    private static Vector3 Flat(Vector3 direction)
    {
        direction.y = 0f;
        return direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;
    }
}