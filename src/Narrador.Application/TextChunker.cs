namespace Narrador.Application;

public sealed class TextChunker
{
    public IReadOnlyList<string> Split(string text, int maxCharacters)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        if (maxCharacters < 2)
            throw new ArgumentOutOfRangeException(nameof(maxCharacters));

        var chunks = new List<string>();
        var remaining = text.AsSpan().Trim();
        while (remaining.Length > maxCharacters)
        {
            var split = 0;
            for (var i = maxCharacters - 1; i >= maxCharacters / 2; i--)
            {
                if (char.IsWhiteSpace(remaining[i]) && remaining[i - 1] is '.' or '!' or '?' or '…')
                {
                    split = i;
                    break;
                }
            }
            if (split == 0)
            {
                for (var i = maxCharacters - 1; i > 0; i--)
                {
                    if (char.IsWhiteSpace(remaining[i]))
                    {
                        split = i;
                        break;
                    }
                }
            }
            if (split == 0)
            {
                split = maxCharacters;
                if (char.IsHighSurrogate(remaining[split - 1]))
                    split--;
            }
            chunks.Add(remaining[..split].Trim().ToString());
            remaining = remaining[split..].TrimStart();
        }
        if (!remaining.IsEmpty)
            chunks.Add(remaining.ToString());
        return chunks.AsReadOnly();
    }
}
