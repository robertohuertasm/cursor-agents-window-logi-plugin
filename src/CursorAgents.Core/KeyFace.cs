using System.Text;

namespace CursorAgents;

public enum RingStyle
{
    Filled,
    Outline,
    Dotted,
}

public readonly record struct Rgb(byte R, byte G, byte B);

// What a key looks like, independent of the Logi drawing API. Same colors as the VS Code plugin.
public static class KeyFace
{
    private const int MaxLabelChars = 12;

    // The Plugin Service shows the action's display name when the label is empty, so an
    // unused slot answers with a zero-width space.
    public const string Blank = "\u200B";

    public static (Rgb Color, RingStyle Style) For(KeyState state) => state switch
    {
        KeyState.InputNeeded => (new Rgb(230, 40, 40), RingStyle.Filled),
        KeyState.Working => (new Rgb(240, 180, 0), RingStyle.Filled),
        KeyState.Unread => (new Rgb(110, 160, 230), RingStyle.Filled),
        KeyState.Idle => (new Rgb(0, 160, 64), RingStyle.Outline),
        KeyState.Offline => (new Rgb(140, 50, 50), RingStyle.Dotted),
        _ => (new Rgb(70, 70, 70), RingStyle.Dotted),
    };

    // Truncates by code point so emoji survive.
    public static string Label(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Blank;
        var runes = text.Trim().EnumerateRunes().ToList();
        if (runes.Count <= MaxLabelChars) return text.Trim();
        var builder = new StringBuilder();
        foreach (var rune in runes.Take(MaxLabelChars - 1)) builder.Append(rune.ToString());
        return builder.Append('…').ToString();
    }
}
