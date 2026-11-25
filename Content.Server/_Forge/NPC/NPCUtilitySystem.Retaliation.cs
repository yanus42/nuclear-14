// Forge-Change: check remembered attackers through the usual utility considerations.
using Content.Server.NPC.Queries;

namespace Content.Server.NPC.Systems;

public sealed partial class NPCUtilitySystem
{
    [Dependency] private readonly NPCRetaliationSystem _retaliation = default!;

    // Keep the current attacker unless a much closer visible hostile needs attention.
    private bool TrySelectRetaliationTarget(NPCBlackboard blackboard, UtilityQueryPrototype query,
        HashSet<EntityUid> candidates, EntityUid? current, EntityUid? latest, out EntityUid target)
    {
        target = default;
        if (current is {} currentTarget && candidates.Contains(currentTarget) &&
            CanSelectRetaliationTarget(blackboard, currentTarget, query))
        {
            target = PreferImmediateThreat(blackboard, query, candidates, currentTarget);
            return true;
        }

        if (latest is {} latestTarget && latest != current && candidates.Contains(latestTarget) &&
            CanSelectRetaliationTarget(blackboard, latestTarget, query))
        {
            target = PreferImmediateThreat(blackboard, query, candidates, latestTarget);
            return true;
        }

        return false;
    }

    private bool CanSelectRetaliationTarget(NPCBlackboard blackboard, EntityUid target, UtilityQueryPrototype query)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        if (!TryComp<TransformComponent>(owner, out var xform) ||
            !TryComp<TransformComponent>(target, out var targetXform) ||
            !xform.Coordinates.TryDistance(EntityManager, _transform, targetXform.Coordinates, out var distance))
            return false;

        return PassesCombatConsiderations(blackboard, target, query, distance);
    }

    private EntityUid PreferImmediateThreat(NPCBlackboard blackboard, UtilityQueryPrototype query,
        HashSet<EntityUid> candidates, EntityUid attacker)
    {
        const float immediateRange = 3f;
        const float distanceLead = 1.5f;
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        if (!TryComp<TransformComponent>(owner, out var xform) ||
            !TryComp<TransformComponent>(attacker, out var attackerXform) ||
            !xform.Coordinates.TryDistance(EntityManager, _transform, attackerXform.Coordinates, out var attackerDistance))
            return attacker;

        var bestDistance = Math.Min(immediateRange, attackerDistance - distanceLead);
        if (bestDistance <= 0f) return attacker;

        var best = attacker;
        foreach (var candidate in candidates)
        {
            if (candidate == attacker || !TryComp<TransformComponent>(candidate, out var candidateXform) ||
                !xform.Coordinates.TryDistance(EntityManager, _transform, candidateXform.Coordinates, out var distance) ||
                distance >= bestDistance || !PassesCombatConsiderations(blackboard, candidate, query, null) ||
                !_examine.InRangeUnOccluded(owner, candidate, immediateRange, null))
                continue;

            best = candidate;
            bestDistance = distance;
        }
        return best;
    }

    private bool PassesCombatConsiderations(NPCBlackboard blackboard, EntityUid target, UtilityQueryPrototype query,
        float? extendedVisionDistance)
    {
        foreach (var consideration in query.Considerations)
        {
            var score = GetScore(blackboard, target, consideration, extendedVisionDistance);
            if (GetScore(consideration.Curve, score) <= 0f)
                return false;
        }

        return true;
    }
}
