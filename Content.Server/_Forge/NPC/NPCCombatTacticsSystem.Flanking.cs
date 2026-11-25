using System.Numerics;
using Content.Server.NPC.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;

namespace Content.Server._Forge.NPC;

public sealed partial class NPCCombatTacticsSystem
{
    private bool CanFlank(EntityUid uid, NPCCombatTacticsComponent tactics, EntityUid target, MapCoordinates origin)
    {
        if ((_timing.CurTime - tactics.TargetStillSince).TotalSeconds < 1.5 ||
            TryComp<PhysicsComponent>(target, out var body) && body.LinearVelocity.LengthSquared() > 0.25f)
            return false;

        var participants = 1;
        foreach (var other in tactics.NearbyShooters)
        {
            if (!TryGetPositioningAlly(uid, other, origin.MapId, out var ally, out var combat) || combat.Target != target)
                continue;
            // A live, short lease serializes local flanking attempts without changing anyone's target.
            if (ally.IsFlanking && ally.FirePosition != null && ally.PositionUntil > _timing.CurTime) return false;
            participants++;
        }
        return participants >= 3 && participants > tactics.KnownEnemyCount;
    }

    private int CollectAllyDirections(EntityUid uid, NPCCombatTacticsComponent tactics, EntityUid target,
        MapId map, Vector2 enemy, Span<Vector2> directions)
    {
        var count = 0;
        foreach (var other in tactics.NearbyShooters)
        {
            if (!TryGetPositioningAlly(uid, other, map, out var ally, out var combat) || combat.Target != target)
                continue;
            var otherRadial = AllyPosition(other, ally, map) - enemy;
            if (otherRadial.LengthSquared() > 0.01f)
                directions[count++] = otherRadial.Normalized();
        }
        return count;
    }

    private static float FlankAngleCost(Vector2 radial, ReadOnlySpan<Vector2> allyDirections)
    {
        if (radial.LengthSquared() < 0.01f) return float.MaxValue;
        radial = radial.Normalized();
        var cost = 0f;
        foreach (var allyDirection in allyDirections)
            cost += Math.Max(0f, Vector2.Dot(radial, allyDirection) - 0.5f) * 10f;
        return cost;
    }

    private bool TrySelectFlank(EntityUid uid, NPCCombatTacticsComponent tactics, EntityUid target,
        MapCoordinates origin, Vector2 enemy, MapGridComponent grid, float radius, float minimum, float maximum,
        out Vector2 direction)
    {
        direction = default;
        if (!CanFlank(uid, tactics, target, origin)) return false;
        var radial = origin.Position - enemy;
        Span<Vector2> buffer = stackalloc Vector2[MaxNearbyShooters];
        var countDirections = CollectAllyDirections(uid, tactics, target, origin.MapId, enemy, buffer);
        var allyDirections = buffer[..countDirections];
        var threshold = FlankAngleCost(radial, allyDirections) + AllyPositionCost(uid, tactics, origin) - 0.35f;
        Span<(Vector2 Point, float Score)> candidates = stackalloc (Vector2, float)[4];
        var count = 0;
        for (var i = 0; i < 4; i++)
        {
            var angle = (i % 2 == 0 ? 1f : -1f) * (i < 2 ? 15f : 25f) * MathF.PI / 180f;
            var rotated = new Vector2(radial.X * MathF.Cos(angle) - radial.Y * MathF.Sin(angle),
                radial.X * MathF.Sin(angle) + radial.Y * MathF.Cos(angle));
            var point = new MapCoordinates(enemy + rotated, origin.MapId);
            var travelSquared = Vector2.DistanceSquared(origin.Position, point.Position);
            if (travelSquared > 16f) continue;
            var score = FlankAngleCost(point.Position - enemy, allyDirections) + AllyPositionCost(uid, tactics, point) +
                        MathF.Sqrt(travelSquared) * 0.12f;
            if (score >= threshold) continue;
            candidates[count++] = (point.Position, score);
        }

        // Test the most useful point first. Each physical validation shares the tick budget.
        while (count > 0 && TrySpendTacticsSearch())
        {
            var best = 0;
            for (var i = 1; i < count; i++)
                if (candidates[i].Score < candidates[best].Score) best = i;

            var point = new MapCoordinates(candidates[best].Point, origin.MapId);
            candidates[best] = candidates[--count];
            if (!ValidFirePosition(uid, target, origin, point, enemy, grid, radius, minimum, maximum) ||
                !SafeFlankSegment(uid, tactics, origin, point, enemy, minimum)) continue;

            tactics.FirePosition = _transform.ToCoordinates(grid.Owner, point);
            tactics.IsFlanking = true;
            tactics.PositionUntil = _timing.CurTime + TimeSpan.FromSeconds(3);
            tactics.Decision = "SeekFlank";
            direction = (point.Position - origin.Position).Normalized();
            return true;
        }
        return false;
    }

    private bool SafeFlankSegment(EntityUid uid, NPCCombatTacticsComponent tactics,
        MapCoordinates origin, MapCoordinates point, Vector2 enemy, float minimum)
    {
        var segment = point.Position - origin.Position;
        if (segment.LengthSquared() < 0.01f) return true;
        var along = Math.Clamp(Vector2.Dot(enemy - origin.Position, segment) / segment.LengthSquared(), 0f, 1f);
        var clearance = Math.Max(minimum, Vector2.Distance(origin.Position, enemy) * 0.9f);
        if (Vector2.Distance(enemy, origin.Position + segment * along) < clearance) return false;
        foreach (var threat in tactics.VisibleShooters)
        {
            if (!TryComp<TransformComponent>(threat, out var xform) || xform.MapID != origin.MapId ||
                _containers.IsEntityInContainer(threat)) continue;
            var other = _transform.GetWorldPosition(xform);
            var near = Math.Clamp(Vector2.Dot(other - origin.Position, segment) / segment.LengthSquared(), 0f, 1f);
            if (Vector2.Distance(other, origin.Position + segment * near) <
                Math.Min(Vector2.Distance(other, origin.Position), minimum + 1f) - 0.05f) return false;
            if (ProtectedByCover(uid, origin, other, out _) && !ProtectedByCover(uid, point, other, out _)) return false;
        }
        return true;
    }
}
