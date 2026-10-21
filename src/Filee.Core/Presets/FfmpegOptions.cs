using System.Text;

namespace Filee.Core.Presets;

/// <summary>FFmpeg output options, passed as individual arguments, never through a shell.</summary>
public static class FfmpegOptions
{
    private static readonly HashSet<string> Switches = ["-an", "-vn", "-sn", "-dn", "-shortest", "-bitexact", "-copyts", "-start_at_zero"];
    private static readonly HashSet<string> Reserved = ["-i", "-y", "-n", "-progress", "-stdin", "-nostdin", "-filter_complex_script", "-filter_script"];

    public static IReadOnlyList<string> Parse(string text)
    {
        if (text.Length > 32768) throw new ArgumentException("FFmpeg options are too long.");
        var tokens = new List<string>();
        var token = new StringBuilder();
        var quote = '\0';
        var started = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\0' || c is '\r' or '\n') throw new ArgumentException("Use one line of FFmpeg options.");
            if (c == '\\' && i + 1 < text.Length && text[i + 1] == quote && quote != '\0') { token.Append(text[++i]); started = true; }
            else if (c == quote) quote = '\0';
            else if (quote == '\0' && c is '\'' or '"') { quote = c; started = true; }
            else if (quote == '\0' && char.IsWhiteSpace(c))
            {
                if (started) { tokens.Add(token.ToString()); token.Clear(); started = false; }
            }
            else { token.Append(c); started = true; }
        }
        if (quote != '\0') throw new ArgumentException("Close the quote in FFmpeg options.");
        if (started) tokens.Add(token.ToString());
        for (var i = 0; i < tokens.Count; i++)
        {
            var option = tokens[i];
            if (!option.StartsWith('-') || Reserved.Contains(option) || option.StartsWith("-i=", StringComparison.Ordinal))
                throw new ArgumentException("Enter output options only; input/output paths and overwrite flags are managed by the app.");
            if (Switches.Contains(option)) continue;
            if (++i >= tokens.Count) throw new ArgumentException($"FFmpeg option {option} needs a value.");
        }
        return tokens;
    }
}
