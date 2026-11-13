using System.Text;
using Narrador.Application;

namespace Narrador.Infrastructure.Files;

public sealed class TextFileReader : ITextFileReader
{
    public async Task<string> ReadAsync(string path, CancellationToken cancellationToken)
    {
        if (!Path.GetExtension(path).Equals(".srt", StringComparison.OrdinalIgnoreCase))
            throw new NarrationException("Selecciona un archivo de subtítulos .srt.");
        if (new FileInfo(path).Length > 20 * 1024 * 1024)
            throw new NarrationException("El SRT supera el máximo de 20 MB.");
        try
        {
            // BOM detection supports UTF-8 and UTF-16. Legacy SRT files often use Windows-1252.
            using var reader = new StreamReader(path, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true);
            return await reader.ReadToEndAsync(cancellationToken);
        }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return await File.ReadAllTextAsync(path, Encoding.GetEncoding(1252), cancellationToken);
        }
    }
}
