using Narrador.Application;
using Narrador.Domain;

namespace Narrador.Tests;

public sealed class GenerateNarrationTests
{
    [Fact]
    public async Task SplitsLongNarrationAndPassesContextInOrder()
    {
        var provider = new FakeSynthesizer();
        var exporter = new FakeExporter();
        var generate = new GenerateNarration([provider], new TextChunker(), exporter);
        var text = "Primera frase. Segunda frase. Tercera frase. Cuarta frase.";
        var result = await generate.ExecuteAsync("fake", text, "test-key", "voz", "modelo", new VoiceSettings(), "salida.mp3");
        Assert.True(provider.Requests.Count > 1);
        Assert.Equal(text, string.Join(" ", provider.Requests.Select(request => request.Text)));
        Assert.Null(provider.Requests[0].PreviousText);
        Assert.Equal(provider.Requests[1].Text, provider.Requests[0].NextText);
        Assert.Equal(provider.Requests[0].Text, provider.Requests[1].PreviousText);
        Assert.Equal(provider.Requests.Count, exporter.Session.Audio.Count);
        Assert.True(exporter.Session.Committed);
        Assert.True(exporter.Session.Disposed);
        Assert.Equal(TimeSpan.FromSeconds(3), result.Duration);
    }

    [Fact]
    public async Task CancellationDisposesSessionAndNeverCommits()
    {
        using var cancellation = new CancellationTokenSource();
        var provider = new FakeSynthesizer { AfterRequest = cancellation.Cancel };
        var exporter = new FakeExporter();
        var generate = new GenerateNarration([provider], new TextChunker(), exporter);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => generate.ExecuteAsync("fake",
            "Primera frase. Segunda frase. Tercera frase.", "test-key", "voz", "modelo", new VoiceSettings(), "salida.mp3",
            cancellationToken: cancellation.Token));
        Assert.False(exporter.Session.Committed);
        Assert.True(exporter.Session.Disposed);
    }

    [Fact]
    public async Task MissingKeyDoesNotOpenOutputOrCallProvider()
    {
        var provider = new FakeSynthesizer();
        var exporter = new FakeExporter();
        await Assert.ThrowsAsync<NarrationException>(() => new GenerateNarration([provider], new TextChunker(), exporter)
            .ExecuteAsync("fake", "Texto", "", "voz", "modelo", new VoiceSettings(), "salida.mp3"));
        Assert.Empty(provider.Requests);
        Assert.False(exporter.Opened);
    }

    private sealed class FakeSynthesizer : ISpeechSynthesizer
    {
        public string Id => "fake";
        public int MaxCharacters => 25;
        public List<SpeechRequest> Requests { get; } = [];
        public Action? AfterRequest { get; init; }
        public Task<byte[]> SynthesizeAsync(SpeechRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            AfterRequest?.Invoke();
            return Task.FromResult<byte[]>([1, 2, 3]);
        }
    }
    private sealed class FakeExporter : IAudioExporter
    {
        public FakeSession Session { get; } = new();
        public bool Opened { get; private set; }
        public Task<IAudioExportSession> BeginAsync(string path, bool overwrite, CancellationToken cancellationToken)
        {
            Opened = true;
            return Task.FromResult<IAudioExportSession>(Session);
        }
    }
    private sealed class FakeSession : IAudioExportSession
    {
        public List<byte[]> Audio { get; } = [];
        public bool Committed { get; private set; }
        public bool Disposed { get; private set; }
        public Task AppendAsync(byte[] audio, CancellationToken cancellationToken, double tempo = 1.0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Audio.Add(audio);
            return Task.CompletedTask;
        }
        public Task<TimeSpan> CommitAsync(CancellationToken cancellationToken)
        {
            Committed = true;
            return Task.FromResult(TimeSpan.FromSeconds(3));
        }
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
}
