using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Narrador.Application;
using Narrador.Domain;

namespace Narrador.Infrastructure.Audio;

public sealed class SrtAudioExporter : ITimedAudioExporter
{
    private const int SampleRate = 44100;
    private static readonly WaveFormat OutputFormat = new(SampleRate, 16, 1);

    public Task<ITimedAudioExportSession> BeginAsync(string outputPath, bool overwrite, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fullPath = Path.GetFullPath(outputPath);
        if (Path.GetExtension(fullPath).ToLowerInvariant() is not (".wav" or ".mp3"))
            throw new NarrationException("El audio sincronizado debe guardarse como .wav o .mp3.");
        if (File.Exists(fullPath) && !overwrite)
            throw new NarrationException("El archivo de destino ya existe. Elige otro nombre o confirma su sustitución.");
        var parent = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(parent);
        var workDirectory = Path.Combine(parent, ".narrador-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDirectory);
        return Task.FromResult<ITimedAudioExportSession>(new ExportSession(fullPath, workDirectory, overwrite));
    }

    // All boundaries are calculated from absolute SRT times, avoiding cumulative rounding drift.
    private static long ToSample(TimeSpan time) => checked((long)decimal.Round(
        (decimal)time.Ticks * SampleRate / TimeSpan.TicksPerSecond, 0, MidpointRounding.AwayFromZero));

    private sealed record Clip(long StartSample, long EndSample, string PcmPath);
    private sealed record PreparedClip(long SampleCount, string PcmPath);

    private sealed class ExportSession(string outputPath, string workDirectory, bool overwrite) : ITimedAudioExportSession
    {
        private readonly List<PreparedClip> _clips = [];

        public async Task<TimeSpan> AppendAsync(SubtitleCue cue, byte[] mp3, CancellationToken cancellationToken, double tempo = 1.0)
        {
            var sourcePath = Path.Combine(workDirectory, $"clip-{_clips.Count:D5}.mp3");
            var pcmPath = Path.ChangeExtension(sourcePath, ".wav");
            await File.WriteAllBytesAsync(sourcePath, mp3, cancellationToken);
            var sampleCount = await Task.Run(() =>
            {
                using var reader = new MediaFoundationReader(sourcePath);
                ISampleProvider samples = reader.ToSampleProvider();
                if (samples.WaveFormat.Channels == 2) samples = new StereoToMonoSampleProvider(samples);
                if (samples.WaveFormat.Channels != 1)
                    throw new NarrationException("El audio recibido no es mono ni estéreo.");
                if (samples.WaveFormat.SampleRate != SampleRate)
                    samples = new WdlResamplingSampleProvider(samples, SampleRate);
                if (Math.Abs(tempo - 1.0) > 1e-12)
                    samples = new PitchPreservingTempoProvider(samples, tempo, cancellationToken);

                long countOfSamples = 0;
                using (var writer = new WaveFileWriter(pcmPath, OutputFormat))
                {
                    var buffer = new float[4096];
                    int count;
                    while ((count = samples.Read(buffer.AsSpan())) > 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        writer.WriteSamples(buffer, 0, count);
                        countOfSamples += count;
                    }
                }
                if (countOfSamples == 0)
                    throw new NarrationException($"El subtítulo {cue.Number} no contiene audio reproducible.");
                return countOfSamples;
            }, cancellationToken);

            File.Delete(sourcePath);
            _clips.Add(new(sampleCount, pcmPath));
            // Round up to a tick so a measured clip is never shortened when rebuilding the sample timeline.
            return TimeSpan.FromTicks(checked((long)decimal.Ceiling((decimal)sampleCount * TimeSpan.TicksPerSecond / SampleRate)));
        }

        public Task<TimeSpan> CommitAsync(IReadOnlyList<SubtitleCue> timeline, CancellationToken cancellationToken) => Task.Run(() =>
        {
            if (_clips.Count == 0) throw new NarrationException("No hay subtítulos generados para exportar.");
            if (timeline.Count != _clips.Count)
                throw new NarrationException("La línea de tiempo no coincide con los audios preparados.");
            var scheduled = new List<Clip>();
            for (var index = 0; index < timeline.Count; index++)
            {
                var start = ToSample(timeline[index].Start);
                var end = Math.Max(ToSample(timeline[index].End), start + _clips[index].SampleCount);
                if (scheduled.Count > 0 && start < scheduled[^1].EndSample)
                    throw new NarrationException("La línea de tiempo adaptada contiene intervalos solapados.");
                scheduled.Add(new(start, end, _clips[index].PcmPath));
            }
            var extension = Path.GetExtension(outputPath).ToLowerInvariant();
            var staged = Path.Combine(workDirectory, "output" + extension);
            using (var provider = new TimelineProvider(scheduled, cancellationToken))
            {
                if (extension == ".wav") WaveFileWriter.CreateWaveFile(staged, provider);
                else MediaFoundationEncoder.EncodeToMp3(provider, staged, 128000);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(staged, outputPath, overwrite);
            return TimeSpan.FromSeconds((double)scheduled[^1].EndSample / SampleRate);
        }, cancellationToken);

        public ValueTask DisposeAsync()
        {
            try { Directory.Delete(workDirectory, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TimelineProvider(IReadOnlyList<Clip> clips, CancellationToken cancellationToken) : IWaveProvider, IDisposable
    {
        private long _position;
        private int _clipIndex;
        private WaveFileReader? _reader;
        public WaveFormat WaveFormat => OutputFormat;

        public int Read(Span<byte> buffer)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var written = 0;
            var usableLength = buffer.Length / OutputFormat.BlockAlign * OutputFormat.BlockAlign;
            while (written < usableLength && _clipIndex < clips.Count)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var clip = clips[_clipIndex];
                var isGap = _position < clip.StartSample;
                var boundary = isGap ? clip.StartSample : clip.EndSample;
                var frames = Math.Min((usableLength - written) / OutputFormat.BlockAlign, boundary - _position);
                var bytes = checked((int)frames * OutputFormat.BlockAlign);
                var destination = buffer.Slice(written, bytes);
                if (isGap) destination.Clear();
                else
                {
                    _reader ??= new WaveFileReader(clip.PcmPath);
                    var read = 0;
                    while (read < destination.Length)
                    {
                        var count = _reader.Read(destination[read..]);
                        if (count == 0) break;
                        read += count;
                    }
                    destination[read..].Clear(); // Fill the rest of the subtitle's slot with digital silence.
                }
                _position += frames;
                written += bytes;
                if (_position == clip.EndSample)
                {
                    _reader?.Dispose();
                    _reader = null;
                    _clipIndex++;
                }
            }
            return written;
        }

        public void Dispose() => _reader?.Dispose();
    }
}
