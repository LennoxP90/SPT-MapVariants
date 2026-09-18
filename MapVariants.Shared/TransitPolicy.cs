#nullable enable
namespace MapVariants.Shared
{
    /// <summary>
    /// First player into a transit zone decides for everyone who joins; later
    /// claimants are told what they joined. Released when a raid there starts
    /// or ends.
    /// </summary>
    public static class TransitPolicy
    {
        public static string Winner(string? held, string? offered)
            => string.IsNullOrEmpty(held) ? MapVariant.Normalise(offered) : MapVariant.Normalise(held);

        public static bool WasFirst(string? held) => string.IsNullOrEmpty(held);
    }
}
