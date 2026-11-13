using Narrador.Domain;

namespace Narrador.Application;

public sealed class GenerateSynchronizedNarration(
    IEnumerable<ISpeechSynthesizer> synthesizers, ITimedAudioExporter exporter)
{
    private readonly IReadOnlyDictionary<string, ISpeechSynthesizer> _providers =
        synthesizers.ToDictionary(provider => provider.Id, StringComparer.OrdinalIgnoreCase);

    public async Task<GeneratedNarration> ExecuteAsync(
        string providerId, IReadOnlyList<SubtitleCue> cues, string apiKey, string voiceId, string modelId,
        VoiceSettings settings, string outputPath, bool overwrite = false,
        IProgress<GenerationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (cues.Count == 0) throw new NarrationException("Carga un SRT con subtítulos antes de generar.");
        if (string.IsNullOrWhiteSpace(apiKey)) throw new NarrationException("Introduce la API key del proveedor.");
        if (string.IsNullOrWhiteSpace(voiceId)) throw new NarrationException("Introduce el ID de voz.");
        if (string.IsNullOrWhiteSpace(modelId)) throw new NarrationException("Selecciona un modelo.");
        var extension = Path.GetExtension(outputPath);
        if (string.IsNullOrWhiteSpace(outputPath)
            || (!extension.Equals(".mp3", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".wav", StringComparison.OrdinalIgnoreCase)))
            throw new NarrationException("Selecciona un archivo de salida .wav o .mp3.");
        if (!_providers.TryGetValue(providerId, out var provider))
            throw new NarrationException("El proveedor seleccionado no está disponible.");
        var nativeSpeed = provider.GetNativeSpeed(settings.Speed);
        var nativeSettings = new VoiceSettings(settings.Stability, settings.SimilarityBoost, nativeSpeed);
        var localTempo = settings.Speed / nativeSpeed;

        // Check the complete timeline before making any billable requests.
        for (var i = 0; i < cues.Count; i++)
        {
            if (i > 0 && cues[i].Start < cues[i - 1].End)
                throw new NarrationException($"Los subtítulos {cues[i - 1].Number} y {cues[i].Number} se solapan o no están ordenados. Revisa sus tiempos en el SRT.");
            if (cues[i].Text.Length > provider.MaxCharacters)
                throw new NarrationException($"El subtítulo {cues[i].Number} supera los {provider.MaxCharacters:N0} caracteres por petición. Divídelo en el SRT.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        await using var session = await exporter.BeginAsync(outputPath, overwrite, cancellationToken);
        var timeline = new AdaptiveSubtitleTimeline();
        for (var i = 0; i < cues.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cue = cues[i];
            progress?.Report(new(i, cues.Count, $"Generando subtítulo {cue.Number} ({i + 1} de {cues.Count})…"));
            var previous = i > 0 ? cues[i - 1].Text : null;
            var next = i + 1 < cues.Count ? cues[i + 1].Text : null;
            var request = new SpeechRequest(cue.Text, apiKey.Trim(), voiceId.Trim(), modelId.Trim(), nativeSettings,
                previous is null ? null : previous[^Math.Min(previous.Length, 1000)..],
                next is null ? null : next[..Math.Min(next.Length, 1000)]);
            var audio = await provider.SynthesizeAsync(request, cancellationToken);
            if (audio.Length == 0)
                throw new NarrationException($"El proveedor devolvió audio vacío para el subtítulo {cue.Number}.");
            var audioDuration = await session.AppendAsync(cue, audio, cancellationToken, localTempo);
            timeline.Append(cue, audioDuration);
        }
        progress?.Report(new(cues.Count, cues.Count, "Insertando silencios y exportando el audio sincronizado…"));
        var duration = await session.CommitAsync(timeline.Cues, cancellationToken);
        return new(outputPath, duration, cues.Sum(cue => cue.Text.Length), cues.Count, timeline.Cues);
    }
}
