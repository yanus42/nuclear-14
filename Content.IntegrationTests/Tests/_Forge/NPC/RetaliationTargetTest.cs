// Forge-Change: regression coverage for distant retaliation and normal target checks.
using System.Linq;
using System.Numerics;
using Content.Server.NPC;
using Content.Server.NPC.Components;
using Content.Server.NPC.Systems;
using Content.Shared.Movement.Components;
using Content.Shared.NPC;
using Content.Shared.NPC.Systems;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._Forge.NPC;

[TestFixture]
public sealed class RetaliationTargetTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: ForgeTestRetaliationMob
  components:
  - type: MobState
  - type: NpcFactionMember
    factions: [Passive]
  - type: NPCRetaliation
    attackMemoryLength: 30

- type: entity
  id: ForgeTestRetaliationOccluder
  components:
  - type: Occluder

- type: entity
  id: ForgeTestRetaliationMelee
  parent: ForgeTestRetaliationMob
  components:
  - type: CombatMode
  - type: MeleeWeapon
    damage:
      types:
        Blunt: 1
";

    [TestCase("NearbyMeleeTargets")]
    [TestCase("NearbyGunTargets")]
    public async Task OrdinaryNpcKeepsFriendlyTargetRules(string query)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.EntMan;
        var utility = server.System<NPCUtilitySystem>();
        var retaliation = server.System<NPCRetaliationSystem>();
        var blackboard = new NPCBlackboard();
        var session = server.PlayerMan.Sessions.Single();
        var previousEntity = session.AttachedEntity;

        await server.WaitAssertion(() =>
        {
            var npc = entities.SpawnEntity("ForgeTestRetaliationMob", map.MapCoords);
            var attacker = entities.SpawnEntity("ForgeTestRetaliationMob",
                new MapCoordinates(map.MapCoords.Position + new Vector2(30, 0), map.MapId));
            server.PlayerMan.SetAttachedEntity(session, attacker);
            blackboard.SetValue(NPCBlackboard.Owner, npc);

            Assert.That(retaliation.TryRetaliate((npc, entities.GetComponent<NPCRetaliationComponent>(npc)), attacker),
                Is.False, "NPCs without ProximityNPC must keep the original friendly-fire rule.");
            Assert.That(utility.GetEntities(blackboard, query).GetHighest(), Is.Not.EqualTo(attacker),
                "NPCs outside this feature must not target a distant player through retaliation memory.");

            server.PlayerMan.SetAttachedEntity(session, previousEntity);
        });

        await pair.CleanReturnAsync();
    }

    [TestCase("NearbyMeleeTargets")]
    [TestCase("NearbyGunTargets")]
    public async Task RetaliationPreservesTargetChecks(string query)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.EntMan;
        var utility = server.System<NPCUtilitySystem>();
        var retaliation = server.System<NPCRetaliationSystem>();
        var factions = server.System<NpcFactionSystem>();
        var containers = server.System<SharedContainerSystem>();
        var blackboard = new NPCBlackboard();
        EntityUid npc = default;
        EntityUid attacker = default;
        EntityUid nearby = default;
        EntityUid wall = default;
        var session = server.PlayerMan.Sessions.Single();
        var previousEntity = session.AttachedEntity;

        MapCoordinates Position(float x, float y) => new(map.MapCoords.Position + new Vector2(x, y), map.MapId);

        await server.WaitAssertion(() =>
        {
            npc = entities.SpawnEntity("ForgeTestRetaliationMob", Position(0, 0));
            entities.AddComponent<Content.Shared._Forge.NPC.ProximityNPCComponent>(npc);
            attacker = entities.SpawnEntity("ForgeTestRetaliationMob", Position(30, 0));
            nearby = entities.SpawnEntity("ForgeTestRetaliationMob", Position(0, 2));
            server.PlayerMan.SetAttachedEntity(session, attacker);
            blackboard.SetValue(NPCBlackboard.Owner, npc);
            factions.AggroEntity(npc, nearby);
            factions.AggroEntity(npc, attacker);

            Assert.That(utility.GetEntities(blackboard, query).GetHighest(), Is.EqualTo(nearby),
                "A distant target without retaliation memory must not bypass the normal vision radius.");

            Assert.That(retaliation.TryRetaliate((npc, entities.GetComponent<NPCRetaliationComponent>(npc)), attacker));
            Assert.That(utility.GetEntities(blackboard, query).GetHighest(), Is.EqualTo(attacker),
                "A visible recent attacker must be selectable beyond the normal vision radius.");

            wall = entities.SpawnEntity("ForgeTestRetaliationOccluder", Position(15, 0));
        });

        await pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
        {
            Assert.That(utility.GetEntities(blackboard, query).GetHighest(), Is.EqualTo(nearby),
                "Retaliation must preserve the occlusion check when acquiring a target.");

            blackboard.SetValue("Target", attacker);
            Assert.That(utility.GetEntities(blackboard, query).GetHighest(), Is.EqualTo(attacker),
                "The usual current-target exception must allow continued pursuit behind cover.");

            entities.DeleteEntity(wall);
            var holder = entities.SpawnEntity(null, Position(30, 0));
            var container = containers.EnsureContainer<Container>(holder, "retaliation-test");
            Assert.That(containers.Insert(attacker, container));
            Assert.That(utility.GetEntities(blackboard, query).GetHighest(), Is.EqualTo(nearby),
                "Even the current attacker must pass accessibility checks; an invalid attacker must not block other targets.");

            server.PlayerMan.SetAttachedEntity(session, previousEntity);
        });

        await pair.CleanReturnAsync();
    }

    [TestCase("NearbyMeleeTargets")]
    [TestCase("NearbyGunTargets")]
    public async Task NoPathForgetsDistantAttackerUntilNextHit(string query)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.EntMan;
        var utility = server.System<NPCUtilitySystem>();
        var retaliation = server.System<NPCRetaliationSystem>();
        var factions = server.System<NpcFactionSystem>();
        var blackboard = new NPCBlackboard();
        var session = server.PlayerMan.Sessions.Single();
        var previousEntity = session.AttachedEntity;

        MapCoordinates Position(float x, float y) => new(map.MapCoords.Position + new Vector2(x, y), map.MapId);

        await server.WaitAssertion(() =>
        {
            var npc = entities.SpawnEntity("ForgeTestRetaliationMob", Position(0, 0));
            var attacker = entities.SpawnEntity("ForgeTestRetaliationMob", Position(30, 0));
            var nearby = entities.SpawnEntity("ForgeTestRetaliationMob", Position(0, 2));
            entities.AddComponent<Content.Shared._Forge.NPC.ProximityNPCComponent>(npc);
            server.PlayerMan.SetAttachedEntity(session, attacker);
            blackboard.SetValue(NPCBlackboard.Owner, npc);
            factions.AggroEntity(npc, nearby);

            var component = entities.GetComponent<NPCRetaliationComponent>(npc);
            Assert.That(retaliation.TryRetaliate((npc, component), attacker));
            Assert.That(utility.GetEntities(blackboard, query).GetHighest(), Is.EqualTo(attacker));

            Assert.That(retaliation.ForgetUnreachableAttacker(npc, attacker));
            Assert.That(utility.GetEntities(blackboard, query).GetHighest(), Is.EqualTo(nearby),
                "An unreachable distant attacker must stop being selected.");

            Assert.That(retaliation.TryRetaliate((npc, component), attacker));
            Assert.That(utility.GetEntities(blackboard, query).GetHighest(), Is.EqualTo(attacker),
                "A new hit must start retaliation again.");

            server.PlayerMan.SetAttachedEntity(session, previousEntity);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SteeringNoPathForgetsAttackerAfterMoveToFinishes()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.EntMan;
        var retaliation = server.System<NPCRetaliationSystem>();
        var utility = server.System<NPCUtilitySystem>();
        var blackboard = new NPCBlackboard();
        var session = server.PlayerMan.Sessions.Single();
        var previousEntity = session.AttachedEntity;
        EntityUid npc = default;
        EntityUid attacker = default;

        await server.WaitAssertion(() =>
        {
            npc = entities.SpawnEntity("ForgeTestRetaliationMob", map.MapCoords);
            attacker = entities.SpawnEntity("ForgeTestRetaliationMob",
                new MapCoordinates(map.MapCoords.Position + new Vector2(30, 0), map.MapId));
            entities.AddComponent<Content.Shared._Forge.NPC.ProximityNPCComponent>(npc);
            entities.AddComponent<ActiveNPCComponent>(npc);
            entities.AddComponent<InputMoverComponent>(npc);
            var steering = entities.AddComponent<NPCSteeringComponent>(npc);
            steering.Coordinates = new EntityCoordinates(attacker, Vector2.Zero);
            steering.Status = SteeringStatus.NoPath;
            server.PlayerMan.SetAttachedEntity(session, attacker);
            blackboard.SetValue(NPCBlackboard.Owner, npc);

            var component = entities.GetComponent<NPCRetaliationComponent>(npc);
            Assert.That(retaliation.TryRetaliate((npc, component), attacker));
            Assert.That(utility.GetEntities(blackboard, "NearbyMeleeTargets").GetHighest(), Is.EqualTo(attacker));
        });

        await pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
        {
            Assert.That(retaliation.TryGetPlayerAttacker(npc, out _), Is.False,
                "A completed MoveTo task must not leave an unreachable distant target in retaliation memory.");
            Assert.That(utility.GetEntities(blackboard, "NearbyMeleeTargets").GetHighest(), Is.Not.EqualTo(attacker));
            server.PlayerMan.SetAttachedEntity(session, previousEntity);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task MeleeKeepsChasingRememberedAttackerBeyondLostRange()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.EntMan;
        var retaliation = server.System<NPCRetaliationSystem>();
        var session = server.PlayerMan.Sessions.Single();
        var previousEntity = session.AttachedEntity;
        EntityUid npc = default;
        EntityUid attacker = default;

        await server.WaitAssertion(() =>
        {
            npc = entities.SpawnEntity("ForgeTestRetaliationMelee", map.MapCoords);
            attacker = entities.SpawnEntity("ForgeTestRetaliationMob",
                new MapCoordinates(map.MapCoords.Position + new Vector2(30, 0), map.MapId));
            entities.AddComponent<Content.Shared._Forge.NPC.ProximityNPCComponent>(npc);
            entities.AddComponent<ActiveNPCComponent>(npc);
            server.PlayerMan.SetAttachedEntity(session, attacker);

            var component = entities.GetComponent<NPCRetaliationComponent>(npc);
            Assert.That(retaliation.TryRetaliate((npc, component), attacker));
            entities.AddComponent<NPCMeleeCombatComponent>(npc).Target = attacker;
        });

        await pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
        {
            Assert.That(entities.GetComponent<NPCMeleeCombatComponent>(npc).Status,
                Is.EqualTo(CombatStatus.TargetOutOfRange),
                "The melee system must keep steering toward a distant remembered attacker.");

            Assert.That(retaliation.ForgetUnreachableAttacker(npc, attacker));
        });

        await pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
        {
            Assert.That(entities.GetComponent<NPCMeleeCombatComponent>(npc).Status,
                Is.EqualTo(CombatStatus.TargetUnreachable),
                "The usual 14-tile limit must apply again after retaliation ends.");
            server.PlayerMan.SetAttachedEntity(session, previousEntity);
        });

        await pair.CleanReturnAsync();
    }
}
