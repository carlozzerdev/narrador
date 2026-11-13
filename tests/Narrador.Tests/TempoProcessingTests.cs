using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Narrador.Infrastructure.Audio;

namespace Narrador.Tests;

public sealed class TempoProcessingTests
{
    [Theory]
    [InlineData(0.5 / 0.7)]
    [InlineData(2.0 / 1.2)]
    public void TempoChangesDurationWhileRetainingPitchAndDrainsTheFinalSamples(double tempo)
    {
        var signal = new SignalGenerator(44100, 1) { Gain = 0.2, Frequency = 440, Type = SignalGeneratorType.Sin };
        var source = new OffsetSampleProvider(signal) { Take = TimeSpan.FromSeconds(2) };
        var processed = new PitchPreservingTempoProvider(source, tempo);
        var audio = new List<float>();
        var buffer = new float[1024];
        int read;
        while ((read = processed.Read(buffer.AsSpan())) > 0)
        {
            audio.AddRange(buffer[..read]);
            Assert.True(audio.Count < 44100 * 5, "El procesador debe terminar después de vaciar su búfer.");
        }
        Assert.InRange(Math.Abs(audio.Count / 44100.0 - 2.0 / tempo), 0, 0.02);
        Assert.Equal(0, processed.Read(buffer.AsSpan()));

        // A frequency check catches accidental sample-rate/pitch shifts (e.g. a 440 Hz tone becoming 880 Hz).
        var first = 44100 / 4;
        var last = first + 44100 / 2;
        var positiveCrossings = 0;
        for (var index = first + 1; index < last; index++)
            if (audio[index - 1] <= 0 && audio[index] > 0) positiveCrossings++;
        var estimatedFrequency = positiveCrossings / ((last - first) / 44100.0);
        Assert.InRange(estimatedFrequency, 435, 445);
    }
}
