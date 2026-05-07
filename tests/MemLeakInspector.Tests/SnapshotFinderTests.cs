using MemLeakInspector.Snapshots;
using Xunit;

namespace MemLeakInspector.Tests;

public class SnapshotFinderTests : IDisposable
{
    private readonly string _dir;

    public SnapshotFinderTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "mli-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void FindsExactJsonMatch()
    {
        File.WriteAllText(Path.Combine(_dir, "snap1.json"), "{}");
        var result = SnapshotFinder.Find(_dir, "snap1");
        Assert.NotNull(result);
        Assert.EndsWith("snap1.json", result);
    }

    [Fact]
    public void FindsGzMatch()
    {
        File.WriteAllText(Path.Combine(_dir, "snap2.json.gz"), "{}");
        var result = SnapshotFinder.Find(_dir, "snap2");
        Assert.NotNull(result);
        Assert.EndsWith("snap2.json.gz", result);
    }

    [Fact]
    public void FindsWithExtensionProvided()
    {
        File.WriteAllText(Path.Combine(_dir, "snap3.json"), "{}");
        var result = SnapshotFinder.Find(_dir, "snap3.json");
        Assert.NotNull(result);
    }

    [Fact]
    public void PrefixMatch_ReturnsNewest()
    {
        File.WriteAllText(Path.Combine(_dir, "20250101_000000.json"), "{}");
        Thread.Sleep(50);
        File.WriteAllText(Path.Combine(_dir, "20250101_120000.json"), "{}");

        var result = SnapshotFinder.Find(_dir, "20250101");
        Assert.NotNull(result);
        Assert.Contains("120000", result); // newest
    }

    [Fact]
    public void SearchesAutosnapSubfolder()
    {
        string auto = Path.Combine(_dir, "autosnap");
        Directory.CreateDirectory(auto);
        File.WriteAllText(Path.Combine(auto, "auto1.json"), "{}");

        var result = SnapshotFinder.Find(_dir, "auto1");
        Assert.NotNull(result);
        Assert.Contains("autosnap", result);
    }

    [Fact]
    public void ReturnsNull_WhenNotFound()
    {
        var result = SnapshotFinder.Find(_dir, "nonexistent");
        Assert.Null(result);
    }

    [Fact]
    public void CaseInsensitiveBaseName()
    {
        File.WriteAllText(Path.Combine(_dir, "MySnap.json"), "{}");
        var result = SnapshotFinder.Find(_dir, "mysnap");
        Assert.NotNull(result);
    }
}
