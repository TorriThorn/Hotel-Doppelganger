#if UNITY_EDITOR
using System;
using System.Collections.Generic;

// The Level 1 conversations from the puzzle sequence, added by NPC Dialogue's "Add Level 1 Lines" menu.
// Node ids start with l1_ so they sit apart from anything already there. Present This Item is left empty on
// the fish scale lines, since it has to be the scale object from the scene
public static class LevelOneLines
{
    public const string StartNode = "l1_start";

    private const string BackText = "I had other questions.";
    private const string ScaleQuestion = "Do you know what this is?";

    private delegate void Writer(NPCDialogue npc);

    private static readonly Dictionary<string, Writer> Writers = new Dictionary<string, Writer>(StringComparer.OrdinalIgnoreCase)
    {
        { "Helena", Helena },
        { "James", James },
        { "Carmela", Carmela },
        { "Zain", Zain },
        { "Murray", Murray },
        { "Yaroslava", Yaroslava }
    };

    // False when the NPC Name isn't one of the six, or the lines were already added
    public static bool AddTo(NPCDialogue npc)
    {
        if (npc == null || !Writers.TryGetValue(npc.npcName?.Trim() ?? string.Empty, out Writer write)) return false;
        if (npc.GetNode(StartNode) != null) return false;

        write(npc);
        npc.allAskedNode = StartNode;
        return true;
    }

    private static void Helena(NPCDialogue npc)
    {
        Intro(npc, "(still sniffling) I just want to go back to my penthouse...");

        Node(npc, "l1_why", "I'm with James. He's attending a fancy business meeting for his new project. I heard wonderful things about this hotel's services and wanted to see for myself.",
            Back());
        Node(npc, "l1_geno", "No? Doesn't ring a bell. But I have seen him around the hotel. Don't know why - he doesn't look the part.",
            Ask("What do you mean?", "l1_geno2"));
        Node(npc, "l1_geno2", "Well, this hotel is usually for a certain... type of people. People with money or big business. Geno didn't look like he had either.",
            Back());
        Node(npc, "l1_where", "After James rejected me, I holed up in my room the rest of the evening.",
            Ask("So how did you end up in the hotel lobby?", "l1_where2"));
        Node(npc, "l1_where2", "(sniffles harder) I needed something to help me sleep in my misery, so I came down here for a drink. But there was so much commotion, and the dead body-! (starts crying again)",
            Last("Uh, there, there."));
        Node(npc, "l1_work", "Work? Me? Are you serious? I could never!",
            Back());

        Present(npc, "Ugh, that stinks! Get it away from me!");

        Node(npc, "l1_tipoff", "Detective, that woman, with the coat? She was stalking behind you a second ago. It looks a little... creepy.",
            End("Thanks for letting me know."));
    }

    private static void James(NPCDialogue npc)
    {
        Intro(npc, "Be quick, detective. Some of us have actual work to do.");

        Node(npc, "l1_why", "Business meeting. Only the best and most serious meet at Hotel Eclipse.",
            Ask("Were you meeting about your new drone initiative?", "l1_why2"));
        Node(npc, "l1_why2", "That's none of your concern.",
            Back());
        Node(npc, "l1_geno", "Never heard of him.",
            Back());
        Node(npc, "l1_where", "Getting away from Helena and all her crying. God, it's an earsore.",
            Ask("You two don't seem to get along.", "l1_where2"));
        Node(npc, "l1_where2", "Our relationship is none of your business, detective.",
            Ask("It is when it's hindering a murder investigation.", "l1_where3"));
        Node(npc, "l1_where3", "(scowls) I wasn't anywhere near here at 10 pm. I was in the smoking room, taking a break. I only came down when I heard the screaming.",
            Back());
        Node(npc, "l1_work", "High-class technology and security. As you saw in the notes, I'm launching a new drone security program that can protect citizens from dangerous people.",
            Ask("What about from monsters?", "l1_work2"));
        Node(npc, "l1_work2", "I don't believe in children's ghost stories, detective. I only deal with real threats.",
            Back());

        Present(npc, "I've never seen anything like it.");
    }

    private static void Carmela(NPCDialogue npc)
    {
        Intro(npc, "(standoff-ish) I shouldn't be here.");

        Node(npc, "l1_why", "I was sent here to deliver something to a client.",
            Ask("What kind of something?", "l1_why2"));
        Node(npc, "l1_why2", "Medicine for inebriation and hangovers.",
            Ask("Is the client still here?", "l1_why3"));
        Node(npc, "l1_why3", "That's confidential, detective.",
            Back());
        Node(npc, "l1_geno", "...No.",
            Ask("You hesitated.", "l1_geno2", "She keeps looking at the elevator..."));
        Node(npc, "l1_geno2", "Because I was thinking, detective.",
            Back());
        Node(npc, "l1_where", "I wasn't even in the building! I was still on my way over here for delivery. By the time I got here, the sirens were blaring by the front door.",
            Ask("Then why, out of all people, did the police chief keep you here?", "l1_where2"));
        Node(npc, "l1_where2", "(getting angry) You're the detective. Go ask him.",
            Back());
        Node(npc, "l1_work", "I'm a travelling doctor. Patients call me with symptoms, and I deliver their medicines.",
            Ask("How do you know what's ailing them if you never see them first?", "l1_work2"));
        Node(npc, "l1_work2", "(narrows eyes) You think I'm a hack? I make sure to properly diagnose my patients both on the phone and in person. Do you need me to show you my medical license to prove it?",
            Last("No, not right now."));

        // No way on from her answer: showing her the scale is what counts, and the conversation just ends
        Present(npc, "Is that a scale? I don't know what that could be.", "");

        // Unlocked once the player has overheard her arguing with Yaroslava by the fireplace
        npc.eventDialogue.Add(new NPCDialogue.EventDialogue
        {
            unlockId = "l1_hiding",
            text = "Are you hiding something?",
            npcReply = "No, detective. I'm not. This woman is being creepy.",
            backText = "",
            choices = new List<NPCDialogue.Choice> { Ask("She claims you have something behind your back. Can I see it?", "l1_hide") }
        });

        Node(npc, "l1_hide", "(shows hands) I don't have anything behind my back. See?",
            Ask("What are you doing here again?", "l1_hide_why"),
            Ask("Have you ever seen the scale I showed you before?", "l1_hide_scale"),
            Back());
        Then(npc, "l1_hide_why", "I already told you - I'm delivering medicine to a patient.", "l1_hide_why2");
        Then(npc, "l1_hide_why2", "And where you getting those meds, you hack?", "l1_hide_why3", "Yaroslava");
        Node(npc, "l1_hide_why3", "(mad) YOU'RE the only hack here!",
            BackTo("l1_hide"));
        Then(npc, "l1_hide_scale", "Like I said, no. I'm not looking at fish like that.", "l1_hide_scale2");
        Then(npc, "l1_hide_scale2", "How'd you know it belongs to a fish?", "l1_hide_scale3", "Yaroslava");
        Node(npc, "l1_hide_scale3", "(mad) If you say one more word-!",
            Ask("What is it you're hiding?", "l1_hide_scale4"));
        Node(npc, "l1_hide_scale4", "I'm not hiding anything. If you want secrets, ask that old lady over there!",
            BackTo("l1_hide"));
    }

    private static void Zain(NPCDialogue npc)
    {
        Intro(npc, "(drunk) 'Suuup Maaaaan?");

        Node(npc, "l1_why", "Just getting that gooood stuff, man.",
            Ask("And how long have you been here getting the good stuff?", "l1_why2"));
        Node(npc, "l1_why2", "I don't know man... The sun was up when I walked in here. Is it gone now?",
            Ask("It's 11 pm at night.", "l1_why3"));
        Node(npc, "l1_why3", "(holds his head) Shiiiiiit.",
            Back());
        Node(npc, "l1_geno", "Whoooo?",
            Back());
        Node(npc, "l1_where", "Here, drinking. Same as I was when I first got here.",
            Ask("Did you see anything then?", "l1_where2"));
        Node(npc, "l1_where2", "One minute there was drinking, then next there was screaming. That body appeared out of nowhere man. If I wasn't so drunk, I would have puked.",
            Last("You'll probably puke anyway."));
        Node(npc, "l1_work", "(getting sad) It doesn't matter anymore...",
            Ask("Why not?", "l1_work2"));
        Node(npc, "l1_work2", "...Can I have another drink?",
            Back());

        npc.allAskedThought = "This one will be harder to tackle.";

        Present(npc, "Whooooaaa man, that's so cool! Wait, that color looks familiar...?",
            BackText, Ask("How so?", "l1_scale2"));
        Node(npc, "l1_scale2", "Ehhhh, must be a trick of the light.",
            Back());
    }

    private static void Murray(NPCDialogue npc)
    {
        Intro(npc, "Terrible business, all this.");

        Node(npc, "l1_why", "I'm here on a business venture. Some of my clients were in the area, and I was having meetings with them all day.",
            Ask("Are any of your clients here now?", "l1_why2"));
        Node(npc, "l1_why2", "No, but I do know James. Respectable, though quite full of himself.",
            Last("You're telling me."));
        Node(npc, "l1_geno", "I can't say that I have. Or what he does for a living.",
            Ask("Why would you mention that?", "l1_geno2"));
        Node(npc, "l1_geno2", "No reason. It just seems, well, odd that he would be in such a place like this. I heard he was a blacksmith?",
            Ask("He was a... craftsman of sorts.", "l1_geno3"));
        Node(npc, "l1_geno3", "Right, well, again, odd that he be here.",
            Back());
        Node(npc, "l1_where", "I was meeting with one of my clients by the pool deck on the fifth floor. That restaurant's caviar is amazing, you know.",
            Ask("So what led you down here?", "l1_where2"));
        Node(npc, "l1_where2", "I was seeing the client out after such a lovely evening, only for it to turn sour when we walked into a catastrophe.",
            Back());
        Node(npc, "l1_work", "Distribution. I move product and produce to the finest tables in the cities. Nobody gets their food from a better company than mine.",
            Ask("Is some of your product used in this hotel?", "l1_work2"));
        Node(npc, "l1_work2", "Why, indeed it is!",
            Back());

        Present(npc, "Why, what an interesting specimen! I've never seen a creature like that!",
            BackText, Ask("How do you know it belongs to a creature?", "l1_scale2"));
        Node(npc, "l1_scale2", "I don't! But what else could it belong to?",
            Back());
    }

    private static void Yaroslava(NPCDialogue npc)
    {
        Intro(npc, "What, you got nothing better to stare at?");

        Node(npc, "l1_why", "That's none of your business.",
            Ask("It is now, as the leading detective of a murder investigation.", "l1_why2"));
        Node(npc, "l1_why2", "I didn't do it, if that's what you're wondering.",
            Last("I most certainly am."));
        Node(npc, "l1_geno", "Oh yes, I know that crafty one. Good with his hands, good with tinkering with things.",
            Ask("How did you know him?", "l1_geno_how"),
            Ask("What type of things did he make?", "l1_geno_make"),
            Back());
        Node(npc, "l1_geno_how", "He would buy my good wares from time to time.",
            Ask("What good wares do you sell?", "l1_geno_wares"));
        Node(npc, "l1_geno_wares", "You ask too many questions.",
            BackTo("l1_geno"));
        Node(npc, "l1_geno_make", "Things only people in certain circles would know about.",
            Ask("Like magical items?", "l1_geno_make2"));
        Node(npc, "l1_geno_make2", "Hmph. So you know some things in that head of yours.",
            BackTo("l1_geno"));
        Node(npc, "l1_where", "Here and there.",
            Ask("Ma'am, you must give me a better answer than that.", "l1_where2"));
        Node(npc, "l1_where2", "I was nowhere near the body, sonny. Geno dying is bad for my business.",
            Ask("What business?", "l1_work"));
        Node(npc, "l1_work", "I sell things.",
            Ask("What kind of things?", "l1_work2"));
        Node(npc, "l1_work2", "You ask too many questions.",
            Back());

        // Ends once she's tried to keep the scale; showing it to her is what counts
        Present(npc, "(eyes go wide, she smiles) That's awfully valuable, sonny.",
            "", Ask("Valuable for what?", "l1_scale2"));
        Node(npc, "l1_scale2", "(muttering to herself) This seems like a prime ingredient...",
            Ask("For what?", "l1_scale3"));
        Node(npc, "l1_scale3", "Why don't I hold onto that for you?",
            End("Okay, we're done here."));

        // Unlocked once Carmela points the finger at her
        npc.eventDialogue.Add(new NPCDialogue.EventDialogue
        {
            unlockId = "l1_tricky",
            text = "Carmela says you're the one with secrets.",
            npcReply = "She thinks she's real tricky, that one. HMPF! What does she know about the real meaning of business..."
        });
    }

    // The greeting, with the four questions Salem asks everyone
    private static void Intro(NPCDialogue npc, string greeting)
    {
        Node(npc, StartNode, greeting,
            Ask("What are you doing here at Hotel Eclipse?", "l1_why"),
            Ask("Do you know the victim, Geno Jones?", "l1_geno"),
            Ask("Where were you at 10 pm tonight?", "l1_where"),
            Ask("What is your line of work?", "l1_work"));
    }

    private static void Present(NPCDialogue npc, string reply, string backText = BackText, params NPCDialogue.Choice[] choices)
    {
        npc.eventDialogue.Add(new NPCDialogue.EventDialogue
        {
            text = ScaleQuestion,
            npcReply = reply,
            backText = backText,
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

    // A line followed straight away by another, with nothing for Salem to say in between
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

    private static NPCDialogue.Choice Ask(string text, string next, string salemThinks = "") =>
        new NPCDialogue.Choice { text = text, nextNode = next, salemThinks = salemThinks };

    // Salem gets the last word, then it's back to the other questions
    private static NPCDialogue.Choice Last(string text) => new NPCDialogue.Choice { text = text, nextNode = StartNode };

    private static NPCDialogue.Choice End(string text) => new NPCDialogue.Choice { text = text, endConversation = true };

    private static NPCDialogue.Choice Back() => BackTo(StartNode);

    private static NPCDialogue.Choice BackTo(string node) =>
        new NPCDialogue.Choice { text = BackText, nextNode = node, greyAfterChosen = false };
}
#endif
