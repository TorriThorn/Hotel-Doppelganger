using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;

// Everything one NPC can say. Nodes are screens linked by Go To Node. Event Dialogue entries stay hidden
// until something calls UnlockChoice with their Event Id
[AddComponentMenu("Game/NPC/NPC Dialogue")]
public class NPCDialogue : MonoBehaviour
{
    [Serializable]
    public class Choice
    {
        [DisplayAs("Player Says")]
        public string text = "...";

        [DisplayAs("Go To Node")]
        public string nextNode = "";

        [MoreOptions]
        [DisplayAs("Ends Conversation")]
        public bool endConversation;

        [MoreOptions]
        [DisplayAs("Grey Out Once Picked")]
        public bool greyAfterChosen = true;

        // Counts play time only
        [MoreOptions]
        [DisplayAs("Appears After (seconds, 0 = always)", Min = 0f)]
        public float appearAfter;

        // Old setups kept event ids here; they're moved to Event Dialogue on load
        [HideInInspector]
        public string unlockId = "";

        [MoreOptions]
        [DisplayAs("Salem Thinks Afterwards (optional)", Lines = 2)]
        public string salemThinks = "";

        [MoreOptions]
        public UnityEvent onChosen;
    }

    [Serializable]
    public class Node
    {
        [DisplayAs("Node Id")]
        public string id = "start";

        [DisplayAs("NPC Says", Lines = 3)]
        public string npcText = "...";

        public List<Choice> choices = new List<Choice>();

        // Two lines in a row: the screen only shows Continue, which goes to this node
        [MoreOptions]
        [DisplayAs("Then Go To (for back-to-back lines)")]
        public string thenNode = "";

        [MoreOptions]
        [DisplayAs("Said By (blank = this NPC)")]
        public string speaker = "";

        [MoreOptions]
        [DisplayAs("Expression Sprite (optional)")]
        public Sprite expression;

        [MoreOptions]
        [DisplayAs("Portrait Expression (optional)")]
        public Sprite portraitExpression;

        // A quick-time screen
        [MoreOptions]
        [DisplayAs("Time Limit (seconds, 0 = none)", Min = 0f)]
        public float timeLimit;

        [MoreOptions]
        [DisplayAs("If Time Runs Out, Go To (blank = end)")]
        public string timeoutNode = "";
    }

    [Serializable]
    public class EventDialogue
    {
        [DisplayAs("Event Id")]
        public string unlockId = "";

        [DisplayAs("Show On Node")]
        public string showInNode = "";

        [DisplayAs("Player Says")]
        public string text = "...";

        // Makes this a "present the item" line, reached by presenting the item rather than listed as a choice
        [DisplayAs("Present This Item (optional)")]
        public Grabbable requiredItem;

        [DisplayAs("NPC Says", Lines = 3)]
        public string npcReply = "...";

        [FormerlySerializedAs("replyChoices")]
        public List<Choice> choices = new List<Choice>();

        [MoreOptions]
        [DisplayAs("Expression Sprite (optional)")]
        public Sprite expression;

        [MoreOptions]
        [DisplayAs("Portrait Expression (optional)")]
        public Sprite portraitExpression;

        [MoreOptions]
        [DisplayAs("Back Option Text")]
        public string backText = "I had other questions.";

        [MoreOptions]
        [DisplayAs("Grey Out Once Picked")]
        public bool greyAfterChosen = true;

        [MoreOptions]
        [DisplayAs("Go To Node (optional)")]
        public string nextNode = "";

        [MoreOptions]
        [DisplayAs("Ends Conversation")]
        public bool endConversation;

        [MoreOptions]
        [DisplayAs("Salem Thinks Afterwards (optional)", Lines = 2)]
        public string salemThinks = "";

        [MoreOptions]
        public UnityEvent onChosen;
    }

    private const string DefaultBackText = "I had other questions.";

    [Header("Basics")]
    [DisplayAs("NPC Name")]
    public string npcName = "Stranger";

    [DisplayAs("First Node")]
    public string startNode = "start";

    // Swapped on the NPC's own sprite while they talk. Keep them the same size and pivot as the normal sprite
    [Foldout("Talking Sprites")]
    [DisplayAs("Idle Sprite (optional)")]
    public Sprite portrait;

    [DisplayAs("Talking Sprite (optional)")]
    public Sprite talkingPortrait;

    // Art that slides in beside the dialogue box, instead of the camera zooming right up to the sprite
    [Foldout("Dialogue Portrait")]
    [DisplayAs("Portrait (optional)")]
    public Sprite dialoguePortrait;

    [DisplayAs("Portrait Talking (optional)")]
    public Sprite dialoguePortraitTalking;

    // The first time a conversation ends with nothing new left to ask
    [Foldout("Once Everything's Asked")]
    [DisplayAs("Only Counts While First Node Is (blank = any)")]
    public string allAskedNode = "";

    [DisplayAs("Salem Thinks (optional)", Lines = 2)]
    public string allAskedThought = "";

    public UnityEvent onAllAsked;

    [Foldout("More Options")]
    [DisplayAs("Add Goodbye Option")]
    public bool addGoodbye = true;

    [DisplayAs("Reply To Unhelpful Items", Lines = 2)]
    public string wrongItemReply = "I don't see what that has to do with anything.";

    [DisplayAs("Face Height Adjust (meters)")]
    public float faceHeightAdjust;

    // Moves the floating ! and overheard lines
    [DisplayAs("Over-Head Height Adjust (meters)")]
    public float headHeightAdjust;

    [Header("Conversation")]
    public List<Node> nodes = new List<Node>();

    [Header("Event Dialogue")]
    [FormerlySerializedAs("topics")]
    public List<EventDialogue> eventDialogue = new List<EventDialogue>();

    // Top of each sprite's drawn pixels, worked out once per sprite
    private static readonly Dictionary<Sprite, float> spriteTops = new Dictionary<Sprite, float>();

    // Ids ignore capitals and stray spaces
    private readonly HashSet<string> unlockedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<Choice> pickedChoices = new HashSet<Choice>();
    private readonly HashSet<EventDialogue> pickedEvents = new HashSet<EventDialogue>();
    private readonly HashSet<object> visited = new HashSet<object>();
    private readonly HashSet<Node> scanned = new HashSet<Node>();
    private readonly Queue<Node> toScan = new Queue<Node>();
    private readonly List<float> appearTimes = new List<float>();
    private Dictionary<string, Node> nodeLookup;
    private bool talkedTo;
    private bool allAskedDone;
    private PlayerDialogue playerDialogue;
    private float playTime;
    private int nextAppear;

    // Goes up whenever what the NPC has left to say changes, so markers only recheck then
    public int Version { get; private set; }

    public Grabbable ShownItem { get; set; }

    private void Awake()
    {
        MoveEventChoicesOutOfNodes();
        CollectAppearTimes();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        nodeLookup = null;
        Version++;
        if (MoveEventChoicesOutOfNodes()) UnityEditor.EditorUtility.SetDirty(this);
    }
#endif

    private void Start() => playerDialogue = FindAnyObjectByType<PlayerDialogue>();

    // Only runs while a timed choice is still waiting to appear
    private void Update()
    {
        if (nextAppear >= appearTimes.Count || IntroCutscene.IsPlaying) return;
        if (playerDialogue != null && playerDialogue.IsTalking) return;

        playTime += Time.deltaTime;

        bool appeared = false;
        while (nextAppear < appearTimes.Count && playTime >= appearTimes[nextAppear])
        {
            nextAppear++;
            appeared = true;
        }
        if (appeared) Version++;
    }

    public Node GetNode(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        if (nodeLookup == null) BuildNodeLookup();

        nodeLookup.TryGetValue(id.Trim(), out Node node);
        return node;
    }

    public bool IsAvailable(Choice choice) =>
        choice != null
        && (string.IsNullOrWhiteSpace(choice.unlockId) || unlockedIds.Contains(choice.unlockId.Trim()))
        && playTime >= choice.appearAfter;

    // A present-the-item line only needs the item, unless it also has an Event Id
    public bool IsAvailable(EventDialogue entry)
    {
        if (entry == null) return false;

        bool hasId = !string.IsNullOrWhiteSpace(entry.unlockId);
        bool unlocked = hasId && unlockedIds.Contains(entry.unlockId.Trim());

        if (entry.requiredItem == null) return unlocked;
        return ShownItem == entry.requiredItem && (!hasId || unlocked);
    }

    // A blank Show On Node means the first node
    public bool IsShownOn(EventDialogue entry, Node screen)
    {
        if (screen == null || !IsAvailable(entry)) return false;

        string target = string.IsNullOrWhiteSpace(entry.showInNode) ? startNode : entry.showInNode;
        return string.Equals(screen.id?.Trim(), target?.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    public bool WasChosen(Choice choice) => pickedChoices.Contains(choice);
    public void MarkChosen(Choice choice) => pickedChoices.Add(choice);

    public bool WasChosen(EventDialogue entry) => pickedEvents.Contains(entry);
    public void MarkChosen(EventDialogue entry) => pickedEvents.Add(entry);

    public void MarkTalkedTo() => SetChanged(ref talkedTo);

    public void MarkVisited(Choice choice)
    {
        if (visited.Add(choice)) Version++;
    }

    public void MarkVisited(EventDialogue entry)
    {
        if (visited.Add(entry)) Version++;
    }

    // -1 if there's no line for this item, or its event hasn't happened
    public int FindItemEntry(Grabbable item)
    {
        if (item == null) return -1;

        for (int i = 0; i < eventDialogue.Count; i++)
        {
            EventDialogue entry = eventDialogue[i];
            if (entry == null || entry.requiredItem != item) continue;

            bool needsId = !string.IsNullOrWhiteSpace(entry.unlockId);
            if (!needsId || unlockedIds.Contains(entry.unlockId.Trim())) return i;
        }

        return -1;
    }

    // For the marker that says "show me that"
    public bool WantsItem(Grabbable item)
    {
        if (item == null) return false;

        foreach (EventDialogue entry in eventDialogue)
        {
            if (entry == null || entry.requiredItem != item || string.IsNullOrWhiteSpace(entry.text) || visited.Contains(entry)) continue;

            bool needsId = !string.IsNullOrWhiteSpace(entry.unlockId);
            if (!needsId || unlockedIds.Contains(entry.unlockId.Trim())) return true;
        }

        return false;
    }

    // True while there's something unheard: a first conversation, an unlocked event line, or a choice that
    // greys out once picked on any reachable screen
    public bool HasNewDialogue()
    {
        if (!talkedTo) return nodes.Count > 0;

        scanned.Clear();
        toScan.Clear();
        QueueScan(GetNode(startNode));

        foreach (EventDialogue entry in eventDialogue)
        {
            if (entry == null || entry.requiredItem != null || !IsAvailable(entry) || string.IsNullOrWhiteSpace(entry.text)) continue;
            if (IsUnheard(entry.greyAfterChosen, visited.Contains(entry))) return true;

            // Heard already, but what it leads to may still be new
            Node target = GetNode(entry.nextNode);
            if (target != null) QueueScan(target);
            else if (HasNewChoice(entry.choices)) return true;
        }

        while (toScan.Count > 0)
        {
            Node next = toScan.Dequeue();
            if (HasNewChoice(next.choices)) return true;
            QueueScan(GetNode(next.thenNode));
        }

        return false;
    }

    public void UnlockChoice(string unlockId)
    {
        if (!string.IsNullOrWhiteSpace(unlockId) && unlockedIds.Add(unlockId.Trim())) Version++;
    }

    public void LockChoice(string unlockId)
    {
        if (!string.IsNullOrWhiteSpace(unlockId) && unlockedIds.Remove(unlockId.Trim())) Version++;
    }

    // Called as a conversation closes. Fires once, and only while the NPC opens on the named node
    public void CheckAllAsked()
    {
        if (allAskedDone || HasNewDialogue()) return;

        bool anyNode = string.IsNullOrWhiteSpace(allAskedNode);
        if (!anyNode && !string.Equals(allAskedNode.Trim(), startNode?.Trim(), StringComparison.OrdinalIgnoreCase)) return;

        allAskedDone = true;
        if (!string.IsNullOrWhiteSpace(allAskedThought)) DetectiveThoughts.Get().Say(allAskedThought);
        onAllAsked?.Invoke();
    }

    public void SetStartNode(string nodeId)
    {
        if (string.IsNullOrEmpty(nodeId)) return;
        startNode = nodeId;
        Version++;
    }

    private void SetChanged(ref bool flag)
    {
        if (flag) return;
        flag = true;
        Version++;
    }

    private bool HasNewChoice(List<Choice> choices)
    {
        if (choices == null) return false;

        foreach (Choice choice in choices)
        {
            if (!IsAvailable(choice) || string.IsNullOrWhiteSpace(choice.text)) continue;

            // A choice that only ends the conversation has nothing to hear, unless it sets something off
            bool worthHearing = !choice.endConversation || choice.onChosen.GetPersistentEventCount() > 0;
            if (worthHearing && IsUnheard(choice.greyAfterChosen, visited.Contains(choice))) return true;

            QueueScan(GetNode(choice.nextNode));
        }

        return false;
    }

    private static bool IsUnheard(bool greysOut, bool picked) => greysOut && !picked;

    private void QueueScan(Node node)
    {
        if (node != null && scanned.Add(node)) toScan.Enqueue(node);
    }

    // Top of the NPC's head, measured by the sprite's drawn pixels so empty space above the art doesn't count
    public Vector3 HeadPosition(Renderer body, float fallbackHeight)
    {
        Vector3 root = transform.position;
        float top = root.y + fallbackHeight;

        if (body != null && body.enabled)
        {
            top = body.bounds.max.y;
            if (body is SpriteRenderer sprite && sprite.sprite != null)
                top = sprite.transform.TransformPoint(new Vector3(0f, VisibleTop(sprite.sprite), 0f)).y;
        }

        return new Vector3(root.x, top + headHeightAdjust, root.z);
    }

    private static float VisibleTop(Sprite sprite)
    {
        if (spriteTops.TryGetValue(sprite, out float top)) return top;

        // A Tight sprite mesh (Unity's default) hugs the drawn pixels
        top = float.MinValue;
        foreach (Vector2 corner in sprite.vertices) top = Mathf.Max(top, corner.y);
        if (top == float.MinValue) top = sprite.bounds.max.y;

        spriteTops[sprite] = top;
        return top;
    }

    private void BuildNodeLookup()
    {
        nodeLookup = new Dictionary<string, Node>(nodes.Count, StringComparer.OrdinalIgnoreCase);

        foreach (Node node in nodes)
        {
            string key = node?.id?.Trim();
            if (!string.IsNullOrEmpty(key) && !nodeLookup.ContainsKey(key)) nodeLookup.Add(key, node);
        }
    }

    // Sorted, so Update only checks the next one due
    private void CollectAppearTimes()
    {
        appearTimes.Clear();
        foreach (Node node in nodes)
        {
            if (node?.choices == null) continue;
            foreach (Choice choice in node.choices)
            {
                if (choice != null && choice.appearAfter > 0f) appearTimes.Add(choice.appearAfter);
            }
        }
        foreach (EventDialogue entry in eventDialogue)
        {
            if (entry?.choices == null) continue;
            foreach (Choice choice in entry.choices)
            {
                if (choice != null && choice.appearAfter > 0f) appearTimes.Add(choice.appearAfter);
            }
        }
        appearTimes.Sort();
        nextAppear = 0;
    }

    // Moves choices that still carry an event id out of their node and into Event Dialogue
    private bool MoveEventChoicesOutOfNodes()
    {
        bool moved = false;

        foreach (Node node in nodes)
        {
            if (node?.choices == null) continue;

            for (int i = 0; i < node.choices.Count; i++)
            {
                Choice choice = node.choices[i];
                if (choice == null || string.IsNullOrWhiteSpace(choice.unlockId)) continue;

                bool staysOnScreen = string.IsNullOrWhiteSpace(choice.nextNode) && !choice.endConversation;

                eventDialogue.Add(new EventDialogue
                {
                    unlockId = choice.unlockId.Trim(),
                    showInNode = node.id,
                    text = choice.text,
                    npcReply = "",
                    backText = staysOnScreen ? DefaultBackText : "",
                    greyAfterChosen = choice.greyAfterChosen,
                    nextNode = choice.nextNode,
                    endConversation = choice.endConversation,
                    onChosen = choice.onChosen
                });

                node.choices.RemoveAt(i--);
                moved = true;
            }
        }

        if (moved) nodeLookup = null;
        return moved;
    }

#if UNITY_EDITOR
    [ContextMenu("Add Level 1 Lines")]
    private void AddLevelOneLines()
    {
        UnityEditor.Undo.RecordObject(this, "Add Level 1 Lines");
        if (!LevelOneLines.AddTo(this)) return;

        nodeLookup = null;
        Version++;
        UnityEditor.EditorUtility.SetDirty(this);
    }
#endif
}
