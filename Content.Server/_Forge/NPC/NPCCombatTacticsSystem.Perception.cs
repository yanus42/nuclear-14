using System.Numerics;
using Content.Shared.Examine;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.NPC;
using Content.Shared._Forge.Structures.BallisticCover;
using Content.Shared.Physics;
using Robust.Shared.Containers;
using Robust.Shared.Physics;
using Content.Server.Storage.Components;
using Content.Server.GameTicking.Rules.Components;
using Robust.Shared.Player;
using Robust.Shared.Map;

namespace Content.Server._Forge.NPC;

public sealed partial class NPCCombatTacticsSystem
{
    [Dependency] private readonly ExamineSystemShared _examine = default!;
    [Dependency] private readonly SharedContainerSystem _containers = default!;

    private void ObserveTarget(EntityUid uid, NPCCombatTacticsComponent tactics, EntityUid target, float distance)
    {
        if (_timing.CurTime < tactics.NextObservation) return;
        tactics.NextObservation = _timing.CurTime + TimeSpan.FromSeconds(0.2);
        if (distance > tactics.PursuitRange || _containers.IsEntityInContainer(target) ||
            !_examine.InRangeUnOccluded(uid, target, tactics.PursuitRange, null)) return;
        tactics.PursuitTarget = target;
        tactics.LastSeenUntil = _timing.CurTime + TimeSpan.FromSeconds(Math.Max(0f, tactics.TargetMemorySeconds));
    }

    /// <summary>Keep an already observed enemy, without extending initial acquisition or seeing into storage.</summary>
    public bool TryGetPursuitTarget(EntityUid uid, out EntityUid target, out float distance)
    {
        target = default;
        distance = 0f;
        if (!_enabled || !HasComp<ActiveNPCComponent>(uid) || HasComp<ActorComponent>(uid) || HasComp<WaveMobComponent>(uid) ||
            !TryComp<NPCCombatTacticsComponent>(uid, out var tactics) || tactics.LastSeenUntil <= _timing.CurTime ||
            tactics.PursuitTarget is not {} tracked || _containers.IsEntityInContainer(tracked) ||
            !TryComp<MobStateComponent>(tracked, out var state) || state.CurrentState != MobState.Alive ||
            _factions.IsEntityFriendly(uid, tracked) ||
            !TryComp<TransformComponent>(uid, out var owner) || !TryComp<TransformComponent>(tracked, out var enemy) ||
            !owner.Coordinates.TryDistance(EntityManager, _transform, enemy.Coordinates, out distance) ||
            distance > tactics.PursuitRange) return false;
        target = tracked;
        return true;
    }

    public void ForgetPursuitTarget(EntityUid uid, EntityUid target)
    {
        if (!TryComp<NPCCombatTacticsComponent>(uid, out var tactics) || tactics.PursuitTarget != target) return;
        tactics.PursuitTarget = null;
        tactics.LastSeenUntil = TimeSpan.Zero;
    }

    public bool IsHiddenInStorage(EntityUid uid, EntityUid target)
    {
        return _enabled && !HasComp<ActorComponent>(uid) && !HasComp<WaveMobComponent>(uid) &&
               TryComp<NPCCombatTacticsComponent>(uid, out var tactics) && tactics.Style == NPCCombatStyle.Humanoid &&
               HasComp<MobStateComponent>(target) &&
               _containers.TryGetContainingContainer(target, out var container) &&
               TryComp<EntityStorageComponent>(container.Owner, out var storage) && !storage.Open;
    }

    /// <summary>Visibility alone is insufficient for a fence. Nearby low cover uses the projectile bypass rule.</summary>
    public bool HasFiringLine(EntityUid uid, EntityUid target, bool ignoreMobs = false)
    {
        if (!_enabled || HasComp<ActorComponent>(uid) || HasComp<WaveMobComponent>(uid) ||
            !HasComp<NPCCombatTacticsComponent>(uid)) return true;
        if (_containers.IsEntityInContainer(target) || !TryComp<TransformComponent>(uid, out var xform) ||
            !TryComp<TransformComponent>(target, out var enemy) || xform.MapID != enemy.MapID) return false;
        return HasFiringLineFrom(uid, target, new MapCoordinates(_transform.GetWorldPosition(xform), xform.MapID), ignoreMobs);
    }

    private bool HasFiringLineFrom(EntityUid uid, EntityUid target, MapCoordinates origin, bool ignoreMobs = false)
    {
        if (!TryComp<TransformComponent>(target, out var enemy) || enemy.MapID != origin.MapId) return false;
        var start = origin.Position;
        var delta = _transform.GetWorldPosition(enemy) - start;
        var length = delta.Length();
        if (length < 0.01f) return true;
        var ray = new CollisionRay(start, delta / length,
            (int) (CollisionGroup.BulletImpassable | CollisionGroup.LowCoverImpassable | CollisionGroup.Impassable));
        foreach (var hit in _physics.IntersectRay(origin.MapId, ray, length, uid, returnOnFirstHit: false))
        {
            if (hit.HitEntity == target) continue;
            // A body can interrupt a shot without making the shooter's firing distance invalid.
            if (ignoreMobs && HasComp<MobStateComponent>(hit.HitEntity)) continue;
            if (TryComp<BallisticCoverComponent>(hit.HitEntity, out var cover) && hit.Distance <= cover.PassRange) continue;
            return false;
        }
        return true;
    }
}
