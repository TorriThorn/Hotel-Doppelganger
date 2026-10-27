using UnityEngine;

// Turns a sprite NPC to face the player when they're within range.
// While the NPC is walking (see NPCPatrol) it always faces the player so the sprite never turns edge-on.
[AddComponentMenu("Game/NPC/NPC Face Player")]
public class NPCFacePlayer : MonoBehaviour
{
    private const float MinFacingDistanceSqr = 0.001f;

    [Foldout("References (filled in automatically)")]
    [DisplayAs("Player (optional)")]
    [SerializeField] private Transform player;

    [Header("Facing")]
    [DisplayAs("Notice Distance", Min = 0f)]
    [SerializeField] private float noticeRange = 6f;

    [DisplayAs("Turn Speed", Min = 0f)]
    [SerializeField] private float turnSpeed = 4f;

    [DisplayAs("Turn Back When Player Leaves")]
    [SerializeField] private bool returnWhenFar = true;

    private NPCPatrol patrol;
    private float startYaw;
    private float currentYaw;

    private void Start()
    {
        patrol = GetComponent<NPCPatrol>();
        startYaw = transform.eulerAngles.y;
        currentYaw = startYaw;

        if (player == null)
        {
            FirstPersonController firstPerson = FindAnyObjectByType<FirstPersonController>();
            if (firstPerson != null) player = firstPerson.transform;
        }
    }

    private void Update()
    {
        if (player == null || !TryGetTargetYaw(out float targetYaw)) return;

        currentYaw = Mathf.LerpAngle(currentYaw, targetYaw, turnSpeed * Time.deltaTime);
        transform.rotation = Quaternion.Euler(0f, currentYaw, 0f);
    }

    private bool TryGetTargetYaw(out float yaw)
    {
        Vector3 toPlayer = player.position - transform.position;
        toPlayer.y = 0f;

        bool isWalking = patrol != null && patrol.IsWalking;
        bool shouldFacePlayer = (isWalking || toPlayer.magnitude <= noticeRange)
                                && toPlayer.sqrMagnitude > MinFacingDistanceSqr;

        if (shouldFacePlayer)
        {
            // Sprites show their front toward -Z, so point +Z away from the player
            yaw = Mathf.Atan2(-toPlayer.x, -toPlayer.z) * Mathf.Rad2Deg;
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