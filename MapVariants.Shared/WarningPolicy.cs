#nullable enable
using System;
using System.Linq;

namespace MapVariants.Shared
{
    /// <summary>
    /// What the choice window does with the lines a registered provider supplies
    /// about its backport. They are whole sentences the provider wrote: this mod
    /// decides only whether to show them and how several become one line.
    /// </summary>
    public static class WarningPolicy
    {
        static readonly string[] None = new string[0];

        // Without the blank filter, an array holding one empty string counts as a warning and the
        // window shows a blank line over a CONFIRM button.
        public static string[] Normalise(string[]? warnings)
            => warnings == null ? None
             : warnings.Where(w => !string.IsNullOrWhiteSpace(w)).Select(w => w.Trim()).ToArray();

        public static string Text(string[]? warnings)
        {
            var lines = Normalise(warnings);
            // Two spaces rather than a separator character: these are sentences, and the label wraps.
            return lines.Length == 0 ? "" : string.Join("  ", lines);
        }

        // The warning is about the backported tile only, so the shipped tile never carries it.
        public static bool Shows(bool backport, string[]? warnings)
            => backport && Normalise(warnings).Length > 0;

        // A deliberate yes is asked for only while a warning is in front of the player.
        public static string AcceptCaption(bool backport, string[]? warnings)
            => Shows(backport, warnings) ? "CONFIRM" : "NEXT";
    }
}
