#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

// The Recover Fish Scale Sequence's conversations, added by NPC Dialogue's "Add Fish Scale Return Lines" menu.
// The reactions to the dried scale only show once Heat Reaction unlocks "scale_dried". Carmela's medicine
// conversation is a Confront: she denies having anything for Zain, and catching the lie gets her to help
public static class FishScaleReturnLines
{
    public const string DriedId = "scale_dried";
    private const string ShowLine = "Take a look at this.";
    private const string BackText = "I had other questions.";
    private const string Salem = "Salem";
    private const string ConfrontName = "Carmela Medicine Confront";

    private delegate void Writer(NPCDialogue npc, Grabbable scale);

    private static readonly Dictionary<string, Writer> Writers = new Dictionary<string, Writer>(StringComparer.OrdinalIgnoreCase)
    {
        { "Helena", Helena },
        { "James", James },
        { "Carmela", Carmela },
        { "Zain", Zain },
        { "Murray", Murray },
        { "Yaroslava", Yaroslava }
    };

    // False if the NPC Name isn't one of the six, or the lines are already there
    public static bool AddTo(NPCDialogue npc)
    {
        if (npc == null || !Writers.TryGetValue(npc.npcName?.Trim() ?? string.Empty, out Writer write)) return false;
        if (HasLines(npc)) return false;

        write(npc, FindScale());
        return true;
    }

    private static void Helena(NPCDialogue npc, Grabbable scale)
    {
        Present(npc, scale, "Aw, it's not pretty anymore! Still stinks though.");
    }

    private static void James(NPCDialogue npc, Grabbable scale)
    {
        Present(npc, scale, "What am I supposed to do with this?");
    }

    private static void Murray(NPCDialogue npc, Grabbable scale)
    {
        Present(npc, scale, "My how fascinating! It seems useless now, though.");
    }

    private static void Zain(NPCDialogue npc, Grabbable scale)
    {
        Present(npc, scale, "(sobering up) What the...? Ow. Sorry. Medicine is working in the worst way.");
    }

    private static void Yaroslava(NPCDialogue npc, Grabbable scale)
    {
        Present(npc, scale, "(excited) Yes, yes! I'll just take that off your hands...", "", "scale_stop");
        Node(npc, "scale_stop", "Stop it, will you!", Salem, End("..."));
    }

    private static void Carmela(NPCDialogue npc, Grabbable scale)
    {
        AddMedicineConfront(npc);
        Then(npc, "medicine_2", "You were delivering medicine to a drunk person, yes? Is the man over there?", "medicine_3", Salem);
        Then(npc, "medicine_3", "...Yes, I believe so.", "medicine_4");
        Then(npc, "medicine_4", "Can you give it to him now? As well as something to calm him down.", "medicine_5", Salem);
        Node(npc, "medicine_5", "I can do that.", "", End("Thank you."));

        // Showing her the dried scale
        Present(npc, scale, "I knew it.", "", "", Ask("Knew what?", "scale_knew"));
        Then(npc, "scale_knew", "(hesitant) ...This scale is definitely not from an ordinary animal. Why Geno had it, I don't know. But I'm certain the owner is still around.", "scale_mine");
        Then(npc, "scale_mine", "Can I have the scale now?", "scale_no", "Yaroslava");
        Then(npc, "scale_no", "NO", "scale_huff", "Salem & Carmela");

        NPCDialogue.Choice done = End("...");
        done.salemThinks = "So it reacts to fire, and the monster is still in here. Maybe...";
        Node(npc, "scale_huff", "*Huffs*", "Yaroslava", done);

        // Zain's "I can't do this right now" is what sends the player to Carmela
        NPCDialogue zain = FindNpc("Zain");
        NPCDialogue.Choice zainLast = zain != null ? FirstChoice(zain.GetNode("zc2")) : null;
        if (zainLast == null) return;

        if (zainLast.onChosen == null) zainLast.onChosen = new UnityEvent();
        UnityEditor.Events.UnityEventTools.AddStringPersistentListener(zainLast.onChosen, npc.SetStartNode, "medicine");
        UnityEditor.EditorUtility.SetDirty(zain);
    }

    // For a Carmela who already has the plain medicine lines: swaps her opening for the Confront version
    public static bool AddCarmelaConfront(NPCDialogue npc)
    {
        if (npc == null || !string.Equals(npc.npcName?.Trim(), "Carmela", StringComparison.OrdinalIgnoreCase)) return false;
        if (npc.GetNode("medicine_deny") != null) return false;

        npc.nodes.RemoveAll(node => node != null && string.Equals(node.id?.Trim(), "medicine", StringComparison.OrdinalIgnoreCase));
        AddMedicineConfront(npc);
        return true;
    }

    // She plays dumb, Salem checks his notes, and catching the lie opens the conversation at Salem's question
    private static void AddMedicineConfront(NPCDialogue npc)
    {
        Node(npc, "medicine", "What?", "",
            Ask("That man by the fire is in a bad way. Can you do anything for him?", "medicine_deny"));

        NPCDialogue.Choice check = End("(Hold on. That's not what she told me before.)");
        Node(npc, "medicine_deny", "Why ask me? I'm not his doctor. I came here to rest for the night, and I didn't bring anything for drunks.", "", check);

        Contradiction confront = FindOrMakeConfront();
        confront.SetUp(
            new Contradiction.Statement("Carmela",
                "Why ask me? I'm [not his doctor]. I came here to [rest for the night], and I [didn't bring anything] for drunks.",
                "didn't bring anything"),
            new Contradiction.Statement("Carmela (earlier)",
                "I was sent here to [deliver something] to a client. [Medicine for inebriation] and hangovers.",
                "Medicine for inebriation"),
            "(She told me she brought medicine for drunks... so why would she have nothing for him?)");

        UnityEditor.Events.UnityEventTools.AddVoidPersistentListener(check.onChosen, confront.Open);

        // Caught out, she opens straight onto Salem's question, and afterwards goes back to her usual lines
        if (confront.onSolved == null) confront.onSolved = new UnityEvent();
        UnityEditor.Events.UnityEventTools.AddStringPersistentListener(confront.onSolved, npc.SetStartNode, LevelOneLines.StartNode);
        UnityEditor.Events.UnityEventTools.AddStringPersistentListener(confront.onSolved, npc.TalkToPlayerAt, "medicine_2");
        UnityEditor.EditorUtility.SetDirty(confront);
    }

    private static Contradiction FindOrMakeConfront()
    {
        foreach (Contradiction existing in UnityEngine.Object.FindObjectsByType<Contradiction>(FindObjectsInactive.Include))
        {
            if (existing.name == ConfrontName) return existing;
        }

        var made = new GameObject(ConfrontName).AddComponent<Contradiction>();
        UnityEditor.Undo.RegisterCreatedObjectUndo(made.gameObject, "Add " + ConfrontName);
        return made;
    }

    private static void Present(NPCDialogue npc, Grabbable scale, string reply, string backText = BackText, string next = "",
        params NPCDialogue.Choice[] choices)
    {
        npc.eventDialogue.Add(new NPCDialogue.EventDialogue
        {
            // Carmela's is the one that matters, so after it no one else asks for the dried scale
            mainReaction = string.Equals(npc.npcName?.Trim(), "Carmela", StringComparison.OrdinalIgnoreCase),
            unlockId = DriedId,
            text = ShowLine,
            requiredItem = scale,
            npcReply = reply,
            backText = backText,
            nextNode = next,
            choices = new List<NPCDialogue.Choice>(choices)
        });
    }

    private static void Node(NPCDialogue npc, string id, string says, string speaker, params NPCDialogue.Choice[] choices)
    {
        npc.nodes.Add(new NPCDialogue.Node
        {
            id = id,
            npcText = says,
            speaker = speaker,
            choices = new List<NPCDialogue.Choice>(choices)
        });
    }

    // Two lines in a row with nothing for Salem to choose in between
    private static void Then(NPCDialogue npc, string id, string says, string next, string speaker = "")
    {
        npc.nodes.Add(new NPCDialogue.Node
        {
            id = id,
            npcText = says,
            speaker = speaker,
            thenNode = next
        });
    }

    private static NPCDialogue.Choice Ask(string text, string next) =>
        new NPCDialogue.Choice { text = text, nextNode = next, onChosen = new UnityEvent() };

    private static NPCDialogue.Choice End(string text) =>
        new NPCDialogue.Choice { text = text, endConversation = true, onChosen = new UnityEvent() };

    private static NPCDialogue.Choice FirstChoice(NPCDialogue.Node node) =>
        node != null && node.choices != null && node.choices.Count > 0 ? node.choices[0] : null;

    private static bool HasLines(NPCDialogue npc)
    {
        foreach (NPCDialogue.EventDialogue entry in npc.eventDialogue)
        {
            if (entry != null && entry.text == ShowLine && entry.unlockId == DriedId) return true;
        }
        return false;
    }

    private static Grabbable FindScale()
    {
        foreach (Grabbable item in UnityEngine.Object.FindObjectsByType<Grabbable>(FindObjectsInactive.Include))
        {
            if (item.DisplayName.IndexOf("scale", StringComparison.OrdinalIgnoreCase) >= 0) return item;
        }
        return null;
    }

    private static NPCDialogue FindNpc(string npcName)
    {
        foreach (NPCDialogue npc in UnityEngine.Object.FindObjectsByType<NPCDialogue>(FindObjectsInactive.Include))
        {
            if (string.Equals(npc.npcName?.Trim(), npcName, StringComparison.OrdinalIgnoreCase)) return npc;
        }
        return null;
    }
}
#endif