using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;

// Everything one NPC can say. Nodes are screens (an NPC line plus the player's replies) linked by Go To Node.
// Event Dialogue entries stay hidden until something calls UnlockChoice with their Event Id,
// e.g. a PlacementZone's On Placed or another dialogue choice's On Chosen.
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

        // Older setups gated choices inside nodes with this; they get moved to Event Dialogue on load
        [HideInInspector]
        public string unlockId = "";

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

        [DisplayAs("Expression (optional)")]
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

        // Set this to make it a "show the item" line: it only appears while the player is carrying that object
        [DisplayAs("Only While Holding (optional)")]
        public Grabbable requiredItem;

        [DisplayAs("NPC Says", Lines = 3)]
        public string npcReply = "...";

        [FormerlySerializedAs("replyChoices")]
        public List<Choice> choices = new List<Choice>();

        [MoreOptions]
        [DisplayAs("Expression (optional)")]
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

    // Left empty, the conversation shows the NPC's own world sprite, so a sprite swap (like a monster reveal) shows up too
    [Header("Portrait")]
    [DisplayAs("Portrait (optional)")]
    public Sprite portrait;

    [DisplayAs("Talking Portrait (optional)")]
    public Sprite talkingPortrait;

    // Nudges the floating ! and overheard lines up or down for this NPC, in meters
    [Header("Over Their Head")]
    [DisplayAs("Height Adjust (meters)")]
    public float headHeightAdjust;

    [Header("Conversation")]
    public List<Node> nodes = new List<Node>();

    [Header("Event Dialogue")]
    [FormerlySerializedAs("topics")]
    public List<EventDialogue> eventDialogue = new List<EventDialogue>();

    // Ids and node names ignore capitals, and stray spaces are trimmed off
    private readonly HashSet<string> unlockedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<Choice> pickedChoices = new HashSet<Choice>();
    private readonly HashSet<EventDialogue> pickedEvents = new HashSet<EventDialogue>();
    private readonly HashSet<object> visited = new HashSet<object>();
    private readonly HashSet<Node> scanned = new HashSet<Node>();
    private readonly Queue<Node> toScan = new Queue<Node>();
    private Dictionary<string, Node> nodeLookup;

    // Top of each sprite's drawn pixels, in the sprite's own units, worked out once per sprite
    private static readonly Dictionary<Sprite, float> spriteTops = new Dictionary<Sprite, float>();
    private bool talkedTo;

    // Goes up whenever something changes what the NPC has left to say, so markers only recheck when needed
    public int Version { get; private set; }

    private void Awake() => MoveEventChoicesOutOfNodes();

#if UNITY_EDITOR
    private void OnValidate()
    {
        nodeLookup = null;
        Version++;
        if (MoveEventChoicesOutOfNodes()) UnityEditor.EditorUtility.SetDirty(this);
    }
#endif

    public Node GetNode(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        if (nodeLookup == null) BuildNodeLookup();

        nodeLookup.TryGetValue(id.Trim(), out Node node);
        return node;
    }

    public bool IsAvailable(Choice choice) =>
        choice != null && (string.IsNullOrWhiteSpace(choice.unlockId) || unlockedIds.Contains(choice.unlockId.Trim()));

    // The object the player brought into this conversation, if any
    public Grabbable ShownItem { get; set; }

    // Event dialogue shows once its event has happened. A held-item line only needs the item, unless it also has an Event Id.
    public bool IsAvailable(EventDialogue entry)
    {
        if (entry == null) return false;

        bool hasId = !string.IsNullOrWhiteSpace(entry.unlockId);
        bool unlocked = hasId && unlockedIds.Contains(entry.unlockId.Trim());

        if (entry.requiredItem == null) return unlocked;
        return ShownItem == entry.requiredItem && (!hasId || unlocked);
    }

    // Whether an unlocked entry belongs on this screen. A blank Show On Node means the first node.
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
    public void MarkVisited(Choice choice) { if (visited.Add(choice)) Version++; }
    public void MarkVisited(EventDialogue entry) { if (visited.Add(entry)) Version++; }

    // True while there's something the player hasn't clicked through yet: a first conversation, an unlocked
    // event, or a choice on any screen they can reach. Choices that only end the conversation don't count.
    public bool HasNewDialogue()
    {
        if (!talkedTo) return nodes.Count > 0;

        foreach (EventDialogue entry in eventDialogue)
        {
            if (entry.requiredItem == null && IsAvailable(entry) && !string.IsNullOrWhiteSpace(entry.text) && !visited.Contains(entry)) return true;
        }

        scanned.Clear();
        toScan.Clear();
        QueueScan(GetNode(startNode));

        foreach (EventDialogue entry in eventDialogue)
        {
            if (!visited.Contains(entry) || !IsAvailable(entry)) continue;

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

    // Hides them again
    public void LockChoice(string unlockId)
    {
        if (!string.IsNullOrWhiteSpace(unlockId) && unlockedIds.Remove(unlockId.Trim())) Version++;
    }

    // Changes which node conversations open on
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

            if (visited.Contains(choice)) QueueScan(GetNode(choice.nextNode));
            else if (!choice.endConversation || choice.onChosen.GetPersistentEventCount() > 0) return true;
        }

        return false;
    }

    private void QueueScan(Node node)
    {
        if (node != null && scanned.Add(node)) toScan.Enqueue(node);
    }

    // Where the top of this NPC's head is in the world, for anything that floats above it.
    // Sprites are measured by their drawn pixels, not the image's box, since character art usually has empty
    // space above the head and every box would come out the same height.
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