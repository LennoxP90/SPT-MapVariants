#nullable enable
using System;
using System.Collections.Concurrent;

namespace MapVariants.Shared
{
    /// <summary>
    /// Which dataset the server serves and which scenes the client loads, under
    /// one location id. The words match FactoryClassic and InterchangeRework.
    /// </summary>
    public static class MapVariant
    {
        public const string Original = "original";
        public const string Backport = "backport";

        // A third wire answer, not a variant: Normalise folds it to "original" like any other
        // unrecognised word, so nothing downstream shifts. See docs/DESIGN-NOTES.md.
        public const string Unmanaged = "unmanaged";

        // Further variants a provider declared, such as "halloween2026". Process-wide rather than per
        // map so every existing call site keeps its signature; a variant is offered only on the map
        // that declared it, so an id shared across maps changes nothing.
        static readonly ConcurrentDictionary<string, byte> Declared = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);

        // False for an empty id and for the three reserved words, which already mean something.
        public static bool Declare(string? variant)
        {
            var id = Clean(variant);
            if (id.Length == 0 || id == Original || id == Backport || id == Unmanaged) return false;
            Declared[id] = 0;
            return true;
        }

        public static bool IsUnmanaged(string? variant) => Clean(variant) == Unmanaged;

        // Undeclared words still fold to Original, so a bad value can never select a new variant.
        public static string Normalise(string? variant)
        {
            var id = Clean(variant);
            return id == Backport || Declared.ContainsKey(id) ? id : Original;
        }

        // "Not Vanilla": the backport and every declared variant. A declared variant is built on the
        // backport, so it takes the backport's server data and leaves its client patches live.
        public static bool IsBackport(string? variant) => Normalise(variant) != Original;

        static string Clean(string? variant) => (variant ?? "").Trim().ToLowerInvariant();
    }
}
