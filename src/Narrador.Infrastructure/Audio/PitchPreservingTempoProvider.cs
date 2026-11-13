using NAudio.Wave;
using SoundTouch;

namespace Narrador.Infrastructure.Audio;

/// <summary>Changes tempo in PCM without transposing the voice. The caller owns the source.</summary>
public sealed class PitchPreservingTempoProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly SoundTouchProcessor _processor;
    private readonly float[] _input;
    private readonly CancellationToken _cancellationToken;
    private bool _flushed;

    public PitchPreservingTempoProvider(ISampleProvider source, double tempo, CancellationToken cancellationToken = default)
    {
        if (!double.IsFinite(tempo) || tempo <= 0)
            throw new ArgumentOutOfRangeException(nameof(tempo));
        _source = source;
        _cancellationToken = cancellationToken;
        _processor = new SoundTouchProcessor
        {
            SampleRate = source.WaveFormat.SampleRate,
            Channels = source.WaveFormat.Channels,
            Tempo = tempo
        };
        // Rate and pitch retain their default value of 1.0.
        _input = new float[4096 * source.WaveFormat.Channels];
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    public int Read(Span<float> buffer)
    {
        var channels = WaveFormat.Channels;
        var length = buffer.Length / channels * channels;
        var written = 0;
        while (written < length)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var received = _processor.ReceiveSamples(buffer[written..length], (length - written) / channels);
            if (received > 0)
            {
                written += received * channels;
                continue;
            }
            if (_flushed) break;

            var read = _source.Read(_input.AsSpan());
            if (read > 0)
            {
                if (read % channels != 0) throw new InvalidDataException("El audio contiene una trama incompleta.");
                _processor.PutSamples(_input.AsSpan(0, read), read / channels);
            }
            else
            {
                // Flush exactly once to retain the final syllables and drain all delayed samples.
                _processor.Flush();
                _flushed = true;
            }
        }
        return written;
    }
}
