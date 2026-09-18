#nullable enable
namespace MapVariants.Shared
{
    /// <summary>
    /// The version a consumer reads off Maps.ApiVersion. Minor for an addition,
    /// major for anything a consumer could be broken by; docs/DESIGN-NOTES.md has
    /// what each bump changed.
    /// </summary>
    public static class ExtensionApiContract
    {
        public const string Version = "3.0";
    }
}
