using Narrador.Domain;

namespace Narrador.Application;

public interface ITextFileReader
{
    Task<string> ReadAsync(string path, CancellationToken cancellationToken);
}

public interface ISpeechSynthesizer
{
    string Id { get; }
    int MaxCharacters { get; }
    // The remaining ratio is applied locally by the audio exporter.
    double GetNativeSpeed(double requestedSpeed) => requestedSpeed;
    Task<byte[]> SynthesizeAsync(SpeechRequest request, CancellationToken cancellationToken);
}

public interface IAudioExporter
{
    Task<IAudioExportSession> BeginAsync(string outputPath, bool overwrite, CancellationToken cancellationToken);
}

public interface IAudioExportSession : IAsyncDisposable
{
    Task AppendAsync(byte[] mp3, CancellationToken cancellationToken, double tempo = 1.0);
    Task<TimeSpan> CommitAsync(CancellationToken cancellationToken);
}

public interface ITimedAudioExporter
{
    Task<ITimedAudioExportSession> BeginAsync(string outputPath, bool overwrite, CancellationToken cancellationToken);
}

public interface ITimedAudioExportSession : IAsyncDisposable
{
    Task<TimeSpan> AppendAsync(SubtitleCue cue, byte[] mp3, CancellationToken cancellationToken, double tempo = 1.0);
    Task<TimeSpan> CommitAsync(IReadOnlyList<SubtitleCue> timeline, CancellationToken cancellationToken);
}

public interface ISettingsStore
{
    AppSettings Load();
    void Save(AppSettings settings);
}

public interface ISecretStore
{
    string? ReadApiKey(string providerId);
    void SaveApiKey(string providerId, string? apiKey);
}

// A class avoids the generated record ToString() exposing the API key.
public sealed class SpeechRequest(
    string text, string apiKey, string voiceId, string modelId, VoiceSettings settings,
    string? previousText = null, string? nextText = null)
{
    public string Text { get; } = text;
    public string ApiKey { get; } = apiKey;
    public string VoiceId { get; } = voiceId;
    public string ModelId { get; } = modelId;
    public VoiceSettings Settings { get; } = settings;
    public string? PreviousText { get; } = previousText;
    public string? NextText { get; } = nextText;
}

public sealed record AppSettings(
    string VoiceId = "JBFqnCBsd6RMkjVDRZzb",
    string ModelId = "eleven_multilingual_v2",
    double Stability = 0.5,
    double SimilarityBoost = 0.75,
    string? OutputDirectory = null,
    bool RememberApiKey = false,
    double Speed = 1.0);

public sealed record LoadedNarration(IReadOnlyList<SubtitleCue> Cues, string Text);
public sealed record GenerationProgress(int CompletedChunks, int TotalChunks, string Message);
public sealed record GeneratedNarration(string FilePath, TimeSpan Duration, int Characters, int Chunks,
    IReadOnlyList<SubtitleCue>? AdjustedCues = null);

public class NarrationException(string message, Exception? innerException = null)
    : Exception(message, innerException);

