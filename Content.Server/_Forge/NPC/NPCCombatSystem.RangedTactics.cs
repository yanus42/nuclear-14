// Forge-Change: keep ranged combat and target-following steering in sync.
using System.Numerics;
using Content.Server._Forge.NPC;
using Content.Server.NPC.Components;
using Robust.Shared.Map;

namespace Content.Server.NPC.Systems;

public sealed partial class NPCCombatSystem
{
    [Dependency] private readonly NPCCombatTacticsSystem _combatTactics = default!;

    internal void ChangeRangedTarget(EntityUid uid, NPCRangedCombatComponent combat, EntityUid target)
    {
        // Other plans can steer the same NPC toward an unrelated position.
        if (TryComp<NPCSteeringComponent>(uid, out var steering) &&
            steering.Coordinates.Equals(new EntityCoordinates(combat.Target, Vector2.Zero)))
        {
            _steering.Register(uid, new EntityCoordinates(target, Vector2.Zero), steering);
            steering.Status = SteeringStatus.Moving;
            steering.FailedPathCount = 0;
            steering.LineOfSightTimer = 0f;
        }

        combat.Target = target;
        combat.LOSAccumulator = 0f;
        combat.TargetInLOS = false;
        combat.ShootAccumulator = 0f;
        if (combat.Status is CombatStatus.TargetUnreachable or CombatStatus.NotInSight)
            combat.Status = CombatStatus.Normal;
    }
}
