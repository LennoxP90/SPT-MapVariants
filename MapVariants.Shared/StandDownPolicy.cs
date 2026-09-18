#nullable enable

namespace MapVariants.Shared
{
    /// <summary>
    /// What has to happen to a backport before a shipped-map raid can be produced.
    /// Refuse means it has loaded already and cannot be stood down.
    /// </summary>
    public enum StandDownAction
    {
        Proceed,
        StandDown,
        Refuse
    }

    /// <summary>
    /// What is known about a backport's own stand-down needs, from maps.json.
    /// Undeclared is the safe default for anything unexamined; the other two mean
    /// someone read the backport's source (docs/DESIGN-NOTES.md).
    /// </summary>
    public enum StandDownDeclaration
    {
        // Nothing is known, or the value did not parse. Refuse rather than guess.
        Undeclared,

        // Examined: it keys its state to its own scenes, never the shipped map's.
        Safe,

        // Examined: it needs standing down, and the named method does it.
        Target
    }

    /// <summary>
    /// Whether an earlier backport raid has left state that would corrupt a
    /// shipped-map raid. Suspending patches stops a backport's loader, not the
    /// static state it already holds.
    /// </summary>
    public static class StandDownPolicy
    {
        // A backport raid needs nothing, and a process that never ran this backport has nothing of
        // its stale, so both gate ahead of the declaration.
        public static StandDownAction Decide(bool wantStock, bool backportLoadedThisSession, StandDownDeclaration declaration)
        {
            if (!wantStock) return StandDownAction.Proceed;
            if (!backportLoadedThisSession) return StandDownAction.Proceed;

            switch (declaration)
            {
                case StandDownDeclaration.Safe: return StandDownAction.Proceed;
                case StandDownDeclaration.Target: return StandDownAction.StandDown;
                default: return StandDownAction.Refuse;
            }
        }

        // An attempt that threw, resolved to nothing or was never reachable leaves exactly the state
        // Decide was trying to clear, so it is the refusal case too.
        public static StandDownAction AfterAttempt(bool succeeded)
            => succeeded ? StandDownAction.Proceed : StandDownAction.Refuse;
    }

    /// <summary>
    /// "Namespace.Type:MethodName", split without touching reflection so the shape
    /// can be checked where no assembly exists. A malformed value yields null and
    /// is ignored: a typo in maps.json must not stop the file loading.
    /// </summary>
    public sealed class StandDownTarget
    {
        public string TypeName { get; }
        public string MethodName { get; }

        StandDownTarget(string typeName, string methodName)
        {
            TypeName = typeName;
            MethodName = methodName;
        }

        public static StandDownTarget? Parse(string? spec)
        {
            if (string.IsNullOrWhiteSpace(spec)) return null;
            var parts = spec!.Split(':');
            if (parts.Length != 2) return null;
            var type = parts[0].Trim();
            var method = parts[1].Trim();
            if (type.Length == 0 || method.Length == 0) return null;
            if (type.IndexOf(' ') >= 0 || method.IndexOf(' ') >= 0) return null;
            return new StandDownTarget(type, method);
        }

        public override string ToString() => TypeName + ":" + MethodName;
    }
}
