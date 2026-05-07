namespace MemLeakInspector.Snapshots;

/// <summary>
/// Resolves snapshot names to file paths using exact match, extension probing,
/// and prefix-based fuzzy matching across the main and autosnap directories.
/// </summary>
internal static class SnapshotFinder
{
    /// <summary>
    /// Find a snapshot file by name. Searches the root snapshot dir and the
    /// autosnap subfolder. Returns the full path or null.
    /// </summary>
    public static string? Find(string snapshotDir, string name)
    {
        IEnumerable<string> dirs = Dirs(snapshotDir);

        // 1. Direct / extension-probed match
        foreach (var dir in dirs)
        {
            if (HasSnapExtension(name))
            {
                string full = Path.Combine(dir, name);
                if (File.Exists(full)) return full;
            }
            else
            {
                string json = Path.Combine(dir, name + ".json");
                if (File.Exists(json)) return json;

                string gz = Path.Combine(dir, name + ".json.gz");
                if (File.Exists(gz)) return gz;
            }
        }

        // 2. Fuzzy: collect all snapshot files, try exact base-name, then prefix
        var all = dirs
            .Where(Directory.Exists)
            .SelectMany(d => Directory.GetFiles(d, "*.json*"))
            .ToList();

        var exact = all.FirstOrDefault(f =>
            string.Equals(BaseName(f), name, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return exact;

        var prefix = all
            .Where(f => BaseName(f).StartsWith(name, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();

        return prefix;
    }

    private static IEnumerable<string> Dirs(string root)
    {
        yield return root;
        string auto = Path.Combine(root, "autosnap");
        if (Directory.Exists(auto)) yield return auto;
    }

    private static bool HasSnapExtension(string s) =>
        s.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
        s.EndsWith(".json.gz", StringComparison.OrdinalIgnoreCase);

    private static string BaseName(string path)
    {
        string file = Path.GetFileName(path);
        if (file.EndsWith(".json.gz", StringComparison.OrdinalIgnoreCase)) return file[..^8];
        if (file.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return file[..^5];
        return Path.GetFileNameWithoutExtension(file);
    }
}
