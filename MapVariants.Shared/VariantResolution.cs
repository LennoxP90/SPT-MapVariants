#nullable enable

namespace MapVariants.Shared
{
    /// <summary>
    /// Which variant a caller should load: its own recorded choice, else the last
    /// choice made for that location, else the backport. The last rung is fixed,
    /// not configured, because the server's table rests on the backport.
    /// </summary>
    public static class VariantResolution
    {
        public const string NoAnswer = "";

        public static string Resolve(string? ownChoice, string? latestForLocation)
        {
            if (!string.IsNullOrEmpty(ownChoice)) return MapVariant.Normalise(ownChoice);
            if (!string.IsNullOrEmpty(latestForLocation)) return MapVariant.Normalise(latestForLocation);
            return MapVariant.Backport;
        }

        // The raid hook installs only when this is true: the table already rests on the backport,
        // and for a location another mod manages our snapshot would overwrite its choice.
        public static bool HasAnswer(string? claimed, string? ownChoice, string? latestForLocation)
            => !string.IsNullOrEmpty(claimed)
            || !string.IsNullOrEmpty(ownChoice)
            || !string.IsNullOrEmpty(latestForLocation);
    }
}
