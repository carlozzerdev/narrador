using Narrador.Application;
using Narrador.Domain;

namespace Narrador.Tests;

public sealed class SynchronizedNarrationTests
{
    [Theory]
    [InlineData(0.5, 0.7)]
    [InlineData(1.15, 1.15)]
    [InlineData(2.0, 1.2)]
    public async Task RequestedSpeedIsSplitBetweenNativeSynthesisAndLocalTempo(double requested, double native)
    {
        var provider = new FakeProvider { LimitNativeSpeed = true };
        var exporter = new FakeExporter();
        await new GenerateSynchronizedNarration([provider], exporter).ExecuteAsync(
            "fake", Cues(), "test-key", "voice", "model", new VoiceSettings(speed: requested), "output.wav");
        Assert.All(provider.Requests, request => Assert.Equal(native, request.Settings.Speed));
        Assert.Equal(2, exporter.Session.Tempos.Count);
        Assert.All(exporter.Session.Tempos, tempo => Assert.Equal(requested / native, tempo, 10));
    }

    [Fact]
    public async Task GeneratesOneRequestPerSubtitleWithOriginalTimesAndContext()
    {
        var cues = Cues();
        var provider = new FakeProvider();
        var exporter = new FakeExporter();
        var result = await new GenerateSynchronizedNarration([provider], exporter).ExecuteAsync(
            "fake", cues, "test-key", "voice", "model", new VoiceSettings(speed: 1.15), "output.wav");
        Assert.Equal(cues.Select(cue => cue.Text), provider.Requests.Select(request => request.Text));
        Assert.All(provider.Requests, request => Assert.Equal(1.15, request.Settings.Speed));
        Assert.Equal(cues, exporter.Session.Cues);
        Assert.Null(provider.Requests[0].PreviousText);
        Assert.Equal(cues[1].Text, provider.Requests[0].NextText);
        Assert.Equal(cues[0].Text, provider.Requests[1].PreviousText);
        Assert.Null(provider.Requests[1].NextText);
        Assert.Equal(TimeSpan.FromSeconds(5), result.Duration);
        Assert.Equal(2, result.Chunks);
        Assert.True(exporter.Session.Committed);
        Assert.True(exporter.Session.Disposed);
    }

    [Fact]
    public async Task OverlapIsRejectedBeforeAnyBillableRequests()
    {
        var provider = new FakeProvider();
        var exporter = new FakeExporter();
        SubtitleCue[] cues = [new(1, TimeSpan.Zero, TimeSpan.FromSeconds(3), "Hola."),
            new(2, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), "Adiós.")];
        var ex = await Assert.ThrowsAsync<NarrationException>(() => new GenerateSynchronizedNarration([provider], exporter)
            .ExecuteAsync("fake", cues, "test-key", "voice", "model", new VoiceSettings(), "output.wav"));
        Assert.Contains("se solapan", ex.Message);
        Assert.Empty(provider.Requests);
        Assert.False(exporter.Opened);
    }

    [Fact]
    public async Task TooLongSubtitleUsesTheFollowingGapToRecoverOriginalTimes()
    {
        var cues = Cues();
        var provider = new FakeProvider();
        var exporter = new FakeExporter { Session = new FakeSession { AudioDurations = [TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(500)] } };
        var result = await new GenerateSynchronizedNarration([provider], exporter)
            .ExecuteAsync("fake", cues, "test-key", "voice", "model", new VoiceSettings(), "output.wav");
        Assert.Equal(2, provider.Requests.Count);
        Assert.True(exporter.Session.Committed);
        Assert.True(exporter.Session.Disposed);
        Assert.NotNull(result.AdjustedCues);
        Assert.Equal(TimeSpan.FromSeconds(1), result.AdjustedCues[0].Start);
        Assert.Equal(TimeSpan.FromSeconds(3), result.AdjustedCues[0].End);
        Assert.Equal(TimeSpan.FromSeconds(4), result.AdjustedCues[1].Start);
        Assert.Equal(TimeSpan.FromSeconds(5), result.AdjustedCues[1].End);
        Assert.Equal(TimeSpan.FromSeconds(5), result.Duration);
        Assert.Equal(TimeSpan.FromSeconds(2), cues[0].End);
        Assert.Equal(TimeSpan.FromSeconds(4), cues[1].Start);
    }

    [Fact]
    public async Task EachGenerationStartsFromOriginalTimesRatherThanAccumulatingPreviousDelays()
    {
        var cues = Cues();
        var generate = new GenerateSynchronizedNarration([new FakeProvider()], new ReusableExporter());
        var first = await generate.ExecuteAsync("fake", cues, "test-key", "voice", "model", new VoiceSettings(), "output.wav");
        var second = await generate.ExecuteAsync("fake", cues, "test-key", "voice", "model", new VoiceSettings(), "output.wav");
        Assert.Equal(first.AdjustedCues, second.AdjustedCues);
        Assert.Equal(TimeSpan.FromSeconds(5), second.AdjustedCues![1].End);
    }

    [Fact]
    public async Task OverlongTextIsRejectedBeforeAnyGeneration()
    {
        var provider = new FakeProvider();
        var exporter = new FakeExporter();
        SubtitleCue[] cues = [new(17, TimeSpan.Zero, TimeSpan.FromSeconds(3), new string('a', 101))];
        var ex = await Assert.ThrowsAsync<NarrationException>(() => new GenerateSynchronizedNarration([provider], exporter)
            .ExecuteAsync("fake", cues, "test-key", "voice", "model", new VoiceSettings(), "output.wav"));
        Assert.Contains("17", ex.Message);
        Assert.Empty(provider.Requests);
        Assert.False(exporter.Opened);
    }

    [Fact]
    public async Task CancellationDisposesTimelineWithoutPublishing()
    {
        using var cancellation = new CancellationTokenSource();
        var provider = new FakeProvider { AfterRequest = cancellation.Cancel };
        var exporter = new FakeExporter();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new GenerateSynchronizedNarration([provider], exporter)
            .ExecuteAsync("fake", Cues(), "test-key", "voice", "model", new VoiceSettings(), "output.wav", cancellationToken: cancellation.Token));
        Assert.False(exporter.Session.Committed);
        Assert.True(exporter.Session.Disposed);
    }

    private static SubtitleCue[] Cues() =>
    [
        new(1, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), "Primera intervención."),
        new(2, TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(5), "Segunda intervención.")
    ];

    private sealed class FakeProvider : ISpeechSynthesizer
    {
        public string Id => "fake";
        public int MaxCharacters => 100;
        public List<SpeechRequest> Requests { get; } = [];
        public Action? AfterRequest { get; init; }
        public bool LimitNativeSpeed { get; init; }
        public double GetNativeSpeed(double requestedSpeed) => LimitNativeSpeed ? Math.Clamp(requestedSpeed, 0.7, 1.2) : requestedSpeed;
        public Task<byte[]> SynthesizeAsync(SpeechRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            AfterRequest?.Invoke();
            return Task.FromResult<byte[]>([1, 2, 3]);
        }
    }

    private sealed class FakeExporter : ITimedAudioExporter
    {
        public FakeSession Session { get; init; } = new();
        public bool Opened { get; private set; }
        public Task<ITimedAudioExportSession> BeginAsync(string path, bool overwrite, CancellationToken cancellationToken)
        {
            Opened = true;
            return Task.FromResult<ITimedAudioExportSession>(Session);
        }
    }

    private sealed class FakeSession : ITimedAudioExportSession
    {
        public List<SubtitleCue> Cues { get; } = [];
        public List<double> Tempos { get; } = [];
        public bool Committed { get; private set; }
        public bool Disposed { get; private set; }
        public IReadOnlyList<TimeSpan>? AudioDurations { get; init; }
        public Task<TimeSpan> AppendAsync(SubtitleCue cue, byte[] mp3, CancellationToken cancellationToken, double tempo = 1.0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var duration = AudioDurations is null ? cue.End - cue.Start : AudioDurations[Cues.Count];
            Cues.Add(cue);
            Tempos.Add(tempo);
            return Task.FromResult(duration);
        }
        public Task<TimeSpan> CommitAsync(IReadOnlyList<SubtitleCue> timeline, CancellationToken cancellationToken)
        {
            Committed = true;
            return Task.FromResult(timeline[^1].End);
        }
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }

    private sealed class ReusableExporter : ITimedAudioExporter
    {
        public Task<ITimedAudioExportSession> BeginAsync(string path, bool overwrite, CancellationToken cancellationToken) =>
            Task.FromResult<ITimedAudioExportSession>(new FakeSession
            {
                AudioDurations = [TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(500)]
            });
    }
}
