using System.Collections;
using System.Reflection;
using MapVariants.Shared;
using SPTarkov.Server.Core.Models.Eft.Common;

namespace MapVariants.Server.Variants;

/// <summary>
/// Reads the replayable parts of a LocationBase into the dictionaries ReplayPolicy compares,
/// and writes a plan back onto another LocationBase. Reflection over the whole type, so a
/// property this mod does not know about is still carried.
/// </summary>
internal static class LocationValueMirror
{
    // Identity, and strings that name zones or exits. Writing any of these across would point
    // the shipped map at the backport's own id, scene or geometry.
    private static readonly HashSet<string> NotConfiguration =
        new(StringComparer.Ordinal)
        { "Id", "IdField", "Name", "Scene", "OpenZones", "ExitZones", "DisabledScavExits" };

    // The exit name is the key both sides are matched on, not a value to replay.
    private static readonly HashSet<string> NotExitConfiguration =
        new(StringComparer.Ordinal) { "Name" };

    // Spawn data is not mirrored in either direction, clearing included. Every other collection
    // is read, so one a future mod empties needs no change here.
    private static readonly HashSet<string> SpawnData =
        new(StringComparer.Ordinal) { "Waves", "BossLocationSpawn", "SpawnPointParams" };

    private static readonly PropertyInfo[] BaseProperties = Readable(typeof(LocationBase));
    private static readonly PropertyInfo[] ExitProperties = Readable(typeof(Exit));

    private static PropertyInfo[] Readable(Type type) => type
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0)
        .ToArray();

    public static LocationValues Read(LocationBase? location)
    {
        if (location is null) return new LocationValues();

        var scalars = new Dictionary<string, object?>(StringComparer.Ordinal);
        var collectionsEmpty = new Dictionary<string, bool>(StringComparer.Ordinal);

        foreach (var property in BaseProperties)
        {
            if (IsScalar(property.PropertyType))
            {
                if (NotConfiguration.Contains(property.Name)) continue;
                if (TryRead(property, location, out var value)) scalars[property.Name] = value;
            }
            else if (!SpawnData.Contains(property.Name) && !NotConfiguration.Contains(property.Name))
            {
                if (TryRead(property, location, out var value)) collectionsEmpty[property.Name] = IsEmpty(value);
            }
        }

        return new LocationValues(scalars, ReadExits(location), collectionsEmpty);
    }

    // Keyed by exit name. A nameless or duplicated exit is dropped, because there would be no
    // way to say which exit on the shipped map it meant.
    private static Dictionary<string, IReadOnlyDictionary<string, object?>> ReadExits(LocationBase location)
    {
        var exits = new Dictionary<string, IReadOnlyDictionary<string, object?>>(StringComparer.Ordinal);
        var duplicates = new HashSet<string>(StringComparer.Ordinal);

        foreach (var exit in location.Exits ?? [])
        {
            if (exit is null || string.IsNullOrEmpty(exit.Name)) continue;
            if (!exits.TryAdd(exit.Name!, ReadExit(exit))) duplicates.Add(exit.Name!);
        }

        foreach (var name in duplicates) exits.Remove(name);
        return exits;
    }

    private static Dictionary<string, object?> ReadExit(Exit exit)
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in ExitProperties)
        {
            if (!IsScalar(property.PropertyType) || NotExitConfiguration.Contains(property.Name)) continue;
            if (TryRead(property, exit, out var value)) values[property.Name] = value;
        }
        return values;
    }

    internal sealed record ReplayOutcome(int Written, IReadOnlyList<string> Refused);

    /// <summary>
    /// Writes a plan onto the shipped map's base. A value it cannot write is named rather than
    /// swallowed, so a partial replay does not read as a complete one.
    /// </summary>
    public static ReplayOutcome Apply(LocationBase? target, ReplayPlan plan)
    {
        var refused = new List<string>();
        if (target is null) return new ReplayOutcome(0, ["the shipped map has no base"]);
        if (plan.IsEmpty) return new ReplayOutcome(0, refused);

        var written = Write(BaseProperties, target, plan.Scalars, "base", refused);

        var reached = new HashSet<string>(StringComparer.Ordinal);
        foreach (var exit in target.Exits ?? [])
        {
            if (exit?.Name is not { } name) continue;
            if (!plan.Exits.TryGetValue(name, out var values)) continue;
            reached.Add(name);
            written += Write(ExitProperties, exit, values, name, refused);
        }

        // An exit the backport has that the shipped map does not. Expected, and said once.
        foreach (var pair in plan.Exits.Where(p => !reached.Contains(p.Key)))
            refused.Add($"'{pair.Key}' ({pair.Value.Count} value(s): absent from the shipped map)");

        foreach (var name in plan.Cleared)
        {
            var property = BaseProperties.FirstOrDefault(p => p.Name == name);
            var empty = property is null ? null : EmptyFor(property.PropertyType);
            if (property is null || empty is null) { refused.Add($"clear {name} (no property or no empty value)"); continue; }
            try { property.SetValue(target, empty); written++; }
            catch (Exception ex) { refused.Add($"clear {name} ({ex.GetType().Name})"); }
        }

        return new ReplayOutcome(written, refused);
    }

    private static int Write(PropertyInfo[] properties, object target,
                             IReadOnlyDictionary<string, object?> values, string where, List<string> refused)
    {
        var written = 0;
        foreach (var pair in values)
        {
            var property = properties.FirstOrDefault(p => p.Name == pair.Key);
            if (property is null) { refused.Add($"{where}.{pair.Key} (no such property)"); continue; }
            try { property.SetValue(target, pair.Value); written++; }
            catch (Exception ex) { refused.Add($"{where}.{pair.Key} ({ex.GetType().Name})"); }
        }
        return written;
    }

    private static bool TryRead(PropertyInfo property, object target, out object? value)
    {
        try { value = property.GetValue(target); return true; }
        catch { value = null; return false; }
    }

    // Value-like, and carrying no reference to another map's geometry.
    private static bool IsScalar(Type type)
    {
        var bare = Nullable.GetUnderlyingType(type) ?? type;
        return bare.IsPrimitive || bare.IsEnum || bare == typeof(string)
               || bare == typeof(decimal) || bare == typeof(DateTime);
    }

    // A getter or enumerator that throws reads as not empty, which writes nothing.
    private static bool IsEmpty(object? value)
    {
        try
        {
            return value switch
            {
                null => true,
                ICollection collection => collection.Count == 0,
                IEnumerable enumerable => !enumerable.GetEnumerator().MoveNext(),
                _ => false,
            };
        }
        catch { return false; }
    }

    private static object? EmptyFor(Type type)
    {
        var element = type.IsArray ? type.GetElementType()
            : type.IsGenericType ? type.GetGenericArguments().FirstOrDefault()
            : null;
        if (element is null) return null;

        object empty = Array.CreateInstance(element, 0);
        if (type.IsInstanceOfType(empty)) return empty;

        try { return Activator.CreateInstance(typeof(List<>).MakeGenericType(element)); }
        catch { return null; }
    }
}
