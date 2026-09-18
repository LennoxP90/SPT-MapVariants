#nullable enable
using System.Collections.Generic;

namespace MapVariants.Shared
{
    /// <summary>
    /// A location has a backport if any captured field reference was replaced
    /// between the Watermark snapshot and PostLoad. By reference, never by value:
    /// a re-deserialised identical file still replaced the object.
    /// </summary>
    public static class DiscoveryPolicy
    {
        public static bool HasBackport(object?[] original, object?[] now) => ChangedIndices(original, now).Length > 0;

        public static int[] ChangedIndices(object?[] original, object?[] now)
        {
            var changed = new List<int>();
            var longest = original.Length > now.Length ? original.Length : now.Length;
            for (var i = 0; i < longest; i++)
            {
                // A slot past the end of either array reads as null, so a trailing addition or
                // removal counts as the change it is rather than being skipped.
                var before = i < original.Length ? original[i] : null;
                var after = i < now.Length ? now[i] : null;
                if (!ReferenceEquals(before, after)) changed.Add(i);
            }
            return changed.ToArray();
        }
    }
}
