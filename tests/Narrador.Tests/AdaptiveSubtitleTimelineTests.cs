using Narrador.Domain;

namespace Narrador.Tests;

public sealed class AdaptiveSubtitleTimelineTests
{
    [Fact]
    public void GapsAbsorbOverrunsAndRestoreTheOriginalSchedule()
    {
        SubtitleCue[] original =
        [
            new(1, TimeSpan.Zero, TimeSpan.FromSeconds(1), "Uno."),
            new(2, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3), "Dos."),
            new(3, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(6), "Tres."),
            new(4, TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(9), "Cuatro.")
        ];
        double[] actualDurations = [2, 3, 0.5, 2];
        var timeline = new AdaptiveSubtitleTimeline();
        for (var index = 0; index < original.Length; index++)
            timeline.Append(original[index], TimeSpan.FromSeconds(actualDurations[index]));

        Assert.Equal(new double[] { 0, 2, 5, 8 }, timeline.Cues.Select(cue => cue.Start.TotalSeconds));
        Assert.Equal(new double[] { 2, 5, 6, 10 }, timeline.Cues.Select(cue => cue.End.TotalSeconds));
        for (var index = 1; index < original.Length; index++)
            Assert.True(timeline.Cues[index].Start >= timeline.Cues[index - 1].End);
        Assert.Equal(TimeSpan.FromSeconds(9), original[^1].End);
    }

    [Fact]
    public void SeveralSmallGapsRecoverTheDelayProgressively()
    {
        SubtitleCue[] original =
        [
            new(1, TimeSpan.Zero, TimeSpan.FromSeconds(1), "Uno."),
            new(2, TimeSpan.FromSeconds(1.3), TimeSpan.FromSeconds(1.8), "Dos."),
            new(3, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2.5), "Tres."),
            new(4, TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(5), "Cuatro.")
        ];
        var timeline = new AdaptiveSubtitleTimeline();
        for (var index = 0; index < original.Length; index++)
            timeline.Append(original[index], TimeSpan.FromSeconds(index == 0 ? 2 : 0.5));
        Assert.Equal(new double[] { 0, 2, 2.5, 4 }, timeline.Cues.Select(cue => cue.Start.TotalSeconds));
        Assert.Equal(new double[] { 2, 2.5, 3, 5 }, timeline.Cues.Select(cue => cue.End.TotalSeconds));
    }

    [Fact]
    public void ShortVoiceRecoversDelayWithinItsOwnSubtitleWindow()
    {
        var timeline = new AdaptiveSubtitleTimeline();
        timeline.Append(new(1, TimeSpan.Zero, TimeSpan.FromSeconds(1), "Largo."), TimeSpan.FromSeconds(2));
        var shortened = timeline.Append(new(2, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3), "Corto."), TimeSpan.FromSeconds(0.5));
        var recovered = timeline.Append(new(3, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(4), "Puntual."), TimeSpan.FromSeconds(0.5));
        Assert.Equal(TimeSpan.FromSeconds(2), shortened.Start);
        Assert.Equal(TimeSpan.FromSeconds(3), shortened.End);
        Assert.Equal(TimeSpan.FromSeconds(3), recovered.Start);
        Assert.Equal(TimeSpan.FromSeconds(4), recovered.End);
    }

    [Fact]
    public void WithNoAvailableSilenceTheDelayContinuesWithoutOverlappingOrCuttingVoice()
    {
        var timeline = new AdaptiveSubtitleTimeline();
        for (var index = 0; index < 3; index++)
            timeline.Append(new(index + 1, TimeSpan.FromSeconds(index), TimeSpan.FromSeconds(index + 1), "Texto."), TimeSpan.FromSeconds(1.5));
        Assert.Equal(new double[] { 0, 1.5, 3 }, timeline.Cues.Select(cue => cue.Start.TotalSeconds));
        Assert.Equal(new double[] { 1.5, 3, 4.5 }, timeline.Cues.Select(cue => cue.End.TotalSeconds));
    }

    [Fact]
    public void LastBlockCanExpandWithoutAnyFollowingBlock()
    {
        var cue = new SubtitleCue(1, TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(6470), "Texto.");
        var adjusted = new AdaptiveSubtitleTimeline().Append(cue, TimeSpan.FromMilliseconds(7119));
        Assert.Equal(cue.Start, adjusted.Start);
        Assert.Equal(TimeSpan.FromMilliseconds(7369), adjusted.End);
        Assert.Equal(cue.End + TimeSpan.FromMilliseconds(899), adjusted.End);
    }
}
