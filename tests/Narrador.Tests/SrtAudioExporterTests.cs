using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Narrador.Application;
using Narrador.Domain;
using Narrador.Infrastructure.Audio;
using Narrador.Infrastructure.Files;

namespace Narrador.Tests;

public sealed class SrtAudioExporterTests
{
    [Fact]
    public async Task FasterLocalTempoIsAppliedBeforeCheckingTheSubtitleWindow()
    {
        using var directory = new TestDirectory();
        var source = CreateTestMp3(directory.Path);
        var path = Path.Combine(directory.Path, "ajustado.wav");
        var cue = new SubtitleCue(1, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(1220), "Texto.");
        await using (var session = await new SrtAudioExporter().BeginAsync(path, false, CancellationToken.None))
        {
            await ExportAsync(session, [cue], source, tempo: 2.0 / 1.2);
        }
        using var reader = new WaveFileReader(path);
        var data = ReadAll(reader);
        Assert.Equal(Sample(cue.End) * 2, data.Length);
        Assert.All(data[..88200], value => Assert.Equal(0, value));
        Assert.Contains(data[88200..], value => value != 0);
    }

    [Fact]
    public async Task SlowLocalTempoIsMeasuredAfterProcessingAndExtendsTheSrtWindow()
    {
        using var directory = new TestDirectory();
        var source = CreateTestMp3(directory.Path);
        var path = Path.Combine(directory.Path, "demasiado-lento.wav");
        var cue = new SubtitleCue(8, TimeSpan.Zero, TimeSpan.FromMilliseconds(350), "Texto.");
        await using (var session = await new SrtAudioExporter().BeginAsync(path, false, CancellationToken.None))
        {
            var duration = await ExportAsync(session, [cue], source, tempo: 0.5 / 0.7);
            Assert.True(duration > cue.End);
        }
        Assert.True(File.Exists(path));
        Assert.Empty(Directory.GetDirectories(directory.Path, ".narrador-*"));
    }

    [Fact]
    public async Task WavHasExactAbsoluteStartsAndDigitalSilenceWithoutCumulativeDrift()
    {
        using var directory = new TestDirectory();
        var source = CreateTestMp3(directory.Path);
        var cues = Enumerable.Range(0, 5).Select(index => new SubtitleCue(index + 1,
            TimeSpan.FromMilliseconds(103 + index * 703), TimeSpan.FromMilliseconds(604 + index * 703), "Texto.")).ToArray();
        var path = Path.Combine(directory.Path, "sincronizado.wav");
        await using (var session = await new SrtAudioExporter().BeginAsync(path, false, CancellationToken.None))
        {
            var duration = await ExportAsync(session, cues, source);
            Assert.InRange(Math.Abs((duration - cues[^1].End).TotalMilliseconds), 0, 0.02);
        }
        using var reader = new WaveFileReader(path);
        Assert.Equal(44100, reader.WaveFormat.SampleRate);
        Assert.Equal(1, reader.WaveFormat.Channels);
        var data = ReadAll(reader);
        Assert.Equal(Sample(cues[^1].End) * 2, data.Length);
        Assert.All(data[..(Sample(cues[0].Start) * 2)], value => Assert.Equal(0, value));

        var reference = data[(Sample(cues[0].Start) * 2)..(Sample(cues[0].Start) * 2 + 18000)];
        Assert.Contains(reference, value => value != 0);
        foreach (var cue in cues)
        {
            var start = Sample(cue.Start) * 2;
            Assert.Equal(reference, data[start..(start + reference.Length)]);
            var silentTail = Sample(cue.Start + TimeSpan.FromMilliseconds(400)) * 2;
            Assert.All(data[silentTail..(Sample(cue.End) * 2)], value => Assert.Equal(0, value));
        }
        for (var index = 0; index + 1 < cues.Length; index++)
            Assert.All(data[(Sample(cues[index].End) * 2)..(Sample(cues[index + 1].Start) * 2)], value => Assert.Equal(0, value));
        Assert.Empty(Directory.GetDirectories(directory.Path, ".narrador-*"));
    }

    [Fact]
    public async Task AdjacentSubtitlesDoNotShiftEachOther()
    {
        using var directory = new TestDirectory();
        var source = CreateTestMp3(directory.Path);
        var path = Path.Combine(directory.Path, "contiguos.wav");
        await using (var session = await new SrtAudioExporter().BeginAsync(path, false, CancellationToken.None))
        {
            await ExportAsync(session,
                [new(1, TimeSpan.Zero, TimeSpan.FromMilliseconds(500), "Uno."),
                 new(2, TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(1), "Dos.")], source);
        }
        using var reader = new WaveFileReader(path);
        var data = ReadAll(reader);
        Assert.Equal(88200, data.Length);
        Assert.Equal(data[..44100], data[44100..]);
    }

    [Fact]
    public async Task LongCueKeepsAllAudioAndRecoversOriginalTimesAtTheNextAvailableSilence()
    {
        using var directory = new TestDirectory();
        var source = CreateTestMp3(directory.Path);
        var path = Path.Combine(directory.Path, "adaptado.wav");
        var srtPath = Path.Combine(directory.Path, "original.srt");
        const string srt = "1\n00:00:00,250 --> 00:00:00,300\nUno.\n\n2\n00:00:00,500 --> 00:00:01,000\nDos.\n\n3\n00:00:01,500 --> 00:00:02,000\nTres.";
        await File.WriteAllTextAsync(srtPath, srt);
        var loaded = await new LoadSrt(new TextFileReader(), new SrtParser()).ExecuteAsync(srtPath);
        var generate = new GenerateSynchronizedNarration([new FixedAudioProvider(source)], new SrtAudioExporter());
        var result = await generate.ExecuteAsync("test", loaded.Cues, "test-key", "voice", "model", new VoiceSettings(), path);

        Assert.NotNull(result.AdjustedCues);
        var first = result.AdjustedCues[0];
        var second = result.AdjustedCues[1];
        var third = result.AdjustedCues[2];
        var delay = first.End - loaded.Cues[0].End;
        Assert.True(delay > TimeSpan.Zero);
        Assert.Equal(first.End, second.Start);
        Assert.True(second.Start - loaded.Cues[1].Start < delay);
        Assert.Equal(loaded.Cues[1].End, second.End);
        Assert.Equal(loaded.Cues[2], third);
        Assert.Equal(TimeSpan.FromMilliseconds(300), loaded.Cues[0].End);
        Assert.Equal(srt, await File.ReadAllTextAsync(srtPath));

        using var reader = new WaveFileReader(path);
        var data = ReadAll(reader);
        var clipBytes = Sample(first.End - first.Start) * 2;
        var firstVoice = data[(Sample(first.Start) * 2)..(Sample(first.Start) * 2 + clipBytes)];
        var secondVoice = data[(Sample(second.Start) * 2)..(Sample(second.Start) * 2 + clipBytes)];
        Assert.Equal(firstVoice, secondVoice);
        Assert.Contains(firstVoice, value => value != 0);
        var thirdVoice = data[(Sample(third.Start) * 2)..(Sample(third.Start) * 2 + clipBytes)];
        Assert.Equal(firstVoice, thirdVoice);
        Assert.All(data[(Sample(second.End) * 2)..(Sample(third.Start) * 2)], value => Assert.Equal(0, value));
        Assert.Equal(Sample(third.End) * 2, data.Length);
        Assert.Empty(Directory.GetDirectories(directory.Path, ".narrador-*"));
    }

    [Fact]
    public async Task Mp3AlsoContainsLeadingAndInterventionSilences()
    {
        using var directory = new TestDirectory();
        var source = CreateTestMp3(directory.Path);
        var path = Path.Combine(directory.Path, "sincronizado.mp3");
        await using (var session = await new SrtAudioExporter().BeginAsync(path, false, CancellationToken.None))
        {
            await ExportAsync(session, [new(1, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), "Uno.")], source);
        }
        using var reader = new MediaFoundationReader(path);
        Assert.InRange(reader.TotalTime.TotalSeconds, 2, 2.1);
        var data = ReadAll(reader);
        // MP3 decoding may expose encoder padding; check regions well away from transitions.
        var bytesPerSecond = reader.WaveFormat.AverageBytesPerSecond;
        Assert.All(data[..(bytesPerSecond / 2)], value => Assert.Equal(0, value));
        Assert.Contains(data[bytesPerSecond..(bytesPerSecond + bytesPerSecond / 2)], value => value != 0);
    }

    [Fact]
    public async Task CancellingTimelineExportLeavesOldOutputUntouched()
    {
        using var directory = new TestDirectory();
        var source = CreateTestMp3(directory.Path);
        var path = Path.Combine(directory.Path, "existente.wav");
        await File.WriteAllTextAsync(path, "existing-audio");
        using var cancellation = new CancellationTokenSource();
        await using (var session = await new SrtAudioExporter().BeginAsync(path, true, CancellationToken.None))
        {
            var cue = new SubtitleCue(1, TimeSpan.Zero, TimeSpan.FromMilliseconds(50), "Uno.");
            var duration = await session.AppendAsync(cue, source, CancellationToken.None);
            var timeline = new AdaptiveSubtitleTimeline();
            timeline.Append(cue, duration);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.CommitAsync(timeline.Cues, cancellation.Token));
        }
        Assert.Equal("existing-audio", await File.ReadAllTextAsync(path));
        Assert.Empty(Directory.GetDirectories(directory.Path, ".narrador-*"));
    }

    private static int Sample(TimeSpan time) => (int)Math.Round(time.TotalSeconds * 44100, MidpointRounding.AwayFromZero);

    private static async Task<TimeSpan> ExportAsync(ITimedAudioExportSession session, IReadOnlyList<SubtitleCue> cues, byte[] source, double tempo = 1.0)
    {
        var timeline = new AdaptiveSubtitleTimeline();
        foreach (var cue in cues)
        {
            var duration = await session.AppendAsync(cue, source, CancellationToken.None, tempo);
            timeline.Append(cue, duration);
        }
        return await session.CommitAsync(timeline.Cues, CancellationToken.None);
    }

    private sealed class FixedAudioProvider(byte[] audio) : ISpeechSynthesizer
    {
        public string Id => "test";
        public int MaxCharacters => 10_000;
        public Task<byte[]> SynthesizeAsync(SpeechRequest request, CancellationToken cancellationToken) => Task.FromResult(audio);
    }

    private static byte[] ReadAll(WaveStream reader)
    {
        using var stream = new MemoryStream();
        reader.CopyTo(stream);
        return stream.ToArray();
    }

    private static byte[] CreateTestMp3(string directory)
    {
        var path = Path.Combine(directory, "source-" + Guid.NewGuid().ToString("N") + ".mp3");
        var signal = new SignalGenerator(44100, 1) { Gain = 0.1, Frequency = 440, Type = SignalGeneratorType.Sin };
        var shortSignal = new OffsetSampleProvider(signal) { Take = TimeSpan.FromMilliseconds(250) };
        MediaFoundationEncoder.EncodeToMp3(new SampleToWaveProvider16(shortSignal), path, 128000);
        return File.ReadAllBytes(path);
    }
}
