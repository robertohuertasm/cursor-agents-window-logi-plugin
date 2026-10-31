namespace Loupedeck.CursorAgentsWindowPlugin;

using CursorAgents;

// Draws key faces: a status ring above the label row Options+ reserves.
internal static class Faces
{
    // `count` > 1 adds a badge with the number of agents at the ring's top right, so keys
    // that open a list look different from keys that open an agent.
    public static BitmapImage Ring(KeyState state, PluginImageSize size, int count = 1)
    {
        var (rgb, style) = KeyFace.For(state);
        var color = new BitmapColor(rgb.R, rgb.G, rgb.B);
        using var b = new BitmapBuilder(size);
        b.Clear(BitmapColor.Black);
        var cx = b.Width / 2f;
        var cy = b.Height * 0.4f;
        var r = Math.Min(b.Width, b.Height) * 0.26f;
        switch (style)
        {
            case RingStyle.Filled:
                b.FillCircle(cx, cy, r, color);
                break;
            case RingStyle.Outline:
                b.DrawArc((int)cx, (int)cy, (int)r, 0, 360, color, Math.Max(2, b.Width / 20f));
                break;
            default:
                for (var a = 0; a < 360; a += 30)
                {
                    b.DrawArc((int)cx, (int)cy, (int)r, a, 12, color, Math.Max(2, b.Width / 24f));
                }
                break;
        }

        if (count > 1)
        {
            var br = r * 0.55f;
            var bx = cx + r * 0.85f;
            var by = cy - r * 0.85f;
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
}
