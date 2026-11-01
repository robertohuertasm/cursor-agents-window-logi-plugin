namespace Loupedeck.CursorAgentsWindowPlugin;

using CursorAgents;

// Draws key faces above the label row Options+ reserves.
internal static class Faces
{
    // The black Cursor logo on a disc colored by state. The Plugin Service only lets a
    // plugin draw the image square above the label row, so a colored disc on black blends
    // in where a colored key background would show as a patch. `count` > 1 adds a badge
    // with the number of chats, so keys that open a chat list look different.
    public static BitmapImage State(KeyState state, PluginImageSize size, int count = 1)
    {
        var rgb = KeyFace.For(state);
        using var b = new BitmapBuilder(size);
        b.Clear(BitmapColor.Black);
        var cx = b.Width / 2f;
        // Lower than the VS Code plugin's 0.4, as the user preferred for Cursor's logo.
        var cy = b.Height * 0.5f;
        var r = Math.Min(b.Width, b.Height) * 0.3f;
        b.FillCircle(cx, cy, r, new BitmapColor(rgb.R, rgb.G, rgb.B));
        var side = (int)(Math.Min(b.Width, b.Height) * 0.44f);
        if (Codicon("cursor", new Rgb(0, 0, 0)) is { } image)
        {
            b.DrawImage(image, (int)(cx - side / 2f), (int)(cy - side / 2f), side, side, BitmapRotation.None);
        }

        if (count > 1)
        {
            var br = r * 0.4f;
            var bx = cx + r * 0.8f;
            var by = cy - r * 0.8f;
            b.FillCircle(bx, by, br + Math.Max(2, b.Width / 40f), BitmapColor.Black);
            b.FillCircle(bx, by, br, BitmapColor.White);
            var text = count > 9 ? "9+" : count.ToString();
            var box = (int)(br * 2);
            b.DrawText(text, (int)(bx - br), (int)(by - br), box, box, BitmapColor.Black, (int)(br * 1.3f), 0, 0, null);
        }

        return b.ToImage();
    }

    // The New key does the same thing either way: the link also starts Cursor when it is
    // closed. Only the label changes, so it is clear what a press will do.
    public static string NewLabel(bool online) => online ? "New" : "Start Cursor";

    public static BitmapImage NewGlyph(bool online, PluginImageSize size) => Glyph(online ? "+" : "▶", size);

    public static BitmapImage Glyph(string text, PluginImageSize size, BitmapColor? background = null)
    {
        using var b = new BitmapBuilder(size);
        b.Clear(background ?? BitmapColor.Black);
        b.DrawText(text, 0, 0, b.Width, (int)(b.Height * 0.8), BitmapColor.White, (int)(b.Height * 0.4), 0, 0, null);
        return b.ToImage();
    }

    // A white codicon, centered above the label row.
    public static BitmapImage Icon(string icon, PluginImageSize size)
    {
        using var b = new BitmapBuilder(size);
        b.Clear(BitmapColor.Black);
        var side = (int)(Math.Min(b.Width, b.Height) * 0.5f);
        if (Codicon(icon) is { } image)
        {
            b.DrawImage(image, (b.Width - side) / 2, (int)(b.Height * 0.4f) - side / 2, side, side, BitmapRotation.None);
        }
        return b.ToImage();
    }

    private static readonly Dictionary<(string, Rgb), BitmapImage?> Codicons = [];

    private static BitmapImage? Codicon(string name) => Codicon(name, new Rgb(255, 255, 255));

    private static BitmapImage? Codicon(string name, Rgb color)
    {
        lock (Codicons)
        {
            if (Codicons.TryGetValue((name, color), out var cached)) return cached;
            BitmapImage? image = null;
            using var stream = typeof(Faces).Assembly.GetManifestResourceStream($"Icons.{name}.svg");
            if (stream is not null)
            {
                // Icons draw with currentColor; the 16px size scales to any key. The Plugin
                // Service draws any single-color SVG white, so a second, invisible color keeps
                // the real one.
                var svg = new StreamReader(stream).ReadToEnd()
                    .Replace("currentColor", $"#{color.R:x2}{color.G:x2}{color.B:x2}")
                    .Replace("width=\"16\" height=\"16\"", "width=\"128\" height=\"128\"")
                    .Replace("</svg>", "<rect width=\"0.01\" height=\"0.01\" fill=\"#000001\"/></svg>");
                image = BitmapImage.FromSvg(svg);
            }
            Codicons[(name, color)] = image;
            return image;
        }
    }
}
