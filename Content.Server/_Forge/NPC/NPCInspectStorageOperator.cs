using System.Threading;
using System.Threading.Tasks;
using Content.Server.NPC;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Server.NPC.Systems;
using Content.Shared.Interaction;

namespace Content.Server._Forge.NPC;

/// <summary>Investigate only a storage entity whose entry was witnessed; use normal navigation and access.</summary>
public sealed partial class NPCInspectStorageOperator : HTNOperator, IHtnConditionalShutdown
{
    [Dependency] private readonly IEntityManager _entities = default!;
    public HTNPlanState ShutdownState { get; } = HTNPlanState.TaskFinished;

    public override Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard, CancellationToken cancelToken)
    {
        var uid = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        var valid = _entities.System<NPCCombatTacticsSystem>().TryGetObservedStorage(uid, out _, out var hidden);
        Dictionary<string, object>? effects = valid ? new() { ["Target"] = hidden } : null;
        return Task.FromResult((valid, effects));
    }

    public override void Startup(NPCBlackboard blackboard)
    {
        base.Startup(blackboard);
        var uid = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        if (!_entities.System<NPCCombatTacticsSystem>().TryGetObservedStorage(uid, out var box, out _)) return;
        var steering = _entities.System<NPCSteeringSystem>().Register(uid, _entities.GetComponent<TransformComponent>(box).Coordinates);
        steering.Range = SharedInteractionSystem.InteractionRange - 0.1f;
        steering.ArriveOnLineOfSight = false;
        steering.ForceMove = true;
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        var uid = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        if (_entities.TryGetComponent<NPCSteeringComponent>(uid, out var steering) && steering.Status == SteeringStatus.NoPath)
            return HTNOperatorStatus.Failed;
        return _entities.System<NPCCombatTacticsSystem>().InspectObservedStorage(uid);
    }

    public void ConditionalShutdown(NPCBlackboard blackboard)
    {
        var uid = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        _entities.System<NPCCombatTacticsSystem>().ForgetObservedStorage(uid);
        _entities.System<NPCSteeringSystem>().Unregister(uid);
        blackboard.Remove<EntityUid>("Target");
    }
}
