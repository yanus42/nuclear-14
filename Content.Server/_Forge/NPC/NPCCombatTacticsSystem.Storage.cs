using Content.Server.GameTicking.Rules.Components;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Server.Storage.Components;
using Content.Server.Storage.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.NPC;
using Content.Shared.Storage.Components;
using Robust.Shared.Player;

namespace Content.Server._Forge.NPC;

public sealed partial class NPCCombatTacticsSystem
{
    private const float StorageObservationRange = 45f;

    [Dependency] private readonly HTNSystem _htn = default!;
    [Dependency] private readonly EntityStorageSystem _storage = default!;
    private readonly HashSet<Entity<NPCCombatTacticsComponent>> _observers = new();

    private void InitializeStorageObservation()
    {
        // Contents have not yet been inserted/hidden at this point; test actual observation here.
        SubscribeLocalEvent<EntityStorageComponent, StorageBeforeCloseEvent>(OnStorageClosing);
        SubscribeLocalEvent<NPCObservedStorageComponent, StorageAfterCloseEvent>(OnStorageClosed);
    }

    private void OnStorageClosing(Entity<EntityStorageComponent> storage, ref StorageBeforeCloseEvent args)
    {
        if (!_enabled) return;
        _observers.Clear();
        _lookup.GetEntitiesInRange(_transform.GetMapCoordinates(storage), StorageObservationRange, _observers);
        foreach (var (uid, tactics) in _observers)
        {
            if (tactics.Style != NPCCombatStyle.Humanoid || !HasComp<ActiveNPCComponent>(uid) ||
                HasComp<ActorComponent>(uid) || HasComp<WaveMobComponent>(uid) ||
                !TryComp<NPCBarrierComponent>(uid, out var policy) || !policy.OpenBarriers ||
                !TryComp<HTNComponent>(uid, out var htn)) continue;
            var target = TryComp<NPCRangedCombatComponent>(uid, out var ranged) ? ranged.Target :
                TryComp<NPCMeleeCombatComponent>(uid, out var melee) ? melee.Target : tactics.PursuitTarget;
            if (target is not {} enemy || !args.Contents.Contains(enemy) ||
                !TryComp<MobStateComponent>(enemy, out var state) || state.CurrentState != MobState.Alive ||
                !_examine.InRangeUnOccluded(uid, enemy, StorageObservationRange, null)) continue;
            tactics.ObservedStorage = storage.Owner;
            tactics.HiddenTarget = enemy;
            tactics.StorageEntryConfirmed = false;
            EnsureComp<NPCObservedStorageComponent>(storage.Owner);
            tactics.InspectUntil = _timing.CurTime + TimeSpan.FromSeconds(Math.Max(0f, tactics.StorageMemorySeconds));
            _htn.Replan(htn);
        }
    }

    private void OnStorageClosed(Entity<NPCObservedStorageComponent> ent, ref StorageAfterCloseEvent args)
    {
        RemCompDeferred<NPCObservedStorageComponent>(ent.Owner);
        if (!TryComp<EntityStorageComponent>(ent.Owner, out var component)) return;
        Entity<EntityStorageComponent> storage = (ent.Owner, component);
        _observers.Clear();
        _lookup.GetEntitiesInRange(_transform.GetMapCoordinates(storage), StorageObservationRange, _observers);
        foreach (var (uid, tactics) in _observers)
        {
            if (tactics.ObservedStorage != storage.Owner || tactics.StorageEntryConfirmed) continue;
            // Confirm the witnessed entry once. Later decisions must not peek into current contents.
            if (storage.Comp.Contents.Contains(tactics.HiddenTarget)) tactics.StorageEntryConfirmed = true;
            else ForgetObservedStorage(uid);
        }
    }

    public bool TryGetObservedStorage(EntityUid uid, out EntityUid storage, out EntityUid hiddenTarget)
    {
        storage = default;
        hiddenTarget = default;
        if (!_enabled || !HasComp<ActiveNPCComponent>(uid) || HasComp<ActorComponent>(uid) || HasComp<WaveMobComponent>(uid) ||
            !TryComp<NPCCombatTacticsComponent>(uid, out var tactics) || tactics.InspectUntil <= _timing.CurTime ||
            tactics.ObservedStorage is not {} observed ||
            !TryComp<EntityStorageComponent>(observed, out var component) || component.Open ||
            !tactics.StorageEntryConfirmed ||
            !TryComp<TransformComponent>(uid, out var xform) || !TryComp<TransformComponent>(observed, out var box) ||
            xform.MapID != box.MapID) return false;
        if (TryComp<MobStateComponent>(tactics.HiddenTarget, out var state) && state.CurrentState == MobState.Alive &&
            !_containers.IsEntityInContainer(tactics.HiddenTarget) &&
            _examine.InRangeUnOccluded(uid, tactics.HiddenTarget, StorageObservationRange, null))
        {
            // A visible escape is new evidence. An unseen escape still leaves the known crate to inspect.
            var distance = (_transform.GetWorldPosition(xform) - _transform.GetWorldPosition(tactics.HiddenTarget)).Length();
            ObserveTarget(uid, tactics, tactics.HiddenTarget, distance);
            ForgetObservedStorage(uid);
            return false;
        }
        storage = observed;
        hiddenTarget = tactics.HiddenTarget;
        return true;
    }

    public HTNOperatorStatus InspectObservedStorage(EntityUid uid)
    {
        if (!TryGetObservedStorage(uid, out var storage, out _)) return HTNOperatorStatus.Finished;
        if (!_interaction.InRangeUnobstructed(uid, storage, SharedInteractionSystem.InteractionRange - 0.1f))
            return HTNOperatorStatus.Continuing;
        // Normal access, welding, locks and opening events apply; knowledge is not permission to bypass them.
        return _storage.TryOpenStorage(uid, storage, silent: true) ? HTNOperatorStatus.Finished : HTNOperatorStatus.Failed;
    }

    public void ForgetObservedStorage(EntityUid uid)
    {
        if (!TryComp<NPCCombatTacticsComponent>(uid, out var tactics)) return;
        tactics.ObservedStorage = null;
        tactics.InspectUntil = TimeSpan.Zero;
        tactics.StorageEntryConfirmed = false;
    }
}
