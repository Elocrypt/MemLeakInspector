using System.IO.Compression;
using System.Text.Json;

namespace MemLeakInspector.Snapshots;

/// <summary>Load, save, compress, and enforce retention on snapshot files.</summary>
internal static class SnapshotStore
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public static string Save(string dir, string name, MemSnapshot snap, bool compress)
    {
        Directory.CreateDirectory(dir);
        string basePath = Path.Combine(dir, $"{name}.json");
        if (compress)
        {
            string gz = basePath + ".gz";
            using var fs = File.Create(gz);
            using var gzst = new GZipStream(fs, CompressionLevel.Optimal);
            using var jw = new Utf8JsonWriter(gzst, new JsonWriterOptions { Indented = true });
            JsonSerializer.Serialize(jw, snap, JsonOpts);
            return gz;
        }
        else
        {
            File.WriteAllText(basePath, JsonSerializer.Serialize(snap, JsonOpts));
            return basePath;
        }
    }

    public static MemSnapshot? Load(string path)
    {
        if (!File.Exists(path)) return null;
        Stream s = File.OpenRead(path);
        if (path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
            s = new GZipStream(s, CompressionMode.Decompress);
        using (s) return JsonSerializer.Deserialize<MemSnapshot>(s, JsonOpts);
    }

    public static void EnforceRetention(string dir, int max)
    {
        if (!Directory.Exists(dir)) return;
        var files = new DirectoryInfo(dir).GetFiles("*.json*")
            .OrderBy(f => f.CreationTimeUtc).ToList();
        for (int i = 0; i < Math.Max(0, files.Count - max); i++)
            files[i].Delete();
    }
}
