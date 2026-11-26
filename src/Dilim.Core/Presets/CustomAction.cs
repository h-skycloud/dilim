using System.Text;

namespace Dilim.Core.Presets;

/// <summary>What a slice of the user's own does with the selected files.</summary>
public enum CustomActionKind
{
    /// <summary>Opens a web address; <c>{name}</c> and the like are filled in (URL-encoded).</summary>
    OpenUrl,
    /// <summary>Starts a program with arguments; <c>{files}</c> and the like are filled in.</summary>
    RunCommand,
    /// <summary>Opens the files with a program.</summary>
    OpenWith,
}

/// <summary>
/// A slice of the user's own on the file actions pie (Settings → Shortcuts): open a web address, run a command or open
/// the files with a program. Placeholders: <c>{files}</c> (every path, quoted), <c>{file}</c> (the first path),
/// <c>{name}</c> (its name without extension), <c>{folder}</c> (its folder).
/// </summary>
public sealed class CustomAction
{
    /// <summary>The prefix of the tool id a custom slice has on the pie ("custom:ab12…").</summary>
    public const string ToolPrefix = "custom:";

    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..10];
    public string Name { get; set; } = "";
    public CustomActionKind Kind { get; set; }

    /// <summary>The web address (with placeholders), or the program to start.</summary>
    public string Target { get; set; } = "";

    /// <summary>The program's arguments (Run a command); empty for the others.</summary>
    public string Arguments { get; set; } = "";

    public string ToolId => ToolPrefix + Id;

    /// <summary>Fills the placeholders of <paramref name="template"/> for <paramref name="files"/>.</summary>
    public static string Expand(string template, IReadOnlyList<string> files, bool url = false)
    {
        var first = files.Count > 0 ? files[0] : "";
        string Value(string text) => url ? Uri.EscapeDataString(text) : text;
        var all = url ? Value(string.Join(" ", files.Select(Path.GetFileName))) : string.Join(" ", files.Select(Quote));
        var builder = new StringBuilder(template);
        builder.Replace("{files}", all);
        builder.Replace("{file}", url ? Value(first) : Quote(first));
        builder.Replace("{name}", Value(Path.GetFileNameWithoutExtension(first)));
        builder.Replace("{folder}", url ? Value(Path.GetDirectoryName(first) ?? "") : Quote(Path.GetDirectoryName(first) ?? ""));
        return builder.ToString();
    }

    private static string Quote(string path) => path.Length == 0 ? "" : "\"" + path.Replace("\"", "") + "\"";

    /// <summary>Why the action can't be saved ("tool.custom.error_*"), or null.</summary>
    public string? Problem()
    {
        if (string.IsNullOrWhiteSpace(Name)) return "tool.custom.error_name";
        if (string.IsNullOrWhiteSpace(Target)) return "tool.custom.error_target";
        if (Kind == CustomActionKind.OpenUrl &&
            !(Uri.TryCreate(Expand(Target, ["x"], url: true), UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)))
            return "tool.custom.error_url";
        return null;
    }
}
