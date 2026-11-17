using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

// Fires On All Done once every step has happened, in any order. Tick steps off by name from any UnityEvent,
// e.g. a dialogue entry's On Chosen calling Done("Carmela"). Or untick Fire As Soon As Done and call Check
// at the moment that matters, to branch on whether it happened
[AddComponentMenu("Game/Puzzle/Game Condition")]
public class GameCondition : MonoBehaviour
{
    [Header("Steps")]
    [DisplayAs("Steps (names, capitals don't matter)")]
    [SerializeField] private List<string> steps = new List<string>();

    [DisplayAs("Fire As Soon As Done")]
    [SerializeField] private bool fireWhenDone = true;

    [Foldout("Events")]
    [DisplayAs("On All Done")]
    public UnityEvent onAllDone;

    // Only from Check
    [DisplayAs("On Not Done Yet")]
    public UnityEvent onNotDone;

    private readonly HashSet<string> done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public bool IsComplete { get; private set; }

    public void Done(string step)
    {
        if (IsComplete || string.IsNullOrWhiteSpace(step)) return;

        done.Add(step.Trim());
        if (fireWhenDone && AllStepsDone())
        {
            IsComplete = true;
            onAllDone?.Invoke();
        }
    }

    // Fires On All Done or On Not Done Yet, depending on where things stand right now
    public void Check()
    {
        if (AllStepsDone())
        {
            IsComplete = true;
            onAllDone?.Invoke();
        }
        else onNotDone?.Invoke();
    }

    private bool AllStepsDone()
    {
        foreach (string needed in steps)
        {
            if (!string.IsNullOrWhiteSpace(needed) && !done.Contains(needed.Trim())) return false;
        }
        return true;
    }
}
