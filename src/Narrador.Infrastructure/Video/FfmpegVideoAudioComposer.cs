using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using Narrador.Application;

namespace Narrador.Infrastructure.Video;

public sealed class FfmpegVideoAudioComposer(string? executablePath = null) : IVideoAudioComposer
{
    public async Task ComposeAsync(string videoPath, string audioPath, string outputPath, bool overwrite,
        IProgress<TimeSpan>? progress, CancellationToken cancellationToken)
    {
        var executable = executablePath ?? FindExecutable();
        if (executable is null || !File.Exists(executable))
            throw new NarrationException("No se encuentra FFmpeg. Utiliza la carpeta completa de la aplicación, incluida tools/ffmpeg, o instala FFmpeg en PATH.");
        var directory = Path.GetDirectoryName(outputPath)!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, ".narrador-video-" + Guid.NewGuid().ToString("N") + Path.GetExtension(outputPath));
        try
        {
            using var process = new Process { StartInfo = BuildStartInfo(executable, videoPath, audioPath, temporary) };
            cancellationToken.ThrowIfCancellationRequested();
            try { process.Start(); }
            catch (Win32Exception ex) { throw new NarrationException("No se pudo iniciar FFmpeg. Comprueba que la carpeta de la aplicación está completa.", ex); }
            var errorsTask = process.StandardError.ReadToEndAsync();
            var progressTask = ReadProgressAsync(process.StandardOutput, progress);
            using var registration = cancellationToken.Register(() => StopProcess(process));
            try { await process.WaitForExitAsync(cancellationToken); }
            catch (OperationCanceledException)
            {
                StopProcess(process);
                await process.WaitForExitAsync(CancellationToken.None);
                await Task.WhenAll(errorsTask, progressTask);
                throw;
            }
            await progressTask;
            var errors = await errorsTask;
            cancellationToken.ThrowIfCancellationRequested();
            if (process.ExitCode != 0)
            {
                var reason = errors.Contains("codec not currently supported in container", StringComparison.OrdinalIgnoreCase)
                    || errors.Contains("Could not find tag for codec", StringComparison.OrdinalIgnoreCase)
                    ? "El formato MP4 no admite el códec de este vídeo. Guarda el resultado como MKV."
                    : "No se pudo aplicar la narración. Comprueba que el vídeo y el audio se pueden reproducir y que hay espacio disponible.";
                throw new NarrationException(reason);
            }
            if (!File.Exists(temporary) || new FileInfo(temporary).Length == 0)
                throw new NarrationException("El montaje no ha producido un vídeo válido.");
            File.Move(temporary, outputPath, overwrite);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    internal static ProcessStartInfo BuildStartInfo(string executable, string videoPath, string audioPath, string outputPath)
    {
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var argument in new[]
        {
            "-hide_banner", "-loglevel", "error", "-nostdin", "-n",
            "-i", videoPath, "-i", audioPath,
            "-map", "0:v:0", "-map", "1:a:0", "-map_metadata", "0",
            "-c:v", "copy", "-c:a", "aac", "-b:a", "192k", "-af", "apad", "-shortest",
            "-progress", "pipe:1", "-nostats"
        }) info.ArgumentList.Add(argument);
        if (Path.GetExtension(outputPath).Equals(".mp4", StringComparison.OrdinalIgnoreCase))
        {
            info.ArgumentList.Add("-movflags");
            info.ArgumentList.Add("+faststart");
        }
        info.ArgumentList.Add(outputPath);
        return info;
    }

    private static async Task ReadProgressAsync(StreamReader reader, IProgress<TimeSpan>? progress)
    {
        while (await reader.ReadLineAsync() is { } line)
            if (line.StartsWith("out_time=", StringComparison.Ordinal)
                && TimeSpan.TryParse(line[9..], CultureInfo.InvariantCulture, out var time) && time >= TimeSpan.Zero)
                progress?.Report(time);
    }

    private static void StopProcess(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
        catch (Win32Exception) { }
    }

    private static string? FindExecutable()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg", "ffmpeg.exe"),
            Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe")
        }.Concat((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(directory => Path.Combine(directory.Trim('"'), "ffmpeg.exe")));
        return candidates.FirstOrDefault(File.Exists);
    }
}
