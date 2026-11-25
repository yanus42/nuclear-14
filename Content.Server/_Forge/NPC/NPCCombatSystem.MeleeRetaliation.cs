// Forge-Change: retain remembered combat targets beyond the usual 14-tile melee loss radius.
namespace Content.Server.NPC.Systems;

public sealed partial class NPCCombatSystem
{
    [Dependency] private readonly NPCRetaliationSystem _retaliation = default!;

    private bool IsRememberedMeleeAttacker(EntityUid uid, EntityUid target)
    {
        return _retaliation.IsRememberedPlayerAttacker(uid, target) ||
               _combatTactics.TryGetPursuitTarget(uid, out var tracked, out _) && tracked == target;
    }
}
