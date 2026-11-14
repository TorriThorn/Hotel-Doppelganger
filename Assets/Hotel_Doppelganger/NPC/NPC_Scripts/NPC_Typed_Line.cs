using System.Collections.Generic;
using System.Text;
using UnityEngine;

// A line that types out letter by letter, with *asterisk* words highlighted. Untyped letters are drawn
// invisible so the layout never shifts. Shared by the dialogue box and the intro cutscene
public sealed class TypedLine
{
    private const string HiddenTag = "<color=#00000000>";
    private const string CloseTag = "</color>";

    private readonly List<char> letters = new List<char>();
    private readonly List<bool> highlighted = new List<bool>();
    private readonly StringBuilder builder = new StringBuilder();

    public int Length => letters.Count;

    public static string ColorTag(Color color) => "<color=#" + ColorUtility.ToHtmlStringRGBA(color) + ">";

    public void Set(string text)
    {
        letters.Clear();
        highlighted.Clear();
        if (string.IsNullOrEmpty(text)) return;

        bool highlight = false;
        foreach (char letter in text)
        {
            if (letter == '*')
            {
                highlight = !highlight;
                continue;
            }

            letters.Add(letter);
            highlighted.Add(highlight);
        }
    }

    // Only pauses at the end of a word, so times like 10:00 don't stutter
    public float PauseAfter(int index, float sentencePause, float commaPause)
    {
        bool endsWord = index + 1 >= letters.Count || char.IsWhiteSpace(letters[index + 1]);
        if (!endsWord) return 0f;

        switch (letters[index])
        {
            case '.': case '!': case '?': return sentencePause;
            case ',': case ';': case ':': return commaPause;
            default: return 0f;
        }
    }

    public string Render(int visibleLetters, string highlightTag)
    {
        builder.Clear();

        // 0 plain, 1 highlighted, 2 hidden
        int open = 0;
        for (int i = 0; i < letters.Count; i++)
        {
            int run = i >= visibleLetters ? 2 : highlighted[i] ? 1 : 0;
            if (run != open)
            {
                if (open != 0) builder.Append(CloseTag);
                if (run == 1) builder.Append(highlightTag);
                else if (run == 2) builder.Append(HiddenTag);
                open = run;
            }
            builder.Append(letters[i]);
        }

        if (open != 0) builder.Append(CloseTag);
        return builder.ToString();
    }
}