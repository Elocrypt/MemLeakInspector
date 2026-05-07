using System.Collections.Concurrent;
using System.Reflection;

namespace MemLeakInspector.Tracking;

/// <summary>
/// Estimates the in-memory size of a managed object by inspecting its field layout.
/// Results are cached per type name to avoid repeated reflection.
/// </summary>
/// <remarks>
/// These are rough estimates — the CLR's actual layout includes padding, alignment,
/// and the object header (typically 8–16 bytes). We add a fixed overhead to account
/// for that. For types we can't reflect on, we fall back to a conservative default.
/// </remarks>
internal static class SizeEstimator
{
    private const int ObjectOverhead = 24;   // sync block + method table + padding
    private const int ReferenceSize = 8;     // 64-bit pointer
    private const int FallbackSize = 300;    // conservative guess for unknown types

    private static readonly ConcurrentDictionary<string, int> Cache = new(StringComparer.Ordinal);

    private static readonly Dictionary<Type, int> PrimitiveSizes = new()
    {
        [typeof(bool)]    = 1,
        [typeof(byte)]    = 1,
        [typeof(sbyte)]   = 1,
        [typeof(short)]   = 2,
        [typeof(ushort)]  = 2,
        [typeof(char)]    = 2,
        [typeof(int)]     = 4,
        [typeof(uint)]    = 4,
        [typeof(float)]   = 4,
        [typeof(long)]    = 8,
        [typeof(ulong)]   = 8,
        [typeof(double)]  = 8,
        [typeof(decimal)] = 16,
        [typeof(nint)]    = 8,
        [typeof(nuint)]   = 8,
    };

    /// <summary>
    /// Estimate the shallow size (in bytes) of a single instance of the given type.
    /// </summary>
    public static int EstimateInstanceSize(string typeName)
    {
        if (Cache.TryGetValue(typeName, out int cached))
            return cached;

        int size = ComputeSize(typeName);
        Cache[typeName] = size;
        return size;
    }

    /// <summary>
    /// Total estimated memory for <paramref name="count"/> instances of a type.
    /// </summary>
    public static long EstimateTotal(string typeName, int count)
        => (long)EstimateInstanceSize(typeName) * count;

    /// <summary>Seed the cache with externally-known sizes (e.g. from a previous snapshot).</summary>
    public static void Seed(IReadOnlyDictionary<string, int> known)
    {
        foreach (var (k, v) in known)
            Cache.TryAdd(k, v);
    }

    /// <summary>Export the current cache (for snapshot serialization).</summary>
    public static Dictionary<string, int> ExportCache()
        => new(Cache, StringComparer.Ordinal);

    public static void ClearCache() => Cache.Clear();

    // ------------------------------------------------------------------

    private static int ComputeSize(string typeName)
    {
        try
        {
            Type? type = Type.GetType(typeName, throwOnError: false);
            if (type is null)
                return FallbackSize;

            int size = ObjectOverhead;
            var fields = type.GetFields(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            foreach (var field in fields)
            {
                size += FieldSize(field.FieldType);
            }

            // Walk one level of base class fields (many VS types inherit deeply)
            var baseType = type.BaseType;
            if (baseType is not null && baseType != typeof(object) && baseType != typeof(ValueType))
            {
                var baseFields = baseType.GetFields(
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                foreach (var field in baseFields)
                    size += FieldSize(field.FieldType);
            }

            return Math.Max(size, ObjectOverhead);
        }
        catch
        {
            return FallbackSize;
        }
    }

    private static int FieldSize(Type fieldType)
    {
        if (PrimitiveSizes.TryGetValue(fieldType, out int prim))
            return prim;

        if (fieldType.IsEnum)
            return PrimitiveSizes.GetValueOrDefault(Enum.GetUnderlyingType(fieldType), 4);

        if (fieldType == typeof(string))
            return ReferenceSize + 24; // ref + estimated string object header

        if (fieldType.IsValueType)
        {
            // For structs, sum their fields (one level only to avoid deep recursion)
            int total = 0;
            foreach (var sf in fieldType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (PrimitiveSizes.TryGetValue(sf.FieldType, out int sp))
                    total += sp;
                else
                    total += ReferenceSize; // treat unknown struct fields as pointer-sized
            }
            return Math.Max(total, 4); // at least 4 bytes for any struct
        }

        // Reference type field: just the pointer
        return ReferenceSize;
    }
}
