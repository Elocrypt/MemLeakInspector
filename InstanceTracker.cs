using System.Text.Json.Serialization;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace MemLeakInspector
{
    /// <summary>
    /// Static class that tracks live object instances across the server for memory diagnostics.
    /// </summary>
    /// <remarks>
    /// <para>Uses weak references and a conditional weak table to avoid interfering with GC behavior.</para>
    /// <para>Entities and block entities are registered via Harmony patches or custom classes like AutoTrackedBE.</para>
    /// </remarks>
    public static class InstanceTracker
    {
        private static readonly ConcurrentDictionary<string, List<WeakReference>> Tracked = new();
        private static readonly ConditionalWeakTable<object, object?> AlreadyTracked = new();

        /// <summary>
        /// Represents metadata for a single tracked instance, such as ID and optional position.
        /// </summary>
        public class InstanceInfo
        {
            /// <summary>
            /// A string identifier for the object, often derived from entity ID or block position.
            /// </summary>
            public string Id { get; set; } = "";

            /// <summary>
            /// The approximate block position of the object, if available.
            /// </summary>
            [JsonIgnore]
            public BlockPos? Pos { get; set; }

            public FlatPos? Position
            {
                get => Pos == null ? null : new FlatPos { X = Pos.X, Y = Pos.Y, Z = Pos.Z };
                set => Pos = value == null ? null : new BlockPos(value.Value.X, value.Value.Y, value.Value.Z);
            }
        }
        public struct FlatPos
        {
            public int X;
            public int Y;
            public int Z;
        }

        /// <summary>
        /// Registers an object to be tracked if it hasn't already been added.
        /// </summary>
        /// <param name="obj">The object to register. Must not be null.</param>
        public static void RegisterObject(object obj)
        {
            if (obj == null || AlreadyTracked.TryGetValue(obj, out _))
                return;

            AlreadyTracked.Add(obj, null);

            Type baseType = obj.GetType();
            string typeKey = baseType.FullName ?? "(unnamed type)";

            if (obj is Entity entity)
            {
                string? code = entity.Code?.ToString();
                if (!string.IsNullOrEmpty(code))
                    typeKey += $":{code ?? "?"}";
            }


            if (!Tracked.ContainsKey(typeKey))
                Tracked[typeKey] = new List<WeakReference>();

            lock (Tracked[typeKey])
            {
                Tracked[typeKey].Add(new WeakReference(obj));
            }

        }

        /// <summary>
        /// Returns all currently live objects grouped by type name.
        /// </summary>
        /// <returns>Dictionary of type name → list of instances.</returns>
        public static Dictionary<string, List<object>> GetLiveObjects()
        {
            return Tracked.ToDictionary(
                entry => entry.Key,
                entry =>
                {
                    lock (entry.Value)
                    {
                        return entry.Value
                            .Where(wr => wr.IsAlive)
                            .Select(wr => wr.Target!)
                            .ToList();
                    }
                });
        }

        /// <summary>
        /// Returns live object counts by type name.
        /// </summary>
        /// <returns>Dictionary of type name → live instance count.</returns>
        public static Dictionary<string, int> GetLiveCounts()
        {
            var result = new Dictionary<string, int>();

            foreach (var kvp in Tracked)
            {
                int live = 0;

                lock (kvp.Value)
                {
                    kvp.Value.RemoveAll(wr => !wr.IsAlive);
                    live = kvp.Value.Count;
                }

                Console.WriteLine($"[DEBUG] Tracked {kvp.Key} = {live}");

                if (live > 0)
                    result[kvp.Key] = live;
            }

            return result;
        }

        /// <summary>
        /// Returns a list of instance metadata for each live object, grouped by type.
        /// </summary>
        /// <returns>Dictionary of type name → instance info list.</returns>
        /// <remarks>
        /// This includes entity IDs and block positions where possible.
        /// </remarks>
        public static Dictionary<string, List<InstanceInfo>> GetInstanceInfoByType()
        {
            var result = new Dictionary<string, List<InstanceInfo>>();
            var tracked = GetLiveObjects(); // Must return Dictionary<string, List<object>>

            foreach (var entry in tracked)
            {
                var typeName = entry.Key;
                var instances = entry.Value;

                if (!result.TryGetValue(typeName, out var list))
                    result[typeName] = list = new List<InstanceInfo>();

                foreach (var inst in instances)
                {
                    var info = new InstanceInfo();

                    switch (inst)
                    {
                        case Entity entity:
                            info.Id = entity.EntityId.ToString();
                            if (entity.ServerPos != null)
                                info.Pos = entity.ServerPos.AsBlockPos;
                            if (entity.Code != null)
                                info.Id += $" [{entity.Code.Path}]";
                            break;

                        case BlockEntity be:
                            info.Id = be.Pos.ToString();
                            info.Pos = be.Pos;
                            if (be.Block?.Code != null)
                                info.Id += $" [{be.Block.Code.Path}]";
                            break;

                        default:
                            info.Id = inst.GetHashCode().ToString();
                            break;
                    }

                    list.Add(info);
                }
            }

            return result;
        }

        /// <summary>
        /// Searches for a tracked instance ID and returns its position if available.
        /// </summary>
        /// <param name="id">The instance ID or ID prefix to search for.</param>
        /// <returns>Block position of the instance, or null if not found.</returns>
        public static BlockPos? GetPositionById(string id)
        {
            foreach (var list in GetInstanceInfoByType().Values)
            {
                foreach (var info in list)
                {
                    if (info.Id.StartsWith(id) || info.Id == id)  // Fuzzy match
                        return info.Pos;
                }
            }
            return null;
        }

        /// <summary>
        /// Clears all tracked objects.
        /// </summary>
        public static void Clear()
        {
            Tracked.Clear();
        }

        /// <summary>
        /// Detects whether the code is running server-side by checking for internal server types.
        /// </summary>
        /// <returns>True if server-side, false otherwise.</returns>
        private static bool IsServerSide()
        {
            return Type.GetType("Vintagestory.Server.ServerSystemModHandler, VintagestoryServer")?.IsClass == true;
        }

    }
}
