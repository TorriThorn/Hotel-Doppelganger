using UnityEngine;
using UnityEngine.Rendering;

// Gives the player a visible body: a humanoid model under the player that walks with them, bends with the
// look direction, and casts a shadow. Its head is hidden from the camera so it never blocks the view.
// Expects a model with a Humanoid rig and an Animator Controller that blends on the Speed parameter.
[AddComponentMenu("Game/Player/Player Body")]
[RequireComponent(typeof(FirstPersonController))]
public class PlayerBody : MonoBehaviour
{
    public enum HeadHiding
    {
        [InspectorName("Shrink Head Bone (simplest)")] ShrinkHeadBone,
        [InspectorName("Head Meshes Cast Shadow Only")] ShadowOnlyMeshes,
        [InspectorName("Don't Hide")] None
    }

    private const float ShrunkScale = 0.001f;
    private const int MaxSpineBones = 3;

    [Header("Model")]
    [DisplayAs("Body Model")]
    [SerializeField] private Animator body;

    [DisplayAs("Line Feet Up With Player Collider")]
    [SerializeField] private bool snapFeetToController = true;

    [DisplayAs("Extra Offset")]
    [SerializeField] private Vector3 bodyOffset = new Vector3(0f, 0f, -0.1f);

    [Header("Hiding The Head")]
    [DisplayAs("How")]
    [SerializeField] private HeadHiding headHiding = HeadHiding.ShrinkHeadBone;

    [DisplayAs("Head Meshes (Shadow Only option)")]
    [SerializeField] private Renderer[] headMeshes = new Renderer[0];

    [Header("Looking Down")]
    [DisplayAs("Upper Body Follows Look", Min = 0f, Max = 1f)]
    [SerializeField] private float spineFollow = 0.4f;

    [DisplayAs("Pull Body Back When Looking Down", Min = 0f, Max = 0.5f)]
    [SerializeField] private float lookDownPullBack = 0.15f;

    [Foldout("Animation")]
    [DisplayAs("Speed Parameter")]
    [SerializeField] private string speedParameter = "Speed";

    [DisplayAs("Sideways Parameter (optional)")]
    [SerializeField] private string moveXParameter = "MoveX";

    [DisplayAs("Forward Parameter (optional)")]
    [SerializeField] private string moveZParameter = "MoveZ";

    [DisplayAs("Grounded Parameter (optional)")]
    [SerializeField] private string groundedParameter = "Grounded";

    [DisplayAs("Smoothing (seconds)", Min = 0f, Max = 0.5f)]
    [SerializeField] private float animationDamping = 0.1f;

    [Foldout("Camera")]
    [DisplayAs("Camera Near Clip (0 = leave as is)", Min = 0f, Max = 0.3f)]
    [SerializeField] private float cameraNearClip = 0.05f;

    private FirstPersonController player;
    private CharacterController controller;
    private Transform head;
    private Vector3 headScale;
    private readonly Transform[] spine = new Transform[MaxSpineBones];
    private int spineCount;

    private int speedHash, moveXHash, moveZHash, groundedHash;
    private bool hasSpeed, hasMoveX, hasMoveZ, hasGrounded;
    private RuntimeAnimatorController cachedController;
    private Vector3 lastPosition;

    private void Awake()
    {
        player = GetComponent<FirstPersonController>();
        controller = GetComponent<CharacterController>();

        if (body == null) body = GetComponentInChildren<Animator>();
        if (body == null)
        {
            enabled = false;
            return;
        }

        SetUpModel();
        CacheBones();
        SetUpCamera();
        lastPosition = transform.position;
    }

    private void OnDisable()
    {
        if (head != null) head.localScale = headScale;
    }

    // Movement is read here so the Animator sees this frame's speed when it updates
    private void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        // Speed comes from how far the player actually moved, so walking into a wall reads as standing still
        Vector3 velocity = (transform.position - lastPosition) / dt;
        lastPosition = transform.position;
        velocity.y = 0f;

        // The Animator's parameter list can be empty until it has started, so keep checking until it's ready.
        // This also picks up a controller swapped in during play.
        if (body.runtimeAnimatorController != cachedController && body.isInitialized) CacheParameters();

        Vector3 local = transform.InverseTransformDirection(velocity);

        if (hasSpeed) body.SetFloat(speedHash, velocity.magnitude, animationDamping, dt);
        if (hasMoveX) body.SetFloat(moveXHash, local.x, animationDamping, dt);
        if (hasMoveZ) body.SetFloat(moveZHash, local.z, animationDamping, dt);
        if (hasGrounded) body.SetBool(groundedHash, controller.isGrounded);
    }

    // Runs after the Animator has posed the bones, so these changes sit on top of the animation
    private void LateUpdate()
    {
        float pitch = player.Pitch;
        float lookingDown = Mathf.Clamp01(pitch / Mathf.Max(1f, player.LookLimit));

        body.transform.localPosition = FeetPosition() + bodyOffset + Vector3.back * (lookDownPullBack * lookingDown);

        // Without a controller nothing resets the bones each frame, so the bend would keep adding up
        if (spineCount > 0 && spineFollow > 0f && body.runtimeAnimatorController != null)
        {
            Quaternion bend = Quaternion.AngleAxis(pitch * spineFollow / spineCount, transform.right);
            for (int i = 0; i < spineCount; i++) spine[i].rotation = bend * spine[i].rotation;
        }

        if (headHiding == HeadHiding.ShrinkHeadBone && head != null) head.localScale = Vector3.one * ShrunkScale;
    }

    private Vector3 FeetPosition()
    {
        if (!snapFeetToController) return Vector3.zero;
        return new Vector3(0f, controller.center.y - controller.height * 0.5f, 0f);
    }

    private void SetUpModel()
    {
        body.applyRootMotion = false;

        // Keeps the shadow animating while the body itself is out of view, e.g. when looking up
        body.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        // The CharacterController is the player's collider. Extra colliders on the model would push against it.
        foreach (Collider part in body.GetComponentsInChildren<Collider>()) part.enabled = false;

        foreach (Renderer part in body.GetComponentsInChildren<Renderer>())
        {
            part.shadowCastingMode = ShadowCastingMode.On;
            if (part is SkinnedMeshRenderer skinned) skinned.updateWhenOffscreen = true;
        }

        if (headHiding == HeadHiding.ShadowOnlyMeshes)
        {
            foreach (Renderer part in headMeshes)
            {
                if (part != null) part.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
            }
        }
    }

    private void CacheBones()
    {
        if (!body.isHuman) return;

        head = body.GetBoneTransform(HumanBodyBones.Head);
        if (head != null) headScale = head.localScale;

        AddSpineBone(HumanBodyBones.Spine);
        AddSpineBone(HumanBodyBones.Chest);
        AddSpineBone(HumanBodyBones.UpperChest);
    }

    private void AddSpineBone(HumanBodyBones bone)
    {
        Transform found = body.GetBoneTransform(bone);
        if (found != null) spine[spineCount++] = found;
    }

    // A blank or missing parameter is skipped, so any Animator Controller works
    private void CacheParameters()
    {
        cachedController = body.runtimeAnimatorController;
        hasSpeed = hasMoveX = hasMoveZ = hasGrounded = false;

        foreach (AnimatorControllerParameter parameter in body.parameters)
        {
            if (Matches(parameter, speedParameter, AnimatorControllerParameterType.Float)) { speedHash = parameter.nameHash; hasSpeed = true; }
            else if (Matches(parameter, moveXParameter, AnimatorControllerParameterType.Float)) { moveXHash = parameter.nameHash; hasMoveX = true; }
            else if (Matches(parameter, moveZParameter, AnimatorControllerParameterType.Float)) { moveZHash = parameter.nameHash; hasMoveZ = true; }
            else if (Matches(parameter, groundedParameter, AnimatorControllerParameterType.Bool)) { groundedHash = parameter.nameHash; hasGrounded = true; }
        }
    }

    private static bool Matches(AnimatorControllerParameter parameter, string name, AnimatorControllerParameterType type) =>
        parameter.type == type && !string.IsNullOrWhiteSpace(name) && parameter.nameHash == Animator.StringToHash(name.Trim());

    // A close near clip stops the camera seeing through the shoulders and arms
    private void SetUpCamera()
    {
        Transform cam = player.CameraTransform;
        if (cameraNearClip > 0f && cam != null && cam.TryGetComponent(out Camera view)) view.nearClipPlane = cameraNearClip;
    }
}