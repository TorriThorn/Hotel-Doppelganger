using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

// Evidence the player can pick up. The first pick-up counts as finding it: it joins the inventory, Salem can
// comment, and On Found fires. A note's text shows on screen while it's held
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

    [Foldout("Inventory")]
    [DisplayAs("Add To Inventory")]
    [SerializeField] private bool addToJournal = true;

    // Only used if the Grabbable's Item Name is blank
    [DisplayAs("Inventory Name (blank = note title)", ShowIf = nameof(addToJournal))]
    [SerializeField] private string journalTitle = "";

    [Foldout("Events")]
    public UnityEvent onFound;

    private readonly List<string> conclusions = new List<string>();
    private Grabbable grabbable;

    public bool Found { get; private set; }
    public bool AddsToInventory => addToJournal;
    public string NoteTitle => noteTitle;
    public string NoteText => noteText;
    public string Thought => thought;

    // Added by a completed Clue Set
    public IReadOnlyList<string> Conclusions => conclusions;

    public event Action<Clue> FoundChanged;

    // Falls back to the note's title, then the object's name
    public string InventoryName =>
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

    // Counts it as found without a pick-up, e.g. when an NPC hands the information over
    public void MarkFound()
    {
        if (Found) return;

        Found = true;
        if (!string.IsNullOrWhiteSpace(thought)) DetectiveThoughts.Get().Say(thought);
        if (addToJournal) Journal.Get().AddItem(grabbable);
        onFound?.Invoke();
        FoundChanged?.Invoke(this);
    }

    public void AddConclusion(string line)
    {
        if (!string.IsNullOrWhiteSpace(line)) conclusions.Add(line.Trim());
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