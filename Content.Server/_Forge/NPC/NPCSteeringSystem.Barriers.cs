// Forge-Change: open accessible barriers, or make a bounded attempt to break them.
using System.Numerics;
using Content.Server._Forge.NPC;
using Content.Server.NPC.Components;
using Content.Server.NPC.Pathfinding;
using Content.Server.Storage.Components;
using Content.Server.Storage.EntitySystems;
using Content.Shared.CombatMode;
using Content.Shared.Climbing.Components;
using Content.Shared.Damage;
using Content.Shared.DoAfter;
using Content.Shared.Doors.Components;
using Content.Shared.Doors.Systems;
using Content.Shared.Interaction;
using Content.Shared.NPC;
using Content.Shared.Physics;
using Robust.Shared.Physics.Components;

namespace Content.Server.NPC.Systems;

public sealed partial class NPCSteeringSystem
{
    [Dependency] private readonly SharedDoorSystem _barrierDoors = default!;
    [Dependency] private readonly EntityStorageSystem _barrierStorage = default!;

    private bool IsForgeChaseObstructed(EntityUid uid, NPCSteeringComponent steering, float distance)
    {
        if ((steering.Flags & (PathFlags.OpenBarriers | PathFlags.BreakBarriers)) == 0)
            return false;

        var (_, mask) = _physics.GetHardCollision(uid);
        return !_interaction.InRangeUnobstructed(uid, steering.Coordinates, distance + 0.1f,
            (CollisionGroup) mask);
    }

    private SteeringObstacleStatus? HandleUpcomingForgeBarrier(EntityUid uid, NPCSteeringComponent steering)
    {
        if ((steering.Flags & (PathFlags.OpenBarriers | PathFlags.BreakBarriers)) == 0)
            return null;
        var inspected = 0;
        foreach (var node in steering.CurrentPath)
        {
            if (++inspected > 8)
                break;
            if (!node.IsValid())
                return null;
            if (node.Barrier is not {} barrier || !Exists(barrier) || Terminating(barrier))
                continue;
            if (TryComp<DoorComponent>(barrier, out var door) && door.State is DoorState.Open or DoorState.Opening ||
                TryComp<EntityStorageComponent>(barrier, out var storage) && storage.Open && !storage.IsCollidableWhenOpen)
                continue;
            var range = SharedInteractionSystem.InteractionRange;
            if ((steering.Flags & PathFlags.BreakBarriers) != 0 && _melee.TryGetWeapon(uid, out _, out var weapon))
                range = Math.Min(range, weapon.Range);
            if (!_interaction.InRangeUnobstructed(uid, barrier, range))
                return null;
            return HandleForgeBarrier(uid, steering, node);
        }
        return null;
    }

    private float ForgeObstacleRange(EntityUid uid, NPCSteeringComponent steering, PathPoly poly)
    {
        var range = SharedInteractionSystem.InteractionRange - 0.05f;
        if (poly.Barrier != null && (steering.Flags & PathFlags.BreakBarriers) != 0 &&
            _melee.TryGetWeapon(uid, out _, out var weapon))
            range = Math.Min(range, weapon.Range * 0.75f);
        return range;
    }

    private void RepathForgeBarrier(EntityUid uid, NPCSteeringComponent steering, TransformComponent xform)
    {
        steering.CurrentPath.Clear();
        steering.PathfindToken?.Cancel();
        steering.PathfindToken = null;
        steering.DoAfterId = null;
        if (xform.Coordinates.TryDistance(EntityManager, steering.Coordinates, out var distance))
            RequestPath(uid, steering, xform, distance);
    }

    private bool TryClimbUpcomingForgeObstacle(EntityUid uid, NPCSteeringComponent steering,
        TransformComponent xform, PhysicsComponent body)
    {
        if ((steering.Flags & PathFlags.Climbing) == 0 ||
            (steering.Flags & (PathFlags.OpenBarriers | PathFlags.BreakBarriers)) == 0 ||
            !TryComp<ClimbingComponent>(uid, out var climbing) || !climbing.CanClimb)
            return false;

        if (climbing.NextTransition != null || _doAfter.GetStatus(steering.DoAfterId) == DoAfterStatus.Running)
            return true;

        if (climbing.IsClimbing || steering.CurrentPath.Count == 0)
            return false;

        var (layer, mask) = _physics.GetHardCollision(uid);
        var inspected = 0;
        foreach (var node in steering.CurrentPath)
        {
            if (++inspected > 8 || !node.IsValid() || node.GraphUid != xform.ParentUid)
                return false;
            if ((node.Data.CollisionMask & layer) == 0 && (node.Data.CollisionLayer & mask) == 0)
                continue;

            // Only consider the first solid obstacle on the route. Doors use their own handler.
            if (node.Barrier != null || (node.Data.Flags & PathfindingBreadcrumbFlag.Climb) == 0 ||
                Vector2.DistanceSquared(node.Box.Center, xform.LocalPosition) > 4f)
                return false;

            var candidates = _entSetPool.Get();
            _lookup.GetLocalEntitiesIntersecting(node.GraphUid, node.Box.Enlarged(-0.01f), candidates,
                flags: LookupFlags.Static);
            try
            {
                foreach (var candidate in candidates)
                {
                    if (!TryComp<ClimbableComponent>(candidate, out var obstacle) ||
                        !TryComp<PhysicsComponent>(candidate, out var obstacleBody) ||
                        !obstacleBody.Hard || !obstacleBody.CanCollide ||
                        ((obstacleBody.CollisionMask & layer) == 0 && (obstacleBody.CollisionLayer & mask) == 0) ||
                        !_climb.CanVault(obstacle, uid, candidate, out _))
                        continue;

                    // Stop input while braking so avoidance can't restart movement and cancel the climb.
                    if (body.LinearVelocity.LengthSquared() > 0.01f)
                        return true;
                    if (!_climb.TryClimb(uid, uid, candidate, out var id, obstacle, climbing))
                        continue;

                    steering.DoAfterId = id;
                    return true;
                }
                return false;
            }
            finally
            {
                _entSetPool.Return(candidates);
            }
        }
        return false;
    }

    private SteeringObstacleStatus? HandleForgeBarrier(EntityUid uid, NPCSteeringComponent steering, PathPoly poly)
    {
        if (poly.Barrier is not {} barrier ||
            (steering.Flags & (PathFlags.OpenBarriers | PathFlags.BreakBarriers)) == 0 ||
            !TryComp<NPCBarrierComponent>(uid, out var policy))
            return null;

        if (!Exists(barrier) || Terminating(barrier))
            return SteeringObstacleStatus.Completed;

        if (policy.BlockedBarriers.TryGetValue(barrier, out var retry) && retry > _timing.CurTime)
            return SteeringObstacleStatus.Repath;

        if (policy.CurrentBarrier != barrier)
        {
            policy.CurrentBarrier = barrier;
            policy.AttemptUntil = _timing.CurTime + TimeSpan.FromSeconds(policy.AttemptSeconds);
            policy.AwaitingDamage = false;
            policy.IneffectiveHits = 0;
        }
        if (_timing.CurTime >= policy.AttemptUntil)
            return BlockForgeBarrier(policy, barrier);

        if (!_interaction.InRangeUnobstructed(uid, barrier, SharedInteractionSystem.InteractionRange))
        {
            // Wait briefly for a passing entity; the attempt deadline prevents indefinite waiting.
            return SteeringObstacleStatus.Continuing;
        }

        if (policy.OpenBarriers)
        {
            var opened = TryComp<DoorComponent>(barrier, out var door)
                ? door.State == DoorState.Open || door.State == DoorState.Opening ||
                  (door.ClickOpen || door.BumpOpen) && _barrierDoors.TryOpen(barrier, door, uid, quiet: true)
                : TryComp<EntityStorageComponent>(barrier, out var storage) && !storage.IsCollidableWhenOpen &&
                  (storage.Open || _barrierStorage.TryOpenStorage(uid, barrier, silent: true));
            if (!opened)
                return BlockForgeBarrier(policy, barrier);
            policy.CurrentBarrier = null;
            return SteeringObstacleStatus.Continuing;
        }

        if (!_melee.TryGetWeapon(uid, out var weaponUid, out var weapon) ||
            !TryComp<DamageableComponent>(barrier, out var damage) ||
            !TryComp<CombatModeComponent>(uid, out var combat))
            return BlockForgeBarrier(policy, barrier);

        if (weapon.NextAttack > _timing.CurTime)
            return SteeringObstacleStatus.Continuing;

        if (policy.AwaitingDamage)
        {
            policy.IneffectiveHits = damage.TotalDamage.Float() > policy.PreviousDamage
                ? 0 : policy.IneffectiveHits + 1;
            policy.AwaitingDamage = false;
            if (policy.IneffectiveHits >= policy.IneffectiveHitLimit)
                return BlockForgeBarrier(policy, barrier);
        }
        var wasInCombat = combat.IsInCombatMode;
        policy.PreviousDamage = damage.TotalDamage.Float();
        _combat.SetInCombatMode(uid, true, combat);
        var attacked = _melee.AttemptLightAttack(uid, weaponUid, weapon, barrier);
        _combat.SetInCombatMode(uid, wasInCombat, combat);
        if (!attacked)
            return BlockForgeBarrier(policy, barrier);

        policy.AwaitingDamage = true;
        return SteeringObstacleStatus.Continuing;
    }

    private SteeringObstacleStatus BlockForgeBarrier(NPCBarrierComponent policy, EntityUid barrier)
    {
        // Bounded storage; state survives replanning so failure does not immediately restart the attempt.
        if (policy.BlockedBarriers.Count >= 16)
            policy.BlockedBarriers.Clear();
        policy.BlockedBarriers[barrier] = _timing.CurTime + TimeSpan.FromSeconds(policy.RetrySeconds);
        policy.CurrentBarrier = null;
        return SteeringObstacleStatus.Repath;
    }
}
