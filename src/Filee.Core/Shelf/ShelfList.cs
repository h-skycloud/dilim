using Filee.Core.Platform;

namespace Filee.Core.Shelf;

/// <summary>
/// The files and folders kept on the shelf, in the order they were added. The shelf holds references only: removing an
/// item, clearing the shelf or a missing file never touches anything on disk. The list survives a restart in
/// <c>shelf.txt</c> (one absolute path per line) next to the settings.
/// </summary>
public sealed class ShelfList
{
    public const string FileName = "shelf.txt";

    /// <summary>More than this many references is almost certainly a mistake (a whole drive dropped); the rest is ignored.</summary>
    public const int Capacity = 500;

    private readonly List<string> _paths = [];
    private readonly string? _file;

    public ShelfList(string? directory = null)
    {
        _file = directory is null ? null : Path.Combine(directory, FileName);
        if (_file is not null && File.Exists(_file))
        {
            try { Add(File.ReadAllLines(_file), save: false); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    public event Action? Changed;

    public IReadOnlyList<string> Paths => _paths;
    public int Count => _paths.Count;

    public bool Contains(string path) => _paths.Contains(Normalize(path), FileSystemPaths.Comparer);

    /// <summary>Adds the paths at the end, skipping blanks, relative paths and duplicates. Returns how many were new.</summary>
    public int Add(IEnumerable<string> paths) => Add(paths, save: true);

    private int Add(IEnumerable<string> paths, bool save)
    {
        var added = 0;
        foreach (var raw in paths)
        {
            if (_paths.Count >= Capacity) break;
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var path = Normalize(raw.Trim());
            if (!Path.IsPathFullyQualified(path) || _paths.Contains(path, FileSystemPaths.Comparer)) continue;
            _paths.Add(path);
            added++;
        }
        if (added > 0 && save) Commit();
        return added;
    }

    public bool Remove(string path)
    {
        var index = _paths.FindIndex(p => FileSystemPaths.Comparer.Equals(p, Normalize(path)));
        if (index < 0) return false;
        _paths.RemoveAt(index);
        Commit();
        return true;
    }

    public int RemoveAll(IEnumerable<string> paths)
    {
        var set = new HashSet<string>(paths.Select(Normalize), FileSystemPaths.Comparer);
        var removed = _paths.RemoveAll(set.Contains);
        if (removed > 0) Commit();
        return removed;
    }

    /// <summary>Moves the item at <paramref name="from"/> to <paramref name="to"/> (drag to reorder).</summary>
    public void Move(int from, int to)
    {
        if (from == to || from < 0 || to < 0 || from >= _paths.Count || to >= _paths.Count) return;
        var path = _paths[from];
        _paths.RemoveAt(from);
        _paths.Insert(to, path);
        Commit();
    }

    public void Clear()
    {
        if (_paths.Count == 0) return;
        _paths.Clear();
        Commit();
    }

    /// <summary>Drops references whose file or folder no longer exists (moved away by a drag, deleted elsewhere).</summary>
    public int RemoveMissing(Func<string, bool>? exists = null)
    {
        exists ??= p => File.Exists(p) || Directory.Exists(p);
        var removed = _paths.RemoveAll(p => !exists(p));
        if (removed > 0) Commit();
        return removed;
    }

    private void Commit()
    {
        if (_file is not null)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
                var temp = _file + ".tmp";
                File.WriteAllLines(temp, _paths);
                File.Move(temp, _file, overwrite: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        Changed?.Invoke();
    }

    private static string Normalize(string path)
    {
        var trimmed = Path.TrimEndingDirectorySeparator(path);
        // "C:\" stays a root; TrimEndingDirectorySeparator already keeps it.
        return trimmed.Length == 0 ? path : trimmed;
    }
}
