namespace Narrador.Application;

public sealed class LoadSrt(ITextFileReader reader, SrtParser parser)
{
    public async Task<LoadedNarration> ExecuteAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var content = await reader.ReadAsync(path, cancellationToken);
        var cues = parser.Parse(content);
        return new LoadedNarration(cues, string.Join(" ", cues.Select(cue => cue.Text)));
    }
}
