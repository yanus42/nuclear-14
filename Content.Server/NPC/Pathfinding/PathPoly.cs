using Content.Shared.NPC;
using Robust.Shared.Map;

namespace Content.Server.NPC.Pathfinding;

public sealed class PathPoly : IEquatable<PathPoly>
{
    [ViewVariables]
    public readonly EntityUid GraphUid;

    [ViewVariables]
    public readonly Vector2i ChunkOrigin;

    [ViewVariables]
    public readonly byte TileIndex;

    [ViewVariables]
    public readonly Box2 Box;

    [ViewVariables]
    public PathfindingData Data;

    [ViewVariables]
    public readonly HashSet<PathPoly> Neighbors;

    // Forge-Change: server-side identity of a single barrier, excluding mixed obstacles.
    public readonly EntityUid? Barrier;
    public readonly bool BarrierCanOpen;
    public readonly EntityUid? ClimbObstacle;
    public readonly float ClimbDelay;

    public PathPoly(EntityUid graphUid, Vector2i chunkOrigin, byte tileIndex, Box2 vertices, PathfindingData data, HashSet<PathPoly> neighbors,
        EntityUid? barrier = null, bool barrierCanOpen = false, EntityUid? climbObstacle = null, float climbDelay = 0f) // Forge-Change
    {
        GraphUid = graphUid;
        ChunkOrigin = chunkOrigin;
        TileIndex = tileIndex;
        Box = vertices;
        Data = data;
        Neighbors = neighbors;
        Barrier = barrier; // Forge-Change
        BarrierCanOpen = barrierCanOpen; // Forge-Change
        ClimbObstacle = climbObstacle; // Forge-Change
        ClimbDelay = climbDelay; // Forge-Change
    }

    public bool IsValid()
    {
        return (Data.Flags & PathfindingBreadcrumbFlag.Invalid) == 0x0;
    }

    [ViewVariables]
    public EntityCoordinates Coordinates => new(GraphUid, Box.Center);

    // Explicitly don't check neighbors.

    public bool IsEquivalent(PathPoly other)
    {
        return GraphUid.Equals(other.GraphUid) &&
               ChunkOrigin.Equals(other.ChunkOrigin) &&
               TileIndex == other.TileIndex &&
               Data.IsEquivalent(other.Data) && Barrier == other.Barrier && BarrierCanOpen == other.BarrierCanOpen && // Forge-Change
               ClimbObstacle == other.ClimbObstacle && ClimbDelay.Equals(other.ClimbDelay) && // Forge-Change
               Box.Equals(other.Box);
    }

    public bool Equals(PathPoly? other)
    {
        return other != null &&
               GraphUid.Equals(other.GraphUid) &&
               ChunkOrigin.Equals(other.ChunkOrigin) &&
               TileIndex == other.TileIndex &&
               Data.Equals(other.Data) && Barrier == other.Barrier && BarrierCanOpen == other.BarrierCanOpen && // Forge-Change
               ClimbObstacle == other.ClimbObstacle && ClimbDelay.Equals(other.ClimbDelay) && // Forge-Change
               Box.Equals(other.Box);
    }

    public override bool Equals(object? obj)
    {
        return ReferenceEquals(this, obj) || obj is PathPoly other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(GraphUid, ChunkOrigin, TileIndex, Box);
    }
}
