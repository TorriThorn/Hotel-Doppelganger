using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

// Groups clues that only mean something together, like the pieces of Geno's notes.
// Once enough of them are found, Salem can put it together and On Complete fires
[AddComponentMenu("Game/Puzzle/Clue Set")]
public class ClueSet : MonoBehaviour
{
    [Header("Clues")]
    [SerializeField] private List<Clue> clues = new List<Clue>();

    [DisplayAs("How Many Are Needed (0 = all)", Min = 0f)]
    [SerializeField] private int needed;

    [Header("When Enough Are Found")]
    [DisplayAs("Salem Thinks (optional)", Lines = 2)]
    [SerializeField] private string thought = "";

    [DisplayAs("Journal Title (blank = this object's name)")]
    [SerializeField] private string journalTitle = "";

    [Foldout("Events")]
    public UnityEvent onComplete;

    public bool IsComplete { get; private set; }
    public int FoundCount { get; private set; }

    private int Target => needed > 0 ? Mathf.Min(needed, clues.Count) : clues.Count;

    private void OnEnable()
    {
        foreach (Clue clue in clues)
        {
            if (clue != null) clue.FoundChanged += OnClueFound;
        }
        Recount();
    }

    private void OnDisable()
    {
        foreach (Clue clue in clues)
        {
            if (clue != null) clue.FoundChanged -= OnClueFound;
        }
    }

    private void OnClueFound(Clue clue) => Recount();

    private void Recount()
    {
        int found = 0;
        foreach (Clue clue in clues)
        {
            if (clue != null && clue.Found) found++;
        }
        FoundCount = found;

        if (IsComplete || clues.Count == 0 || found < Target) return;

        IsComplete = true;
        if (!string.IsNullOrWhiteSpace(thought))
        {
            DetectiveThoughts.Get().Think(thought, false);
            Journal.Get().Add(string.IsNullOrWhiteSpace(journalTitle) ? gameObject.name : journalTitle.Trim(), "", thought);
        }
        onComplete?.Invoke();
    }
}