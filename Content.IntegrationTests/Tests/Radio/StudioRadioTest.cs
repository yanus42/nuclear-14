#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Client.UserInterface.Systems.Chat;
using Content.Server._Forge.Radio;
using Content.Server.Power.Components;
using Content.Server.Radio.EntitySystems;
using Content.Shared.ADT.TapeRecorder.Components;
using Content.Shared.ADT.TapeRecorder.Events;
using Content.Shared.Audio.Jukebox;
using Content.Shared.Chat;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.DeviceLinking;
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
        EntityUid microphone = default;
        EntityUid first = default;
        EntityUid second = default;
        EntityUid other = default;

        await server.WaitPost(() =>
        {
            jukebox = entities.SpawnEntity("Jukebox", map.GridCoords);
            microphone = entities.SpawnEntity("RadioDJMicrophone", map.GridCoords);
            first = entities.SpawnEntity("N14RadioTable", map.GridCoords);
            second = entities.SpawnEntity("N14RadioTable", map.GridCoords);
            other = entities.SpawnEntity("N14RadioTableWasteland", map.GridCoords);
            entities.GetComponent<ApcPowerReceiverComponent>(jukebox).NeedsPower = false;
            entities.GetComponent<ApcPowerReceiverComponent>(microphone).NeedsPower = false;
            radios.SetSpeakerEnabled(second, null, false);
            Link(links, jukebox, microphone);
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
            radios.SetSpeakerEnabled(second, null, false);
            Assert.That(ReceiverAudio(entities, second), Is.Null);
            Send(entities, jukebox, new JukeboxStopMessage());
            Assert.That(ReceiverAudio(entities, first), Is.Null);
            Assert.That(entities.GetComponent<JukeboxComponent>(jukebox).Broadcasting, Is.False);
        });

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
}
