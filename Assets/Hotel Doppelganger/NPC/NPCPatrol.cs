using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Application = UnityEngine.Application;

// Walks an NPC between waypoints, pausing at each one.
// Uses a NavMeshAgent to path around obstacles when one is present; otherwise walks in straight lines.
// The route can be changed at runtime from UnityEvents (AddWaypoint, RemoveWaypoint, StopPatrol, ...).
[AddComponentMenu("Game/NPC/NPC Patrol")]
public class NPCPatrol : MonoBehaviour
{
    // PingPong: A-B-A-B. Loop: A-B-C-A. Once: A-B-C, then stop.
    public enum PatrolMode
    {
        [InspectorName("Back And Forth  (A ? B ? A ? B)")] PingPong,
        [InspectorName("Loop  (A ? B ? C ? A)")] Loop,
        [InspectorName("Once  (A ? B ? C, then stop)")] Once
    }

    [Serializable]
    public class Waypoint
    {
        [DisplayAs("Spot")]
        public Transform point;

        [DisplayAs("Part Of Route")]
        public bool active = true;

        // Recorded once so waypoints parented to the NPC don't move with it
        [NonSerialized] public Vector3 position;
        [NonSerialized] public bool cached;
    }

    private const float MovingSpeedThreshold = 0.05f;
    private const float StuckSpeedThreshold = 0.1f;
    private const float FlipDeadZone = 0.1f;

    [Header("Route")]
    [SerializeField] private List<Waypoint> waypoints = new List<Waypoint>();

    [DisplayAs("Walk Pattern")]
    [SerializeField] private PatrolMode mode = PatrolMode.PingPong;

    [DisplayAs("Start On First Spot")]
    [SerializeField] private bool startAtFirstPoint = true;

    [DisplayAs("Wait At Each Spot (seconds)", HideIf = nameof(randomWait), Min = 0f)]
    [SerializeField] private float defaultWaitTime = 2f;

    // When on, the wait at each spot is picked at random from this range
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

    public bool IsWalking { get; private set; }
    public bool IsStopped => isStopped;

    // Tunable values, e.g. for the Playtest menu
    public float WalkSpeed { get => walkSpeed; set => walkSpeed = Mathf.Max(0f, value); }
    public bool UseRandomWait { get => randomWait; set => randomWait = value; }
    public float WaitTime { get => defaultWaitTime; set => defaultWaitTime = Mathf.Max(0f, value); }
    public float ShortestWait { get => minRandomWait; set => minRandomWait = Mathf.Max(0f, value); }
    public float LongestWait { get => maxRandomWait; set => maxRandomWait = Mathf.Max(0f, value); }

    // Temporarily holds the NPC in place, e.g. during a conversation.
    public bool Paused { get; set; }

    private readonly List<Waypoint> route = new List<Waypoint>();
    private CharacterController controller;
    private NavMeshAgent agent;

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

    private bool UsingAgent => agent != null && agent.enabled && agent.isOnNavMesh;

    // ---------- Lifecycle ----------

    // Every active patrolling NPC, so other systems (like the Playtest menu) can adjust them all
    public static readonly List<NPCPatrol> All = new List<NPCPatrol>();

    private void OnEnable()
    {
        if (!All.Contains(this)) All.Add(this);
    }

    private void OnDisable() => All.Remove(this);

    private void Start()
    {
        controller = GetComponent<CharacterController>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (sprite == null) sprite = GetComponentInChildren<SpriteRenderer>();

        if (player == null)
        {
            FirstPersonController firstPerson = FindAnyObjectByType<FirstPersonController>();
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
        waitTimer = GetWaitTime();
        Advance();
    }

    private void ConfigureAgent()
    {
        if (!useNavMesh) return;

        agent = GetComponent<NavMeshAgent>();
        if (agent == null) return;

        agent.speed = walkSpeed;
        agent.acceleration = acceleration;
        agent.stoppingDistance = 0f;
        agent.autoBraking = true;
        agent.updateRotation = false;   // NPCFacePlayer owns the facing

        if (addColliderIfMissing && GetComponentInChildren<Collider>() == null)
        {
            CapsuleCollider capsule = gameObject.AddComponent<CapsuleCollider>();
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
            if (parameter.nameHash == walkingHash && parameter.type == AnimatorControllerParameterType.Bool)
                hasWalkingBool = true;
        }
    }

    private void Update()
    {
        bool hasDestination = (route.Count > 0 || stopOnArrival) && !isFinished && target != null;
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

    // ---------- UnityEvent actions ----------

    // Turns a waypoint on. A point not in the list is appended to the route.
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

    // Turns a waypoint off. It keeps its place in the list for AddWaypoint later.
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

    // Stops walking where the NPC stands, until ResumePatrol is called.
    public void StopPatrol()
    {
        isStopped = true;
        stopOnArrival = false;
        waitTimer = 0f;
        speed = 0f;
        StopAgent();
        SetWalking(false);
    }

    // Walks to this point and stops there, until ResumePatrol is called.
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

    // Returns to the normal route after StopPatrol or StopAtWaypoint.
    public void ResumePatrol()
    {
        isStopped = false;
        stopOnArrival = false;
        target = null;
        routeIndex = 0;
        BuildRoute();
    }

    // ---------- Route ----------

    private Waypoint FindWaypoint(Transform point) => waypoints.Find(w => w.point == point);

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
            if (waypoint != null && waypoint.active && waypoint.point != null && waypoint.cached)
                route.Add(waypoint);
        }

        // Stopped or heading to a final spot: keep the new route for ResumePatrol, but don't redirect
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
            // The point being walked to was removed: head for whatever now sits in its slot
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

    private float GetWaitTime()
    {
        if (!randomWait) return defaultWaitTime;

        float shortest = Mathf.Min(minRandomWait, maxRandomWait);
        float longest = Mathf.Max(minRandomWait, maxRandomWait);
        return UnityEngine.Random.Range(shortest, longest);
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

        waitTimer = GetWaitTime();
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
                if (routeIndex + pingPongStep >= count || routeIndex + pingPongStep < 0) pingPongStep = -pingPongStep;
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

    // ---------- Movement ----------

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

        if (!agent.pathPending)
        {
            // An unreachable point is skipped; a partial path counts as arrived at its end
            if (agent.pathStatus == NavMeshPathStatus.PathInvalid || agent.remainingDistance <= arriveDistance)
            {
                Arrive();
                return;
            }
        }

        Vector3 velocity = agent.velocity;
        velocity.y = 0f;
        float currentSpeed = velocity.magnitude;
        bool moving = currentSpeed > MovingSpeedThreshold;

        SetWalking(moving);
        if (moving) UpdateSpriteFlip(velocity / currentSpeed);

        // Not held up, but not getting anywhere either: give up on this point
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

        // Accelerate, yield to the player, and ease to a stop at the waypoint
        bool blocked = Paused || IsPlayerNearby();
        float desiredSpeed = blocked ? 0f : walkSpeed;
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

        // A CharacterController overrides direct position changes unless briefly disabled
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

    // ---------- Visuals ----------

    private void SetWalking(bool walking)
    {
        IsWalking = walking;
        if (hasWalkingBool) animator.SetBool(walkingHash, walking);
    }

    private void UpdateSpriteFlip(Vector3 direction)
    {
        if (!flipSpriteToWalkDirection || sprite == null) return;

        Camera cam = Camera.main;
        if (cam == null) return;

        // Walking straight toward or away from the camera keeps the current flip
        float side = Vector3.Dot(direction, cam.transform.right);
        if (Mathf.Abs(side) < FlipDeadZone) return;

        bool movingRight = side > 0f;
        sprite.flipX = spriteFacesRight ? !movingRight : movingRight;
    }

    // ---------- Editor ----------

    // Green = active route, faded = inactive points, cyan = live NavMesh path, orange = player stop range
    private void OnDrawGizmosSelected()
    {
        DrawRouteGizmos();

        if (Application.isPlaying && UsingAgent && agent.hasPath)
        {
            Gizmos.color = Color.cyan;
            Vector3[] corners = agent.path.corners;
            for (int i = 1; i < corners.Length; i++)
                Gizmos.DrawLine(corners[i - 1], corners[i]);
        }

        if (stopForPlayer)
        {
            Gizmos.color = new Color(1f, 0.5f, 0.2f, 0.6f);
            Gizmos.DrawWireSphere(transform.position, stopRange);
        }
    }

    private void DrawRouteGizmos()
    {
        var activeColor = new Color(0.3f, 1f, 0.5f);
        var inactiveColor = new Color(0.3f, 1f, 0.5f, 0.25f);
        const float pointRadius = 0.15f;

        Vector3 first = Vector3.zero;
        Vector3 previous = Vector3.zero;
        int activeCount = 0;

        foreach (Waypoint waypoint in waypoints)
        {
            if (waypoint == null || waypoint.point == null) continue;

            Vector3 position = Application.isPlaying && waypoint.cached ? waypoint.position : waypoint.point.position;

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