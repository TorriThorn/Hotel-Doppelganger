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

        public List<Choice> choices = new List<Choice>();
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

        [DisplayAs("NPC Says", Lines = 3)]
        public string npcReply = "...";

        [FormerlySerializedAs("replyChoices")]
        public List<Choice> choices = new List<Choice>();

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

    [Header("Conversation")]
    public List<Node> nodes = new List<Node>();

    [Header("Event Dialogue")]
    [FormerlySerializedAs("topics")]
    public List<EventDialogue> eventDialogue = new List<EventDialogue>();

    // Ids and node names ignore capitals, and stray spaces are trimmed off
    private readonly HashSet<string> unlockedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<Choice> pickedChoices = new HashSet<Choice>();
    private readonly HashSet<EventDialogue> pickedEvents = new HashSet<EventDialogue>();
    private Dictionary<string, Node> nodeLookup;

    private void Awake() => MoveEventChoicesOutOfNodes();

#if UNITY_EDITOR
    private void OnValidate()
    {
        nodeLookup = null;
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

    // Event dialogue only ever shows once its event has happened
    public bool IsAvailable(EventDialogue entry) =>
        entry != null && !string.IsNullOrWhiteSpace(entry.unlockId) && unlockedIds.Contains(entry.unlockId.Trim());

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

    // Shows every Event Dialogue entry with this id
    public void UnlockChoice(string unlockId)
    {
        if (!string.IsNullOrWhiteSpace(unlockId)) unlockedIds.Add(unlockId.Trim());
    }

    // Hides them again
    public void LockChoice(string unlockId)
    {
        if (!string.IsNullOrWhiteSpace(unlockId)) unlockedIds.Remove(unlockId.Trim());
    }

    // Changes which node conversations open on
    public void SetStartNode(string nodeId)
    {
        if (!string.IsNullOrEmpty(nodeId)) startNode = nodeId;
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