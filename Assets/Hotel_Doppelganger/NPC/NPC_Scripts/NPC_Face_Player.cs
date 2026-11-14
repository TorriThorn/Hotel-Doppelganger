using UnityEngine;

// Turns a sprite NPC to face the camera when the player is close, and always while walking or talking
[AddComponentMenu("Game/NPC/NPC Face Player")]
public class NPCFacePlayer : MonoBehaviour
{
    private const float MinFacingDistanceSqr = 0.001f;

    private const float ConversationTurnSpeed = 10f;

    [Header("Facing")]
    [DisplayAs("Notice Distance", Min = 0f)]
    [SerializeField] private float noticeRange = 6f;

    [DisplayAs("Turn Speed", Min = 0f)]
    [SerializeField] private float turnSpeed = 4f;

    [DisplayAs("Turn Back When Player Leaves")]
    [SerializeField] private bool returnWhenFar = true;

    // A flat sprite looks the same from either side, so it only turns the short way
    [DisplayAs("Flat Sprite (never turn more than 90\u00b0)")]
    [SerializeField] private bool flatSprite = true;

    [Foldout("References (filled in automatically)")]
    [DisplayAs("Player (optional)")]
    [SerializeField] private Transform player;

    private NPCPatrol patrol;
    private Transform viewer;
    private float startYaw;
    private float currentYaw;

    public float NoticeRange { get => noticeRange; set => noticeRange = Mathf.Max(0f, value); }
    public float TurnSpeed { get => turnSpeed; set => turnSpeed = Mathf.Max(0f, value); }

    public bool InConversation { get; set; }

    private void Start()
    {
        TryGetComponent(out patrol);
        startYaw = transform.eulerAngles.y;
        currentYaw = startYaw;

        if (player == null)
        {
            var firstPerson = FindAnyObjectByType<FirstPersonController>();
            if (firstPerson != null) player = firstPerson.transform;
        }

        FirstPersonController controller = player != null ? player.GetComponent<FirstPersonController>() : null;
        viewer = controller != null && controller.CameraTransform != null ? controller.CameraTransform : player;
        if (viewer == null && Camera.main != null) viewer = Camera.main.transform;
    }

    private void Update()
    {
        if (viewer == null || !TryGetTargetYaw(out float targetYaw)) return;
        if (flatSprite) targetYaw = ShortestFacing(currentYaw, targetYaw);

        float speed = InConversation ? Mathf.Max(turnSpeed, ConversationTurnSpeed) : turnSpeed;
        currentYaw = Mathf.LerpAngle(currentYaw, targetYaw, speed * Time.deltaTime);
        transform.rotation = Quaternion.Euler(0f, currentYaw, 0f);
    }

    private static float ShortestFacing(float from, float target)
    {
        float flipped = target + 180f;
        return Mathf.Abs(Mathf.DeltaAngle(from, target)) <= Mathf.Abs(Mathf.DeltaAngle(from, flipped)) ? target : flipped;
    }

    private bool TryGetTargetYaw(out float yaw)
    {
        Vector3 toViewer = viewer.position - transform.position;
        toViewer.y = 0f;
        float distanceSqr = toViewer.sqrMagnitude;

        bool alwaysFace = InConversation || (patrol != null && patrol.IsWalking);
        if ((alwaysFace || distanceSqr <= noticeRange * noticeRange) && distanceSqr > MinFacingDistanceSqr)
        {
            // Sprites show their front toward -Z, so point +Z away from the camera
            yaw = Mathf.Atan2(-toViewer.x, -toViewer.z) * Mathf.Rad2Deg;
            return true;
        }

        yaw = startYaw;
        return returnWhenFar;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, noticeRange);
    }
}