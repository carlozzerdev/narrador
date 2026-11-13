namespace Narrador.Domain;

/// <summary>Delays cues only while needed; available silence restores the original schedule.</summary>
public sealed class AdaptiveSubtitleTimeline
{
    private readonly List<SubtitleCue> _cues = [];
    private TimeSpan _previousOriginalEnd;

    public IReadOnlyList<SubtitleCue> Cues => _cues.AsReadOnly();

    public SubtitleCue Append(SubtitleCue original, TimeSpan audioDuration)
    {
        if (audioDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(audioDuration));
        if (_cues.Count > 0 && original.Start < _previousOriginalEnd)
            throw new ArgumentException("Los subtítulos originales deben estar ordenados y no solaparse.", nameof(original));

        var start = _cues.Count > 0 && _cues[^1].End > original.Start ? _cues[^1].End : original.Start;
        var audioEnd = start + audioDuration;
        var end = audioEnd > original.End ? audioEnd : original.End;
        var adjusted = new SubtitleCue(original.Number, start, end, original.Text);
        _cues.Add(adjusted);
        _previousOriginalEnd = original.End;
        return adjusted;
    }
}
