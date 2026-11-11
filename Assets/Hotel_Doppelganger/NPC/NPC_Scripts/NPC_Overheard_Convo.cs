using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Text = UnityEngine.UI.Text;

// NPCs talking among themselves: each line floats above its speaker's head. Up close the player can read it;
// out of earshot it only shows "...", so they know something's being said but have to get closer to catch it.
// Good for arguments, whispered deals and lines like Helena pointing the finger at Murray
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
        [DisplayAs("Text", Lines = 2)]
        public string text = "...";

        [DisplayAs("Speaker")]
        public NPCDialogue speaker;

        [DisplayAs("Seconds On Screen (0 = auto)", Min = 0f)]
        public float seconds;

        // Played from the speaker, so it can be heard from across the room
        [DisplayAs("Sound (optional)")]
        public AudioClip sound;
    }

    private const float PixelsPerMeter = 100f;
    private const float AutoBaseTime = 1.5f;
    private const float AutoTimePerLetter = 0.06f;
    private const float FallbackHeight = 2f;
    private const string OutOfEarshot = "...";
    private const float HopSpeed = 9f;

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

    // The one talking bounces on the spot, like two people butting heads. Only works when the sprite sits
    // on a child of the NPC, not on the NPC object itself
    [DisplayAs("Speaker Hops While Talking")]
    [SerializeField] private bool hop;

    [DisplayAs("Hop Height (meters)", ShowIf = nameof(hop), Min = 0f)]
    [SerializeField] private float hopHeight = 0.12f;

    [DisplayAs("Sound Volume", Min = 0f, Max = 1f)]
    [SerializeField] private float soundVolume = 1f;

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

    private static readonly List<OverheardConversation> Active = new List<OverheardConversation>();

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
    private Transform hopping;
    private Vector3 hopRest;

    public bool IsPlaying => playing != null;

    private void OnValidate()
    {
        if (bubble != null) ApplyLook();
    }

    private void OnEnable() => Active.Add(this);

    private void OnDisable()
    {
        Active.Remove(this);
        Stop();
    }

    private void Start()
    {
        var player = FindAnyObjectByType<FirstPersonController>();
        if (player != null) viewer = player.CameraTransform != null ? player.CameraTransform : player.transform;
        dialogue = FindAnyObjectByType<PlayerDialogue>();

        BuildBubble();
        if (startMode == StartMode.OnSceneStart) Play();
    }

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
        SetHopping(null);
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
                SetHopping(hop ? HopTarget(line.speaker, currentBody) : null);
                if (line.sound != null) AudioSource.PlayClipAtPoint(line.sound, line.speaker.transform.position, soundVolume);
                // Floating text is plain, so highlight markers are just dropped
                currentLine = line.text.Replace("*", string.Empty).Trim();

                float seconds = line.seconds > 0f ? line.seconds : AutoBaseTime + currentLine.Length * AutoTimePerLetter;
                yield return Wait(seconds);
            }

            currentSpeaker = null;
            SetHopping(null);
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
        if (hopping != null) hopping.localPosition = hopRest + Vector3.up * (Mathf.Abs(Mathf.Sin(Time.time * HopSpeed)) * hopHeight);

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

    // True while this NPC's speech bubble is up, so other things drawn over their head (like the new dialogue
    // marker) can make way for it
    public static bool IsSpeaking(NPCDialogue npc)
    {
        if (npc == null) return false;

        foreach (OverheardConversation conversation in Active)
        {
            if (conversation.currentSpeaker == npc && conversation.bubble != null && conversation.bubble.gameObject.activeSelf) return true;
        }
        return false;
    }

    // Moving the NPC object itself would fight its walking, so only a sprite on a child can hop
    private static Transform HopTarget(NPCDialogue speaker, Renderer body)
    {
        if (body == null) return null;

        NPCPatrol patrol = speaker.GetComponentInParent<NPCPatrol>();
        Transform root = patrol != null ? patrol.transform : speaker.transform;
        return body.transform != root ? body.transform : null;
    }

    private void SetHopping(Transform body)
    {
        if (hopping == body) return;

        if (hopping != null) hopping.localPosition = hopRest;
        hopping = body;
        if (hopping != null) hopRest = hopping.localPosition;
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

#if UNITY_EDITOR
    // Fills in Carmela and Yaroslava's fireplace argument from Level 1, finding both by NPC Name
    [ContextMenu("Add Level 1 Fireplace Argument")]
    private void AddFireplaceArgument()
    {
        NPCDialogue carmela = FindNpc("Carmela");
        NPCDialogue yaroslava = FindNpc("Yaroslava");

        UnityEditor.Undo.RecordObject(this, "Add Level 1 Fireplace Argument");
        lines.Add(new Line { speaker = carmela, text = "Leave me alone, you hag!" });
        lines.Add(new Line { speaker = yaroslava, text = "You're the hag, with your fake credentials and fake tonics?" });
        lines.Add(new Line { speaker = carmela, text = "Fake?! Look who's talking, miss \"poison master\"! There's a reason the society rebuked your application." });
        lines.Add(new Line { speaker = yaroslava, text = "Those ancient kooks can tell the difference between useful and pointless! They only care about \"not damaging the reputation\" and other bullshit." });
        lines.Add(new Line { speaker = carmela, text = "Do you hear yourself? You will never get a certification with that attitude!" });
        lines.Add(new Line { speaker = yaroslava, text = "And you will never know true hard work and appreciation for the craft!" });
        lines.Add(new Line { speaker = carmela, text = "Keep your thoughts to yourself!" });
        lines.Add(new Line { speaker = yaroslava, text = "And what's that you're keeping behind your back, missy?" });

        loop = true;
        loopPause = 2f;
        hop = true;
        UnityEditor.EditorUtility.SetDirty(this);
    }

    private static NPCDialogue FindNpc(string npcName)
    {
        foreach (NPCDialogue npc in FindObjectsByType<NPCDialogue>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (string.Equals(npc.npcName?.Trim(), npcName, StringComparison.OrdinalIgnoreCase)) return npc;
        }
        return null;
    }
#endif

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.5f, 0.8f, 1f, 0.6f);
        foreach (Line line in lines)
        {
            if (line != null && line.speaker != null) Gizmos.DrawWireSphere(line.speaker.transform.position, hearingRange);
        }
    }
}