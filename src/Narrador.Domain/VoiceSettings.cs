namespace Narrador.Domain;

public sealed record VoiceSettings
{
    public double Stability { get; }
    public double SimilarityBoost { get; }
    public double Speed { get; }

    public VoiceSettings(double stability = 0.5, double similarityBoost = 0.75, double speed = 1.0)
    {
        if (!double.IsFinite(stability) || stability is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(stability), "La estabilidad debe estar entre 0 y 1.");
        if (!double.IsFinite(similarityBoost) || similarityBoost is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(similarityBoost), "La similitud debe estar entre 0 y 1.");
        if (!double.IsFinite(speed) || speed is < 0.5 or > 2.0)
            throw new ArgumentOutOfRangeException(nameof(speed), "La velocidad debe estar entre 0,50 y 2,00.");
        Stability = stability;
        SimilarityBoost = similarityBoost;
        Speed = speed;
    }
}
