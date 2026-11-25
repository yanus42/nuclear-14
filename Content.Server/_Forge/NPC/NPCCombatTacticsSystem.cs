using System.Numerics;
using Content.Server.DoAfter;
using Content.Server.GameTicking.Rules.Components;
using Content.Server.NPC.Components;
using Content.Server.Weapons.Ranged.Systems;
using Content.Shared.Climbing.Components;
using Content.Shared.CCVar;
using Content.Shared.DoAfter;
using Content.Shared.Gravity;
using Content.Shared.Interaction;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.NPC;
using Content.Shared.Physics;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Map;
using Robust.Shared.Configuration;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Forge.NPC;

public sealed partial class NPCCombatTacticsSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _config = default!;
    private bool _enabled;

    public override void Initialize()
    {
        Subs.CVar(_config, CCVars.NPCCombatTacticsEnabled, value => _enabled = value, true);
        InitializeStorageObservation();
    }

    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly GunSystem _guns = default!;
    [Dependency] private readonly SharedMeleeWeaponSystem _melee = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly SharedGravitySystem _gravity = default!;
    [Dependency] private readonly DoAfterSystem _doAfter = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;

    public bool TryGetMovement(EntityUid uid, NPCSteeringComponent steering, TransformComponent xform,
        float speed, out Vector2 direction)
    {
        var wasTactical = TryComp<NPCCombatTacticsComponent>(uid, out var tactics) && tactics.Decision != "Pursuit";
        var result = TryGetMovementInternal(uid, steering, xform, speed, out direction);
        if (!result && wasTactical)
        {
            // The pursuit path predates the maneuver. Resume from the actual position.
            steering.CurrentPath.Clear();
            steering.PathfindToken?.Cancel();
            steering.PathfindToken = null;
            steering.LineOfSightTimer = 0f;
            steering.Status = SteeringStatus.Moving;
            steering.ForceMove = true;
        }
        return result;
    }

    private bool TryGetMovementInternal(EntityUid uid, NPCSteeringComponent steering, TransformComponent xform,
        float speed, out Vector2 direction)
    {
        direction = default;
        if (!TryComp<NPCCombatTacticsComponent>(uid, out var tactics))
            return false;

        // Never contend with another movement owner, player control, sleep, or obstacle interactions.
        if (!_enabled || !HasComp<ActiveNPCComponent>(uid) || HasComp<ActorComponent>(uid) || HasComp<WaveMobComponent>(uid) ||
            HasComp<NPCJukeComponent>(uid) || speed <= 0f ||
            _doAfter.GetStatus(steering.DoAfterId) == DoAfterStatus.Running ||
            TryComp<ClimbingComponent>(uid, out var climb) && (climb.IsClimbing || climb.NextTransition != null) ||
            xform.GridUid is not {} gridUid || xform.ParentUid != gridUid ||
            !TryComp<MapGridComponent>(gridUid, out var grid) || _gravity.IsWeightless(uid, xform: xform))
            return Release(tactics);

        var ranged = TryComp<NPCRangedCombatComponent>(uid, out var gunCombat);
        EntityUid target;
        EntityUid weapon;
        float minimum;
        float maximum;
        if (ranged)
        {
            target = gunCombat!.Target;
            if (!_guns.TryGetGun(uid, out weapon, out var gun))
                return Release(tactics);
            var ammo = new GetAmmoCountEvent();
            RaiseLocalEvent(weapon, ref ammo);
            // Integrated NPC guns recharge between shots; that is not a reason to abandon the firing band.
            if (ammo.Count <= 0 && !HasComp<RechargeBasicEntityAmmoComponent>(weapon))
                return Release(tactics);
            GetFiringBand(weapon, tactics, out minimum, out maximum);
        }
        else if (TryComp<NPCMeleeCombatComponent>(uid, out var meleeCombat) &&
                 _melee.TryGetWeapon(uid, out weapon, out var melee))
        {
            target = meleeCombat.Target;
            minimum = 0f;
            maximum = melee.Range * 0.85f;
        }
        else
            return Release(tactics);

        if (!TryComp<TransformComponent>(target, out var enemy) || enemy.MapID != xform.MapID ||
            !TryComp<MobStateComponent>(target, out var state) || state.CurrentState != MobState.Alive ||
            steering.Coordinates.EntityId != target)
            return Release(tactics);

        var position = _transform.GetWorldPosition(xform);
        var targetPosition = _transform.GetWorldPosition(enemy);
        var delta = targetPosition - position;
        var distance = delta.Length();
        if (distance < 0.05f)
            return Release(tactics);

        if (IsHiddenInStorage(uid, target))
        {
            // Do not follow a concealed body's live coordinates while the HTN switches plans.
            tactics.Decision = "AwaitInspection";
            tactics.Direction = Vector2.Zero;
            steering.Status = SteeringStatus.InRange;
            return true;
        }

        ObserveTarget(uid, tactics, target, distance);

        if (tactics.Target != target || tactics.Weapon != weapon)
        {
            Release(tactics);
            tactics.NextDecision = TimeSpan.Zero;
            tactics.NextCoverSearch = TimeSpan.Zero;
            tactics.NextThreatScan = TimeSpan.Zero;
            tactics.VisibleShooters.Clear();
            tactics.NearbyShooters.Clear();
            tactics.NextAllyScan = TimeSpan.Zero;
            tactics.NextPositionSearch = TimeSpan.Zero;
            tactics.Target = target;
            tactics.Weapon = weapon;
            // A pack starting in one spot should not default to the same side on a tie.
            if (!ranged && tactics.Style == NPCCombatStyle.Skirmish)
                tactics.MovingLeft = (uid.Id & 1) == 0;
            tactics.Anchor = xform.Coordinates;
            tactics.TargetPosition = targetPosition;
        }

        if (_timing.CurTime < tactics.NextDecision)
        {
            direction = tactics.Direction;
            return tactics.Decision != "Pursuit";
        }
        tactics.NextDecision = _timing.CurTime + TimeSpan.FromSeconds(0.2);

        // LOS is checked independently of the combat system's cached flag. Hidden targets use normal pursuit.
        if (!_interaction.InRangeUnobstructed(uid, target, distance + 0.1f))
            return Release(tactics);

        var map = new MapCoordinates(position, xform.MapID);
        var radius = Math.Max(steering.Radius, tactics.MinimumBodyRadius);
        if (ranged && tactics.Style != NPCCombatStyle.Direct) RefreshNearbyShooters(uid, tactics, map);
        if (ranged && tactics.Style == NPCCombatStyle.Humanoid &&
            TryCover(uid, tactics, xform, grid, radius, target, targetPosition,
                minimum, maximum, out direction))
        {
            tactics.FirePosition = null;
            tactics.IsFlanking = false;
            tactics.Direction = direction;
            steering.Status = SteeringStatus.InRange;
            return true;
        }

        if (ranged && tactics.Style != NPCCombatStyle.Direct &&
            TryFirePosition(uid, tactics, xform, grid, radius, target, targetPosition,
                minimum, maximum, out direction))
        {
            tactics.Direction = direction;
            steering.Status = SteeringStatus.InRange;
            return true;
        }
        if (ranged && !HasFiringLine(uid, target, ignoreMobs: true)) return Release(tactics);

        // Seeing through low cover does not mean a melee approach is traversable.
        // A blocked approach belongs to A*, including end barriers and climbing.
        if ((!ranged || distance > maximum) &&
            !CanWalk(uid, target, map, targetPosition, grid, radius))
            return Release(tactics);

        var toward = delta / distance;
        var side = new Vector2(-toward.Y, toward.X);
        var anchor = tactics.Anchor.ToMapPos(EntityManager, _transform);
        if (Vector2.DistanceSquared(targetPosition, tactics.TargetPosition) > 4f ||
            Vector2.DistanceSquared(position, anchor) > 36f)
        {
            tactics.Anchor = xform.Coordinates;
            tactics.TargetPosition = targetPosition;
            anchor = position;
        }
        var amplitude = Math.Clamp(speed * 0.6f, 1.2f, 3f);
        var offset = Vector2.Dot(position - anchor, side);
        if (offset >= amplitude) tactics.MovingLeft = false;
        if (offset <= -amplitude) tactics.MovingLeft = true;

        if (!ranged)
            tactics.RadialDirection = distance > maximum ? 1 : 0;
        else if (distance > maximum)
            tactics.RadialDirection = 1;
        else if (distance < minimum)
            tactics.RadialDirection = -1;
        else if (tactics.RadialDirection == 1 && distance <= maximum - 0.5f ||
                 tactics.RadialDirection == -1 && distance >= minimum + 0.5f)
            tactics.RadialDirection = 0;

        var radial = tactics.RadialDirection;
        if (tactics.Style == NPCCombatStyle.Direct && radial == 0)
        {
            tactics.Decision = "HoldRange";
            tactics.Direction = Vector2.Zero;
            steering.Status = SteeringStatus.InRange;
            return true;
        }
        if (radial < 0 && TryComp<Robust.Shared.Physics.Components.PhysicsComponent>(target, out var pursuer) &&
            -Vector2.Dot(pursuer.LinearVelocity, toward) >= speed)
            radial = 0; // Running straight back cannot open the gap; keep firing and maneuver laterally.
        var lateral = 1f;
        if (radial != 0)
        {
            // Preserve useful closing speed, especially when chasing a faster or fleeing player.
            var required = speed * Math.Clamp(tactics.MinimumClosingFraction, 0.1f, 1f);
            if (TryComp<Robust.Shared.Physics.Components.PhysicsComponent>(target, out var physics))
                required = Math.Max(required, Vector2.Dot(physics.LinearVelocity, toward * radial) + 0.5f);
            required = Math.Min(speed, required);
            lateral = MathF.Sqrt(Math.Max(0f, speed * speed - required * required)) / Math.Max(0.01f, required);
        }
        if (tactics.Style == NPCCombatStyle.Direct && radial != 0) lateral = 0f;
        if (!ranged && tactics.Style == NPCCombatStyle.Skirmish && radial > 0 && distance <= 8f)
            PreferFreeMeleeApproach(uid, tactics, target, map, toward, side);
        var sign = tactics.MovingLeft ? 1f : -1f;
        direction = (toward * radial + side * (lateral * sign)).Normalized();
        var step = Math.Clamp(speed * 0.25f, 0.35f, 0.8f);
        if (!ranged && Vector2.Distance(position + direction * step, targetPosition) > maximum && radial == 0)
            direction = toward;

        if (!CanWalk(uid, target, map, position + direction * step, grid, radius))
        {
            // Reverse only on an actual obstruction; never flip on every tick in a narrow passage.
            direction = (toward * radial - side * (lateral * sign)).Normalized();
            if (lateral == 0f || !CanWalk(uid, target, map, position + direction * step, grid, radius))
            {
                if (!ranged) return Release(tactics);
                // A blocked strafe/retreat is not permission to rush into melee via native pursuit.
                tactics.Decision = "BlockedManeuver";
                tactics.Direction = Vector2.Zero;
                steering.Status = SteeringStatus.InRange;
                return true;
            }
            tactics.MovingLeft = !tactics.MovingLeft;
            tactics.Anchor = xform.Coordinates;
        }
        tactics.Decision = radial > 0 ? "Approach" : radial < 0 ? "Withdraw" : "Strafe";
        tactics.Direction = direction;
        steering.Status = ranged || distance <= maximum ? SteeringStatus.InRange : SteeringStatus.Moving;
        return true;
    }

    private static bool Release(NPCCombatTacticsComponent tactics)
    {
        tactics.Decision = "Pursuit";
        tactics.Direction = Vector2.Zero;
        tactics.Cover = null;
        tactics.CoverPosition = null;
        tactics.FirePosition = null;
        tactics.PositionUntil = TimeSpan.Zero;
        tactics.IsFlanking = false;
        tactics.TargetStillSince = TimeSpan.Zero;
        return false;
    }

    private void GetFiringBand(EntityUid weapon, NPCCombatTacticsComponent tactics, out float minimum, out float maximum)
    {
        NPCWeaponTacticsComponent? profile = CompOrNull<NPCWeaponTacticsComponent>(weapon);
        if (profile == null && TryComp<BasicEntityAmmoProviderComponent>(weapon, out var ammo) &&
            _prototypes.TryIndex<EntityPrototype>(ammo.Proto, out var proto))
            proto.TryGetComponent(out profile);
        minimum = Math.Max(0f, profile?.MinimumRange ?? tactics.MinimumRange);
        maximum = Math.Max(minimum + 1f, profile?.MaximumRange ?? tactics.MaximumRange);
    }

    private bool CanWalk(EntityUid uid, EntityUid target, MapCoordinates start, Vector2 end,
        MapGridComponent grid, float radius)
    {
        var origin = start.Position;
        var delta = end - origin;
        var length = delta.Length();
        if (length < 0.01f) return true;
        var norm = delta / length;
        var side = new Vector2(-norm.Y, norm.X) * radius;
        var (_, mask) = _physics.GetHardCollision(uid);
        for (var i = -1; i <= 1; i++)
        {
            var offset = side * i;
            // Floor samples include both edges of the body, not just the endpoint's tile.
            for (var j = 1; j <= 3; j++)
            {
                var coords = _transform.ToCoordinates(grid.Owner, new MapCoordinates(origin + delta * (j / 3f) + offset, start.MapId));
                if (!grid.TryGetTileRef(coords, out var tile) || tile.Tile.IsEmpty) return false;
            }
            var ray = new CollisionRay(origin + offset, norm, mask);
            foreach (var hit in _physics.IntersectRay(start.MapId, ray, length, uid, returnOnFirstHit: false))
                if (hit.HitEntity != target) return false;
        }
        return true;
    }
}
