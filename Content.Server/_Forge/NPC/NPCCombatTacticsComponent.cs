using System.Numerics;
using Robust.Shared.Map;

namespace Content.Server._Forge.NPC;

public enum NPCCombatStyle : byte
{
    Humanoid,
    Skirmish,
    Direct,
}

/// <summary>Opt-in combat movement; pursuit remains owned by NPCSteeringSystem.</summary>
[RegisterComponent]
public sealed partial class NPCCombatTacticsComponent : Component
{
    [DataField] public NPCCombatStyle Style = NPCCombatStyle.Humanoid;
    [DataField] public float MinimumBodyRadius;
    [DataField] public float MinimumRange = 6f;
    [DataField] public float MaximumRange = 10f;
    [DataField] public float CoverSearchRange = 4f;
    [DataField] public float MinimumClosingFraction = 0.85f;
    [DataField] public float PursuitRange = 30f;
    [DataField] public float TargetMemorySeconds = 5f;
    [DataField] public float StorageMemorySeconds = 15f;

    [ViewVariables] public EntityUid Target;
    [ViewVariables] public EntityUid? Weapon;
    [ViewVariables] public EntityUid? Cover;
    [ViewVariables] public bool MovingLeft = true;
    [ViewVariables] public string Decision = "Pursuit";
    internal EntityCoordinates Anchor;
    internal Vector2 TargetPosition;
    internal Vector2 Direction;
    internal EntityCoordinates? CoverPosition;
    internal TimeSpan NextDecision;
    internal TimeSpan NextCoverSearch;
    internal int RadialDirection;
    internal TimeSpan NextThreatScan;
    internal readonly List<EntityUid> VisibleShooters = new();
    internal TimeSpan NextAllyScan;
    internal readonly List<EntityUid> NearbyShooters = new();
    internal TimeSpan NextPositionSearch;
    internal TimeSpan PositionUntil;
    [ViewVariables] public EntityCoordinates? FirePosition;
    [ViewVariables] public bool IsFlanking;
    internal TimeSpan TargetStillSince;
    internal Vector2 StillTargetPosition;
    internal int KnownEnemyCount = 1;
    [ViewVariables] public EntityUid? PursuitTarget;
    internal TimeSpan LastSeenUntil;
    internal TimeSpan NextObservation;
    [ViewVariables] public EntityUid? ObservedStorage;
    internal EntityUid HiddenTarget;
    internal TimeSpan InspectUntil;
    internal bool StorageEntryConfirmed;
}

/// <summary>Preferred firing band; may also live on ammunition used by integrated NPC guns.</summary>
[RegisterComponent]
public sealed partial class NPCWeaponTacticsComponent : Component
{
    [DataField] public float MinimumRange = 6f;
    [DataField] public float MaximumRange = 10f;
}

/// <summary>Temporary close-event observer; avoids sharing another system's storage subscription.</summary>
[RegisterComponent]
public sealed partial class NPCObservedStorageComponent : Component;
