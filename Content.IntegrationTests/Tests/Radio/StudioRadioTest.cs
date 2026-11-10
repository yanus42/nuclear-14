#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Client.UserInterface.Systems.Chat;
using Content.Server._Forge.Radio;
using Content.Server._Nuclear14.CartridgeLoader.Cartridges;
using Content.Server.CartridgeLoader;
using Content.Server.Power.Components;
using Content.Server.Radio.Components;
using Content.Server.Radio.EntitySystems;
using Content.Shared._Forge.Radio;
using Content.Shared._Nuclear14.CartridgeLoader.Cartridges;
using Content.Shared.ADT.TapeRecorder.Components;
using Content.Shared.ADT.TapeRecorder.Events;
using Content.Shared.Audio.Jukebox;
using Content.Shared.CartridgeLoader;
using Content.Shared.Chat;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.DeviceLinking;
using Content.Shared.Inventory;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests.Radio;

[TestFixture]
public sealed class StudioRadioTest
{
    [TestPrototypes]
    private const string Tape = """
        - type: entity
          id: StudioRadioTestCassette
          parent: N14CassetteTape
          components:
          - type: TapeCassette
            recordedData:
            - timestamp: 0.1
              name: Test Reporter
              message: Interview from the wasteland.
              language: English
        """;

    [Test]
    public async Task MusicFollowsTheStudioFrequencyAndPlaybackClock()
    {
        await using var pair = await PoolManager.GetServerClient();
        var (server, client) = pair;
        await client.WaitAssertion(() =>
        {
            var cache = client.ResolveDependency<IResourceCache>();
            foreach (var track in client.ProtoMan.EnumeratePrototypes<JukeboxPrototype>())
            {
                var stream = cache.GetResource<AudioResource>(track.PositionalPath.Path).AudioStream;
                Assert.That(stream.ChannelCount, Is.EqualTo(1), track.ID);
            }
        });

        var map = await pair.CreateTestMap();
        var entities = server.EntMan;
        var radios = server.System<RadioDeviceSystem>();
        var links = server.System<SharedDeviceLinkSystem>();
        var timing = server.ResolveDependency<IGameTiming>();
        EntityUid jukebox = default;
        EntityUid contender = default;
        EntityUid operatorUid = default;
        EntityUid microphone = default;
        EntityUid first = default;
        EntityUid second = default;
        EntityUid other = default;

        await server.WaitPost(() =>
        {
            jukebox = entities.SpawnEntity("Jukebox", map.GridCoords);
            contender = entities.SpawnEntity("Jukebox", map.GridCoords);
            operatorUid = entities.SpawnEntity("MobHuman", map.GridCoords);
            microphone = entities.SpawnEntity("RadioDJMicrophone", map.GridCoords);
            first = entities.SpawnEntity("N14RadioTable", map.GridCoords);
            second = entities.SpawnEntity("N14RadioTable", map.GridCoords);
            other = entities.SpawnEntity("N14RadioTableWasteland", map.GridCoords);
            entities.GetComponent<ApcPowerReceiverComponent>(jukebox).NeedsPower = false;
            entities.GetComponent<ApcPowerReceiverComponent>(contender).NeedsPower = false;
            entities.GetComponent<ApcPowerReceiverComponent>(microphone).NeedsPower = false;
            radios.SetSpeakerEnabled(second, null, false);
            Link(links, jukebox, microphone);
            Link(links, contender, microphone);
        });
        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            // Big Iron has a stereo original and needs the mono version on both speakers.
            const string monoPath = "/Audio/_Nuclear14/Lobby/MUS_Big_Iron.radio-mono.ogg";
            Send(entities, jukebox, new JukeboxSelectedMessage("BigIron"));
            Send(entities, jukebox, new JukeboxBroadcastMessage());
            var player = entities.GetComponent<JukeboxComponent>(jukebox);
            Assert.That(player.Broadcasting, Is.True);
            Assert.That(entities.GetComponent<AudioComponent>(player.AudioStream!.Value).FileName, Is.EqualTo(monoPath));
            Assert.That(ReceiverAudio(entities, first)?.FileName, Is.EqualTo(monoPath));
            Assert.That(ReceiverAudio(entities, second), Is.Null);
            Assert.That(ReceiverAudio(entities, other), Is.Null);
            Assert.That(entities.GetComponent<RadioMicrophoneComponent>(other).BroadcastChannel, Is.EqualTo("Wasteland"));

            var tuning = entities.GetComponent<StudioRadioTuningComponent>(other);
            tuning.CanTune = false;
            Send(entities, other, new StudioRadioSelectChannelMessage("PBS") { Actor = operatorUid });
            Assert.That(ReceiverAudio(entities, other), Is.Null);
            tuning.CanTune = true;
            Send(entities, other, new StudioRadioSelectChannelMessage("PBS") { Actor = operatorUid });
            Assert.That(ReceiverAudio(entities, other)?.FileName, Is.EqualTo(monoPath));
            Send(entities, other, new StudioRadioSelectChannelMessage("Wasteland") { Actor = operatorUid });
            Assert.That(ReceiverAudio(entities, other), Is.Null);
        });
        await server.WaitRunTicks(45);

        await server.WaitAssertion(() =>
        {
            radios.SetSpeakerEnabled(second, null, true);
            var firstAudio = ReceiverAudio(entities, first);
            var secondAudio = ReceiverAudio(entities, second);
            Assert.That(firstAudio, Is.Not.Null);
            Assert.That(secondAudio, Is.Not.Null);
            Assert.That((timing.CurTime - secondAudio!.AudioStart).TotalSeconds, Is.GreaterThan(0.5));
            Assert.That(secondAudio.AudioStart.TotalSeconds, Is.EqualTo(firstAudio!.AudioStart.TotalSeconds).Within(0.02));
            Send(entities, jukebox, new JukeboxPauseMessage());
        });
        await server.WaitRunTicks(30);

        await server.WaitAssertion(() =>
        {
            Assert.That(ReceiverAudio(entities, first)?.State, Is.EqualTo(AudioState.Paused));
            Send(entities, contender, new JukeboxSelectedMessage("BillieHolidayCrazy"));
            Send(entities, contender, new JukeboxBroadcastMessage { Actor = operatorUid });
            Assert.That(entities.GetComponent<JukeboxComponent>(contender).Broadcasting, Is.False);
            radios.SetSpeakerEnabled(second, null, false);
            Assert.That(ReceiverAudio(entities, second), Is.Null);

            Send(entities, microphone, new StudioRadioSelectChannelMessage("Wasteland") { Actor = operatorUid });
            Assert.That(ReceiverAudio(entities, first), Is.Null);
            Assert.That(ReceiverAudio(entities, other), Is.Null);
            Assert.That(entities.GetComponent<JukeboxComponent>(jukebox).Broadcasting, Is.False);

            Send(entities, contender, new JukeboxBroadcastMessage { Actor = operatorUid });
            Assert.That(entities.GetComponent<JukeboxComponent>(contender).Broadcasting, Is.True);
            Assert.That(ReceiverAudio(entities, other), Is.Not.Null);
            Assert.That(ReceiverAudio(entities, first), Is.Null);
            Send(entities, contender, new JukeboxStopMessage());
            Assert.That(ReceiverAudio(entities, other), Is.Null);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task PipBoyPlaysStudioMusicOnlyForItsWearerAndSwitchesSources()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var (server, client) = pair;
        var map = await pair.CreateTestMap();
        var entities = server.EntMan;
        var links = server.System<SharedDeviceLinkSystem>();
        var inventory = server.System<InventorySystem>();
        var loader = server.System<CartridgeLoaderSystem>();
        var session = server.PlayerMan.Sessions.Single();
        EntityUid jukebox = default;
        EntityUid pipBoy = default;
        EntityUid wearer = default;
        EntityUid receiver = default;
        EntityUid player = default;

        try
        {
            await server.WaitAssertion(() =>
            {
                jukebox = entities.SpawnEntity("Jukebox", map.GridCoords);
                var mic = entities.SpawnEntity("N14MicrophoneStand", map.GridCoords);
                entities.GetComponent<ApcPowerReceiverComponent>(jukebox).NeedsPower = false;
                Link(links, jukebox, mic);

                wearer = entities.SpawnEntity("MobHuman", map.GridCoords);
                server.PlayerMan.SetAttachedEntity(session, wearer);
                pipBoy = entities.SpawnEntity("N14Pipboy", map.GridCoords);
                Assert.That(inventory.TryEquip(wearer, pipBoy, "id", silent: true, force: true), Is.True);
                Assert.That(loader.TryGetProgram<PipBoyReceiverCartridgeComponent>(pipBoy, out var receiverUid), Is.True);
                Assert.That(loader.TryGetProgram<PipBoyRadioCartridgeComponent>(pipBoy, out var playerUid), Is.True);
                receiver = receiverUid!.Value;
                player = playerUid!.Value;
            });
            await pair.RunTicksSync(5);

            await server.WaitAssertion(() =>
            {
                Send(entities, jukebox, new JukeboxSelectedMessage("BigIron"));
                Send(entities, jukebox, new JukeboxBroadcastMessage { Actor = wearer });

                SendCartridge(entities, player, pipBoy, wearer, new PipBoyRadioUiMessageEvent(PipBoyRadioAction.Select, "LonesomeValley"));
                SendCartridge(entities, player, pipBoy, wearer, new PipBoyRadioUiMessageEvent(PipBoyRadioAction.Play));
                var personal = entities.GetComponent<PipBoyRadioCartridgeComponent>(player);
                Assert.That(personal.Playing, Is.True);

                SendCartridge(entities, receiver, pipBoy, jukebox, new PipBoyReceiverUiMessageEvent("PBS"));
                Assert.That(ReceiverAudio(entities, pipBoy), Is.Null);
                Assert.That(entities.GetComponent<RadioSpeakerComponent>(pipBoy).Enabled, Is.False);
                SendCartridge(entities, receiver, pipBoy, wearer, new PipBoyReceiverUiMessageEvent("PBS"));
                var stream = ReceiverAudio(entities, pipBoy);
                Assert.That(stream?.IncludedEntities, Is.EquivalentTo(new[] { wearer }));
                Assert.That(stream!.FileName, Does.EndWith("MUS_Big_Iron.radio-mono.ogg"));
                Assert.That(personal.Paused, Is.True);

                SendCartridge(entities, receiver, pipBoy, wearer, new PipBoyReceiverUiMessageEvent("Wasteland"));
                Assert.That(ReceiverAudio(entities, pipBoy), Is.Null);
                SendCartridge(entities, receiver, pipBoy, wearer, new PipBoyReceiverUiMessageEvent("PBS"));
                Assert.That(ReceiverAudio(entities, pipBoy), Is.Not.Null);

                SendCartridge(entities, player, pipBoy, wearer, new PipBoyRadioUiMessageEvent(PipBoyRadioAction.Play));
                Assert.That(personal.Playing, Is.True);
                Assert.That(ReceiverAudio(entities, pipBoy), Is.Null);

                SendCartridge(entities, receiver, pipBoy, wearer, new PipBoyReceiverUiMessageEvent("PBS"));
                Assert.That(entities.GetComponent<RadioSpeakerComponent>(pipBoy).Enabled, Is.False);
                Assert.That(personal.Playing, Is.True);
                SendCartridge(entities, receiver, pipBoy, wearer, new PipBoyReceiverUiMessageEvent("PBS"));
                Assert.That(entities.GetComponent<RadioSpeakerComponent>(pipBoy).Enabled, Is.True);
                Assert.That(personal.Paused, Is.True);
                Assert.That(ReceiverAudio(entities, pipBoy), Is.Not.Null);
                SendCartridge(entities, receiver, pipBoy, wearer, new PipBoyReceiverUiMessageEvent("PBS"));
                Assert.That(ReceiverAudio(entities, pipBoy), Is.Null);
                SendCartridge(entities, receiver, pipBoy, wearer, new PipBoyReceiverUiMessageEvent("PBS"));
                Assert.That(ReceiverAudio(entities, pipBoy), Is.Not.Null);
                Assert.That(inventory.TryUnequip(wearer, "id", silent: true, force: true), Is.True);
                Assert.That(ReceiverAudio(entities, pipBoy), Is.Null);
            });
        }
        finally
        {
            await client.WaitPost(() => client.ResolveDependency<IClientNetManager>().ClientDisconnect("Studio radio test complete"));
            await pair.RunTicksSync(5);
        }

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task LinkedDeckRelaysTheRecordedSpeakerToADistantRadio()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var (server, client) = pair;
        var map = await pair.CreateTestMap();
        var entities = server.EntMan;
        var links = server.System<SharedDeviceLinkSystem>();
        var slots = server.System<ItemSlotsSystem>();
        var mapSystem = server.System<SharedMapSystem>();
        var session = server.PlayerMan.Sessions.Single();
        var messages = new List<ChatMessage>();
        EntityUid deck = default;
        NetEntity receiver = default;

        await server.WaitAssertion(() =>
        {
            deck = entities.SpawnEntity("N14StudioTapeDeck", map.GridCoords);
            var microphone = entities.SpawnEntity("RadioDJMicrophone", map.GridCoords);
            var tape = entities.SpawnEntity("StudioRadioTestCassette", map.GridCoords);
            entities.GetComponent<ApcPowerReceiverComponent>(deck).NeedsPower = false;
            entities.GetComponent<ApcPowerReceiverComponent>(microphone).NeedsPower = false;
            Assert.That(slots.TryInsert(deck, "cassette_tape", tape, null), Is.True);
            Link(links, deck, microphone);

            mapSystem.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(20, 0), map.Tile.Tile);
            mapSystem.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(21, 0), map.Tile.Tile);
            var radio = entities.SpawnEntity("N14RadioTable", new EntityCoordinates(map.Grid.Owner, 20.5f, 0.5f));
            receiver = entities.GetNetEntity(radio);
            var listener = entities.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid.Owner, 21.5f, 0.5f));
            server.PlayerMan.SetAttachedEntity(session, listener);
        });

        var chat = client.ResolveDependency<IUserInterfaceManager>().GetUIController<ChatUIController>();
        await client.WaitPost(() => chat.MessageAdded += messages.Add);
        try
        {
            await pair.RunTicksSync(10);
            await server.WaitPost(() => Send(entities, deck, new ChangeModeTapeRecorderMessage(TapeRecorderMode.Playing)));
            await pair.RunTicksSync(30);
            await client.WaitAssertion(() =>
            {
                var matching = messages.Where(message => message.SenderEntity == receiver).ToArray();
                Assert.That(matching, Has.Length.EqualTo(1), string.Join(" | ", messages.Select(message => message.WrappedMessage)));
                var relayed = matching.Single();
                Assert.That(relayed.Message, Is.EqualTo("Interview from the wasteland."));
                Assert.That(relayed.WrappedMessage, Does.Contain("Test Reporter"));
            });
        }
        finally
        {
            await client.WaitPost(() =>
            {
                chat.MessageAdded -= messages.Add;
                // Disconnect before the pooled fixture deletes the test map.
                client.ResolveDependency<IClientNetManager>().ClientDisconnect("Studio radio test complete");
            });
            await pair.RunTicksSync(5);
        }

        await pair.CleanReturnAsync();
    }

    private static AudioComponent? ReceiverAudio(IEntityManager entities, EntityUid receiver)
    {
        var audio = entities.GetComponent<StudioRadioReceiverComponent>(receiver).ReceiverAudio;
        return entities.TryGetComponent<AudioComponent>(audio, out var component) ? component : null;
    }

    private static void Link(SharedDeviceLinkSystem links, EntityUid source, EntityUid microphone)
    {
        links.SaveLinks(null, source, microphone, [("StudioRadioOutput", "StudioRadioInput")]);
    }

    private static void Send(IEntityManager entities, EntityUid uid, BoundUserInterfaceMessage message)
    {
        entities.EventBus.RaiseLocalEvent(uid, (object) message, true);
    }

    private static void SendCartridge(IEntityManager entities, EntityUid cartridge, EntityUid loader, EntityUid actor, CartridgeMessageEvent message)
    {
        message.LoaderUid = entities.GetNetEntity(loader);
        message.Actor = actor;
        entities.EventBus.RaiseLocalEvent(cartridge, message);
    }
}
