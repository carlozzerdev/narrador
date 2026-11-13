using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Narrador.Domain;

namespace Narrador.Application;

public sealed partial class SrtParser
{
    public IReadOnlyList<SubtitleCue> Parse(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            throw new NarrationException("El archivo SRT está vacío.");
        var normalized = content.TrimStart('\uFEFF').ReplaceLineEndings("\n").Trim();
        var cues = new List<SubtitleCue>();
        foreach (var block in BlockSeparator().Split(normalized))
        {
            var lines = block.Split('\n');
            if (lines.Length < 3 || !int.TryParse(lines[0].Trim(), out var number))
                throw new NarrationException($"Bloque SRT {cues.Count + 1}: falta el índice, el intervalo o el texto.");

            var match = Interval().Match(lines[1].Trim());
            if (!match.Success)
                throw new NarrationException($"Subtítulo {number}: las marcas de tiempo no tienen un formato válido.");

            var text = string.Join(" ", lines.Skip(2).Select(line => line.Trim()));
            text = FormattingTags().Replace(text, "");
            text = WebUtility.HtmlDecode(text);
            text = Whitespace().Replace(text, " ").Trim();
            try
            {
                cues.Add(new SubtitleCue(number, ParseTime(match.Groups[1].Value),
                    ParseTime(match.Groups[2].Value), text));
            }
            catch (Exception ex) when (ex is ArgumentException or OverflowException or FormatException)
            {
                throw new NarrationException($"Subtítulo {number}: el intervalo o el texto no es válido.", ex);
            }
        }

        if (cues.Count == 0)
            throw new NarrationException("El archivo SRT no contiene subtítulos.");
        return cues.AsReadOnly();
    }

    private static TimeSpan ParseTime(string value)
    {
        var parts = value.Split(':', ',');
        var hours = int.Parse(parts[0], CultureInfo.InvariantCulture);
        var minutes = int.Parse(parts[1], CultureInfo.InvariantCulture);
        var seconds = int.Parse(parts[2], CultureInfo.InvariantCulture);
        var milliseconds = int.Parse(parts[3], CultureInfo.InvariantCulture);
        if (minutes > 59 || seconds > 59)
            throw new FormatException("Minutos o segundos fuera de rango.");
        return TimeSpan.FromHours(hours) + TimeSpan.FromMinutes(minutes)
            + TimeSpan.FromSeconds(seconds) + TimeSpan.FromMilliseconds(milliseconds);
    }

    [GeneratedRegex(@"(?:\n[ \t]*){2,}")]
    private static partial Regex BlockSeparator();
    [GeneratedRegex(@"^(\d{2,}:\d{2}:\d{2},\d{3})\s*-->\s*(\d{2,}:\d{2}:\d{2},\d{3})(?:\s+.*)?$")]
    private static partial Regex Interval();
    [GeneratedRegex(@"</?(?:i|b|u|s|font|c)(?:\s[^>]*|\.[^>]*)?>|\{\\[^}]*\}", RegexOptions.IgnoreCase)]
    private static partial Regex FormattingTags();
    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
