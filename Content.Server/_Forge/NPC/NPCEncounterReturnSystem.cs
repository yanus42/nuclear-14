using Content.Server.DoAfter;
using Content.Server.GameTicking.Rules.Components;
using Content.Server.NPC;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Pathfinding;
using Content.Server.NPC.Systems;
using Content.Shared._NC.Mountable.Components;
using Content.Shared.CCVar;
using Content.Shared.Climbing.Components;
using Content.Shared.DoAfter;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.NPC;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._Forge.NPC;

/// <summary>Runtime state for a tactical NPC that has engaged a player.</summary>
[RegisterComponent]
public sealed partial class NPCEncounterReturnComponent : Component
{
    public EntityCoordinates Home;
    public TimeSpan? SleepGraceUntil;
    public NPCSteeringComponent? ReturnSteering;
    public EntityCoordinates LastPosition;
    public TimeSpan LastMovement;
    public TimeSpan ReturnDeadline;
}

public sealed class NPCEncounterReturnSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _config = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly PathfindingSystem _pathfinding = default!;
    [Dependency] private readonly NPCSteeringSystem _steering = default!;
    [Dependency] private readonly DoAfterSystem _doAfter = default!;

    private const float ArrivalRange = 0.5f;
    private const float CheckInterval = 0.5f;
    private static readonly TimeSpan SleepGrace = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan StuckTimeout = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan ReturnTimeout = TimeSpan.FromMinutes(1);
    private float _elapsed;
    private bool _enabled;

    public override void Initialize()
    {
        Subs.CVar(_config, CCVars.NPCCombatTacticsEnabled, value => _enabled = value, true);
    }

    /// <summary>Remember the position before the first movement towards a player in this encounter.</summary>
    public void RememberStart(EntityUid uid, EntityUid target)
    {
        if (!_enabled || HasComp<NPCEncounterReturnComponent>(uid) || !HasComp<NPCCombatTacticsComponent>(uid) ||
            !HasComp<ActiveNPCComponent>(uid) || !HasComp<ActorComponent>(target) || HasComp<WaveMobComponent>(uid) ||
            TryComp<MountableComponent>(uid, out var mount) && mount.Rider != null ||
            !TryComp<HTNComponent>(uid, out var htn) ||
            !htn.Blackboard.TryGetValue<EntityUid>("Target", out var enemy, EntityManager) || enemy != target ||
            !TryComp<MobStateComponent>(target, out var state) || state.CurrentState != MobState.Alive)
            return;

        var xform = Transform(uid);
        if (xform.GridUid != null && xform.ParentUid == xform.GridUid)
            AddComp<NPCEncounterReturnComponent>(uid).Home = xform.Coordinates;
    }

    public bool DelaySleep(Entity<NPCEncounterReturnComponent> encounter, bool playerNearby)
    {
        if (playerNearby)
        {
            encounter.Comp.SleepGraceUntil = null;
            return false;
        }

        // Give HTN time to drop the combat plan and select the return task at the edge of the sleep radius.
        encounter.Comp.SleepGraceUntil ??= _timing.CurTime + SleepGrace;
        if (_enabled && _timing.CurTime < encounter.Comp.SleepGraceUntil)
            return true;

        Finish(encounter);
        return false;
    }

    public bool CanReturn(EntityUid uid)
    {
        return _enabled && HasComp<NPCEncounterReturnComponent>(uid);
    }

    public void StartReturn(EntityUid uid)
    {
        if (!TryComp<NPCEncounterReturnComponent>(uid, out var encounter))
            return;

        var xform = Transform(uid);
        var position = xform.Coordinates;
        if (xform.GridUid != encounter.Home.EntityId ||
            !position.TryDistance(EntityManager, encounter.Home, out var distance) || distance <= ArrivalRange)
        {
            Finish((uid, encounter));
            return;
        }

        // Discard any target left on the blackboard by an interrupted movement task.
        Comp<HTNComponent>(uid).Blackboard.Remove<EntityUid>("Target");
        var steering = _steering.Register(uid, encounter.Home);
        encounter.ReturnSteering = steering;
        steering.Flags = _pathfinding.GetFlags(uid) | PathFlags.RequireFloor;
        steering.Range = ArrivalRange;
        steering.ArriveOnLineOfSight = false;
        steering.Status = SteeringStatus.Moving;
        encounter.LastPosition = position;
        encounter.LastMovement = _timing.CurTime;
        encounter.ReturnDeadline = _timing.CurTime + ReturnTimeout;
    }

    public bool IsReturning(EntityUid uid)
    {
        return TryComp<NPCEncounterReturnComponent>(uid, out var encounter) && encounter.ReturnSteering != null;
    }

    public void EndReturn(EntityUid uid, bool interrupted)
    {
        if (!TryComp<NPCEncounterReturnComponent>(uid, out var encounter))
            return;

        if (interrupted)
            StopSteering((uid, encounter));
        else
            Finish((uid, encounter));
    }

    private void StopSteering(Entity<NPCEncounterReturnComponent> encounter)
    {
        if (encounter.Comp.ReturnSteering is {} steering &&
            TryComp<NPCSteeringComponent>(encounter, out var activeSteering) && activeSteering == steering &&
            steering.Coordinates.Equals(encounter.Comp.Home))
        {
            if (_doAfter.GetStatus(steering.DoAfterId) == DoAfterStatus.Running)
                _doAfter.Cancel(steering.DoAfterId);
            _steering.Unregister(encounter, steering);
        }

        encounter.Comp.ReturnSteering = null;
    }

    private void Finish(Entity<NPCEncounterReturnComponent> encounter)
    {
        StopSteering(encounter);
        RemComp<NPCEncounterReturnComponent>(encounter);
    }

    public override void Update(float frameTime)
    {
        _elapsed += frameTime;
        if (_elapsed < CheckInterval)
            return;

        _elapsed = 0f;
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<NPCEncounterReturnComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var encounter, out var xform))
        {
            if (!_enabled || !HasComp<ActiveNPCComponent>(uid) ||
                TryComp<MountableComponent>(uid, out var mount) && mount.Rider != null ||
                !encounter.Home.IsValid(EntityManager))
            {
                Finish((uid, encounter));
                continue;
            }

            if (encounter.ReturnSteering is not {} steering)
                continue;

            var position = xform.Coordinates;
            // A route can lead an NPC off the floor at a grid edge, where it cannot walk home.
            if (xform.GridUid != encounter.Home.EntityId ||
                !TryComp<NPCSteeringComponent>(uid, out var activeSteering) || activeSteering != steering ||
                !steering.Coordinates.Equals(encounter.Home) || steering.Status == SteeringStatus.NoPath ||
                !position.TryDistance(EntityManager, encounter.Home, out var distance) || distance <= ArrivalRange ||
                now >= encounter.ReturnDeadline)
            {
                Finish((uid, encounter));
                continue;
            }

            // Waiting for a climb is progress towards the next waypoint.
            if (_doAfter.GetStatus(steering.DoAfterId) == DoAfterStatus.Running ||
                TryComp<ClimbingComponent>(uid, out var climb) && climb.NextTransition != null ||
                position.TryDistance(EntityManager, encounter.LastPosition, out var moved) && moved >= 0.25f)
            {
                encounter.LastPosition = position;
                encounter.LastMovement = now;
            }
            else if (now - encounter.LastMovement >= StuckTimeout)
                Finish((uid, encounter));
        }
    }
}
