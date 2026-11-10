using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;

// Everything one NPC can say. Nodes are screens (an NPC line plus the player's replies) linked by Go To Node.
// Event Dialogue entries stay hidden until something calls UnlockChoice with their Event Id,
// e.g. a PlacementZone's On Placed or another dialogue choice's On Chosen
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

        // Counts play time only: menus, the intro and conversations don't use it up
        [MoreOptions]
        [DisplayAs("Appears After (seconds, 0 = always)", Min = 0f)]
        public float appearAfter;

        // Older setups gated choices inside nodes with this; they get moved to Event Dialogue on load
        [HideInInspector]
        public string unlockId = "";

        // Shown once the conversation closes, and written in the journal
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

        [DisplayAs("Expression Sprite (optional)")]
        public Sprite expression;

        public List<Choice> choices = new List<Choice>();

        // A quick-time screen: pick before the bar runs out or the conversation moves on without you
        [DisplayAs("Time Limit (seconds, 0 = none)", Min = 0f)]
        public float timeLimit;

        [DisplayAs("If Time Runs Out, Go To (blank = end)")]
        public string timeoutNode = "";
    }

    // Laid out like a Node: the line the player picks, the NPC's answer, and the replies under that
    [Serializable]
    public class EventDialogue
    {
        [DisplayAs("Event Id")]
        public string unlockId = "";

        [DisplayAs("Show On Node")]
        public string showInNode = "";

        [DisplayAs("Player Says")]
        public string text = "...";

        // Set this to make it a "present the item" line. It's never listed as a choice; the player reaches it by
        // presenting the item from their inventory, and Player Says is logged as what they say while showing it
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

    [DisplayAs("Add Goodbye Option")]
    public bool addGoodbye = true;

    [Header("Presenting Items")]
    [DisplayAs("Reply To Unhelpful Items", Lines = 2)]
    public string wrongItemReply = "I don't see what that has to do with anything.";

    // Shown on the NPC itself while they talk. Left empty, Idle Sprite is whatever the NPC already shows.
    // Both should match the NPC's normal sprite in size and pivot so nothing jumps when they swap
    [Header("Talking Sprites")]
    [DisplayAs("Idle Sprite (optional)")]
    public Sprite portrait;

    [DisplayAs("Talking Sprite (optional)")]
    public Sprite talkingPortrait;

    // Moves the spot the conversation camera zooms in on up or down for this NPC, in meters
    [Header("Conversation Camera")]
    [DisplayAs("Face Height Adjust (meters)")]
    public float faceHeightAdjust;

    // Nudges the floating ! and overheard lines up or down for this NPC, in meters
    [Header("Over Their Head")]
    [DisplayAs("Height Adjust (meters)")]
    public float headHeightAdjust;

    [Header("Conversation")]
    public List<Node> nodes = new List<Node>();

    [Header("Event Dialogue")]
    [FormerlySerializedAs("topics")]
    public List<EventDialogue> eventDialogue = new List<EventDialogue>();

    // Top of each sprite's drawn pixels, in the sprite's own units, worked out once per sprite
    private static readonly Dictionary<Sprite, float> spriteTops = new Dictionary<Sprite, float>();

    // Ids and node names ignore capitals, and stray spaces are trimmed off
    private readonly HashSet<string> unlockedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<Choice> pickedChoices = new HashSet<Choice>();
    private readonly HashSet<EventDialogue> pickedEvents = new HashSet<EventDialogue>();
    private readonly HashSet<object> visited = new HashSet<object>();
    private readonly HashSet<Node> scanned = new HashSet<Node>();
    private readonly Queue<Node> toScan = new Queue<Node>();
    private readonly List<float> appearTimes = new List<float>();
    private Dictionary<string, Node> nodeLookup;
    private bool talkedTo;
    private PlayerDialogue playerDialogue;
    private float playTime;
    private int nextAppear;

    // Goes up whenever something changes what the NPC has left to say, so markers only recheck when needed
    public int Version { get; private set; }

    // The item the player last presented in this conversation, if any
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

    // Event dialogue shows once its event has happened. A present-the-item line only needs the item, unless it also has an Event Id
    public bool IsAvailable(EventDialogue entry)
    {
        if (entry == null) return false;

        bool hasId = !string.IsNullOrWhiteSpace(entry.unlockId);
        bool unlocked = hasId && unlockedIds.Contains(entry.unlockId.Trim());

        if (entry.requiredItem == null) return unlocked;
        return ShownItem == entry.requiredItem && (!hasId || unlocked);
    }

    // Whether an unlocked entry belongs on this screen. A blank Show On Node means the first node
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

    // Seen is tracked separately from greying out, so choices that never grey out still count as heard
    public void MarkTalkedTo() => SetChanged(ref talkedTo);

    public void MarkVisited(Choice choice)
    {
        if (visited.Add(choice)) Version++;
    }

    public void MarkVisited(EventDialogue entry)
    {
        if (visited.Add(entry)) Version++;
    }

    // The "present the item" line for this object, if its event has happened (or it has none). -1 if there isn't one
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

    // Whether this NPC has an unheard "present the item" line for this object, so a marker can say "show me that"
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

    // True while there's something the player hasn't heard yet: a first conversation, an unlocked event line,
    // or a choice that greys out once picked on any screen they can reach. Choices that never grey out
    // ("I had other questions") only move between screens, so they're followed but never counted
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

            // Heard (or never greys out): whatever it leads to may still hold something new
            Node target = GetNode(entry.nextNode);
            if (target != null) QueueScan(target);
            else if (HasNewChoice(entry.choices)) return true;
        }

        while (toScan.Count > 0)
        {
            if (HasNewChoice(toScan.Dequeue().choices)) return true;
        }

        return false;
    }

    // Shows every Event Dialogue entry with this id
    public void UnlockChoice(string unlockId)
    {
        if (!string.IsNullOrWhiteSpace(unlockId) && unlockedIds.Add(unlockId.Trim())) Version++;
    }

    public void LockChoice(string unlockId)
    {
        if (!string.IsNullOrWhiteSpace(unlockId) && unlockedIds.Remove(unlockId.Trim())) Version++;
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

            // A choice that only closes the conversation has nothing to hear, unless it sets something off
            bool worthHearing = !choice.endConversation || choice.onChosen.GetPersistentEventCount() > 0;
            if (worthHearing && IsUnheard(choice.greyAfterChosen, visited.Contains(choice))) return true;

            QueueScan(GetNode(choice.nextNode));
        }

        return false;
    }

    // Only lines that grey out once picked can be "done", which matches what the player sees on screen
    private static bool IsUnheard(bool greysOut, bool picked) => greysOut && !picked;

    private void QueueScan(Node node)
    {
        if (node != null && scanned.Add(node)) toScan.Enqueue(node);
    }

    // Where the top of this NPC's head is in the world, for anything that floats above it.
    // Sprites are measured by their drawn pixels, not the image's box, since character art usually has empty
    // space above the head and every box would come out the same height
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

        // A sprite's mesh hugs its drawn pixels when its Mesh Type is Tight (Unity's default)
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

    // Every timed choice's time in order, so Update only ever checks the next one due
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

    // Upgrade path: choices that still carry an event id are moved out of their node into Event Dialogue
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
}