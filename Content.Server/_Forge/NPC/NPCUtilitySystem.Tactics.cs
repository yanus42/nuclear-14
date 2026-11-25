namespace Content.Server.NPC.Systems;

public sealed partial class NPCUtilitySystem
{
    [Dependency] private readonly Content.Server._Forge.NPC.NPCCombatTacticsSystem _combatTactics = default!;

    private EntityUid? AddTrackedCombatTarget(NPCBlackboard blackboard, string query, HashSet<EntityUid> candidates,
        out float distance)
    {
        distance = 0f;
        if (query is not ("NearbyMeleeTargets" or "NearbyGunTargets") ||
            !_combatTactics.TryGetPursuitTarget(blackboard.GetValue<EntityUid>(NPCBlackboard.Owner), out var target, out distance))
            return null;

        candidates.Add(target);
        return target;
    }
}
