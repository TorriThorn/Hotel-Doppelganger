using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using Random = UnityEngine.Random;

// The lights stutter, then go out for a moment. On Dark is where to move things around, so the room has changed
// when they come back. A black overlay covers the ambient light that switching lights off leaves behind
[AddComponentMenu("Game/Puzzle/Light Flicker")]
public class LightFlicker : MonoBehaviour
{
    private const int SortingOrder = 95;

    [Header("Lights")]
    [DisplayAs("Lights (empty = every light in the scene)")]
    [SerializeField] private List<Light> lights = new List<Light>();

    [Header("Afterwards")]
    [DisplayAs("Salem Thinks (optional)", Lines = 2)]
    [SerializeField] private string thought = "It's going to be an interesting night.";

    [DisplayAs("Only Flicker Once")]
    [SerializeField] private bool onlyOnce = true;

    [Foldout("Timing")]
    [DisplayAs("Flickers Before Going Out", Min = 0f)]
    [SerializeField] private int flickers = 3;

    [DisplayAs("Shortest Blink (seconds)", Min = 0.01f)]
    [SerializeField] private float shortestBlink = 0.05f;

    [DisplayAs("Longest Blink (seconds)", Min = 0.01f)]
    [SerializeField] private float longestBlink = 0.2f;

    [DisplayAs("Time Fully Dark (seconds)", Min = 0f)]
    [SerializeField] private float darkTime = 1.2f;

    [Foldout("Events")]
    public UnityEvent onDark;

    public UnityEvent onFinished;

    private readonly List<Light> switchedOff = new List<Light>();
    private Canvas canvas;
    private UnityEngine.UI.Image overlay;
    private bool running;
    private bool done;

    private void Awake()
    {
        canvas = RuntimeUI.CreateCanvas("Light Flicker UI", SortingOrder);
        overlay = RuntimeUI.CreateImage("Dark", canvas.transform, Color.clear);
        RuntimeUI.Stretch(overlay.rectTransform);
        canvas.gameObject.SetActive(false);
    }

    private void OnDisable()
    {
        if (running) SetLightsOn(true);
        running = false;
        if (canvas != null) canvas.gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        if (canvas != null) Destroy(canvas.gameObject);
    }

    // For UnityEvents, e.g. a Game Trigger set to After A Delay
    public void Flicker()
    {
        if (running || (onlyOnce && done)) return;
        StartCoroutine(FlickerRoutine());
    }

    private IEnumerator FlickerRoutine()
    {
        running = true;
        done = true;
        canvas.gameObject.SetActive(true);

        for (int i = 0; i < flickers; i++)
        {
            SetDark(1f);
            yield return new WaitForSeconds(Random.Range(shortestBlink, longestBlink));
            SetDark(0f);
            yield return new WaitForSeconds(Random.Range(shortestBlink, longestBlink) * 1.5f);
        }

        SetDark(1f);
        onDark?.Invoke();
        yield return new WaitForSeconds(darkTime);

        SetDark(0f);
        canvas.gameObject.SetActive(false);
        running = false;

        if (!string.IsNullOrWhiteSpace(thought)) DetectiveThoughts.Get().Say(thought);
        onFinished?.Invoke();
    }

    private void SetDark(float amount)
    {
        overlay.color = new Color(0f, 0f, 0f, amount);
        SetLightsOn(amount <= 0f);
    }

    // Only lights that were on get switched back on, so lights meant to be off stay off
    private void SetLightsOn(bool on)
    {
        if (on)
        {
            foreach (Light light in switchedOff)
            {
                if (light != null) light.enabled = true;
            }
            switchedOff.Clear();
            return;
        }

        IEnumerable<Light> targets = lights.Count > 0 ? lights : FindObjectsByType<Light>();
        foreach (Light light in targets)
        {
            if (light == null || !light.enabled || switchedOff.Contains(light)) continue;

            light.enabled = false;
            switchedOff.Add(light);
        }
    }
}