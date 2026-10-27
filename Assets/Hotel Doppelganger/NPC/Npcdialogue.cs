using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;

// An NPC's dialogue: a tree of nodes, each one NPC line plus the player's replies,
// and a separate list of event dialogue that only appears once an event has happened
// (e.g. a PlacementZone's On Placed calling UnlockChoice with the entry's Event Id).
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

        // Older setups put event choices inside nodes; those are moved to Event Dialogue automatically
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

    // An option that only appears once its event has happened. Laid out like a Node:
    // the line the player picks, what the NPC says back, and the choices under that.
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

    private const string EventKeyPrefix = "event/";
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

    private readonly HashSet<string> chosenKeys = new HashSet<string>();
    private readonly HashSet<string> unlockedIds = new HashSet<string>();

    // ---------- Lifecycle ----------

    private void Awake() => MoveEventChoicesOutOfNodes();

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (MoveEventChoicesOutOfNodes()) UnityEditor.EditorUtility.SetDirty(this);
    }
#endif

    // ---------- Queries ----------

    // Ignores capitals and stray spaces, so "BottleThanks " finds "bottleThanks"
    public Node GetNode(string id)
    {
        string wanted = id?.Trim() ?? string.Empty;
        return nodes.Find(n => n != null && string.Equals(n.id?.Trim(), wanted, StringComparison.OrdinalIgnoreCase));
    }

    public bool WasSaid(Node node, int index) => chosenKeys.Contains(ChoiceKey(node, index));
    public void MarkSaid(Node node, int index) => chosenKeys.Add(ChoiceKey(node, index));

    public bool WasEventSaid(int index) => chosenKeys.Contains(EventKeyPrefix + index);
    public void MarkEventSaid(int index) => chosenKeys.Add(EventKeyPrefix + index);

    public bool IsAvailable(Choice choice)
    {
        if (choice == null) return false;

        string id = NormalizeId(choice.unlockId);
        return id.Length == 0 || unlockedIds.Contains(id);
    }

    // Event dialogue only ever shows once its event has happened
    public bool IsAvailable(EventDialogue entry) =>
        entry != null && unlockedIds.Contains(NormalizeId(entry.unlockId));

    // ---------- UnityEvent actions ----------

    // Shows every event dialogue entry with this Event Id.
    public void UnlockChoice(string unlockId)
    {
        string id = NormalizeId(unlockId);
        if (id.Length > 0) unlockedIds.Add(id);
    }

    // Hides every event dialogue entry with this Event Id again.
    public void LockChoice(string unlockId)
    {
        unlockedIds.Remove(NormalizeId(unlockId));
    }

    // Changes which node conversations open on.
    public void SetStartNode(string nodeId)
    {
        if (!string.IsNullOrEmpty(nodeId)) startNode = nodeId;
    }

    // ---------- Upgrading older setups ----------

    // Choices with an event id inside a node become Event Dialogue entries on that node's screen.
    // Returns true if anything was moved.
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
                    choices = new List<Choice>(),
                    backText = staysOnScreen ? DefaultBackText : "",
                    greyAfterChosen = choice.greyAfterChosen,
                    nextNode = choice.nextNode,
                    endConversation = choice.endConversation,
                    onChosen = choice.onChosen
                });

                node.choices.RemoveAt(i);
                i--;
                moved = true;
            }
        }

        return moved;
    }

    // ---------- Helpers ----------

    private static string ChoiceKey(Node node, int index) => node.id + "/" + index;

    // Case- and whitespace-insensitive, so "BottlePlaced " matches "bottleplaced"
    private static string NormalizeId(string id) => string.IsNullOrWhiteSpace(id) ? string.Empty : id.Trim().ToLowerInvariant();
}