using Robust.Shared.Prototypes;

namespace Content.Shared._Forge.ResourceExtractor;

/// <summary>
/// Server-wide safety and update settings for resource extractors.
/// These settings are deliberately shared by all extractors: per-entity values
/// would let a map bypass the global output-work budget.
/// </summary>
[Prototype]
public sealed partial class ResourceExtractorSettingsPrototype : IPrototype
{
    public const string DefaultId = "N14ResourceExtractorSettings";

    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField]
    public int MaxOutputOperationsPerTick { get; private set; } = 8;

    [DataField]
    public int MaxAllowedBatchSize { get; private set; } = 32;

    [DataField]
    public int MaxSpawnedEntitiesPerTick { get; private set; } = 64;

    [DataField]
    public int MaxEventsPerProduction { get; private set; } = 4;

    [DataField]
    public int MaxEventSpawnsPerProduction { get; private set; } = 8;

    [DataField]
    public int MaxEventOperationsPerTick { get; private set; } = 8;

    [DataField]
    public int MaxEventSpawnRadius { get; private set; } = 8;

    [DataField]
    public TimeSpan MaxTelegraphDuration { get; private set; } = TimeSpan.FromSeconds(10);

    [DataField]
    public TimeSpan MinExtractionDuration { get; private set; } = TimeSpan.FromSeconds(1);

    [DataField]
    public TimeSpan OutputRetryPeriod { get; private set; } = TimeSpan.FromSeconds(1);

    [DataField]
    public TimeSpan UiUpdatePeriod { get; private set; } = TimeSpan.FromSeconds(1);

    [DataField]
    public float FuelEpsilon { get; private set; } = 0.000001f;
}
