using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using NAudio.Wave;
using Narrador.Application;
using Narrador.Infrastructure.Video;

namespace Narrador.Tests;

public sealed class VideoCompositionTests
{
    [Theory]
    [InlineData(".mp4", 0.5)]
    [InlineData(".mp4", 3.0)]
    [InlineData(".mkv", 0.5)]
    public async Task ReplacesOriginalSoundPreservesVideoAndMatchesItsDuration(string extension, double audioSeconds)
    {
        using var directory = new TestDirectory();
        var folder = Path.Combine(directory.Path, "vídeos con espacios");
        Directory.CreateDirectory(folder);
        var (video, audio) = await CreateMediaAsync(folder, audioSeconds);
        var original = await File.ReadAllBytesAsync(video);
        var output = Path.Combine(folder, "resultado" + extension);
        var times = new List<TimeSpan>();
        await new ApplyNarrationToVideo(new FfmpegVideoAudioComposer(Ffmpeg)).ExecuteAsync(video, audio, output,
            progress: new InlineProgress<TimeSpan>(times.Add));
        Assert.Equal(original, await File.ReadAllBytesAsync(video));
        using var info = JsonDocument.Parse(await RunAsync(Ffprobe, "-v", "error", "-show_streams", "-show_format", "-of", "json", output));
        var streams = info.RootElement.GetProperty("streams").EnumerateArray().ToArray();
        Assert.Single(streams, stream => stream.GetProperty("codec_type").GetString() == "video");
        Assert.Single(streams, stream => stream.GetProperty("codec_type").GetString() == "audio");
        var duration = double.Parse(info.RootElement.GetProperty("format").GetProperty("duration").GetString()!, CultureInfo.InvariantCulture);
        Assert.InRange(duration, 1.9, 2.15);
        Assert.Equal(await VideoHashAsync(video), await VideoHashAsync(output));
        Assert.NotEmpty(times);
        Assert.Empty(Directory.GetFiles(folder, ".narrador-video-*"));

        var decoded = Path.Combine(folder, "decoded.wav");
        await RunAsync(Ffmpeg, "-v", "error", "-i", output, "-map", "0:a:0", "-ac", "1", "-ar", "44100", "-c:a", "pcm_s16le", decoded);
        using var reader = new WaveFileReader(decoded);
        var samples = new float[(int)reader.SampleCount];
        var count = reader.ToSampleProvider().Read(samples.AsSpan());
        Assert.True(count > 44100);
        var start = 4410;
        var length = 8820;
        var crossings = Enumerable.Range(start + 1, length - 1).Count(index => samples[index - 1] < 0 && samples[index] >= 0);
        Assert.InRange(crossings / (length / 44100.0), 850, 910);
        if (audioSeconds < 2)
        {
            var tail = samples.Skip(66150).Take(count - 66150).ToArray();
            Assert.NotEmpty(tail);
            Assert.InRange(Math.Sqrt(tail.Average(sample => (double)sample * sample)), 0, 0.002);
        }
    }

    [Fact]
    public async Task CanAddNarrationToASilentVideo()
    {
        using var directory = new TestDirectory();
        var (video, audio) = await CreateMediaAsync(directory.Path, 1);
        var silent = Path.Combine(directory.Path, "silent.mp4");
        await RunAsync(Ffmpeg, "-v", "error", "-i", video, "-an", "-c:v", "copy", silent);
        var output = Path.Combine(directory.Path, "narrated.mp4");
        await new ApplyNarrationToVideo(new FfmpegVideoAudioComposer(Ffmpeg)).ExecuteAsync(silent, audio, output);
        using var info = JsonDocument.Parse(await RunAsync(Ffprobe, "-v", "error", "-select_streams", "a", "-show_entries", "stream=codec_name", "-of", "json", output));
        Assert.Single(info.RootElement.GetProperty("streams").EnumerateArray());
    }

    [Fact]
    public async Task CancellationLeavesExistingDestinationAndInputsIntactAndRemovesTemporaryVideo()
    {
        using var directory = new TestDirectory();
        var (video, audio) = await CreateMediaAsync(directory.Path, 3);
        var output = Path.Combine(directory.Path, "existing.mp4");
        await File.WriteAllTextAsync(output, "previous-video");
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new ApplyNarrationToVideo(new FfmpegVideoAudioComposer(Ffmpeg)).ExecuteAsync(video, audio, output, overwrite: true,
                progress: new InlineProgress<TimeSpan>(_ => cancellation.Cancel()), cancellationToken: cancellation.Token));
        Assert.Equal("previous-video", await File.ReadAllTextAsync(output));
        Assert.True(File.Exists(video));
        Assert.True(File.Exists(audio));
        Assert.Empty(Directory.GetFiles(directory.Path, ".narrador-video-*"));
    }

    [Fact]
    public async Task InvalidVideoDoesNotReplaceExistingDestination()
    {
        using var directory = new TestDirectory();
        var (video, audio) = await CreateMediaAsync(directory.Path, 1);
        await File.WriteAllTextAsync(video, "invalid-video");
        var output = Path.Combine(directory.Path, "existing.mp4");
        await File.WriteAllTextAsync(output, "previous-video");
        await Assert.ThrowsAsync<NarrationException>(() =>
            new ApplyNarrationToVideo(new FfmpegVideoAudioComposer(Ffmpeg)).ExecuteAsync(video, audio, output, overwrite: true));
        Assert.Equal("previous-video", await File.ReadAllTextAsync(output));
        Assert.Empty(Directory.GetFiles(directory.Path, ".narrador-video-*"));
    }

    [Fact]
    public async Task InputsCannotBeOverwrittenAndMissingAudioPreventsStartingComposer()
    {
        using var directory = new TestDirectory();
        var video = Path.Combine(directory.Path, "source.mp4");
        var audio = Path.Combine(directory.Path, "voice.wav");
        await File.WriteAllTextAsync(video, "video");
        await File.WriteAllTextAsync(audio, "audio");
        var composer = new RejectingComposer();
        var apply = new ApplyNarrationToVideo(composer);
        await Assert.ThrowsAsync<NarrationException>(() => apply.ExecuteAsync(video, audio, video, overwrite: true));
        File.Delete(audio);
        await Assert.ThrowsAsync<NarrationException>(() => apply.ExecuteAsync(video, audio, Path.Combine(directory.Path, "result.mp4")));
        Assert.False(composer.Called);
    }

    private static async Task<(string Video, string Audio)> CreateMediaAsync(string directory, double audioSeconds)
    {
        var video = Path.Combine(directory, "original.mp4");
        var audio = Path.Combine(directory, "narracion.wav");
        await RunAsync(Ffmpeg, "-v", "error", "-f", "lavfi", "-i", "color=c=blue:s=64x48:r=10:d=2",
            "-f", "lavfi", "-i", "sine=frequency=200:sample_rate=44100:duration=2", "-c:v", "mpeg4", "-bf", "0", "-c:a", "aac", video);
        await RunAsync(Ffmpeg, "-v", "error", "-f", "lavfi", "-i",
            "sine=frequency=880:sample_rate=44100:duration=" + audioSeconds.ToString(CultureInfo.InvariantCulture), "-c:a", "pcm_s16le", audio);
        return (video, audio);
    }

    private static Task<string> VideoHashAsync(string path) => RunAsync(Ffmpeg, "-v", "error", "-i", path, "-map", "0:v:0", "-c:v", "copy", "-f", "hash", "-hash", "sha256", "-");

    private static async Task<string> RunAsync(string executable, params string[] arguments)
    {
        using var process = new Process { StartInfo = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true } };
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        process.Start();
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, await error);
        return await output;
    }

    private static string Ffmpeg => FindTool("ffmpeg.exe");
    private static string Ffprobe => FindTool("ffprobe.exe");
    private static string FindTool(string name)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(directory.FullName, "Narrador.sln")))
            directory = directory.Parent ?? throw new InvalidOperationException("No se encuentra la solución.");
        var path = Path.Combine(directory.FullName, ".tools", "ffmpeg", name);
        if (!File.Exists(path)) throw new InvalidOperationException("Instala las herramientas de vídeo ejecutando tools/Install-Ffmpeg.ps1.");
        return path;
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    private sealed class RejectingComposer : IVideoAudioComposer
    {
        public bool Called { get; private set; }
        public Task ComposeAsync(string videoPath, string audioPath, string outputPath, bool overwrite, IProgress<TimeSpan>? progress, CancellationToken cancellationToken)
        {
            Called = true;
            throw new InvalidOperationException("No debe iniciarse el montaje.");
        }
    }
}
