using System.Text;
using Narrador.Application;
using Narrador.Domain;
using Narrador.Infrastructure.Files;

namespace Narrador.Tests;

public sealed class SrtAndTextTests
{
    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void SrtPreservesSpanishAndJoinsMultilineText(string lineEnding)
    {
        var srt = "\uFEFF1\n00:00:01,000 --> 00:00:04,500\n<i>¡Bienvenido!</i>\nAprenderás a editar.\n\n2\n00:00:05,000 --> 00:00:08,000\nPulsa &quot;Guardar&quot;.";
        var cues = new SrtParser().Parse(srt.Replace("\n", lineEnding));
        Assert.Equal(2, cues.Count);
        Assert.Equal("¡Bienvenido! Aprenderás a editar.", cues[0].Text);
        Assert.Equal("Pulsa \"Guardar\".", cues[1].Text);
        Assert.Equal(TimeSpan.FromMilliseconds(4500), cues[0].End);
    }

    [Theory]
    [InlineData("1\n00:00:01,000 --> 00:00:00,000\nTexto")]
    [InlineData("1\n00:99:01,000 --> 00:99:04,000\nTexto")]
    [InlineData("1\n00:00:01.000 --> 00:00:04.000\nTexto")]
    [InlineData("Texto sin formato SRT")]
    [InlineData("1\n00:00:01,000 --> 00:00:04,000\n<i></i>")]
    public void InvalidSubtitlesAreRejected(string srt) =>
        Assert.Throws<NarrationException>(() => new SrtParser().Parse(srt));

    [Fact]
    public void MultipleBlankLinesWithSpacesAreAccepted()
    {
        var cues = new SrtParser().Parse("1\n00:00:00,000 --> 00:00:01,000\nHola.\n \n \n2\n00:00:02,000 --> 00:00:03,000\nAdiós.");
        Assert.Equal(2, cues.Count);
    }

    [Fact]
    public async Task ReaderSupportsLegacyWindows1252()
    {
        using var directory = new TestDirectory();
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var path = Path.Combine(directory.Path, "acentos.srt");
        await File.WriteAllBytesAsync(path, Encoding.GetEncoding(1252).GetBytes("¿Cómo estás? Mañana en Zaragoza."));
        var text = await new TextFileReader().ReadAsync(path, CancellationToken.None);
        Assert.Equal("¿Cómo estás? Mañana en Zaragoza.", text);
    }

    [Fact]
    public void ChunksPreferSentencesAndPreserveText()
    {
        var text = "Bienvenido al tutorial. Ahora vamos a guardar el documento. Después lo volveremos a abrir.";
        var chunks = new TextChunker().Split(text, 60);
        Assert.All(chunks, chunk => Assert.InRange(chunk.Length, 1, 60));
        Assert.EndsWith(".", chunks[0]);
        Assert.Equal(text, string.Join(" ", chunks));
    }

    [Fact]
    public void LongWordsDoNotSplitSurrogatePairs()
    {
        var text = string.Concat(Enumerable.Repeat("á😀", 20));
        var chunks = new TextChunker().Split(text, 8);
        Assert.Equal(text, string.Concat(chunks));
        Assert.All(chunks, chunk =>
        {
            Assert.InRange(chunk.Length, 1, 8);
            Assert.False(char.IsLowSurrogate(chunk[0]));
            Assert.False(char.IsHighSurrogate(chunk[^1]));
        });
    }

    [Theory]
    [InlineData(double.NaN, 0.5)]
    [InlineData(0.5, double.PositiveInfinity)]
    [InlineData(-0.1, 0.5)]
    [InlineData(0.5, 1.1)]
    public void InvalidVoiceSettingsAreRejected(double stability, double similarity) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new VoiceSettings(stability, similarity));
}

