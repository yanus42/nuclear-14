// Forge-Change: cache barrier identity in graph polygons, not physics queries in A*.
using Content.Server._Forge.NPC;
using Content.Server.GameTicking.Rules.Components;
using Content.Server.Storage.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Movement.Components;
using Content.Shared.NPC;
using Content.Shared.Traits.Assorted.Components;
using Robust.Shared.Collections;
using System.Numerics;

namespace Content.Server.NPC.Pathfinding;

public sealed partial class PathfindingSystem
{
    private bool TrySampleThinForgeObstacle(Box2 bounds, Vector2 point, out Vector2 sample)
    {
        sample = point;
        if (Math.Min(bounds.Width, bounds.Height) >= 1f / SubStep ||
            !bounds.Enlarged(StepOffset - 0.01f).Contains(point))
            return false;

        // Verify the sample against the actual fixture shape in the caller.
        sample = bounds.Width < bounds.Height
            ? new Vector2(bounds.Center.X, point.Y)
            : new Vector2(point.X, bounds.Center.Y);
        return true;
    }

    private (EntityUid? Obstacle, float ClimbDelay) GetForgeObstacle(ValueList<EntityUid> entities, Box2 box)
    {
        EntityUid? obstacle = null;
        var mixed = false;
        var climbDelay = 0f;
        foreach (var uid in entities)
        {
            if (!_fixturesQuery.TryGetComponent(uid, out var fixtures))
                continue;
            var intersects = false;
            foreach (var fixture in fixtures.Fixtures.Values)
            {
                if (!fixture.Hard || (fixture.CollisionMask & PathfindingCollisionLayer) == 0 &&
                    (fixture.CollisionLayer & PathfindingCollisionMask) == 0)
                    continue;
                foreach (var proxy in fixture.Proxies)
                    intersects |= proxy.AABB.Intersects(box.Enlarged(-0.01f));
            }
            if (!intersects)
                continue;
            // Mixed fixtures lose their individual identity, but a fence still takes time to climb.
            mixed |= obstacle != null;
            obstacle = uid;
            if (_climbableQuery.TryGetComponent(uid, out var climbable))
                climbDelay = Math.Max(climbDelay, climbable.ClimbDelay);
        }
        return (mixed ? null : obstacle, climbDelay);
    }

    private float GetForgeClimbCostPerSecond(EntityUid uid, PathFlags flags)
    {
        if ((flags & PathFlags.Climbing) == 0 ||
            (flags & (PathFlags.OpenBarriers | PathFlags.BreakBarriers | PathFlags.RequireFloor)) == 0)
            return 0f;

        var sprintSpeed = TryComp<MovementSpeedModifierComponent>(uid, out var movement)
            ? movement.CurrentSprintSpeed
            : MovementSpeedModifierComponent.DefaultBaseSprintSpeed;
        var climbModifier = TryComp<ClimbDelayModifierComponent>(uid, out var climb) ? climb.ClimbDelayMultiplier : 1f;
        return Math.Max(0f, sprintSpeed * climbModifier);
    }

    private bool CanOpenForgeBarrier(EntityUid? uid)
    {
        return uid is {} barrier &&
               (_doorQuery.TryGetComponent(barrier, out var door) && (door.ClickOpen || door.BumpOpen) ||
                TryComp<EntityStorageComponent>(barrier, out var storage) && !storage.IsCollidableWhenOpen);
    }

    private PathFlags GetForgeBarrierFlags(NPCBlackboard blackboard)
    {
        if (!blackboard.TryGetValue<EntityUid>(NPCBlackboard.Owner, out var uid, EntityManager) ||
            HasComp<WaveMobComponent>(uid) || !TryComp<NPCBarrierComponent>(uid, out var policy))
            return PathFlags.None;

        if (blackboard.TryGetValue<EntityUid>("Target", out var target, EntityManager) &&
            TryComp<MobStateComponent>(target, out var state) && state.CurrentState == MobState.Alive)
            return policy.OpenBarriers ? PathFlags.OpenBarriers : PathFlags.BreakBarriers;

        return policy.OpenBarriers && TryComp<NPCEncounterReturnComponent>(uid, out var encounter) &&
               encounter.ReturnSteering != null
            ? PathFlags.OpenBarriers
            : PathFlags.None;
    }

    private HashSet<EntityUid>? GetBlockedForgeBarriers(EntityUid uid)
    {
        if (HasComp<WaveMobComponent>(uid) || !TryComp<NPCBarrierComponent>(uid, out var policy))
            return null;
        HashSet<EntityUid>? blocked = null;
        foreach (var (barrier, until) in policy.BlockedBarriers)
        {
            if (until <= _timing.CurTime || !Exists(barrier))
                continue;
            blocked ??= new HashSet<EntityUid>();
            blocked.Add(barrier);
        }
        return blocked;
    }
}
