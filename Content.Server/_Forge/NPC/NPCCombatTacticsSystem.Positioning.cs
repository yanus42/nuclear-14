using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Content.Server.GameTicking.Rules.Components;
using Content.Server.NPC.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.NPC;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;

namespace Content.Server._Forge.NPC;

public sealed partial class NPCCombatTacticsSystem
{
    private const int MaxNearbyShooters = 16;

    private uint _tacticsSearchTick;
    private int _tacticsSearchCount;

    // Bound bursts when many NPCs acquire a target together. Basic firing-distance control never waits.
    private bool TrySpendTacticsSearch()
    {
        if (_tacticsSearchTick != _timing.CurTick.Value)
        {
            _tacticsSearchTick = _timing.CurTick.Value;
            _tacticsSearchCount = 0;
        }
        if (_tacticsSearchCount >= 8) return false;
        _tacticsSearchCount++;
        return true;
    }

    private void RefreshNearbyShooters(EntityUid uid, NPCCombatTacticsComponent tactics, MapCoordinates origin)
    {
        if (_timing.CurTime < tactics.NextAllyScan || !TrySpendTacticsSearch()) return;
        tactics.NextAllyScan = _timing.CurTime + TimeSpan.FromSeconds(1);
        tactics.NearbyShooters.Clear();
        _neighbors.Clear();
        _lookup.GetEntitiesInRange(origin, 10f, _neighbors);
        foreach (var (other, _) in _neighbors)
        {
            if (other == uid || !IsPositioningAlly(uid, other) ||
                !_examine.InRangeUnOccluded(uid, other, 10f, null)) continue;
            tactics.NearbyShooters.Add(other);
            if (tactics.NearbyShooters.Count >= MaxNearbyShooters) break;
        }
        tactics.KnownEnemyCount = 1; // The acquired target is already known, even beyond the local scan radius.
        if (tactics.Style != NPCCombatStyle.Humanoid || tactics.NearbyShooters.Count < 2) return;
        _threats.Clear();
        _factions.GetNearbyHostiles(uid, 10f, _threats);
        foreach (var threat in _threats)
        {
            if (threat == tactics.Target || _containers.IsEntityInContainer(threat) ||
                !TryComp<MobStateComponent>(threat, out var state) || state.CurrentState != MobState.Alive ||
                !_examine.InRangeUnOccluded(uid, threat, 10f, null)) continue;
            if (++tactics.KnownEnemyCount > MaxNearbyShooters) break;
        }
    }

    private bool IsPositioningAlly(EntityUid uid, EntityUid other)
    {
        return HasComp<NPCRangedCombatComponent>(other) && IsActiveCombatAlly(uid, other);
    }

    private bool TryGetPositioningAlly(EntityUid uid, EntityUid other, MapId map,
        [NotNullWhen(true)] out NPCCombatTacticsComponent? tactics, [NotNullWhen(true)] out NPCRangedCombatComponent? combat)
    {
        tactics = null;
        return TryComp(other, out combat) && IsActiveCombatAlly(uid, other) && TryComp(other, out tactics) &&
               TryComp<TransformComponent>(other, out var xform) && xform.MapID == map;
    }

    private bool IsActiveCombatAlly(EntityUid uid, EntityUid other)
    {
        return HasComp<ActiveNPCComponent>(other) && !HasComp<ActorComponent>(other) &&
               !HasComp<WaveMobComponent>(other) &&
               TryComp<MobStateComponent>(other, out var state) && state.CurrentState == MobState.Alive &&
               _factions.IsEntityFriendly(uid, other);
    }

    private void PreferFreeMeleeApproach(EntityUid uid, NPCCombatTacticsComponent tactics, EntityUid target,
        MapCoordinates origin, Vector2 toward, Vector2 side)
    {
        if (_timing.CurTime < tactics.NextAllyScan || !TrySpendTacticsSearch()) return;
        // A sprinting pack can reach bite range within one second.
        tactics.NextAllyScan = _timing.CurTime + TimeSpan.FromSeconds(0.4);

        var center = origin.Position + toward;
        var left = center + side * 0.75f;
        var right = center - side * 0.75f;
        var leftCost = 0f;
        var rightCost = 0f;
        var count = 0;
        _neighbors.Clear();
        _lookup.GetEntitiesInRange(origin, 3f, _neighbors);
        foreach (var (other, _) in _neighbors)
        {
            if (other == uid || !TryComp<NPCMeleeCombatComponent>(other, out var melee) ||
                melee.Target != target || !IsActiveCombatAlly(uid, other)) continue;

            var position = _transform.GetWorldPosition(other);
            leftCost += Math.Max(0f, 2.25f - Vector2.DistanceSquared(left, position));
            rightCost += Math.Max(0f, 2.25f - Vector2.DistanceSquared(right, position));
            if (++count >= 8) break;
        }

        if (Math.Abs(leftCost - rightCost) > 0.25f)
            tactics.MovingLeft = leftCost < rightCost;
    }

    private Vector2 AllyPosition(EntityUid other, NPCCombatTacticsComponent tactics, MapId map)
    {
        var reserved = tactics.CoverPosition ?? (tactics.PositionUntil > _timing.CurTime ? tactics.FirePosition : null);
        if (reserved is {} point)
        {
            var coordinates = point.ToMap(EntityManager, _transform);
            if (coordinates.MapId == map)
                return coordinates.Position;
        }
        return _transform.GetWorldPosition(other);
    }

    // A soft preference, not an assigned enemy or a compulsory sector around the target.
    private float AllyPositionCost(EntityUid uid, NPCCombatTacticsComponent tactics, MapCoordinates point)
    {
        var cost = 0f;
        foreach (var other in tactics.NearbyShooters)
        {
            if (!TryGetPositioningAlly(uid, other, point.MapId, out var ally, out var combat)) continue;
            var position = AllyPosition(other, ally, point.MapId);
            cost += Math.Max(0f, 1.75f - Vector2.Distance(point.Position, position)) * 3f;
            if (_containers.IsEntityInContainer(combat.Target) ||
                !TryComp<TransformComponent>(combat.Target, out var enemy) || enemy.MapID != point.MapId) continue;
            var shot = _transform.GetWorldPosition(enemy) - position;
            if (shot.LengthSquared() < 0.01f) continue;
            var along = Vector2.Dot(point.Position - position, shot) / shot.LengthSquared();
            if (along > 0.05f && along < 0.95f)
                cost += Math.Max(0f, 0.75f - Vector2.Distance(point.Position, position + shot * along)) * 4f;
        }
        return cost;
    }

    private bool TryFirePosition(EntityUid uid, NPCCombatTacticsComponent tactics, TransformComponent xform,
        MapGridComponent grid, float radius, EntityUid target, Vector2 enemy, float minimum, float maximum,
        out Vector2 direction)
    {
        direction = default;
        var origin = new MapCoordinates(_transform.GetWorldPosition(xform), xform.MapID);
        if (tactics.TargetStillSince == TimeSpan.Zero || Vector2.DistanceSquared(enemy, tactics.StillTargetPosition) > 0.5625f)
        {
            tactics.TargetStillSince = _timing.CurTime;
            tactics.StillTargetPosition = enemy;
        }
        // Closing/retreating and obstacle traversal take precedence over group spacing.
        var distance = Vector2.Distance(origin.Position, enemy);
        if (distance < minimum || distance > maximum)
        {
            tactics.FirePosition = null;
            tactics.IsFlanking = false;
            return false;
        }
        if (tactics.FirePosition is {} chosen && tactics.PositionUntil > _timing.CurTime)
        {
            var point = chosen.ToMap(EntityManager, _transform);
            if ((!tactics.IsFlanking || CanFlank(uid, tactics, target, origin)) &&
                ValidFirePosition(uid, target, origin, point, enemy, grid, radius, minimum, maximum) &&
                (!tactics.IsFlanking || SafeFlankSegment(uid, tactics, origin, point, enemy, minimum)))
            {
                direction = point.Position - origin.Position;
                if (direction.LengthSquared() > 0.16f)
                {
                    direction = direction.Normalized();
                    tactics.Decision = tactics.IsFlanking ? "SeekFlank" : "SeekFirePosition";
                    return true;
                }
                // Resume the existing combat maneuver around this position, rather than stand still.
                tactics.Anchor = chosen;
                tactics.IsFlanking = false;
                return false;
            }
        }
        tactics.FirePosition = null;
        tactics.IsFlanking = false;
        if (_timing.CurTime < tactics.NextPositionSearch || tactics.NearbyShooters.Count == 0 || !TrySpendTacticsSearch()) return false;
        tactics.NextPositionSearch = _timing.CurTime + TimeSpan.FromSeconds(1.2);
        if (tactics.Style == NPCCombatStyle.Humanoid &&
            TrySelectFlank(uid, tactics, target, origin, enemy, grid, radius, minimum, maximum, out direction))
            return true;
        var current = AllyPositionCost(uid, tactics, origin);
        if (current < 1f) return false;
        var best = current - 0.75f; // A meaningful improvement is required before abandoning the present position.
        var side = new Vector2(-(enemy - origin.Position).Y, (enemy - origin.Position).X).Normalized();
        for (var i = 0; i < 6; i++)
        {
            var offset = side * ((i % 2 == 0 ? 1f : -1f) * (1.5f + i / 2));
            var point = new MapCoordinates(origin.Position + offset, origin.MapId);
            var score = AllyPositionCost(uid, tactics, point) + offset.Length() * 0.25f;
            if (score >= best || !ValidFirePosition(uid, target, origin, point, enemy, grid, radius, minimum, maximum)) continue;
            best = score;
            tactics.FirePosition = _transform.ToCoordinates(grid.Owner, point);
        }
        if (tactics.FirePosition is not {} selected) return false;
        tactics.PositionUntil = _timing.CurTime + TimeSpan.FromSeconds(3);
        direction = (selected.ToMapPos(EntityManager, _transform) - origin.Position).Normalized();
        tactics.Decision = "SeekFirePosition";
        return true;
    }

    private bool ValidFirePosition(EntityUid uid, EntityUid target,
        MapCoordinates origin, MapCoordinates point, Vector2 enemy, MapGridComponent grid, float radius,
        float minimum, float maximum)
    {
        var distance = Vector2.Distance(point.Position, enemy);
        return point.MapId == origin.MapId && distance >= minimum && distance <= maximum &&
               Vector2.DistanceSquared(point.Position, origin.Position) <= 16f &&
               HasFiringLineFrom(uid, target, point) &&
               CanWalk(uid, target, origin, point.Position, grid, radius);
    }
}
