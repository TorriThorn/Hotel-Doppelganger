using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

// Fires On All Done once every step has happened, in any order. Tick steps off by name from any UnityEvent,
// e.g. a dialogue entry's On Chosen calling Done("Carmela")
[AddComponentMenu("Game/Puzzle/Game Condition")]
public class GameCondition : MonoBehaviour
{
    [Header("Steps")]
    [DisplayAs("Steps (names, capitals don't matter)")]
    [SerializeField] private List<string> steps = new List<string>();

    [Foldout("Events")]
    [DisplayAs("On All Done")]
    public UnityEvent onAllDone;

    private readonly HashSet<string> done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public bool IsComplete { get; private set; }

    public void Done(string step)
    {
        if (IsComplete || string.IsNullOrWhiteSpace(step)) return;

        done.Add(step.Trim());
        foreach (string needed in steps)
        {
            if (!string.IsNullOrWhiteSpace(needed) && !done.Contains(needed.Trim())) return;
        }

        IsComplete = true;
        onAllDone?.Invoke();
    }
}
