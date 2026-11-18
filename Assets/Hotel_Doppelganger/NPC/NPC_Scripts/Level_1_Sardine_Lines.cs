#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

// The Sardine Tin Sequence's conversations, added by NPC Dialogue's "Add Sardine Tin Lines" menu.
// Present This Item is filled in with the scene's Sardine Tin if there is one. Events that move people or
// dim the lights are left for the Inspector, as in the setup guide
public static class SardineTinLines
{
    private const string BackText = "I had other questions.";
    private const string TinQuestion = "Do you recognize this powder?";
    private const string Salem = "Salem";

    private delegate void Writer(NPCDialogue npc, Grabbable tin);

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
        if (HasTinLine(npc)) return false;

        write(npc, FindTin());
        return true;
    }

    private static void Helena(NPCDialogue npc, Grabbable tin)
    {
        Present(npc, tin, "Hmm? Is that facial powder? What's it doing in a tin can?");
    }

    private static void Carmela(NPCDialogue npc, Grabbable tin)
    {
        Present(npc, tin, "Ugh. Get that stuff away from me.");
    }

    private static void Yaroslava(NPCDialogue npc, Grabbable tin)
    {
        Present(npc, tin, "*snatches at the tin*", "", "tin_grab");

        Then(npc, "tin_grab", "Hey! This is crime scene evidence!", "tin_pinch", Salem);
        Then(npc, "tin_pinch", "You don't need all of it. Just give me a pinch.", "tin_confusing");
        Then(npc, "tin_confusing", "I don't want to make you more confusing than you already are.", "tin_huff", Salem);
        Node(npc, "tin_huff", "*Huffs*", End("..."));
    }

    private static void Murray(NPCDialogue npc, Grabbable tin)
    {
        Present(npc, tin, "Well, that's contraband if I've ever seen it! Not surprising, though.", "", "",
            Ask("Oh?", "tin_cans"));

        Node(npc, "tin_cans", "I heard it comes in through ordinary objects. My restaurant business has seen a few shares in cans and sealed packages. But also electronics. Drones might be the next big thing as well.",
            Ask("Really.", "tin_doubt"));

        // Opens up James's smuggling question
        NPCDialogue.Choice done = End("...");
        NPCDialogue james = FindNpc("James");
        if (james != null) UnityEditor.Events.UnityEventTools.AddStringPersistentListener(done.onChosen, james.UnlockChoice, "smuggling");
        Node(npc, "tin_doubt", "Indeed. *muttering to himself* But I highly doubt it.", done);

        Then(npc, "after_chase", "What was that?", "mc2", Salem);
        Node(npc, "mc2", "(angry) The drunkard tried to steal from me! I was only trying to calm him down.",
            Ask("What did he try to steal?", "mc3"));
        Node(npc, "mc3", "My watch. Can't pay his tab, the pathetic drunk.", End("I see."));
    }

    private static void Zain(NPCDialogue npc, Grabbable tin)
    {
        Present(npc, tin, "(!!!) Why do you have that?!", "", "",
            Ask("It was at the bar. You didn't see it?", "tin_panic"));

        // Its On Chosen is where the panic starts
        Node(npc, "tin_panic", "It wasn't there before! No no no no! They can't be here! Not yet!", End("Zain, wait-!"));

        Then(npc, "after_chase", "What was that?", "zc2", Salem);
        NPCDialogue.Choice leave = End("...");
        leave.salemThinks = "I need to get him sobered up.";
        Node(npc, "zc2", "(too drunk & rattled) I-I.. Just.. stop.. I can't do this right now.", leave);
    }

    private static void James(NPCDialogue npc, Grabbable tin)
    {
        Present(npc, tin, "(raises an eyebrow) If I were to take drugs, I wouldn't do so in such an unsophisticated manner.");

        // Unlocked by Murray's tin conversation
        npc.eventDialogue.Add(new NPCDialogue.EventDialogue
        {
            unlockId = "smuggling",
            text = "What do you know about drug smuggling in electronics?",
            npcReply = "If you are accusing me of that, you'll need far more evidence.",
            backText = "",
            nextNode = "smuggle_2"
        });
        Then(npc, "smuggle_2", "Just asking everyone with a business.", "smuggle_3", Salem);
        Then(npc, "smuggle_3", "Then you'll know that no one with a business is a total saint, no matter how much Murray tries to act like one.", "smuggle_4");
        Then(npc, "smuggle_4", "So you're accusing Murray of smuggling drugs?", "smuggle_5", Salem);
        Node(npc, "smuggle_5", "I'm not accusing anyone of anything. I'm just being smarter than you.", Back());

        // Pulling him away from Zain once the lights are back
        Then(npc, "separate", "Zain, you can't leave. The police chief is right outside this door.", "separate_2", Salem);
        Then(npc, "separate_2", "But-!", "separate_3", "Zain");
        Then(npc, "separate_3", "James, I need you to follow me.", "separate_4", Salem);
        Node(npc, "separate_4", "Fine.", End("This way."));

        // Branch 1: the player overheard him and Zain
        Then(npc, "confront_heard", "What the heck was that about?", "h2", Salem);
        Then(npc, "h2", "I had to stop an obvious suspect from running.", "h3");
        Then(npc, "h3", "No, I meant that conversation. You two know each other?", "h4", Salem);
        Then(npc, "h4", "(hesitant) He used to work for me, yes.", "h5");
        Then(npc, "h5", "Used to?", "h6", Salem);
        Node(npc, "h6", "He disappeared one day, apartment ransacked. I had my security team looking for him, but nothing. He left no trace. This is first time I've seen him in three months.",
            Ask("Interesting how a security tech CEO couldn't protect his own people.", "a1"),
            Ask("Interesting how invested a big-time CEO is in such a small-time employee.", "b1"));

        Then(npc, "a1", "(offended) This was before the technology was perfected. I couldn't risk putting my drones out there before their systems were finalized.", "a2");
        Then(npc, "a2", "Uh-huh.", "a3", Salem);
        Then(npc, "a3", "Fine. Mind telling me the real reason he's freaked out?", "debt", Salem);

        Then(npc, "b1", "(agitated) I-I care for all my employees. And Zain was one of the best.", "b2");
        Then(npc, "b2", "What did he do for you, exactly?", "b3", Salem);
        Node(npc, "b3", "H-He works on the engineering team and perfected the drones' efficiency.",
            Ask("Who are the other employees he worked with?", "b4"),
            Ask("What was he building specifically?", "b5"),
            Ask("Fine. Mind telling me the real reason he's freaked out?", "debt"));
        Then(npc, "b4", "Um, Patrice, er, patricia, no maybe Pattie... Is that really important right now?", "b3");
        Then(npc, "b5", "That's NDA information.", "b3");

        // Branch 2: the player didn't hear them
        Then(npc, "confront_missed", "What the heck was that about?", "m2", Salem);
        Then(npc, "m2", "I had to stop an obvious suspect from running.", "m3");
        Then(npc, "m3", "That's not your job. The police chief would have handled it.", "m4", Salem);
        Then(npc, "m4", "Well, someone had to take care of your mess.", "m5");
        Then(npc, "m5", "Do you know why he's so freaked out?", "debt", Salem);

        // Its On Chosen sets off the chase
        Node(npc, "debt", "He's in debt. Took too many drugs from the mafia. Now they want to collect. That sardine can might be proof that one of them is in here.",
            End("..."));
    }

    private static void Present(NPCDialogue npc, Grabbable tin, string reply, string backText = BackText, string next = "",
        params NPCDialogue.Choice[] choices)
    {
        npc.eventDialogue.Add(new NPCDialogue.EventDialogue
        {
            // Zain's panic is the one that matters, so after it no one else asks for the tin
            mainReaction = string.Equals(npc.npcName?.Trim(), "Zain", StringComparison.OrdinalIgnoreCase),
            text = TinQuestion,
            requiredItem = tin,
            npcReply = reply,
            backText = backText,
            nextNode = next,
            choices = new List<NPCDialogue.Choice>(choices)
        });
    }

    private static void Node(NPCDialogue npc, string id, string says, params NPCDialogue.Choice[] choices)
    {
        npc.nodes.Add(new NPCDialogue.Node
        {
            id = id,
            npcText = says,
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

    private static NPCDialogue.Choice Back() =>
        new NPCDialogue.Choice { text = BackText, nextNode = LevelOneLines.StartNode, greyAfterChosen = false, onChosen = new UnityEvent() };

    private static bool HasTinLine(NPCDialogue npc)
    {
        foreach (NPCDialogue.EventDialogue entry in npc.eventDialogue)
        {
            if (entry != null && entry.text == TinQuestion) return true;
        }
        return false;
    }

    private static Grabbable FindTin()
    {
        foreach (Grabbable item in UnityEngine.Object.FindObjectsByType<Grabbable>(FindObjectsInactive.Include))
        {
            if (item.DisplayName.IndexOf("sardine", StringComparison.OrdinalIgnoreCase) >= 0) return item;
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