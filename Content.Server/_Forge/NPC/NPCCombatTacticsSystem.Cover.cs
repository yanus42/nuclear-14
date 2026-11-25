using System.Numerics;
using Content.Server.NPC.Components;
using Content.Shared._Forge.Structures.BallisticCover;
using Content.Shared.Physics;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.NPC.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;

namespace Content.Server._Forge.NPC;

public sealed partial class NPCCombatTacticsSystem
{
    private readonly HashSet<Entity<BallisticCoverComponent>> _covers = new();
    private readonly HashSet<EntityUid> _threats = new();
    private readonly HashSet<Entity<NPCCombatTacticsComponent>> _neighbors = new();
    [Dependency] private readonly NpcFactionSystem _factions = default!;

    private bool TryCover(EntityUid uid, NPCCombatTacticsComponent tactics, TransformComponent xform,
        MapGridComponent grid, float radius, EntityUid target, Vector2 enemy, float minimum, float maximum,
        out Vector2 direction)
    {
        direction = default;
        var position = _transform.GetWorldPosition(xform);
        var origin = new MapCoordinates(position, xform.MapID);
        var enemyCoordinates = new MapCoordinates(enemy, xform.MapID);
        var range = Vector2.Distance(position, enemy);
        if (_timing.CurTime >= tactics.NextThreatScan && TrySpendTacticsSearch())
        {
            tactics.NextThreatScan = _timing.CurTime + TimeSpan.FromSeconds(0.8);
            tactics.VisibleShooters.Clear();
            _factions.GetNearbyHostiles(uid, 10f, _threats);
            foreach (var threat in _threats)
            {
                if (threat == target || !_guns.TryGetGun(threat, out _, out _) ||
                    !TryComp<MobStateComponent>(threat, out var state) || state.CurrentState != MobState.Alive ||
                    !_interaction.InRangeUnobstructed(uid, threat, 10.1f)) continue;
                tactics.VisibleShooters.Add(threat);
                if (tactics.VisibleShooters.Count >= 4) break;
            }
        }
        if (range >= minimum - 0.5f && range <= maximum + 0.5f &&
            HasFiringLineFrom(uid, target, origin, ignoreMobs: true) &&
            ProtectedByCover(uid, origin, enemy, out var current) &&
            SafeFromOtherShooters(uid, tactics, origin))
        {
            tactics.Cover = current;
            tactics.CoverPosition = null;
            tactics.Decision = "HoldCover";
            return true;
        }

        if (tactics.CoverPosition is {} selected && tactics.Cover is {} selectedCover && Exists(selectedCover))
        {
            var spot = selected.ToMap(EntityManager, _transform);
            var distance = Vector2.Distance(spot.Position, enemy);
            if (spot.MapId == origin.MapId && distance >= minimum && distance <= maximum &&
                ProtectedByCover(uid, spot, enemy, out var blocker) && blocker == selectedCover &&
                HasFiringLineFrom(uid, target, spot) &&
                SafeFromOtherShooters(uid, tactics, spot) && !Reserved(uid, spot) &&
                CanWalk(uid, target, origin, spot.Position, grid, radius))
            {
                direction = spot.Position - position;
                direction = direction.LengthSquared() < 0.01f ? Vector2.Zero : direction.Normalized();
                tactics.Decision = "SeekCover";
                return true;
            }
        }
        tactics.Cover = null;
        tactics.CoverPosition = null;
        if (_timing.CurTime < tactics.NextCoverSearch || !TrySpendTacticsSearch()) return false;
        tactics.NextCoverSearch = _timing.CurTime + TimeSpan.FromSeconds(0.8);

        _covers.Clear();
        _lookup.GetEntitiesInRange(origin, tactics.CoverSearchRange, _covers);
        var best = float.MaxValue;
        var count = 0;
        foreach (var (coverUid, cover) in _covers)
        {
            if (!TryComp<FixturesComponent>(coverUid, out var fixtures) ||
                !fixtures.Fixtures.TryGetValue(cover.Fixture, out var fixture)) continue;
            if (++count > 8) break;
            var pose = new Robust.Shared.Physics.Transform(_transform.GetWorldPosition(coverUid), _transform.GetWorldRotation(coverUid));
            var bounds = fixture.Shape.ComputeAABB(pose, 0);
            var away = bounds.Center - enemy;
            if (away.LengthSquared() < 0.01f) continue;
            away = away.Normalized();
            var extent = (Math.Abs(away.X) * bounds.Width + Math.Abs(away.Y) * bounds.Height) * 0.5f;
            var point = bounds.Center + away * (extent + radius + 0.2f);
            var distance = Vector2.Distance(point, enemy);
            var travel = Vector2.Distance(position, point);
            if (distance < minimum || distance > maximum || travel > tactics.CoverSearchRange) continue;
            var spot = new MapCoordinates(point, origin.MapId);
            var score = travel + cover.PenetrationChance * 3f + AllyPositionCost(uid, tactics, spot);
            if (score >= best ||
                !ProtectedByCover(uid, spot, enemy, out var blocker) || blocker != coverUid ||
                !SafeFromOtherShooters(uid, tactics, spot) || Reserved(uid, spot) || !HasFiringLineFrom(uid, target, spot) ||
                !_interaction.InRangeUnobstructed(spot, enemyCoordinates, distance + 0.1f, predicate: e => e == uid || e == target) ||
                !CanWalk(uid, target, origin, point, grid, radius)) continue;
            best = score;
            tactics.Cover = coverUid;
            tactics.CoverPosition = _transform.ToCoordinates(grid.Owner, spot);
        }
        if (tactics.CoverPosition == null) return false;
        direction = (tactics.CoverPosition.Value.ToMapPos(EntityManager, _transform) - position).Normalized();
        tactics.Decision = "SeekCover";
        return true;
    }

    private bool Reserved(EntityUid uid, MapCoordinates spot)
    {
        _neighbors.Clear();
        _lookup.GetEntitiesInRange(spot, 0.8f, _neighbors);
        foreach (var (other, tactics) in _neighbors)
            if (other != uid && IsPositioningAlly(uid, other) && tactics.Decision == "HoldCover") return true;
        // An approaching NPC can reserve a point before physically arriving there.
        _neighbors.Clear();
        _lookup.GetEntitiesInRange(spot, 5f, _neighbors);
        foreach (var (other, tactics) in _neighbors)
        {
            if (other == uid || !IsPositioningAlly(uid, other) ||
                !(tactics.Decision == "SeekCover" && tactics.CoverPosition is {} || tactics.PositionUntil > _timing.CurTime) ||
                (tactics.CoverPosition ?? tactics.FirePosition) is not {} point)
                continue;

            var reserved = point.ToMap(EntityManager, _transform);
            if (reserved.MapId == spot.MapId && Vector2.DistanceSquared(reserved.Position, spot.Position) < 0.64f)
                return true;
        }
        return false;
    }

    private bool SafeFromOtherShooters(EntityUid uid, NPCCombatTacticsComponent tactics, MapCoordinates position)
    {
        foreach (var other in tactics.VisibleShooters)
        {
            if (!TryComp<TransformComponent>(other, out var xform) || xform.MapID != position.MapId ||
                !TryComp<MobStateComponent>(other, out var state) || state.CurrentState != MobState.Alive) continue;
            var point = _transform.GetWorldPosition(xform);
            if (!_interaction.InRangeUnobstructed(position, other, Vector2.Distance(point, position.Position) + 0.1f,
                    predicate: e => e == uid)) continue;
            if (!ProtectedByCover(uid, position, point, out _)) return false;
        }
        return true;
    }

    private bool ProtectedByCover(EntityUid uid, MapCoordinates position, Vector2 enemy, out EntityUid coverUid)
    {
        coverUid = default;
        var delta = position.Position - enemy;
        var length = delta.Length();
        if (length < 0.01f) return false;
        var ray = new CollisionRay(enemy, delta / length, (int) CollisionGroup.LowCoverImpassable);
        foreach (var hit in _physics.IntersectRay(position.MapId, ray, length, uid, returnOnFirstHit: false))
        {
            if (!TryComp<BallisticCoverComponent>(hit.HitEntity, out var cover) ||
                !TryComp<PhysicsComponent>(hit.HitEntity, out var body) || !body.CanCollide ||
                hit.Distance <= cover.PassRange || length - hit.Distance > cover.PassRange) continue;
            coverUid = hit.HitEntity;
            return true;
        }
        return false;
    }
}
