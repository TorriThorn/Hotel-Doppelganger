using System.Collections;
using UnityEngine;

// Changes an NPC's sprite from UnityEvents, with an optional squash-and-stretch pop
[AddComponentMenu("Game/NPC/NPC Sprite Swap")]
public class NPCSpriteSwap : MonoBehaviour
{
    private const float PopWidthRatio = 0.5f;

    // For reveals: the conversation camera pulls back to show the new sprite
    [Header("Swapping")]
    [DisplayAs("Zoom Out To Show The Swap")]
    [SerializeField] private bool zoomOutOnSwap = true;

    [DisplayAs("Turn Off Animator On Swap")]
    [SerializeField] private bool disableAnimatorOnSwap = true;

    [Foldout("Pop Effect")]
    [DisplayAs("Pop When Changing")]
    [SerializeField] private bool popOnSwap = true;

    [DisplayAs("Pop Time", ShowIf = nameof(popOnSwap), Min = 0f)]
    [SerializeField] private float popDuration = 0.25f;

    [DisplayAs("Pop Strength", ShowIf = nameof(popOnSwap), Min = 0f, Max = 0.5f)]
    [SerializeField] private float popAmount = 0.15f;

    [Foldout("References (filled in automatically)")]
    [DisplayAs("Sprite (optional)")]
    [SerializeField] private SpriteRenderer spriteRenderer;

    private Animator animator;
    private Sprite originalSprite;
    private Vector3 baseScale;
    private Coroutine popRoutine;

    public bool IsSwapped { get; private set; }

    public bool ZoomOutOnSwap => zoomOutOnSwap;

    private void Awake()
    {
        if (spriteRenderer == null) spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        animator = GetComponentInChildren<Animator>();

        if (spriteRenderer == null) return;

        originalSprite = spriteRenderer.sprite;
        baseScale = spriteRenderer.transform.localScale;
    }

    // An Animator would overwrite the new sprite, so it's turned off while swapped
    public void SetSprite(Sprite sprite)
    {
        if (spriteRenderer == null || sprite == null) return;

        if (disableAnimatorOnSwap && animator != null) animator.enabled = false;
        spriteRenderer.sprite = sprite;
        IsSwapped = true;
        Pop();
    }

    public void ResetSprite()
    {
        if (spriteRenderer == null) return;

        spriteRenderer.sprite = originalSprite;
        IsSwapped = false;
        if (disableAnimatorOnSwap && animator != null) animator.enabled = true;
        Pop();
    }

    private void Pop()
    {
        if (!popOnSwap || popDuration <= 0f) return;

        if (popRoutine != null) StopCoroutine(popRoutine);
        popRoutine = StartCoroutine(PopRoutine());
    }

    private IEnumerator PopRoutine()
    {
        Transform target = spriteRenderer.transform;

        for (float time = 0f; time < popDuration; time += Time.deltaTime)
        {
            float stretch = Mathf.Sin(time / popDuration * Mathf.PI) * popAmount;
            target.localScale = new Vector3(baseScale.x * (1f - stretch * PopWidthRatio), baseScale.y * (1f + stretch), baseScale.z);
            yield return null;
        }

        target.localScale = baseScale;
        popRoutine = null;
    }
}