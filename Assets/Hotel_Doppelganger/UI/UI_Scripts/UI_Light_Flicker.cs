using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using Random = UnityEngine.Random;

// The lights stutter, then go out for a moment. On Dark is where to move things around, so the room has changed
// when they come back. With Stay Dim, they stay low until Restore is called, e.g. from a light switch
[AddComponentMenu("Game/Puzzle/Light Flicker")]
public class LightFlicker : MonoBehaviour
{
    // Below Salem's thoughts (90) and dialogue (100), so they stay readable while the room is dark
    private const int SortingOrder = 85;

    // Gentle flickering eases in and out and never blinks faster than about twice a second, which keeps it
    // well clear of the rapid full-contrast flashing that can cause discomfort or seizures
    private const float GentleFade = 0.15f;
    private const float GentleShortestGap = 0.35f;
    private const float GoingOutFade = 0.45f;
    private const float ComingBackFade = 0.6f;

    [Header("Lights")]
    [DisplayAs("Lights (empty = every light in the scene)")]
    [SerializeField] private List<Light> lights = new List<Light>();

    [Header("Afterwards")]
    [DisplayAs("Salem Thinks (optional)", Lines = 2)]
    [SerializeField] private string thought = "It's going to be an interesting night.";

    // Counted from once the lights have finished flickering and settled, so the line isn't lost in the flashing
    [DisplayAs("Wait Before Salem Thinks (seconds)", Min = 0f)]
    [SerializeField] private float thoughtDelay = 1f;

    [DisplayAs("Only Flicker Once")]
    [SerializeField] private bool onlyOnce = true;

    [DisplayAs("Stay Dim Until Restored")]
    [SerializeField] private bool stayDim;

    // How dark the screen stays, from 0 (normal) to 1 (black)
    [DisplayAs("Dimness", ShowIf = nameof(stayDim), Min = 0f, Max = 1f)]
    [SerializeField] private float dimness = 0.75f;

    [DisplayAs("Salem Thinks When Restored", ShowIf = nameof(stayDim), Lines = 2)]
    [SerializeField] private string restoredThought = "";

    [Foldout("Timing")]
    [DisplayAs("Gentle Flicker (easier on the eyes)")]
    [SerializeField] private bool gentle = true;

    // From 0 (no change) to 1 (black)
    [DisplayAs("How Dark Each Flicker Gets", ShowIf = nameof(gentle), Min = 0f, Max = 1f)]
    [SerializeField] private float flickerDepth = 0.55f;

    [DisplayAs("Wait Before Flickering (seconds)", Min = 0f)]
    [SerializeField] private float startDelay;

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

    [DisplayAs("On Restored")]
    public UnityEvent onRestored;

    private readonly List<Light> switchedOff = new List<Light>();
    private Canvas canvas;
    private UnityEngine.UI.Image overlay;
    private bool running;
    private bool done;

    private static int dimCount;

    // For things that behave differently in the dark, like eavesdropping
    public static bool AnyDim => dimCount > 0;

    public bool IsDim { get; private set; }

    private void Awake()
    {
        canvas = RuntimeUI.CreateCanvas("Light Flicker UI", SortingOrder);
        overlay = RuntimeUI.CreateImage("Dark", canvas.transform, Color.clear);
        RuntimeUI.Stretch(overlay.rectTransform);
        canvas.gameObject.SetActive(false);
    }

    private void OnDisable()
    {
        if (running || IsDim) SetLightsOn(true);
        SetDimFlag(false);
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
        if (running || IsDim || (onlyOnce && done)) return;
        StartCoroutine(FlickerRoutine());
    }

    // Brings the lights back after Stay Dim, e.g. from a light switch's On Used
    public void Restore()
    {
        if (!IsDim) return;

        SetDimFlag(false);
        SetLightsOn(true);
        StartCoroutine(FadeAway());

        if (!string.IsNullOrWhiteSpace(restoredThought)) DetectiveThoughts.Get().Say(restoredThought);
        onRestored?.Invoke();
    }

    private IEnumerator FlickerRoutine()
    {
        running = true;
        done = true;
        if (startDelay > 0f) yield return new WaitForSeconds(startDelay);

        canvas.gameObject.SetActive(true);

        if (gentle) yield return GentleFlickers();
        else yield return HardFlickers();

        onDark?.Invoke();
        yield return new WaitForSeconds(darkTime);

        if (stayDim)
        {
            // Lights stay off, with the overlay easing off to the dim level
            yield return Fade(1f, dimness, gentle ? ComingBackFade : 0f);
            SetDimFlag(true);
        }
        else
        {
            SetLightsOn(true);
            yield return Fade(1f, 0f, gentle ? ComingBackFade : 0f);
            canvas.gameObject.SetActive(false);
        }
        running = false;
        onFinished?.Invoke();

        if (string.IsNullOrWhiteSpace(thought)) yield break;
        if (thoughtDelay > 0f) yield return new WaitForSeconds(thoughtDelay);
        DetectiveThoughts.Get().Say(thought);
    }

    private void SetDimFlag(bool dim)
    {
        if (IsDim == dim) return;
        IsDim = dim;
        dimCount += dim ? 1 : -1;
    }

    private IEnumerator FadeAway()
    {
        yield return Fade(overlay.color.a, 0f, ComingBackFade);
        canvas.gameObject.SetActive(false);
    }

    // Soft dips that don't go fully black, then an eased fade out
    private IEnumerator GentleFlickers()
    {
        for (int i = 0; i < flickers; i++)
        {
            float depth = flickerDepth * Random.Range(0.7f, 1f);
            yield return Fade(0f, depth, GentleFade);
            yield return new WaitForSeconds(Random.Range(shortestBlink, longestBlink));
            yield return Fade(depth, 0f, GentleFade);
            yield return new WaitForSeconds(Mathf.Max(GentleShortestGap, Random.Range(shortestBlink, longestBlink) * 1.5f));
        }

        yield return Fade(0f, 1f, GoingOutFade);
        SetLightsOn(false);
    }

    // The original instant on/off blinks
    private IEnumerator HardFlickers()
    {
        for (int i = 0; i < flickers; i++)
        {
            SetDark(1f);
            yield return new WaitForSeconds(Random.Range(shortestBlink, longestBlink));
            SetDark(0f);
            yield return new WaitForSeconds(Random.Range(shortestBlink, longestBlink) * 1.5f);
        }

        SetDark(1f);
    }

    private IEnumerator Fade(float from, float to, float duration)
    {
        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            overlay.color = new Color(0f, 0f, 0f, Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t / duration)));
            yield return null;
        }
        overlay.color = new Color(0f, 0f, 0f, to);
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