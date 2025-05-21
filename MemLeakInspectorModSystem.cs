using System.Reflection;
using System.Text.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Server;


namespace MemLeakInspector
{
    public class MemLeakInspectorModSystem : ModSystem
    {
        private ICoreServerAPI sapi = null!;
        private string snapshotDir = null!;

        #region Entry Points

        public override void StartServerSide(ICoreServerAPI api)
        {
            sapi = api;
            snapshotDir = Path.Combine(api.GetOrCreateDataPath("MemLeakInspector"), "snapshots");

            if (!Directory.Exists(snapshotDir))
            {
                Directory.CreateDirectory(snapshotDir);
            }

            RegisterServerCommands(api);

            sapi.Logger.Notification("[MemLeakInspector] Initialized.");
        }

        public override void Dispose()
        {
            sapi?.Logger.Notification("{MemLeakInspector] Unloaded.");
        }

        #endregion

        #region Command Registration

        private void RegisterServerCommands(ICoreServerAPI sapi)
        {
            sapi.ChatCommands
                .Create("mem")
                .WithDescription("Memory debugging tools (MemLeakInspector)")
                .RequiresPrivilege("controlserver")

                .BeginSubCommand("snap")
                    .WithDescription("Take a memory snapshot. Optionally provide a name.")
                    .WithArgs(sapi.ChatCommands.Parsers.OptionalWord("name"))
                    .HandleWith(ctx =>
                    {
                        string name = ctx.Parsers[0].GetValue() as string ?? DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
                        return CmdMemSnap(name);
                    })
                .EndSubCommand()

                .BeginSubCommand("list")
                    .WithDescription("List available memory snapshot files.")
                    .HandleWith(_ => CmdListSnapshots())
                .EndSubCommand()

                .BeginSubCommand("diff")
                    .WithDescription("Compare two memory snapshots: /mem diff <old> <new>")
                    .WithArgs(
                        sapi.ChatCommands.Parsers.Word("snapshotA"),
                        sapi.ChatCommands.Parsers.Word("snapshotB")
                    )
                    .HandleWith(ctx =>
                    {
                        string? snapA = ctx.Parsers[0].GetValue()?.ToString();
                        string? snapB = ctx.Parsers[1].GetValue()?.ToString();

                        if (snapA == null || snapB == null)
                            return TextCommandResult.Error("[MemLeakInspector] One or both snapshot names are null.");

                        return CmdDiffSnapshots(snapA, snapB);
                    })
                .EndSubCommand()

                .BeginSubCommand("report")
                    .WithDescription("Show largest memory consumers from a snapshot: /mem report <name>")
                    .WithArgs(sapi.ChatCommands.Parsers.Word("snapshotName"))
                    .HandleWith(ctx =>
                    {
                        string? name = ctx.Parsers[0].GetValue()?.ToString();
                        if (name == null)
                            return TextCommandResult.Error("[MemLeakInspector] No snapshot name provided.");

                        return CmdReportSnapshot(name);
                    })
                .EndSubCommand();

            sapi.Logger.Notification("[MemLeakInspector] Chat commands registered.");
        }

        #endregion

        #region Command Logic

        private TextCommandResult CmdMemSnap(string name)
        {
            try
            {
                MemSnapshot snapshot = TakeSnapshot();
                string json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });

                string filePath = Path.Combine(snapshotDir, $"{name}.json");
                File.WriteAllText(filePath, json);

                return TextCommandResult.Success(
                    $"[MemLeakInspector] Snapshot '{name}' saved ({snapshot.TotalManagedMemoryBytes / 1024 / 1024} MB)"
                );
            }
            catch (Exception ex)
            {
                return TextCommandResult.Error($"[MemLeakInspector] Snapshot failed: {ex.Message}");
            }
        }

        private TextCommandResult CmdDiffSnapshots(string nameA, string nameB)
        {
            string fileA = Path.Combine(snapshotDir, $"{nameA}.json");
            string fileB = Path.Combine(snapshotDir, $"{nameB}.json");

            if (!File.Exists(fileA)) return TextCommandResult.Error($"Snapshot '{nameA}' not found.");
            if (!File.Exists(fileB)) return TextCommandResult.Error($"Snapshot '{nameB}' not found.");

            try
            {
                var snapA = JsonSerializer.Deserialize<MemSnapshot>(File.ReadAllText(fileA));
                var snapB = JsonSerializer.Deserialize<MemSnapshot>(File.ReadAllText(fileB));

                if (snapA == null || snapB == null)
                    return TextCommandResult.Error("Failed to load one or both snapshots.");

                var resultLines = new List<string> {
                    $"[MemLeakInspector] Snapshot diff: {nameA} → {nameB}",
                    $"Memory: {snapA.TotalManagedMemoryBytes / 1024 / 1024} MB → {snapB.TotalManagedMemoryBytes / 1024 / 1024} MB",
                    "Changed types:"
                };

                var allKeys = new HashSet<string>(snapA.ObjectCountsByType.Keys);
                allKeys.UnionWith(snapB.ObjectCountsByType.Keys);

                foreach (var key in allKeys.OrderBy(k => k))
                {
                    snapA.ObjectCountsByType.TryGetValue(key, out int countA);
                    snapB.ObjectCountsByType.TryGetValue(key, out int countB);
                    int delta = countB - countA;

                    if (delta != 0)
                        resultLines.Add($"{(delta > 0 ? "+" : "")}{delta,4}  {key}");
                }

                if (resultLines.Count == 3)
                    resultLines.Add("No differences detected.");

                return TextCommandResult.Success(string.Join("\n", resultLines));
            }
            catch (Exception ex)
            {
                return TextCommandResult.Error($"[MemLeakInspector] Error diffing snapshots: {ex.Message}");
            }
        }

        private TextCommandResult CmdReportSnapshot(string name)
        {
            string filePath = Path.Combine(snapshotDir, $"{name}.json");

            if (!File.Exists(filePath))
                return TextCommandResult.Error($"[MemLeakInspector] Snapshot '{name}' not found.");

            try
            {
                MemSnapshot? snap = JsonSerializer.Deserialize<MemSnapshot>(File.ReadAllText(filePath));
                if (snap == null)
                    return TextCommandResult.Error($"[MemLeakInspector] Failed to load snapshot '{name}'.");

                var topTypes = snap.ObjectCountsByType
                    .OrderByDescending(kv => kv.Value)
                    .Take(10)
                    .ToList();

                var result = new List<string>
                {
                    $"[MemLeakInspector] Snapshot Report: '{name}'",
                    $"Total Heap: {snap.TotalManagedMemoryBytes / 1024 / 1024} MB",
                    $"Top {topTypes.Count} types by instance count:"
                };

                foreach (var (type, count) in topTypes)
                    result.Add($"{count,6} × {type}");

                return TextCommandResult.Success(string.Join("\n", result));
            }
            catch (Exception ex)
            {
                return TextCommandResult.Error($"[MemLeakInspector] Report failed: {ex.Message}");
            }
        }

        #endregion

        #region Snapshot Logic

        private MemSnapshot TakeSnapshot()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var snapshot = new MemSnapshot
            {
                Timestamp = DateTime.UtcNow,
                TotalManagedMemoryBytes = GC.GetTotalMemory(forceFullCollection: true),
                ObjectCountsByType = new Dictionary<string, int>()
            };

            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                foreach (Type type in SafeGetTypes(asm))
                {
                    if (!type.IsClass || type.IsAbstract) continue;

                    int count = FakeInstanceCounter(type);
                    if (count > 0)
                    {
                        string key = type.FullName ?? "(unnamed type)";
                        snapshot.ObjectCountsByType[key] = count;
                    }
                }
            }

            return snapshot;
        }

        private IEnumerable<Type> SafeGetTypes(Assembly asm)
        {
            try
            {
                return asm.GetTypes();
            }
            catch
            {
                return Array.Empty<Type>();
            }
        }

        private int FakeInstanceCounter(Type type)
        {
            // TODO: Replace with real tracking in Phase 2
            return 0;
        }

        #endregion

        #region Helpers

        private TextCommandResult CmdListSnapshots()
        {
            if (!Directory.Exists(snapshotDir))
                return TextCommandResult.Error("[MemLeakInspector] Snapshot folder not found.");

            string[] files = Directory.GetFiles(snapshotDir, "*.json");
            if (files.Length == 0)
                return TextCommandResult.Success("[MemLeakInspector] No snapshots found.");

            string output = "[MemLeakInspector] Snapshots:\n" + string.Join("\n", files.Select(f => Path.GetFileName(f)));
            return TextCommandResult.Success(output);
        }
        
        #endregion
    }
}