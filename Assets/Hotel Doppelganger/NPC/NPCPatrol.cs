using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Application = UnityEngine.Application;
using Random = UnityEngine.Random;

// Walks an NPC between waypoints, waiting at each one. Paths around obstacles when it has a NavMeshAgent
// on a baked NavMesh, otherwise walks in straight lines. The route can be changed at runtime from UnityEvents.
[AddComponentMenu("Game/NPC/NPC Patrol")]
public class NPCPatrol : MonoBehaviour
{
    public enum PatrolMode
    {
        [InspectorName("Back And Forth  (A > B > A > B)")] PingPong,
        [InspectorName("Loop  (A > B > C > A)")] Loop,
        [InspectorName("Once  (A > B > C, then stop)")] Once
    }

    [Serializable]
    public class Waypoint
    {
        [DisplayAs("Spot")]
        public Transform point;

        [DisplayAs("Part Of Route")]
        public bool active = true;

        // Recorded once at start, so waypoints parented to the NPC don't move with it
        [NonSerialized] public Vector3 position;
        [NonSerialized] public bool cached;
    }

    private const float MovingSpeedThreshold = 0.05f;
    private const float StuckSpeedThreshold = 0.1f;
    private const float FlipDeadZone = 0.1f;

    private static readonly List<NPCPatrol> active = new List<NPCPatrol>();

    [Header("Route")]
    [SerializeField] private List<Waypoint> waypoints = new List<Waypoint>();

    [DisplayAs("Walk Pattern")]
    [SerializeField] private PatrolMode mode = PatrolMode.PingPong;

    [DisplayAs("Start On First Spot")]
    [SerializeField] private bool startAtFirstPoint = true;

    [DisplayAs("Wait At Each Spot (seconds)", HideIf = nameof(randomWait), Min = 0f)]
    [SerializeField] private float defaultWaitTime = 2f;

    [DisplayAs("Random Wait Time")]
    [SerializeField] private bool randomWait;

    [DisplayAs("Shortest Wait (seconds)", ShowIf = nameof(randomWait), Min = 0f)]
    [SerializeField] private float minRandomWait = 1f;

    [DisplayAs("Longest Wait (seconds)", ShowIf = nameof(randomWait), Min = 0f)]
    [SerializeField] private float maxRandomWait = 10f;

    [Header("Movement")]
    [DisplayAs("Walk Speed", Min = 0f)]
    [SerializeField] private float walkSpeed = 1.5f;

    [DisplayAs("Speed Up / Slow Down", Min = 0f)]
    [SerializeField] private float acceleration = 4f;

    [DisplayAs("Arrival Distance", Min = 0f)]
    [SerializeField] private float arriveDistance = 0.1f;

    [DisplayAs("Stay At Own Height")]
    [SerializeField] private bool keepHeight = true;

    [Header("Walking Around Obstacles")]
    [DisplayAs("Walk Around Obstacles")]
    [SerializeField] private bool useNavMesh = true;

    [DisplayAs("Give Up After (seconds)", ShowIf = nameof(useNavMesh), Min = 0f)]
    [SerializeField] private float giveUpTime = 4f;

    [DisplayAs("Add Collider If Missing", ShowIf = nameof(useNavMesh))]
    [SerializeField] private bool addColliderIfMissing = true;

    [Header("Player")]
    [DisplayAs("Stop When Player Is Close")]
    [SerializeField] private bool stopForPlayer = true;

    [DisplayAs("Stop Distance", ShowIf = nameof(stopForPlayer), Min = 0f)]
    [SerializeField] private float stopRange = 2f;

    [DisplayAs("Player (optional)")]
    [SerializeField] private Transform player;

    [Foldout("Animation & Sprite")]
    [DisplayAs("Animator (optional)")]
    [SerializeField] private Animator animator;

    [DisplayAs("Walking Parameter Name")]
    [SerializeField] private string walkingBool = "IsWalking";

    [DisplayAs("Sprite (optional)")]
    [SerializeField] private SpriteRenderer sprite;

    [DisplayAs("Flip Sprite When Walking")]
    [SerializeField] private bool flipSpriteToWalkDirection = true;

    [DisplayAs("Artwork Faces Right", ShowIf = nameof(flipSpriteToWalkDirection))]
    [SerializeField] private bool spriteFacesRight = true;

    private readonly List<Waypoint> route = new List<Waypoint>();
    private CharacterController controller;
    private NavMeshAgent agent;
    private Transform cameraTransform;

    private Waypoint target;
    private int routeIndex;
    private int pingPongStep = 1;
    private float waitTimer;
    private float speed;
    private float stuckTimer;
    private bool destinationSet;
    private bool hasStarted;
    private bool isFinished;
    private bool isStopped;
    private bool stopOnArrival;

    private int walkingHash;
    private bool hasWalkingBool;

    // Every enabled patrol, so other systems (like the Playtest menu) can adjust them all at once
    public static IReadOnlyList<NPCPatrol> Active => active;

    public bool IsWalking { get; private set; }
    public bool IsStopped => isStopped;

    // Holds the NPC in place temporarily, e.g. for the length of a conversation
    public bool Paused { get; set; }

    public float WalkSpeed { get => walkSpeed; set => walkSpeed = Mathf.Max(0f, value); }
    public bool UseRandomWait { get => randomWait; set => randomWait = value; }
    public float WaitTime { get => defaultWaitTime; set => defaultWaitTime = Mathf.Max(0f, value); }
    public float ShortestWait { get => minRandomWait; set => minRandomWait = Mathf.Max(0f, value); }
    public float LongestWait { get => maxRandomWait; set => maxRandomWait = Mathf.Max(0f, value); }
    public bool StopForPlayer { get => stopForPlayer; set => stopForPlayer = value; }
    public float StopRange { get => stopRange; set => stopRange = Mathf.Max(0f, value); }

    private bool UsingAgent => agent != null && agent.enabled && agent.isOnNavMesh;

    private void OnEnable()
    {
        if (!active.Contains(this)) active.Add(this);
    }

    private void OnDisable() => active.Remove(this);

    private void Start()
    {
        TryGetComponent(out controller);
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (sprite == null) sprite = GetComponentInChildren<SpriteRenderer>();

        if (player == null)
        {
            var firstPerson = FindAnyObjectByType<FirstPersonController>();
            if (firstPerson != null) player = firstPerson.transform;
        }

        ConfigureAgent();
        CacheWalkingParameter();

        foreach (Waypoint waypoint in waypoints) CachePosition(waypoint);

        hasStarted = true;
        BuildRoute();

        if (route.Count == 0 || !startAtFirstPoint) return;

        Teleport(UsingAgent ? route[0].position : Flatten(route[0].position));
        routeIndex = 0;
        waitTimer = NextWaitTime();
        Advance();
    }

    private void Update()
    {
        bool hasDestination = target != null && !isFinished && (route.Count > 0 || stopOnArrival);
        if (isStopped || !hasDestination)
        {
            SetWalking(false);
            return;
        }

        if (waitTimer > 0f)
        {
            waitTimer -= Time.deltaTime;
            speed = 0f;
            SetWalking(false);
            return;
        }

        if (UsingAgent) UpdateWithAgent();
        else UpdateStraightLine();
    }

    // Turns a waypoint on. One that isn't in the list yet goes on the end of the route.
    public void AddWaypoint(Transform point)
    {
        if (point == null) return;

        Waypoint waypoint = FindWaypoint(point);
        if (waypoint == null)
        {
            waypoint = new Waypoint { point = point };
            waypoints.Add(waypoint);
        }

        waypoint.active = true;
        if (hasStarted) CachePosition(waypoint);
        BuildRoute();
    }

    // Turns a waypoint off. It keeps its place in the list so AddWaypoint can bring it back.
    public void RemoveWaypoint(Transform point)
    {
        Waypoint waypoint = FindWaypoint(point);
        if (waypoint == null) return;

        waypoint.active = false;
        BuildRoute();
    }

    public void ToggleWaypoint(Transform point)
    {
        Waypoint waypoint = FindWaypoint(point);
        if (waypoint != null && waypoint.active) RemoveWaypoint(point);
        else AddWaypoint(point);
    }

    // Stops where the NPC is standing until ResumePatrol is called
    public void StopPatrol()
    {
        isStopped = true;
        stopOnArrival = false;
        waitTimer = 0f;
        speed = 0f;
        StopAgent();
        SetWalking(false);
    }

    // Walks to this point and stays there until ResumePatrol is called
    public void StopAtWaypoint(Transform point)
    {
        if (point == null)
        {
            StopPatrol();
            return;
        }

        Waypoint waypoint = FindWaypoint(point) ?? new Waypoint { point = point };
        waypoint.position = point.position;
        waypoint.cached = true;

        target = waypoint;
        isStopped = false;
        stopOnArrival = true;
        isFinished = false;
        waitTimer = 0f;
        StopAgent();
    }

    public void ResumePatrol()
    {
        isStopped = false;
        stopOnArrival = false;
        target = null;
        routeIndex = 0;
        BuildRoute();
    }

    private void ConfigureAgent()
    {
        if (!useNavMesh || !TryGetComponent(out agent)) return;

        agent.speed = walkSpeed;
        agent.acceleration = acceleration;
        agent.stoppingDistance = 0f;
        agent.autoBraking = true;
        agent.updateRotation = false;   // NPCFacePlayer handles facing

        // Agents have no collider of their own, so without one the player can't aim at the NPC to talk
        if (addColliderIfMissing && GetComponentInChildren<Collider>() == null)
        {
            var capsule = gameObject.AddComponent<CapsuleCollider>();
            capsule.radius = agent.radius;
            capsule.height = agent.height;
            capsule.center = new Vector3(0f, agent.height * 0.5f - agent.baseOffset, 0f);
        }
    }

    private void CacheWalkingParameter()
    {
        if (animator == null || animator.runtimeAnimatorController == null || string.IsNullOrEmpty(walkingBool)) return;

        walkingHash = Animator.StringToHash(walkingBool);
        foreach (AnimatorControllerParameter parameter in animator.parameters)
        {
            if (parameter.nameHash != walkingHash || parameter.type != AnimatorControllerParameterType.Bool) continue;

            hasWalkingBool = true;
            animator.SetBool(walkingHash, false);   // SetWalking only sends changes, so start from a known state
            break;
        }
    }

    private Waypoint FindWaypoint(Transform point)
    {
        foreach (Waypoint waypoint in waypoints)
        {
            if (waypoint != null && waypoint.point == point) return waypoint;
        }

        return null;
    }

    private static void CachePosition(Waypoint waypoint)
    {
        if (waypoint == null || waypoint.point == null || waypoint.cached) return;

        waypoint.position = waypoint.point.position;
        waypoint.cached = true;
    }

    // Rebuilds the active route and keeps the NPC heading somewhere sensible
    private void BuildRoute()
    {
        if (!hasStarted) return;

        Waypoint previousTarget = target;
        int previousIndex = routeIndex;

        route.Clear();
        foreach (Waypoint waypoint in waypoints)
        {
            if (waypoint != null && waypoint.active && waypoint.point != null && waypoint.cached) route.Add(waypoint);
        }

        // While stopped (or walking to a final spot) keep the new route for later but don't redirect
        if (isStopped || stopOnArrival) return;

        if (route.Count == 0)
        {
            target = null;
            isFinished = true;
            StopAgent();
            return;
        }

        if (mode != PatrolMode.Once) isFinished = false;

        int keptIndex = previousTarget != null ? route.IndexOf(previousTarget) : -1;
        if (keptIndex >= 0)
        {
            routeIndex = keptIndex;
        }
        else
        {
            // The point it was walking to got removed, so head for whatever now sits in that slot
            routeIndex = Mathf.Clamp(previousIndex, 0, route.Count - 1);
            destinationSet = false;
            if (waitTimer <= 0f) StopAgent();
        }

        if (mode == PatrolMode.PingPong)
        {
            if (routeIndex >= route.Count - 1) pingPongStep = -1;
            else if (routeIndex <= 0) pingPongStep = 1;
        }

        target = route[routeIndex];
    }

    private float NextWaitTime()
    {
        if (!randomWait) return defaultWaitTime;
        return Random.Range(Mathf.Min(minRandomWait, maxRandomWait), Mathf.Max(minRandomWait, maxRandomWait));
    }

    private void Arrive()
    {
        speed = 0f;
        stuckTimer = 0f;
        StopAgent();

        if (stopOnArrival)
        {
            stopOnArrival = false;
            isStopped = true;
            SetWalking(false);
            return;
        }

        waitTimer = NextWaitTime();
        Advance();
    }

    private void Advance()
    {
        int count = route.Count;
        if (count < 2)
        {
            isFinished = true;
            return;
        }

        switch (mode)
        {
            case PatrolMode.Loop:
                routeIndex = (routeIndex + 1) % count;
                break;

            case PatrolMode.PingPong:
                int next = routeIndex + pingPongStep;
                if (next >= count || next < 0) pingPongStep = -pingPongStep;
                routeIndex += pingPongStep;
                break;

            case PatrolMode.Once:
                if (routeIndex >= count - 1) isFinished = true;
                else routeIndex++;
                break;
        }

        target = route[routeIndex];
        destinationSet = false;
    }

    private void UpdateWithAgent()
    {
        if (!destinationSet)
        {
            agent.SetDestination(target.position);
            destinationSet = true;
            stuckTimer = 0f;
        }

        bool blocked = Paused || IsPlayerNearby();
        agent.isStopped = blocked;
        agent.speed = walkSpeed;
        agent.acceleration = acceleration;

        // An unreachable point gets skipped; a partial path counts as arriving at its end
        if (!agent.pathPending && (agent.pathStatus == NavMeshPathStatus.PathInvalid || agent.remainingDistance <= arriveDistance))
        {
            Arrive();
            return;
        }

        Vector3 velocity = agent.velocity;
        velocity.y = 0f;
        float currentSpeed = velocity.magnitude;
        bool moving = currentSpeed > MovingSpeedThreshold;

        SetWalking(moving);
        if (moving) UpdateSpriteFlip(velocity / currentSpeed);

        // Not blocked, but not getting anywhere either, so give up on this point
        if (!blocked && !agent.pathPending && currentSpeed < StuckSpeedThreshold)
        {
            stuckTimer += Time.deltaTime;
            if (stuckTimer >= giveUpTime) Arrive();
        }
        else
        {
            stuckTimer = 0f;
        }
    }

    private void UpdateStraightLine()
    {
        Vector3 toTarget = Flatten(target.position) - transform.position;
        if (keepHeight) toTarget.y = 0f;
        float distance = toTarget.magnitude;

        if (distance <= arriveDistance)
        {
            Arrive();
            return;
        }

        // Ease in, stop for the player, and brake so it comes to rest right on the waypoint
        float desiredSpeed = Paused || IsPlayerNearby() ? 0f : walkSpeed;
        desiredSpeed = Mathf.Min(desiredSpeed, Mathf.Sqrt(2f * acceleration * distance));
        speed = Mathf.MoveTowards(speed, desiredSpeed, acceleration * Time.deltaTime);

        Vector3 direction = toTarget / distance;
        Vector3 delta = direction * Mathf.Min(speed * Time.deltaTime, distance);

        if (controller != null && controller.enabled) controller.Move(delta);
        else transform.position += delta;

        bool moving = speed > MovingSpeedThreshold;
        SetWalking(moving);
        if (moving) UpdateSpriteFlip(direction);
    }

    private void StopAgent()
    {
        destinationSet = false;
        if (UsingAgent) agent.ResetPath();
    }

    private void Teleport(Vector3 position)
    {
        if (UsingAgent)
        {
            agent.Warp(position);
            return;
        }

        // A CharacterController overrides direct position changes unless it's briefly turned off
        bool hadController = controller != null && controller.enabled;
        if (hadController) controller.enabled = false;
        transform.position = position;
        if (hadController) controller.enabled = true;
    }

    private bool IsPlayerNearby()
    {
        if (!stopForPlayer || player == null) return false;

        Vector3 toPlayer = player.position - transform.position;
        toPlayer.y = 0f;
        return toPlayer.sqrMagnitude <= stopRange * stopRange;
    }

    private Vector3 Flatten(Vector3 point)
    {
        if (keepHeight) point.y = transform.position.y;
        return point;
    }

    private void SetWalking(bool walking)
    {
        if (walking == IsWalking) return;

        IsWalking = walking;
        if (hasWalkingBool) animator.SetBool(walkingHash, walking);
    }

    private void UpdateSpriteFlip(Vector3 direction)
    {
        if (!flipSpriteToWalkDirection || sprite == null) return;

        if (cameraTransform == null)
        {
            Camera cam = Camera.main;
            if (cam == null) return;
            cameraTransform = cam.transform;
        }

        // Walking straight toward or away from the camera keeps the current flip
        float side = Vector3.Dot(direction, cameraTransform.right);
        if (Mathf.Abs(side) < FlipDeadZone) return;

        bool movingRight = side > 0f;
        sprite.flipX = spriteFacesRight ? !movingRight : movingRight;
    }

    // Green: active route. Faded: inactive spots. Cyan: live NavMesh path. Orange: player stop range.
    private void OnDrawGizmosSelected()
    {
        DrawRouteGizmos();

        if (UnityEngine.Application.isPlaying && UsingAgent && agent.hasPath)
        {
            Gizmos.color = Color.cyan;
            Vector3[] corners = agent.path.corners;
            for (int i = 1; i < corners.Length; i++) Gizmos.DrawLine(corners[i - 1], corners[i]);
        }

        if (stopForPlayer)
        {
            Gizmos.color = new Color(1f, 0.5f, 0.2f, 0.6f);
            Gizmos.DrawWireSphere(transform.position, stopRange);
        }
    }

    private void DrawRouteGizmos()
    {
        const float pointRadius = 0.15f;
        var activeColor = new Color(0.3f, 1f, 0.5f);
        var inactiveColor = new Color(0.3f, 1f, 0.5f, 0.25f);

        Vector3 first = Vector3.zero;
        Vector3 previous = Vector3.zero;
        int activeCount = 0;

        foreach (Waypoint waypoint in waypoints)
        {
            if (waypoint == null || waypoint.point == null) continue;

            Vector3 position = UnityEngine.Application.isPlaying && waypoint.cached ? waypoint.position : waypoint.point.position;

            Gizmos.color = waypoint.active ? activeColor : inactiveColor;
            Gizmos.DrawWireSphere(position, pointRadius);
            if (!waypoint.active) continue;

            if (activeCount == 0) first = position;
            else Gizmos.DrawLine(previous, position);

            previous = position;
            activeCount++;
        }

        if (mode == PatrolMode.Loop && activeCount > 2)
        {
            Gizmos.color = activeColor;
            Gizmos.DrawLine(previous, first);
        }
    }
}