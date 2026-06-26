using System.Numerics;

namespace Spectralis.Core.Visualizers.Renderers;

/// <summary>Falling-note piano roll over an 88-key keyboard, driven by MIDI note state.</summary>
public sealed class PianoRollRenderer : VisualizerRendererBase
{
    private const int FirstKey = 21;
    private const int LastKey = 108;
    private const float ScrollAheadSeconds = 6.0f;
    private static readonly int[] BlackKeyPitchClasses = [1, 3, 6, 8, 10];

    public override void Draw(IVizCanvas canvas, VizRect bounds, VisualizerScene scene)
    {
        DrawBackground(canvas, bounds, scene);
        DrawGrid(canvas, bounds, scene);
        DrawRoll(canvas, bounds, scene);
        DrawKeyboard(canvas, bounds, scene);
        DrawHud(canvas, bounds, scene);
    }

    private static void DrawRoll(IVizCanvas canvas, VizRect bounds, VisualizerScene scene)
    {
        var keyboardBounds = GetKeyboardBounds(bounds);
        var rollBounds = new VizRect(
            bounds.Left + 18,
            bounds.Top + 58,
            bounds.Width - 36,
            Math.Max(40, keyboardBounds.Top - bounds.Top - 72));

        canvas.PushClipRect(rollBounds);
        try
        {
            foreach (var note in scene.MidiNotes)
            {
                if (note.Note < FirstKey || note.Note > LastKey)
                {
                    continue;
                }

                var keyCenter = GetKeyCenter(keyboardBounds, note.Note);
                var velocity = Math.Clamp(note.Velocity / 127f, 0.18f, 1f);
                var width = Math.Max(5f, GetWhiteKeyWidth(keyboardBounds) * (IsBlackKey(note.Note) ? 0.58f : 0.72f));
                var startY = GetScrollY(rollBounds, scene.PlaybackTimeSeconds, note.StartSeconds);
                var endY = GetScrollY(rollBounds, scene.PlaybackTimeSeconds, note.EndSeconds);
                var top = Math.Min(startY, endY);
                var bottom = Math.Max(startY, endY);
                var minimumHeight = Math.Max(8f, 18f * velocity);

                if (bottom - top < minimumHeight)
                {
                    bottom = top + minimumHeight;
                }

                if (bottom < rollBounds.Top || top > rollBounds.Bottom)
                {
                    continue;
                }

                var rect = new VizRect(keyCenter - (width / 2), top, width, bottom - top);
                var radius = Math.Max(4, width / 2);
                canvas.FillRoundedRect(rect, radius, scene.Theme.BarGlowColor.WithAlpha(40));
                canvas.FillRoundedRectGradientV(rect, radius, scene.Theme.BarStartColor, scene.Theme.BarEndColor);
            }
        }
        finally
        {
            canvas.Restore();
        }

        var octaveLineColor = scene.Theme.AmbientGridColor.WithAlpha(28);
        for (var octave = 24; octave <= LastKey; octave += 12)
        {
            var x = GetKeyCenter(keyboardBounds, octave);
            canvas.DrawLine(new Vector2(x, rollBounds.Top), new Vector2(x, keyboardBounds.Bottom), octaveLineColor, 1f);
        }
    }

    private static void DrawKeyboard(IVizCanvas canvas, VizRect bounds, VisualizerScene scene)
    {
        var keyboardBounds = GetKeyboardBounds(bounds);
        var whiteKeys = Enumerable.Range(FirstKey, LastKey - FirstKey + 1)
            .Where(static note => !IsBlackKey(note))
            .ToArray();
        var whiteKeyWidth = GetWhiteKeyWidth(keyboardBounds);
        var now = scene.PlaybackTimeSeconds;
        var activeNotes = new Dictionary<int, int>();
        foreach (var note in scene.MidiNotes.Where(note => note.StartSeconds <= now && now < note.EndSeconds))
        {
            activeNotes[note.Note] = Math.Max(activeNotes.GetValueOrDefault(note.Note), note.Velocity);
        }

        var whiteTop = scene.Theme.HudLabelColor.WithAlpha(248);
        var whiteBottom = scene.Theme.PlaceholderColor.WithAlpha(238);
        var borderColor = scene.Theme.BackgroundBottomColor.WithAlpha(80);

        for (var index = 0; index < whiteKeys.Length; index++)
        {
            var note = whiteKeys[index];
            var rect = new VizRect(
                keyboardBounds.Left + (index * whiteKeyWidth),
                keyboardBounds.Top,
                whiteKeyWidth + 0.7f,
                keyboardBounds.Height);
            if (activeNotes.ContainsKey(note))
            {
                canvas.FillRectGradientV(rect, scene.Theme.BarStartColor, scene.Theme.BarEndColor);
            }
            else
            {
                canvas.FillRectGradientV(rect, whiteTop, whiteBottom);
            }

            canvas.DrawRoundedRect(rect, 0, borderColor, 1f);
        }

        var blackTop = new VizColor(248, 18, 20, 24);
        var blackBottom = new VizColor(248, 5, 6, 9);
        foreach (var note in Enumerable.Range(FirstKey, LastKey - FirstKey + 1).Where(static note => IsBlackKey(note)))
        {
            var width = whiteKeyWidth * 0.58f;
            var height = keyboardBounds.Height * 0.62f;
            var center = GetKeyCenter(keyboardBounds, note);
            var rect = new VizRect(center - (width / 2), keyboardBounds.Top, width, height);

            if (activeNotes.ContainsKey(note))
            {
                canvas.FillRect(rect, scene.Theme.BarEndColor.WithAlpha(210));
            }
            else
            {
                canvas.FillRectGradientV(rect, blackTop, blackBottom);
            }

            canvas.DrawRoundedRect(rect, 0, borderColor, 1f);
        }

        DrawMidiCaption(canvas, bounds, scene);
    }

    private static void DrawMidiCaption(IVizCanvas canvas, VizRect bounds, VisualizerScene scene)
    {
        var text = scene.MidiInstrumentName is { Length: > 0 } instrument
            ? $"MIDI {instrument}"
            : "MIDI Piano";
        canvas.DrawText(
            text,
            new VizRect(bounds.Left + 18, bounds.Bottom - 28, bounds.Width - 36, 18),
            scene.Theme.HudInfoColor,
            12,
            VizTextAlign.Right);
    }

    private static VizRect GetKeyboardBounds(VizRect bounds)
    {
        var height = Math.Clamp(bounds.Height / 4, 58, 104);
        return new VizRect(bounds.Left + 18, bounds.Bottom - height - 22, bounds.Width - 36, height);
    }

    private static float GetWhiteKeyWidth(VizRect keyboardBounds) => keyboardBounds.Width / 52f;

    private static float GetScrollY(VizRect rollBounds, float currentSeconds, float noteSeconds)
    {
        var secondsUntilHit = noteSeconds - currentSeconds;
        return rollBounds.Bottom - ((secondsUntilHit / ScrollAheadSeconds) * rollBounds.Height);
    }

    private static float GetKeyCenter(VizRect keyboardBounds, int midiNote)
    {
        var whiteIndex = CountWhiteKeysBefore(midiNote);
        var whiteKeyWidth = GetWhiteKeyWidth(keyboardBounds);

        if (!IsBlackKey(midiNote))
        {
            return keyboardBounds.Left + (whiteIndex * whiteKeyWidth) + (whiteKeyWidth / 2);
        }

        return keyboardBounds.Left + (whiteIndex * whiteKeyWidth);
    }

    private static int CountWhiteKeysBefore(int midiNote)
    {
        var count = 0;
        for (var note = FirstKey; note <= midiNote; note++)
        {
            if (!IsBlackKey(note))
            {
                count++;
            }
        }

        return IsBlackKey(midiNote) ? count : count - 1;
    }

    private static bool IsBlackKey(int midiNote) =>
        BlackKeyPitchClasses.Contains(midiNote % 12);
}
