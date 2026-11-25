// Forge-Change: opt-in handling of doors and storage that obstruct a combat route.
namespace Content.Server._Forge.NPC;

[RegisterComponent]
public sealed partial class NPCBarrierComponent : Component
{
    [DataField]
    public bool OpenBarriers;

    [DataField]
    public float AttemptSeconds = 10f;

    [DataField]
    public float RetrySeconds = 30f;

    [DataField]
    public int IneffectiveHitLimit = 3;

    public EntityUid? CurrentBarrier;
    public TimeSpan AttemptUntil;
    public float PreviousDamage;
    public bool AwaitingDamage;
    public int IneffectiveHits;
    public readonly Dictionary<EntityUid, TimeSpan> BlockedBarriers = new();
}
