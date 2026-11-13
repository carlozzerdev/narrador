namespace Narrador.Domain;

public sealed record SubtitleCue
{
    public int Number { get; }
    public TimeSpan Start { get; }
    public TimeSpan End { get; }
    public string Text { get; }

    public SubtitleCue(int number, TimeSpan start, TimeSpan end, string text)
    {
        if (number < 1 || start < TimeSpan.Zero || end <= start)
            throw new ArgumentException("El subtítulo tiene un índice o un intervalo no válido.");
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        Number = number;
        Start = start;
        End = end;
        Text = text;
    }
}
