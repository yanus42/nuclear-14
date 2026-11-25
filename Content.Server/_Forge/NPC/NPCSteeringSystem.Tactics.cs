// Forge-Change: interpret tactical movement before normal steering.
using System.Numerics;
using Content.Server._Forge.NPC;
using Content.Server.NPC.Components;
using Content.Shared.Movement.Components;

namespace Content.Server.NPC.Systems;

public sealed partial class NPCSteeringSystem
{
    [Dependency] private readonly NPCCombatTacticsSystem _combatTactics = default!;
    [Dependency] private readonly NPCEncounterReturnSystem _encounterReturn = default!;

    // Null leaves movement to pursuit. A zero direction clears input and holds position.
    private Vector2? GetForgeCombatDirection(EntityUid uid, InputMoverComponent mover, NPCSteeringComponent steering,
        TransformComponent xform, float moveSpeed)
    {
        if (!_combatTactics.TryGetMovement(uid, steering, xform, moveSpeed, out var direction))
            return null;

        ResetStuck(steering, xform.Coordinates);
        if (direction == Vector2.Zero)
        {
            Array.Clear(steering.Interest);
            Array.Clear(steering.Danger);
            steering.LastSteerDirection = Vector2.Zero;
            SetDirection(uid, mover, steering, Vector2.Zero, false);
        }
        return direction;
    }
}
