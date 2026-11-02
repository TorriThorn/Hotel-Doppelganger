using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Text = UnityEngine.UI.Text;

// NPCs talking among themselves: each line floats above its speaker's head. Up close the player can read it;
// out of earshot it only shows "...", so they know something's being said but have to get closer to catch it.
// Good for arguments, whispered deals and lines like Helena pointing the finger at Murray.
[AddComponentMenu("Game/NPC/Overheard Conversation")]
public class OverheardConversation : MonoBehaviour
{
    public enum StartMode
    {
        [InspectorName("Only When Play Is Called")] Manually,
        [InspectorName("When Scene Starts")] OnSceneStart
    }

    [Serializable]
    public class Line
    {
        [DisplayAs("Text", Lines = 2)] public string text = "...";
        [DisplayAs("Speaker")] public NPCDialogue speaker;
        [DisplayAs("Seconds On Screen (0 = auto)", Min = 0f)] public float seconds;
    }

    private const float PixelsPerMeter = 100f;
    private const float AutoBaseTime = 1.5f;
    private const float AutoTimePerLetter = 0.06f;
    private const float FallbackHeight = 2f;
    private const string OutOfEarshot = "...";

    [Header("Lines")]
    [SerializeField] private List<Line> lines = new List<Line>();

    [Header("Playing")]
    [DisplayAs("Start")]
    [SerializeField] private StartMode startMode = StartMode.Manually;

    [DisplayAs("Wait Before Starting (seconds)", Min = 0f)]
    [SerializeField] private float startDelay;

    [DisplayAs("Hearing Distance (meters)", Min = 0f)]
    [SerializeField] private float hearingRange = 6f;

    [DisplayAs("Loop")]
    [SerializeField] private bool loop;

    [DisplayAs("Pause Between Loops (seconds)", ShowIf = nameof(loop), Min = 0f)]
    [SerializeField] private float loopPause = 8f;

    [DisplayAs("Speakers Stop Walking While Talking")]
    [SerializeField] private bool holdSpeakers = true;

    [Foldout("Events")]
    [DisplayAs("On Finished, Player Heard It")]
    public UnityEvent onHeard;

    [DisplayAs("On Finished, Player Missed It")]
    public UnityEvent onMissed;

    [Foldout("Look")]
    [DisplayAs("Text Color")]
    [SerializeField] private Color textColor = new Color(0.95f, 0.93f, 0.88f);

    [DisplayAs("Text Shadow Color")]
    [SerializeField] private Color shadowColor = new Color(0f, 0f, 0f, 0.9f);

    [DisplayAs("Text Size", Min = 1f)]
    [SerializeField] private int textSize = 36;

    [DisplayAs("Size In The World", Min = 0.1f, Max = 3f)]
    [SerializeField] private float worldSize = 0.6f;

    [DisplayAs("Height Above Head")]
    [SerializeField] private float heightAboveHead = 0.6f;

    private Transform viewer;
    private PlayerDialogue dialogue;
    private Canvas bubble;
    private Text bubbleText;
    private Shadow bubbleShadow;
    private Coroutine playing;
    private NPCDialogue currentSpeaker;
    private Renderer currentBody;
    private string currentLine;
    private bool heardAny;

    public bool IsPlaying => playing != null;

    private void Start()
    {
        var player = FindAnyObjectByType<FirstPersonController>();
        if (player != null) viewer = player.CameraTransform != null ? player.CameraTransform : player.transform;
        dialogue = FindAnyObjectByType<PlayerDialogue>();

        BuildBubble();
        if (startMode == StartMode.OnSceneStart) Play();
    }

    private void OnValidate()
    {
        if (bubble != null) ApplyLook();
    }

    private void OnDisable() => Stop();

    private void OnDestroy()
    {
        if (bubble != null) Destroy(bubble.gameObject);
    }

    public void Play()
    {
        if (playing != null || lines.Count == 0 || !isActiveAndEnabled) return;
        playing = StartCoroutine(PlayRoutine());
    }

    public void Stop()
    {
        if (playing != null) StopCoroutine(playing);
        playing = null;
        HoldSpeakers(false);
        currentSpeaker = null;
        if (bubble != null) bubble.gameObject.SetActive(false);
    }

    private IEnumerator PlayRoutine()
    {
        if (startDelay > 0f) yield return Wait(startDelay);

        do
        {
            heardAny = false;
            HoldSpeakers(true);

            foreach (Line line in lines)
            {
                if (line == null || line.speaker == null || string.IsNullOrWhiteSpace(line.text)) continue;

                currentSpeaker = line.speaker;
                currentBody = FindBody(line.speaker);
                // Floating text is plain, so highlight markers are just dropped
                currentLine = line.text.Replace("*", string.Empty).Trim();

                float seconds = line.seconds > 0f ? line.seconds : AutoBaseTime + currentLine.Length * AutoTimePerLetter;
                yield return Wait(seconds);
            }

            currentSpeaker = null;
            bubble.gameObject.SetActive(false);
            HoldSpeakers(false);

            if (heardAny) onHeard?.Invoke();
            else onMissed?.Invoke();

            if (loop) yield return Wait(loopPause);
        }
        while (loop);

        playing = null;
    }

    // Counts game time only, and stands still while the player is in a conversation of their own
    private IEnumerator Wait(float seconds)
    {
        for (float time = 0f; time < seconds;)
        {
            if (dialogue == null || !dialogue.IsTalking) time += Time.deltaTime;
            yield return null;
        }
    }

    private void LateUpdate()
    {
        bool show = currentSpeaker != null && viewer != null && (dialogue == null || !dialogue.IsTalking);
        if (bubble.gameObject.activeSelf != show) bubble.gameObject.SetActive(show);
        if (!show) return;

        Vector3 top = HeadPosition();
        bool inEarshot = (top - viewer.position).sqrMagnitude <= hearingRange * hearingRange;
        if (inEarshot) heardAny = true;

        string shown = inEarshot ? currentLine : OutOfEarshot;
        if (bubbleText.text != shown) bubbleText.text = shown;

        bubble.transform.SetPositionAndRotation(top + Vector3.up * heightAboveHead, viewer.rotation);
    }

    private Vector3 HeadPosition() => currentSpeaker.HeadPosition(currentBody, FallbackHeight);

    private static Renderer FindBody(NPCDialogue speaker)
    {
        NPCPatrol patrol = speaker.GetComponentInParent<NPCPatrol>();
        Transform root = patrol != null ? patrol.transform : speaker.transform;

        Renderer body = root.GetComponentInChildren<SpriteRenderer>();
        return body != null ? body : root.GetComponentInChildren<Renderer>();
    }

    // Walking speakers stay put for the length of the exchange, so the player can catch up with it
    private void HoldSpeakers(bool hold)
    {
        if (!holdSpeakers) return;

        foreach (Line line in lines)
        {
            if (line == null || line.speaker == null) continue;
            NPCPatrol patrol = line.speaker.GetComponentInParent<NPCPatrol>();
            if (patrol != null) patrol.Paused = hold;
        }
    }

    private void BuildBubble()
    {
        bubble = new GameObject(name + " Speech").AddComponent<Canvas>();
        bubble.renderMode = RenderMode.WorldSpace;
        RectTransform rect = bubble.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(700f, 200f);
        rect.pivot = new Vector2(0.5f, 0f);

        bubbleText = RuntimeUI.CreateText("Line", bubble.transform, null, textSize, textColor, TextAnchor.LowerCenter, shadowColor, out bubbleShadow);
        RuntimeUI.Stretch(bubbleText.rectTransform);
        bubbleText.verticalOverflow = VerticalWrapMode.Overflow;

        ApplyLook();
        bubble.gameObject.SetActive(false);
    }

    private void ApplyLook()
    {
        bubble.transform.localScale = Vector3.one * (worldSize / PixelsPerMeter);
        bubbleText.color = textColor;
        bubbleText.fontSize = textSize;
        bubbleShadow.effectColor = shadowColor;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.5f, 0.8f, 1f, 0.6f);
        foreach (Line line in lines)
        {
            if (line != null && line.speaker != null) Gizmos.DrawWireSphere(line.speaker.transform.position, hearingRange);
        }
    }
}