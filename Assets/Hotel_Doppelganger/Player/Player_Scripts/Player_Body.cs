using UnityEngine;
using UnityEngine.Rendering;

// A visible player body that walks, leans with the look and casts a shadow, with the head hidden from the
// camera. Needs a Humanoid rig and an Animator Controller that blends on Speed
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

    [Foldout("Placement")]
    [DisplayAs("Line Feet Up With Player Collider")]
    [SerializeField] private bool snapFeetToController = true;

    [DisplayAs("Extra Offset")]
    [SerializeField] private Vector3 bodyOffset = new Vector3(0f, 0f, -0.1f);

    [Foldout("Hiding The Head")]
    [DisplayAs("How")]
    [SerializeField] private HeadHiding headHiding = HeadHiding.ShrinkHeadBone;

    [DisplayAs("Head Meshes (Shadow Only option)")]
    [SerializeField] private Renderer[] headMeshes = new Renderer[0];

    [Foldout("Looking Down")]
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

    private readonly Transform[] spine = new Transform[MaxSpineBones];

    private FirstPersonController player;
    private CharacterController controller;
    private Transform head;
    private Vector3 headScale;
    private int spineCount;

    private int speedHash;
    private int moveXHash;
    private int moveZHash;
    private int groundedHash;
    private bool hasSpeed;
    private bool hasMoveX;
    private bool hasMoveZ;
    private bool hasGrounded;
    private RuntimeAnimatorController cachedController;
    private Vector3 lastPosition;
    private Renderer[] bodyParts = new Renderer[0];
    private ShadowCastingMode[] normalShadowModes = new ShadowCastingMode[0];
    private bool hiddenFromView;

    // Hidden from the conversation camera, still casting its shadow
    public bool HiddenFromView
    {
        get => hiddenFromView;
        set
        {
            if (value == hiddenFromView) return;

            hiddenFromView = value;
            for (int i = 0; i < bodyParts.Length; i++)
            {
                if (bodyParts[i] != null) bodyParts[i].shadowCastingMode = value ? ShadowCastingMode.ShadowsOnly : normalShadowModes[i];
            }
        }
    }

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

    private void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        // From how far the player actually moved, so walking into a wall reads as standing still
        Vector3 velocity = (transform.position - lastPosition) / dt;
        lastPosition = transform.position;
        velocity.y = 0f;

        // The parameter list can be empty until the Animator starts, and the controller can be swapped
        if (body.runtimeAnimatorController != cachedController && body.isInitialized) CacheParameters();

        Vector3 local = transform.InverseTransformDirection(velocity);

        if (hasSpeed) body.SetFloat(speedHash, velocity.magnitude, animationDamping, dt);
        if (hasMoveX) body.SetFloat(moveXHash, local.x, animationDamping, dt);
        if (hasMoveZ) body.SetFloat(moveZHash, local.z, animationDamping, dt);
        if (hasGrounded) body.SetBool(groundedHash, controller.isGrounded);
    }

    // After the Animator, so these sit on top of the animation
    private void LateUpdate()
    {
        float pitch = player.Pitch;
        float lookingDown = Mathf.Clamp01(pitch / Mathf.Max(1f, player.LookLimit));

        body.transform.localPosition = FeetPosition() + bodyOffset + Vector3.back * (lookDownPullBack * lookingDown);

        // Without a controller nothing resets the bones, so the bend would keep adding up
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

        // Keeps the shadow animating while the body is out of view
        body.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        // Extra colliders would push against the CharacterController
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

        bodyParts = body.GetComponentsInChildren<Renderer>();
        normalShadowModes = new ShadowCastingMode[bodyParts.Length];
        for (int i = 0; i < bodyParts.Length; i++) normalShadowModes[i] = bodyParts[i].shadowCastingMode;
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

    // A blank or missing parameter is skipped
    private void CacheParameters()
    {
        cachedController = body.runtimeAnimatorController;
        hasSpeed = false;
        hasMoveX = false;
        hasMoveZ = false;
        hasGrounded = false;

        foreach (AnimatorControllerParameter parameter in body.parameters)
        {
            if (Matches(parameter, speedParameter, AnimatorControllerParameterType.Float))
            {
                speedHash = parameter.nameHash;
                hasSpeed = true;
            }
            else if (Matches(parameter, moveXParameter, AnimatorControllerParameterType.Float))
            {
                moveXHash = parameter.nameHash;
                hasMoveX = true;
            }
            else if (Matches(parameter, moveZParameter, AnimatorControllerParameterType.Float))
            {
                moveZHash = parameter.nameHash;
                hasMoveZ = true;
            }
            else if (Matches(parameter, groundedParameter, AnimatorControllerParameterType.Bool))
            {
                groundedHash = parameter.nameHash;
                hasGrounded = true;
            }
        }
    }

    private static bool Matches(AnimatorControllerParameter parameter, string name, AnimatorControllerParameterType type) =>
        parameter.type == type && !string.IsNullOrWhiteSpace(name) && parameter.nameHash == Animator.StringToHash(name.Trim());

    // Stops the camera seeing through the shoulders
    private void SetUpCamera()
    {
        Transform cam = player.CameraTransform;
        if (cameraNearClip > 0f && cam != null && cam.TryGetComponent(out Camera view)) view.nearClipPlane = cameraNearClip;
    }
}