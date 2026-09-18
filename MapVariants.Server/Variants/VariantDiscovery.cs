using JetBrains.Annotations;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;

namespace MapVariants.Server.Variants;

// Late in PostLoad, after the backports (Preload + 4) and after spawn mods assigned their lists.
[Injectable(TypePriority = OnLoadOrder.PostLoad + 500000), UsedImplicitly]
public class VariantDiscovery(VariantRegistry registry, ISptLogger<VariantDiscovery> logger) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        // Same rule as VariantSnapshot: a defect here must not take the loader, and every other mod
        // behind it, down with it.
        try
        {
            registry.DiscoverAll();
        }
        catch (Exception ex)
        {
            logger.Error("[MV] discovery at PostLoad failed; no location will be switchable this run", ex);
        }
        return Task.CompletedTask;
    }
}
