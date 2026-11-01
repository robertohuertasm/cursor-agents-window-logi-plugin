using System.Text;

namespace CursorAgents;

public readonly record struct Rgb(byte R, byte G, byte B);

// What a key looks like, independent of the Logi drawing API. Same colors as the VS Code plugin.
public static class KeyFace
{
    private const int MaxLabelChars = 12;

    // The Plugin Service shows the action's display name when the label is empty, so an
    // unused slot answers with a zero-width space.
    public const string Blank = "\u200B";

    // The disc color behind the black Cursor logo, as in the VS Code plugin.
    public static Rgb For(KeyState state) => state switch
    {
        KeyState.InputNeeded => new Rgb(230, 40, 40),
        KeyState.Working => new Rgb(240, 180, 0),
        KeyState.Unread => new Rgb(110, 160, 230),
        KeyState.Idle => new Rgb(0, 160, 64),
        KeyState.Offline => new Rgb(140, 50, 50),
        _ => new Rgb(45, 45, 45),
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
