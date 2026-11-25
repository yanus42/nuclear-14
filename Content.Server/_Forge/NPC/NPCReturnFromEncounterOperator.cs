using System.Threading;
using System.Threading.Tasks;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;

namespace Content.Server._Forge.NPC;

public sealed partial class NPCReturnFromEncounterOperator : HTNOperator
{
    [Dependency] private readonly IEntityManager _entities = default!;

    public override Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard, CancellationToken cancelToken)
    {
        var uid = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        return Task.FromResult((_entities.System<NPCEncounterReturnSystem>().CanReturn(uid), (Dictionary<string, object>?) null));
    }

    public override void Startup(NPCBlackboard blackboard)
    {
        var uid = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        _entities.System<NPCEncounterReturnSystem>().StartReturn(uid);
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        var uid = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        return _entities.System<NPCEncounterReturnSystem>().IsReturning(uid)
            ? HTNOperatorStatus.Continuing
            : HTNOperatorStatus.Finished;
    }

    public override void TaskShutdown(NPCBlackboard blackboard, HTNOperatorStatus status)
    {
        var uid = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        _entities.System<NPCEncounterReturnSystem>().EndReturn(uid, status == HTNOperatorStatus.BetterPlan);
    }
}
