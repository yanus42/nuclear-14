using Content.Shared._Forge.ResourceExtractor;
using Content.Shared.Maps;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Forge.ResourceExtractor;

/// <summary>
/// Rolls optional production events and performs their telegraphed world spawn.
/// Mobile entities never block a spawn tile; only anchored entities with hard
/// fixtures do, preventing players from suppressing events by body-blocking.
/// </summary>
public sealed class ResourceExtractorSpawnEventSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    private ResourceExtractorSettingsPrototype Settings =>
        _prototypes.Index<ResourceExtractorSettingsPrototype>(ResourceExtractorSettingsPrototype.DefaultId);

    public bool ValidateEvents(ResourceExtractorProductionPrototype production, out string? error)
    {
        if (production.Events.Count > Settings.MaxEventsPerProduction)
        {
            error = $"production {production.ID} defines {production.Events.Count} events; " +
                    $"the global limit is {Settings.MaxEventsPerProduction}";
            return false;
        }

        var totalSpawns = 0L;
        foreach (var spawnEvent in production.Events)
        {
            if (spawnEvent.Amount < 1)
            {
                error = $"production {production.ID} has invalid event amount {spawnEvent.Amount}";
                return false;
            }

            totalSpawns += spawnEvent.Amount;
            if (totalSpawns > Settings.MaxEventSpawnsPerProduction)
            {
                error = $"production {production.ID} can spawn {totalSpawns} event entities per cycle; " +
                        $"the global limit is {Settings.MaxEventSpawnsPerProduction}";
                return false;
            }

            if (!float.IsFinite(spawnEvent.ChancePerRoll) || spawnEvent.ChancePerRoll is < 0f or > 1f)
            {
                error = $"production {production.ID} has invalid chancePerRoll {spawnEvent.ChancePerRoll}";
                return false;
            }

            if (spawnEvent.TelegraphDuration <= TimeSpan.Zero ||
                spawnEvent.TelegraphDuration > Settings.MaxTelegraphDuration)
            {
                error = $"production {production.ID} has telegraph duration {spawnEvent.TelegraphDuration}; " +
                        $"the allowed range is greater than zero and at most {Settings.MaxTelegraphDuration}";
                return false;
            }

            if (spawnEvent.SpawnRadius < 1 || spawnEvent.SpawnRadius > Settings.MaxEventSpawnRadius)
            {
                error = $"production {production.ID} has spawn radius {spawnEvent.SpawnRadius}; " +
                        $"the allowed range is 1 to {Settings.MaxEventSpawnRadius}";
                return false;
            }

            if (!_prototypes.HasIndex<EntityPrototype>(spawnEvent.Entity))
            {
                error = $"production {production.ID} references missing event entity {spawnEvent.Entity}";
                return false;
            }

            if (!_prototypes.HasIndex<EntityPrototype>(spawnEvent.Telegraph))
            {
                error = $"production {production.ID} references missing telegraph {spawnEvent.Telegraph}";
                return false;
            }
        }

        error = null;
        return true;
    }

    public void RollEvents(EntityUid extractor, ResourceExtractorProductionPrototype production)
    {
        var reservedTiles = new HashSet<Vector2i>();
        foreach (var spawnEvent in production.Events)
        {
            if (!_random.Prob(spawnEvent.ChancePerRoll))
                continue;

            for (var i = 0; i < spawnEvent.Amount; i++)
            {
                if (!TryFindSpawnCoordinates(
                        extractor,
                        spawnEvent.SpawnRadius,
                        reservedTiles,
                        out var coordinates,
                        out var tile))
                {
                    break;
                }

                reservedTiles.Add(tile);
                var effect = Spawn(spawnEvent.Telegraph, coordinates);
                var pending = EnsureComp<ResourceExtractorBurrowEffectComponent>(effect);
                pending.SpawnPrototype = spawnEvent.Entity;
                pending.SpawnCoordinates = coordinates;
                pending.SpawnAt = _timing.CurTime + spawnEvent.TelegraphDuration;
            }
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var remainingBudget = Settings.MaxEventOperationsPerTick;
        var query = EntityQueryEnumerator<ResourceExtractorBurrowEffectComponent>();
        while (remainingBudget > 0 && query.MoveNext(out var uid, out var component))
        {
            if (component.SpawnAt > _timing.CurTime)
                continue;

            remainingBudget--;

            if (IsAvailableSpawnTile(component.SpawnCoordinates))
                Spawn(component.SpawnPrototype, component.SpawnCoordinates);

            // No retry: if construction occupied the tile during the warning,
            // the event is resolved without spawning its entity.
            QueueDel(uid);
        }
    }

    private bool TryFindSpawnCoordinates(
        EntityUid extractor,
        int radius,
        HashSet<Vector2i> reservedTiles,
        out EntityCoordinates coordinates,
        out Vector2i selectedTile)
    {
        coordinates = default;
        selectedTile = default;
        if (!Transform(extractor).Coordinates.TryGetTileRef(out var originRef) ||
            originRef is not { } origin ||
            !TryComp(origin.GridUid, out MapGridComponent? grid))
        {
            return false;
        }

        var candidates = new List<Vector2i>();
        for (var x = -radius; x <= radius; x++)
        {
            for (var y = -radius; y <= radius; y++)
            {
                if (x == 0 && y == 0 || x * x + y * y > radius * radius)
                    continue;

                candidates.Add(origin.GridIndices + new Vector2i(x, y));
            }
        }

        _random.Shuffle(candidates);
        foreach (var candidate in candidates)
        {
            if (reservedTiles.Contains(candidate) ||
                !grid.TryGetTileRef(candidate, out var tile) ||
                tile.Tile.IsEmpty ||
                IsBlockedByConstruction(tile, grid))
            {
                continue;
            }

            coordinates = grid.GridTileToLocal(candidate);
            selectedTile = candidate;
            return true;
        }

        return false;
    }

    private bool IsAvailableSpawnTile(EntityCoordinates coordinates)
    {
        if (!coordinates.TryGetTileRef(out var tileRef) || tileRef is not { } tile || tile.Tile.IsEmpty ||
            !TryComp(tile.GridUid, out MapGridComponent? grid))
        {
            return false;
        }

        return !IsBlockedByConstruction(tile, grid);
    }

    private bool IsBlockedByConstruction(TileRef tile, MapGridComponent grid)
    {
        foreach (var entity in grid.GetAnchoredEntities(tile.GridIndices))
        {
            if (!TryComp<FixturesComponent>(entity, out var fixtures))
                continue;

            foreach (var fixture in fixtures.Fixtures.Values)
            {
                if (fixture.Hard)
                    return true;
            }
        }

        return false;
    }
}
