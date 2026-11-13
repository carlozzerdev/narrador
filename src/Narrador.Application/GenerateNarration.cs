using Narrador.Domain;

namespace Narrador.Application;

public sealed class GenerateNarration(
    IEnumerable<ISpeechSynthesizer> synthesizers, TextChunker chunker, IAudioExporter exporter)
{
    private readonly IReadOnlyDictionary<string, ISpeechSynthesizer> _providers =
        synthesizers.ToDictionary(provider => provider.Id, StringComparer.OrdinalIgnoreCase);

    public async Task<GeneratedNarration> ExecuteAsync(
        string providerId, string text, string apiKey, string voiceId, string modelId,
        VoiceSettings settings, string outputPath, bool overwrite = false,
        IProgress<GenerationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new NarrationException("Carga un SRT con texto antes de generar.");
        if (string.IsNullOrWhiteSpace(apiKey)) throw new NarrationException("Introduce la API key del proveedor.");
        if (string.IsNullOrWhiteSpace(voiceId)) throw new NarrationException("Introduce el ID de voz.");
        if (string.IsNullOrWhiteSpace(modelId)) throw new NarrationException("Selecciona un modelo.");
        if (string.IsNullOrWhiteSpace(outputPath) || !outputPath.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase))
            throw new NarrationException("Selecciona un archivo de salida con extensión .mp3.");
        if (!_providers.TryGetValue(providerId, out var provider))
            throw new NarrationException("El proveedor seleccionado no está disponible.");
        var nativeSpeed = provider.GetNativeSpeed(settings.Speed);
        var nativeSettings = new VoiceSettings(settings.Stability, settings.SimilarityBoost, nativeSpeed);
        var localTempo = settings.Speed / nativeSpeed;

        var chunks = chunker.Split(text, provider.MaxCharacters);
        cancellationToken.ThrowIfCancellationRequested();
        await using var session = await exporter.BeginAsync(outputPath, overwrite, cancellationToken);
        for (var i = 0; i < chunks.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new(i, chunks.Count, $"Generando fragmento {i + 1} de {chunks.Count}…"));
            var previous = i > 0 ? chunks[i - 1] : null;
            var next = i + 1 < chunks.Count ? chunks[i + 1] : null;
            var request = new SpeechRequest(chunks[i], apiKey.Trim(), voiceId.Trim(), modelId.Trim(), nativeSettings,
                previous is null ? null : previous[^Math.Min(previous.Length, 1000)..],
                next is null ? null : next[..Math.Min(next.Length, 1000)]);
            var audio = await provider.SynthesizeAsync(request, cancellationToken);
            if (audio.Length == 0) throw new NarrationException("El proveedor devolvió un audio vacío.");
            await session.AppendAsync(audio, cancellationToken, localTempo);
        }
        progress?.Report(new(chunks.Count, chunks.Count, "Preparando el archivo MP3…"));
        var duration = await session.CommitAsync(cancellationToken);
        return new(outputPath, duration, text.Length, chunks.Count);
    }
}
