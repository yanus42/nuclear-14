// Forge-Change: release distant retaliation when steering cannot reach the target.
using Content.Server.NPC.Components;

namespace Content.Server.NPC.Systems;

public sealed partial class NPCSteeringSystem
{
    private bool ForgetUnreachableAttacker(EntityUid uid, NPCSteeringComponent steering)
    {
        _combatTactics.ForgetPursuitTarget(uid, steering.Coordinates.EntityId);
        return EntityManager.System<NPCRetaliationSystem>()
            .ForgetUnreachableAttacker(uid, steering.Coordinates.EntityId);
    }
}
