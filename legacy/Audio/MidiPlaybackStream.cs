using System.Buffers;
using System.IO;
using System.Runtime.InteropServices;
using MeltySynth;
using NAudio.Wave;

namespace Spectralis;

public sealed record MidiNoteState(
    int Note,
    int Velocity,
    int Channel,
    float AgeSeconds,
    float StartSeconds = 0,
    float EndSeconds = 0);

internal sealed class MidiPlaybackStream : WaveStream
{
    private const int DefaultSampleRate = 44100;
    private const int ChannelCount = 2;
    private const int BytesPerSample = sizeof(float);
    private const int BytesPerFrame = ChannelCount * BytesPerSample;
    private const int PercussionChannel = 9;
    private const int RenderFrames = 2048;
    private const double PianoRollLookAheadSeconds = 6.0;

    private readonly MidiPlaybackInstrument instrument;
    private readonly WaveFormat waveFormat;
    private readonly FileStream renderedAudio;
    private readonly string renderedAudioPath;
    private readonly List<MidiNoteSpan> noteSpans = [];
    private readonly MidiPendingNote[,] pendingNotes = new MidiPendingNote[16, 128];
    private readonly object syncRoot = new();

    private long renderSampleCursor;

    public MidiPlaybackStream(string midiPath, string soundFontPath, MidiPlaybackInstrument instrument)
    {
        this.instrument = MidiPlaybackInstrumentCatalog.Normalize(instrument);
        waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(DefaultSampleRate, ChannelCount);
        renderedAudioPath = CreateTempAudioPath();

        try
        {
            RenderMidiToRawPcm(midiPath, soundFontPath);
            renderedAudio = new FileStream(
                renderedAudioPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 64 * 1024,
                FileOptions.RandomAccess);
        }
        catch
        {
            TryDeleteRenderedAudio();
            throw;
        }
    }

    public string InstrumentDisplayName => MidiPlaybackInstrumentCatalog.GetLabel(instrument);

    public override WaveFormat WaveFormat => waveFormat;

    public override long Length => renderedAudio.Length;

    public override long Position
    {
        get
        {
            lock (syncRoot)
                return renderedAudio.Position;
        }
        set
        {
            lock (syncRoot)
            {
                var alignedPosition = Math.Clamp(value, 0, renderedAudio.Length);
                alignedPosition -= alignedPosition % BytesPerFrame;
                renderedAudio.Position = alignedPosition;
            }
        }
    }

    public MidiNoteState[] GetActiveNotes()
    {
        lock (syncRoot)
        {
            var currentSample = renderedAudio.Position / BytesPerFrame;
            var lookAheadSample = currentSample + (long)(PianoRollLookAheadSeconds * DefaultSampleRate);
            return noteSpans
                .Where(span => span.EndSample >= currentSample && span.StartSample <= lookAheadSample)
                .Select(span => new MidiNoteState(
                    span.Note,
                    span.Velocity,
                    span.Channel,
                    (float)((currentSample - span.StartSample) / (double)DefaultSampleRate),
                    (float)(span.StartSample / (double)DefaultSampleRate),
                    (float)(span.EndSample / (double)DefaultSampleRate)))
                .OrderBy(static note => note.Note)
                .ThenBy(static note => note.Channel)
                .ToArray();
        }
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        lock (syncRoot)
        {
            var alignedCount = count - (count % BytesPerFrame);
            return alignedCount <= 0 ? 0 : renderedAudio.Read(buffer, offset, alignedCount);
        }
    }

    private void RenderMidiToRawPcm(string midiPath, string soundFontPath)
    {
        var forcedProgram = MidiPlaybackInstrumentCatalog.GetProgram(instrument);
        var midiFile = new MidiFile(midiPath);
        var settings = new SynthesizerSettings(DefaultSampleRate)
        {
            BlockSize = 64,
            MaximumPolyphony = 128,
            EnableReverbAndChorus = true
        };
        var synthesizer = new Synthesizer(soundFontPath, settings);
        var sequencer = new MidiFileSequencer(synthesizer)
        {
            OnSendMessage = (synth, channel, command, data1, data2) =>
            {
                var finalData1 = forcedProgram is { } program && channel != PercussionChannel && command == 0xC0
                    ? program
                    : data1;

                UpdateNoteTimeline(channel, command, data1, data2);
                synth.ProcessMidiMessage(channel, command, finalData1, data2);
            }
        };

        sequencer.Play(midiFile, loop: false);
        ApplyForcedProgram(synthesizer, forcedProgram);

        var totalSamples = Math.Max(1, (long)Math.Ceiling(midiFile.Length.TotalSeconds * DefaultSampleRate));
        var left = ArrayPool<float>.Shared.Rent(RenderFrames);
        var right = ArrayPool<float>.Shared.Rent(RenderFrames);
        var interleavedBytes = ArrayPool<byte>.Shared.Rent(RenderFrames * BytesPerFrame);

        try
        {
            using var output = new FileStream(
                renderedAudioPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 64 * 1024,
                FileOptions.SequentialScan);

            while (renderSampleCursor < totalSamples)
            {
                var frames = (int)Math.Min(RenderFrames, totalSamples - renderSampleCursor);
                sequencer.Render(left.AsSpan(0, frames), right.AsSpan(0, frames));
                WriteInterleaved(output, left, right, interleavedBytes, frames);
                renderSampleCursor += frames;
            }
        }
        finally
        {
            sequencer.Stop();
            ClosePendingNotes(Math.Max(1, totalSamples));
            ArrayPool<float>.Shared.Return(left);
            ArrayPool<float>.Shared.Return(right);
            ArrayPool<byte>.Shared.Return(interleavedBytes);
        }
    }

    private static void ApplyForcedProgram(Synthesizer synthesizer, int? forcedProgram)
    {
        if (forcedProgram is not { } program)
            return;

        for (var channel = 0; channel < 16; channel++)
        {
            if (channel != PercussionChannel)
                synthesizer.ProcessMidiMessage(channel, 0xC0, program, 0);
        }
    }

    private static void WriteInterleaved(
        Stream output,
        float[] left,
        float[] right,
        byte[] byteBuffer,
        int frames)
    {
        var destination = MemoryMarshal.Cast<byte, float>(byteBuffer.AsSpan(0, frames * BytesPerFrame));
        for (var frame = 0; frame < frames; frame++)
        {
            destination[frame * 2] = left[frame];
            destination[(frame * 2) + 1] = right[frame];
        }

        output.Write(byteBuffer, 0, frames * BytesPerFrame);
    }

    private void UpdateNoteTimeline(int channel, int command, int data1, int data2)
    {
        if (channel < 0 || channel >= 16)
            return;

        switch (command)
        {
            case 0x80:
                CloseNote(channel, data1);
                break;
            case 0x90 when data2 <= 0:
                CloseNote(channel, data1);
                break;
            case 0x90:
                OpenNote(channel, data1, data2);
                break;
            case 0xB0 when data1 is 120 or 121 or 123:
                CloseChannel(channel);
                break;
        }
    }

    private void OpenNote(int channel, int note, int velocity)
    {
        if (note < 0 || note >= 128)
            return;

        CloseNote(channel, note);
        pendingNotes[channel, note] = new MidiPendingNote(Math.Clamp(velocity, 0, 127), renderSampleCursor);
    }

    private void CloseNote(int channel, int note)
    {
        if (note < 0 || note >= 128)
            return;

        var pending = pendingNotes[channel, note];
        if (pending.Velocity <= 0)
            return;

        var endSample = Math.Max(pending.StartSample + 1, renderSampleCursor);
        if (channel != PercussionChannel)
            noteSpans.Add(new MidiNoteSpan(note, pending.Velocity, channel, pending.StartSample, endSample));

        pendingNotes[channel, note] = default;
    }

    private void CloseChannel(int channel)
    {
        for (var note = 0; note < 128; note++)
            CloseNote(channel, note);
    }

    private void ClosePendingNotes(long endSample)
    {
        for (var channel = 0; channel < 16; channel++)
        {
            for (var note = 0; note < 128; note++)
            {
                var pending = pendingNotes[channel, note];
                if (pending.Velocity <= 0)
                    continue;

                if (channel != PercussionChannel)
                    noteSpans.Add(new MidiNoteSpan(
                        note,
                        pending.Velocity,
                        channel,
                        pending.StartSample,
                        Math.Max(pending.StartSample + 1, endSample)));

                pendingNotes[channel, note] = default;
            }
        }
    }

    private static string CreateTempAudioPath()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Spectralis", "MidiCache");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $"{Guid.NewGuid():N}.raw");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            renderedAudio.Dispose();
            TryDeleteRenderedAudio();
        }

        base.Dispose(disposing);
    }

    private void TryDeleteRenderedAudio()
    {
        try { File.Delete(renderedAudioPath); } catch { }
    }

    private readonly record struct MidiPendingNote(int Velocity, long StartSample);

    private readonly record struct MidiNoteSpan(
        int Note,
        int Velocity,
        int Channel,
        long StartSample,
        long EndSample);
}
