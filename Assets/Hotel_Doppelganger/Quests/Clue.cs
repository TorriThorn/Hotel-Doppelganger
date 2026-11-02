using System;
using UnityEngine;
using UnityEngine.Events;

// A piece of evidence the player can pick up. The first pick-up counts as finding it: Salem can comment,
// and On Found can unlock dialogue or start other events. A note's text shows on screen while it's held.
[AddComponentMenu("Game/Puzzle/Clue")]
[RequireComponent(typeof(Grabbable))]
public class Clue : MonoBehaviour
{
    [Header("When First Picked Up")]
    [DisplayAs("Salem Thinks (optional)", Lines = 2)]
    [SerializeField] private string thought = "";

    [Header("Readable Note (optional)")]
    [DisplayAs("Note Title")]
    [SerializeField] private string noteTitle = "";

    [DisplayAs("Note Text (shown while held)", Lines = 5)]
    [SerializeField] private string noteText = "";

    [Header("Journal")]
    [DisplayAs("Add To Journal")]
    [SerializeField] private bool addToJournal = true;

    [DisplayAs("Journal Title (blank = note title)", ShowIf = nameof(addToJournal))]
    [SerializeField] private string journalTitle = "";

    [Foldout("Events")]
    public UnityEvent onFound;

    private Grabbable grabbable;

    public bool Found { get; private set; }

    // For Clue Sets, which count how many pieces have been found
    public event Action<Clue> FoundChanged;

    // Falls back to the note's title, then the object's name, so every entry has something to show in the list
    private string JournalTitle =>
        !string.IsNullOrWhiteSpace(journalTitle) ? journalTitle.Trim()
        : !string.IsNullOrWhiteSpace(noteTitle) ? noteTitle.Trim()
        : gameObject.name;

    private void Awake() => grabbable = GetComponent<Grabbable>();

    private void OnEnable()
    {
        grabbable.onGrabbed.AddListener(OnGrabbed);
        grabbable.onDropped.AddListener(OnDropped);
    }

    private void OnDisable()
    {
        grabbable.onGrabbed.RemoveListener(OnGrabbed);
        grabbable.onDropped.RemoveListener(OnDropped);
        OnDropped();
    }

    // Counts the clue as found without picking it up, e.g. when an NPC hands the information over
    public void MarkFound()
    {
        if (Found) return;

        Found = true;
        if (!string.IsNullOrWhiteSpace(thought)) DetectiveThoughts.Get().Think(thought, false);
        if (addToJournal) Journal.Get().Add(JournalTitle, noteText, thought);
        onFound?.Invoke();
        FoundChanged?.Invoke(this);
    }

    private void OnGrabbed()
    {
        if (!string.IsNullOrWhiteSpace(noteText)) DetectiveThoughts.Get().ShowNote(this, noteTitle, noteText);
        MarkFound();
    }

    private void OnDropped()
    {
        if (string.IsNullOrWhiteSpace(noteText)) return;
        DetectiveThoughts thoughts = FindAnyObjectByType<DetectiveThoughts>();
        if (thoughts != null) thoughts.HideNote(this);
    }
}