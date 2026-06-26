using System.Drawing;
using System.Drawing.Drawing2D;

namespace Spectralis;

internal sealed class PianoRollVisualizerRenderer : VisualizerRendererBase
{
    private const int FirstKey = 21;
    private const int LastKey = 108;
    private const float ScrollAheadSeconds = 6.0f;
    private static readonly int[] BlackKeyPitchClasses = [1, 3, 6, 8, 10];

    public override void Draw(Graphics graphics, Rectangle bounds, VisualizerScene scene)
    {
        DrawBackground(graphics, bounds, scene);
        DrawGrid(graphics, bounds, scene);
        DrawRoll(graphics, bounds, scene);
        DrawKeyboard(graphics, bounds, scene);
        DrawHud(graphics, bounds, scene);
    }

    private static void DrawRoll(Graphics graphics, Rectangle bounds, VisualizerScene scene)
    {
        var keyboardBounds = GetKeyboardBounds(bounds);
        var rollBounds = new Rectangle(
            bounds.Left + 18,
            bounds.Top + 58,
            bounds.Width - 36,
            Math.Max(40, keyboardBounds.Top - bounds.Top - 72));

        using var glowBrush = new SolidBrush(Color.FromArgb(40, scene.Theme.BarGlowColor));
        using var noteBrush = new LinearGradientBrush(
            rollBounds,
            scene.Theme.BarStartColor,
            scene.Theme.BarEndColor,
            LinearGradientMode.Vertical);
        using var clipRegion = new Region(rollBounds);
        var previousClip = graphics.Clip;
        graphics.Clip = clipRegion;

        try
        {
            foreach (var note in scene.MidiNotes)
            {
                if (note.Note < FirstKey || note.Note > LastKey)
                    continue;

                var keyCenter = GetKeyCenter(keyboardBounds, note.Note);
                var velocity = Math.Clamp(note.Velocity / 127f, 0.18f, 1f);
                var width = Math.Max(5f, GetWhiteKeyWidth(keyboardBounds) * (IsBlackKey(note.Note) ? 0.58f : 0.72f));
                var startY = GetScrollY(rollBounds, scene.PlaybackTimeSeconds, note.StartSeconds);
                var endY = GetScrollY(rollBounds, scene.PlaybackTimeSeconds, note.EndSeconds);
                var top = Math.Min(startY, endY);
                var bottom = Math.Max(startY, endY);
                var minimumHeight = Math.Max(8f, 18f * velocity);

                if (bottom - top < minimumHeight)
                    bottom = top + minimumHeight;

                if (bottom < rollBounds.Top || top > rollBounds.Bottom)
                    continue;

                var rect = new RectangleF(keyCenter - (width / 2), top, width, bottom - top);

                using var path = CreateRoundedRectangle(Rectangle.Round(rect), Math.Max(4, (int)(width / 2)));
                graphics.FillPath(glowBrush, path);
                graphics.FillPath(noteBrush, path);
            }
        }
        finally
        {
            graphics.Clip = previousClip;
            previousClip.Dispose();
        }

        using var linePen = new Pen(Color.FromArgb(28, scene.Theme.AmbientGridColor));
        for (var octave = 24; octave <= LastKey; octave += 12)
        {
            var x = GetKeyCenter(keyboardBounds, octave);
            graphics.DrawLine(linePen, x, rollBounds.Top, x, keyboardBounds.Bottom);
        }
    }

    private static void DrawKeyboard(Graphics graphics, Rectangle bounds, VisualizerScene scene)
    {
        var keyboardBounds = GetKeyboardBounds(bounds);
        var whiteKeys = Enumerable.Range(FirstKey, LastKey - FirstKey + 1)
            .Where(static note => !IsBlackKey(note))
            .ToArray();
        var whiteKeyWidth = GetWhiteKeyWidth(keyboardBounds);
        var now = scene.PlaybackTimeSeconds;
        var activeNotes = new Dictionary<int, int>();
        foreach (var note in scene.MidiNotes.Where(note => note.StartSeconds <= now && now < note.EndSeconds))
            activeNotes[note.Note] = Math.Max(activeNotes.GetValueOrDefault(note.Note), note.Velocity);

        using var whiteBrush = new LinearGradientBrush(
            keyboardBounds,
            Color.FromArgb(248, scene.Theme.HudLabelColor),
            Color.FromArgb(238, scene.Theme.PlaceholderColor),
            LinearGradientMode.Vertical);
        using var activeBrush = new LinearGradientBrush(
            keyboardBounds,
            scene.Theme.BarStartColor,
            scene.Theme.BarEndColor,
            LinearGradientMode.Vertical);
        using var borderPen = new Pen(Color.FromArgb(80, scene.Theme.BackgroundBottomColor));

        for (var index = 0; index < whiteKeys.Length; index++)
        {
            var note = whiteKeys[index];
            var rect = new RectangleF(
                keyboardBounds.Left + (index * whiteKeyWidth),
                keyboardBounds.Top,
                whiteKeyWidth + 0.7f,
                keyboardBounds.Height);
            var brush = activeNotes.ContainsKey(note) ? activeBrush : whiteBrush;
            graphics.FillRectangle(brush, rect);
            graphics.DrawRectangle(borderPen, Rectangle.Round(rect));
        }

        using var blackBrush = new LinearGradientBrush(
            keyboardBounds,
            Color.FromArgb(248, 18, 20, 24),
            Color.FromArgb(248, 5, 6, 9),
            LinearGradientMode.Vertical);

        foreach (var note in Enumerable.Range(FirstKey, LastKey - FirstKey + 1).Where(static note => IsBlackKey(note)))
        {
            var width = whiteKeyWidth * 0.58f;
            var height = keyboardBounds.Height * 0.62f;
            var center = GetKeyCenter(keyboardBounds, note);
            var rect = new RectangleF(center - (width / 2), keyboardBounds.Top, width, height);

            if (activeNotes.TryGetValue(note, out _))
            {
                using var glow = new SolidBrush(Color.FromArgb(210, scene.Theme.BarEndColor));
                graphics.FillRectangle(glow, rect);
            }
            else
            {
                graphics.FillRectangle(blackBrush, rect);
            }

            graphics.DrawRectangle(borderPen, Rectangle.Round(rect));
        }

        DrawMidiCaption(graphics, bounds, scene);
    }

    private static void DrawMidiCaption(Graphics graphics, Rectangle bounds, VisualizerScene scene)
    {
        var text = scene.MidiInstrumentName is { Length: > 0 } instrument
            ? $"MIDI {instrument}"
            : "MIDI Piano";
        var captionBounds = new Rectangle(bounds.Left + 18, bounds.Bottom - 28, bounds.Width - 36, 18);

        using var infoBrush = new SolidBrush(scene.Theme.HudInfoColor);
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Far,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap
        };
        graphics.DrawString(text, scene.Font, infoBrush, captionBounds, format);
    }

    private static Rectangle GetKeyboardBounds(Rectangle bounds)
    {
        var height = Math.Clamp(bounds.Height / 4, 58, 104);
        return new Rectangle(bounds.Left + 18, bounds.Bottom - height - 22, bounds.Width - 36, height);
    }

    private static float GetWhiteKeyWidth(Rectangle keyboardBounds) => keyboardBounds.Width / 52f;

    private static float GetScrollY(Rectangle rollBounds, float currentSeconds, float noteSeconds)
    {
        var secondsUntilHit = noteSeconds - currentSeconds;
        return rollBounds.Bottom - ((secondsUntilHit / ScrollAheadSeconds) * rollBounds.Height);
    }

    private static float GetKeyCenter(Rectangle keyboardBounds, int midiNote)
    {
        var whiteIndex = CountWhiteKeysBefore(midiNote);
        var whiteKeyWidth = GetWhiteKeyWidth(keyboardBounds);

        if (!IsBlackKey(midiNote))
            return keyboardBounds.Left + (whiteIndex * whiteKeyWidth) + (whiteKeyWidth / 2);

        return keyboardBounds.Left + (whiteIndex * whiteKeyWidth);
    }

    private static int CountWhiteKeysBefore(int midiNote)
    {
        var count = 0;
        for (var note = FirstKey; note <= midiNote; note++)
        {
            if (!IsBlackKey(note))
                count++;
        }

        return IsBlackKey(midiNote) ? count : count - 1;
    }

    private static bool IsBlackKey(int midiNote) =>
        BlackKeyPitchClasses.Contains(midiNote % 12);
}
