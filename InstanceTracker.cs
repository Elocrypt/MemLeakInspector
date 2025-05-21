using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Vintagestory.API.Common.Entities;

namespace MemLeakInspector
{
    public static class InstanceTracker
    {
        private static readonly ConcurrentDictionary<string, List<WeakReference>> Tracked = new();
        private static readonly ConditionalWeakTable<object, object?> AlreadyTracked = new();

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

        public static void Clear()
        {
            Tracked.Clear();
        }

        private static bool IsServerSide()
        {
            return Type.GetType("Vintagestory.Server.ServerSystemModHandler, VintagestoryServer")?.IsClass == true;
        }

    }
}
