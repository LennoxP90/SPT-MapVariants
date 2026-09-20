using JetBrains.Annotations;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;

namespace MapVariants.Server.Variants;

/// <summary>
/// Probes that read each backport's values before a load-time mod configures them. Several, at
/// rising priorities, because a backport and a config mod can sit one priority apart and leave
/// nowhere to insert between them. The registry keeps the earliest valid reading, so these are
/// a net rather than a schedule and no load order is assumed.
/// </summary>
public abstract class VariantPristineProbe(VariantRegistry registry, ISptLogger<VariantPristineProbe> logger) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        // A defect here must not take the loader down. Losing a probe costs the replay only.
        try
        {
            registry.CaptureBackportValues();
        }
        catch (Exception ex)
        {
            logger.Error("[MV] a pristine probe failed; load-time configuration may not be replayed", ex);
        }
        return Task.CompletedTask;
    }
}

[Injectable(TypePriority = OnLoadOrder.Preload + 4), UsedImplicitly]
public sealed class VariantPristineProbeOnInstall(VariantRegistry registry, ISptLogger<VariantPristineProbe> logger)
    : VariantPristineProbe(registry, logger);

[Injectable(TypePriority = OnLoadOrder.Preload + 5), UsedImplicitly]
public sealed class VariantPristineProbeAfterInstall(VariantRegistry registry, ISptLogger<VariantPristineProbe> logger)
    : VariantPristineProbe(registry, logger);

// Catches a backport that installs outside the Preload band.
[Injectable(TypePriority = OnLoadOrder.Preload + 50000), UsedImplicitly]
public sealed class VariantPristineProbeLate(VariantRegistry registry, ISptLogger<VariantPristineProbe> logger)
    : VariantPristineProbe(registry, logger);
