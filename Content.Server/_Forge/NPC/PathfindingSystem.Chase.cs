// Forge-Change: reject disproportionately long routes around barriers when NPCs chase players.
using System.Numerics;
using Content.Shared.CCVar;
using Content.Shared._Forge.NPC;
using Robust.Shared.Map;
using Robust.Shared.Player;

namespace Content.Server.NPC.Pathfinding;

public sealed partial class PathfindingSystem
{
    /// <summary>
    /// A distant player on an open road can be chased, but a short straight-line distance
    /// must not turn into a long trip around an impassable barrier.
    /// </summary>
    public bool ExceedsPlayerChaseDetour(EntityUid uid, EntityCoordinates start, EntityCoordinates target,
        IReadOnlyList<PathPoly> route)
    {
        if (!HasComp<ProximityNPCComponent>(uid) || !HasComp<ActorComponent>(target.EntityId) || route.Count == 0)
            return false;

        var maxDetour = _configManager.GetCVar(CCVars.NPCMaxPlayerChaseDetour);
        if (maxDetour < 0f)
            return false;

        var startMap = start.ToMap(EntityManager, _transform);
        var targetMap = target.ToMap(EntityManager, _transform);
        if (startMap.MapId != targetMap.MapId)
            return false;

        var directDistance = Vector2.Distance(startMap.Position, targetMap.Position);
        var routeDistance = 0f;
        var previous = startMap.Position;

        foreach (var node in route)
        {
            var waypoint = node.Coordinates.ToMap(EntityManager, _transform);
            if (waypoint.MapId != startMap.MapId)
                return false;

            routeDistance += Vector2.Distance(previous, waypoint.Position);
            if (routeDistance > directDistance + maxDetour)
                return true;

            previous = waypoint.Position;
        }

        routeDistance += Vector2.Distance(previous, targetMap.Position);
        return routeDistance > directDistance + maxDetour;
    }
}
