namespace Narrador.Application;

public sealed class ApplyNarrationToVideo(IVideoAudioComposer composer)
{
    public async Task ExecuteAsync(string videoPath, string audioPath, string outputPath, bool overwrite = false,
        IProgress<TimeSpan>? progress = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(videoPath) || !File.Exists(videoPath))
            throw new NarrationException("Selecciona un archivo de vídeo existente.");
        if (string.IsNullOrWhiteSpace(audioPath) || !File.Exists(audioPath))
            throw new NarrationException("Selecciona un archivo de narración existente o genera el audio antes de aplicar el montaje.");
        if (string.IsNullOrWhiteSpace(outputPath)
            || Path.GetExtension(outputPath).ToLowerInvariant() is not (".mp4" or ".mkv"))
            throw new NarrationException("Selecciona un archivo de salida .mp4 o .mkv.");
        var output = Path.GetFullPath(outputPath);
        if (string.Equals(output, Path.GetFullPath(videoPath), StringComparison.OrdinalIgnoreCase)
            || string.Equals(output, Path.GetFullPath(audioPath), StringComparison.OrdinalIgnoreCase))
            throw new NarrationException("Elige una salida diferente del vídeo y del audio originales.");
        if (File.Exists(output) && !overwrite)
            throw new NarrationException("El archivo de destino ya existe. Selecciona otro nombre o confirma su sustitución.");
        cancellationToken.ThrowIfCancellationRequested();
        await composer.ComposeAsync(Path.GetFullPath(videoPath), Path.GetFullPath(audioPath), output, overwrite, progress, cancellationToken);
    }
}
