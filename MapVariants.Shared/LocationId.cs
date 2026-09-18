#nullable enable

namespace MapVariants.Shared
{
    /// <summary>
    /// One spelling of a location id everywhere one crosses a boundary: trimmed,
    /// lower case. Two halves that normalised differently would never match.
    /// NOT the server's VariantRegistry.Key, which also maps through the location
    /// table; folding that in here would break Factory.
    /// </summary>
    public static class LocationId
    {
        public static string Normalise(string? locationId) => (locationId ?? "").Trim().ToLowerInvariant();
    }
}
