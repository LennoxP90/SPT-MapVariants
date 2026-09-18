using JetBrains.Annotations;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;

namespace MapVariants.Server.Variants;

/// <summary>
/// Captures every shipped location before any Preload mod runs. The database is a
/// singleton by the time IOnLoad runs, so this sees them untouched.
/// </summary>
[Injectable(TypePriority = OnLoadOrder.Watermark + 1), UsedImplicitly]
public class VariantSnapshot(VariantRegistry registry, ISptLogger<VariantSnapshot> logger) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        // Swallowed so a first-party mod can never stop the server starting.
        try
        {
            registry.SnapshotAll();
        }
        catch (Exception ex)
        {
            logger.Error("[MV] snapshot at Watermark failed; no location will be switchable this run", ex);
        }
        return Task.CompletedTask;
    }
}
