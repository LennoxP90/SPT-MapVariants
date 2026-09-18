using System.Reflection;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Utils.Json;

namespace MapVariants.Server.Variants;

/// <summary>
/// Copies a LazyLoad transformer the backport's lazy has and the stock lazy lacks,
/// by delegate reference so nothing is added twice. It writes into the STOCK lazy
/// only. The field names are SPT's private layout, checked once per T.
/// </summary>
internal static class LazyLoadTransformers
{
    public static int MirrorMissing<T>(LazyLoad<T>? from, LazyLoad<T>? to)
    {
        if (from is null || to is null || ReferenceEquals(from, to)) return 0;
        var source = Accessor<T>.Snapshot(from);
        var present = new HashSet<Func<T?, T?>>(Accessor<T>.Snapshot(to));
        var copied = 0;
        foreach (var transformer in source)
        {
            if (present.Contains(transformer)) continue;
            to.AddTransformer(transformer);
            copied++;
        }
        return copied;
    }

    // Forces the three closed Accessor<T> type initialisers so a layout change surfaces during
    // server load, inside VariantSnapshot's catch, rather than on the first raid that switches.
    // A failed type initialiser is cached, so without this the first switch and every switch after
    // it fail identically for the life of the process.
    public static void VerifyLayout()
    {
        Accessor<LooseLoot>.Verify();
        Accessor<StaticContainerDetails>.Verify();
        Accessor<Dictionary<MongoId, StaticLootDetails>>.Verify();
    }

    private static class Accessor<T>
    {
        private static readonly FieldInfo List = Resolve("_lazyLoadTransformers", typeof(List<Func<T?, T?>>));
        private static readonly FieldInfo Lock = Resolve("_lazyLoadTransformersLock", typeof(ReaderWriterLockSlim));

        // Touching any static member runs the initialiser, which is where the layout is validated.
        public static void Verify() => _ = List;

        public static Func<T?, T?>[] Snapshot(LazyLoad<T> lazy)
        {
            var list = (List<Func<T?, T?>>)List.GetValue(lazy)!;
            var gate = (ReaderWriterLockSlim)Lock.GetValue(lazy)!;
            gate.EnterReadLock();
            try { return list.ToArray(); }
            finally { gate.ExitReadLock(); }
        }

        private static FieldInfo Resolve(string name, Type expected)
        {
            var field = typeof(LazyLoad<T>).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field is null || field.FieldType != expected)
                throw new InvalidOperationException($"LazyLoad<{typeof(T).Name}> has no private field '{name}' of {expected.Name}; SPT's layout changed");
            return field;
        }
    }
}
