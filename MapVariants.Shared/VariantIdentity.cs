#nullable enable
using System;
using System.Linq;
using System.Text;

namespace MapVariants.Shared
{
    /// <summary>
    /// The descriptive name of a choice, for other mods to key on (API 3.3): which mod's variant
    /// loaded, as "owner:variant". The owner is the plugin GUID that registered it, the variant that
    /// mod's own id for it. The map SPT ships is "spt:shipped". The wire words (original, backport,
    /// a source id) are unchanged and still what the callbacks hear.
    /// </summary>
    public static class VariantIdentity
    {
        public const string ShippedOwner = "spt";
        public const string ShippedVariant = "shipped";

        // An owner nobody could name: a registration whose callback is not in any loaded plugin.
        public const string UnknownOwner = "unknown";

        public static string Compose(string owner, string variant) => owner + ":" + variant;

        // "Halloween 2026" -> "halloween-2026". Letters and digits kept, every other run one dash.
        public static string Slug(string? name)
        {
            var sb = new StringBuilder();
            foreach (var c in (name ?? "").Trim().ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(c)) sb.Append(c);
                else if (sb.Length > 0 && sb[sb.Length - 1] != '-') sb.Append('-');
            }
            return sb.ToString().Trim('-');
        }

        /// <summary>
        /// The owner and variant a choice on this map belongs to. backportOwner names whoever
        /// registered the backport (null falls to the maps.json plugin GUID); sourceOwner names a
        /// source's registering plugin by source id.
        /// </summary>
        public static (string Owner, string Variant) For(ManagedMap map, string? chosen, string? backportOwner,
                                                         Func<string, string?> sourceOwner)
        {
            var id = map.Offered(chosen);
            var owner = map.OwnerOf(id);
            if (owner == MapVariant.Original) return (ShippedOwner, ShippedVariant);
            if (owner == MapVariant.Backport)
            {
                var who = Named(backportOwner) ?? Named(map.PluginGuid) ?? UnknownOwner;
                var variant = id == MapVariant.Backport ? Slug(map.BackportName) : id;
                return (who, variant.Length > 0 ? variant : MapVariant.Backport);
            }
            return (Named(sourceOwner(owner)) ?? UnknownOwner, owner);
        }

        static string? Named(string? s) => string.IsNullOrWhiteSpace(s) ? null : s!.Trim();
    }
}
