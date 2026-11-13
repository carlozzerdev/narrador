using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Narrador.Application;

namespace Narrador.Infrastructure.Audio;

public sealed class Mp3AudioExporter : IAudioExporter
{
    public Task<IAudioExportSession> BeginAsync(string outputPath, bool overwrite, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fullPath = Path.GetFullPath(outputPath);
        if (File.Exists(fullPath) && !overwrite)
            throw new NarrationException("El archivo de destino ya existe. Elige otro nombre o confirma su sustitución.");
        var parent = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(parent);
        var workDirectory = Path.Combine(parent, ".narrador-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDirectory);
        return Task.FromResult<IAudioExportSession>(new ExportSession(fullPath, workDirectory, overwrite));
    }

    private sealed class ExportSession(string outputPath, string workDirectory, bool overwrite) : IAudioExportSession
    {
        private readonly List<(string Path, double Tempo)> _clips = [];

        public async Task AppendAsync(byte[] mp3, CancellationToken cancellationToken, double tempo = 1.0)
        {
            var path = Path.Combine(workDirectory, $"clip-{_clips.Count:D5}.mp3");
            await File.WriteAllBytesAsync(path, mp3, cancellationToken);
            _clips.Add((path, tempo));
        }

        public Task<TimeSpan> CommitAsync(CancellationToken cancellationToken) => Task.Run(() =>
        {
            if (_clips.Count == 0) throw new NarrationException("No hay audio para guardar.");
            cancellationToken.ThrowIfCancellationRequested();
            var staged = Path.Combine(workDirectory, "output.mp3");
            var readers = new List<MediaFoundationReader>();
            TimeSpan duration;
            try
            {
                foreach (var clip in _clips)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var reader = new MediaFoundationReader(clip.Path);
                    readers.Add(reader);
                    if (reader.TotalTime <= TimeSpan.Zero)
                        throw new NarrationException("El audio recibido no contiene una narración reproducible.");
                }
                duration = TimeSpan.FromSeconds(readers.Select((reader, index) => reader.TotalTime.TotalSeconds / _clips[index].Tempo).Sum());
                if (_clips.Count == 1 && Math.Abs(_clips[0].Tempo - 1.0) <= 1e-12)
                {
                    // Preserve the exact provider bytes when no assembly is necessary.
                    File.Copy(_clips[0].Path, staged);
                }
                else
                {
                    var concatenated = new ConcatenatingSampleProvider(readers.Select((reader, index) =>
                    {
                        ISampleProvider samples = reader.ToSampleProvider();
                        return Math.Abs(_clips[index].Tempo - 1.0) > 1e-12
                            ? new PitchPreservingTempoProvider(samples, _clips[index].Tempo, cancellationToken) : samples;
                    }));
                    var pcm = new SampleToWaveProvider16(concatenated);
                    MediaFoundationEncoder.EncodeToMp3(new CancellableWaveProvider(pcm, cancellationToken), staged, 128000);
                }
            }
            finally
            {
                foreach (var reader in readers) reader.Dispose();
            }

            cancellationToken.ThrowIfCancellationRequested();
            // Staging is on the same volume; a failed/cancelled operation leaves the old destination intact.
            File.Move(staged, outputPath, overwrite);
            return duration;
        }, cancellationToken);

        public ValueTask DisposeAsync()
        {
            try { Directory.Delete(workDirectory, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return ValueTask.CompletedTask;
        }
    }

    private sealed class CancellableWaveProvider(IWaveProvider source, CancellationToken cancellationToken) : IWaveProvider
    {
        public WaveFormat WaveFormat => source.WaveFormat;
        public int Read(Span<byte> buffer)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return source.Read(buffer);
        }
    }
}
