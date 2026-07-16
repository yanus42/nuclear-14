using Content.Server.GameTicking;
using Content.Server.GameTicking.Events;
using Content.Shared._Forge.WorldTime;
using Content.Shared._NC14.DayNightCycle;
using Robust.Shared.Map;

namespace Content.Server._Forge.WorldTime;

/// <summary>
/// Marks the main map as the single source of time-of-day readings for the round.
/// Additional maps never replace this source.
/// </summary>
public sealed class WorldTimeSourceSystem : EntitySystem
{
    [Dependency] private readonly GameTicker _gameTicker = default!;
    [Dependency] private readonly IMapManager _mapManager = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RoundStartingEvent>(OnRoundStarting);
    }

    private void OnRoundStarting(RoundStartingEvent args)
    {
        var mapId = _gameTicker.DefaultMap;
        if (mapId == MapId.Nullspace || !_mapManager.MapExists(mapId))
        {
            Log.Warning("Unable to select the world time source: the default map does not exist.");
            return;
        }

        var mapUid = _mapManager.GetMapEntityId(mapId);
        if (!HasComp<DayNightCycleComponent>(mapUid))
        {
            Log.Warning($"Unable to select {ToPrettyString(mapUid)} as the world time source: it has no DayNightCycle component.");
            return;
        }

        EnsureComp<WorldTimeSourceComponent>(mapUid);
    }
}
