using UnityEngine;
using UnityEngine.Events;

// Quietly takes an item out of Salem's inventory, e.g. Carmela lifting the fish scale. He notices the next time
// the journal opens; if it stays shut too long, On Not Noticed fires instead
[AddComponentMenu("Game/Puzzle/Item Theft")]
public class ItemTheft : MonoBehaviour
{
    [Header("Theft")]
    [DisplayAs("Item To Steal")]
    [SerializeField] private Grabbable item;

    [DisplayAs("Salem Notices", Lines = 2)]
    [SerializeField] private string noticeThought = "What the...? Where did it go?";

    // Counts play time only
    [DisplayAs("If Not Noticed Within (seconds, 0 = never)", Min = 0f)]
    [SerializeField] private float noticeTimeout = 180f;

    [Foldout("Events")]
    public UnityEvent onStolen;

    public UnityEvent onNoticed;

    [DisplayAs("On Not Noticed In Time")]
    public UnityEvent onNotNoticed;

    [Foldout("References (filled in automatically)")]
    [DisplayAs("Object Grabber (optional)")]
    [SerializeField] private ObjectGrabber grabber;

    private PlayerDialogue dialogue;
    private float missingFor;
    private bool noticed;
    private bool timedOut;

    public bool IsStolen { get; private set; }

    private void Start()
    {
        if (grabber == null) grabber = FindAnyObjectByType<ObjectGrabber>();
        dialogue = FindAnyObjectByType<PlayerDialogue>();
    }

    private void Update()
    {
        if (!IsStolen || noticed) return;

        if (Journal.Get().IsOpen)
        {
            noticed = true;
            if (!string.IsNullOrWhiteSpace(noticeThought)) DetectiveThoughts.Get().Say(noticeThought);
            onNoticed?.Invoke();
            return;
        }

        if (noticeTimeout <= 0f || timedOut) return;
        if (IntroCutscene.IsPlaying || (dialogue != null && dialogue.IsTalking)) return;

        missingFor += Time.deltaTime;
        if (missingFor < noticeTimeout) return;

        timedOut = true;
        onNotNoticed?.Invoke();
    }

    // For UnityEvents, e.g. NPC Follow Player's On Finished
    public void Steal()
    {
        if (IsStolen || item == null) return;

        // Put down first if it's in hand
        bool inHand = grabber != null && grabber.HeldObject != null && grabber.HeldObject.gameObject == item.gameObject;
        if (inHand) grabber.LetGo();

        Journal.Get().RemoveItem(item);
        item.gameObject.SetActive(false);

        IsStolen = true;
        noticed = false;
        timedOut = false;
        missingFor = 0f;
        onStolen?.Invoke();
    }

    // Puts the item back in the world, e.g. where the thief dropped it
    public void DropAt(Transform point)
    {
        if (item == null || point == null) return;

        item.transform.SetPositionAndRotation(point.position, point.rotation);
        item.gameObject.SetActive(true);
        IsStolen = false;
    }
}
