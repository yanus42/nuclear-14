using System.Diagnostics.CodeAnalysis;
using Content.Server._Nuclear14.CartridgeLoader;
using Content.Server.Power.EntitySystems;
using Content.Server.Radio.Components;
using Content.Shared.DeviceLinking;
using Content.Shared.Radio;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Forge.Radio;

public sealed class StudioRadioSystem : EntitySystem
{
    public static readonly ProtoId<SourcePortPrototype> StudioOutput = "StudioRadioOutput";
    public static readonly ProtoId<SinkPortPrototype> StudioInput = "StudioRadioInput";

    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private readonly Dictionary<StudioRadioFrequency, MusicBroadcast> _broadcasts = new();
    private readonly List<MusicBroadcast> _expiredBroadcasts = new();
    private TimeSpan _nextRefresh;

    public override void Initialize()
    {
        SubscribeLocalEvent<StudioRadioReceiverComponent, MapInitEvent>(OnReceiverMapInit);
        SubscribeLocalEvent<StudioRadioReceiverComponent, ComponentShutdown>(OnReceiverShutdown);
    }

    /// <summary>
    /// Resolve the studio's tuning from exactly one linked microphone.
    /// The speech switch is independent from the music and tape inputs.
    /// </summary>
    public bool TryGetTransmitter(EntityUid source, out EntityUid transmitter, out StudioRadioFrequency tuning)
    {
        transmitter = EntityUid.Invalid;
        tuning = default;

        if (!TryComp<DeviceLinkSourceComponent>(source, out var links))
            return false;

        var map = Transform(source).MapID;
        var powered = false;
        foreach (var (sink, ports) in links.LinkedPorts)
        {
            if (!ports.Contains((StudioOutput, StudioInput)) ||
                TerminatingOrDeleted(sink) ||
                !TryComp<RadioMicrophoneComponent>(sink, out var microphone) ||
                map != Transform(sink).MapID)
            {
                continue;
            }

            if (transmitter.Valid)
                return false;

            transmitter = sink;
            tuning = new StudioRadioFrequency(microphone.BroadcastChannel, microphone.Frequency);
            powered = !microphone.PowerRequired || this.IsPowered(sink, EntityManager);
        }

        return transmitter.Valid && powered;
    }

    public bool IsOccupied(StudioRadioFrequency tuning) => _broadcasts.ContainsKey(tuning);

    public void StartMusic(EntityUid source, EntityUid transmitter, StudioRadioFrequency tuning, EntityUid audio, SoundSpecifier song)
    {
        _broadcasts.Add(tuning, new MusicBroadcast(source, transmitter, tuning, audio, song));
        RefreshReceivers();
    }

    public void StopMusic(EntityUid source)
    {
        StopMusicFrom(source, transmitter: false);
    }

    public void ReplaceMusic(EntityUid source, EntityUid audio, SoundSpecifier song)
    {
        foreach (var broadcast in _broadcasts.Values)
        {
            if (broadcast.Source != source)
                continue;

            _broadcasts[broadcast.Tuning] = broadcast with { Audio = audio, Song = song };
            RefreshReceivers();
            return;
        }
    }

    public bool StopTransmitterMusic(EntityUid microphone) => StopMusicFrom(microphone, transmitter: true);

    private bool StopMusicFrom(EntityUid uid, bool transmitter)
    {
        foreach (var broadcast in _broadcasts.Values)
        {
            if ((transmitter ? broadcast.Transmitter : broadcast.Source) != uid)
                continue;

            EndMusic(broadcast);
            RefreshReceivers();
            return true;
        }

        return false;
    }

    public override void Update(float frameTime)
    {
        if (_broadcasts.Count == 0 || _timing.CurTime < _nextRefresh)
            return;

        _nextRefresh = _timing.CurTime + TimeSpan.FromSeconds(0.25);
        foreach (var broadcast in _broadcasts.Values)
        {
            if (TerminatingOrDeleted(broadcast.Source) ||
                !this.IsPowered(broadcast.Source, EntityManager) ||
                !TryGetSourceAudio(broadcast, out _) ||
                !TryGetTransmitter(broadcast.Source, out var transmitter, out var tuning) ||
                transmitter != broadcast.Transmitter ||
                tuning != broadcast.Tuning)
            {
                _expiredBroadcasts.Add(broadcast);
            }
        }

        foreach (var broadcast in _expiredBroadcasts)
        {
            EndMusic(broadcast);
        }
        _expiredBroadcasts.Clear();

        RefreshReceivers();
    }

    private void EndMusic(MusicBroadcast broadcast)
    {
        _broadcasts.Remove(broadcast.Tuning);
        if (!TerminatingOrDeleted(broadcast.Source))
            RaiseLocalEvent(broadcast.Source, new StudioMusicEndedEvent());
    }

    private void OnReceiverMapInit(Entity<StudioRadioReceiverComponent> receiver, ref MapInitEvent args)
    {
        RefreshReceiver(receiver);
    }

    private void OnReceiverShutdown(Entity<StudioRadioReceiverComponent> receiver, ref ComponentShutdown args)
    {
        StopReceiver(receiver.Comp);
    }

    public void RefreshReceiver(EntityUid uid)
    {
        if (TryComp<StudioRadioReceiverComponent>(uid, out var receiver))
            RefreshReceiver((uid, receiver));
    }

    private void RefreshReceivers()
    {
        var receivers = AllEntityQuery<StudioRadioReceiverComponent>();
        while (receivers.MoveNext(out var uid, out var receiver))
        {
            RefreshReceiver((uid, receiver));
        }
    }

    private void RefreshReceiver(Entity<StudioRadioReceiverComponent> receiver)
    {
        if (TerminatingOrDeleted(receiver) ||
            !TryComp<RadioMicrophoneComponent>(receiver, out var tuning) ||
            !CanListen(receiver, tuning.BroadcastChannel, out var listener) ||
            !_broadcasts.TryGetValue(new StudioRadioFrequency(tuning.BroadcastChannel, tuning.Frequency), out var broadcast) ||
            !TryGetSourceAudio(broadcast, out var audio))
        {
            StopReceiver(receiver.Comp);
            return;
        }

        var state = receiver.Comp;
        var newStream = state.SourceAudio != broadcast.Audio ||
                        state.Listener != listener ||
                        TerminatingOrDeleted(state.ReceiverAudio);
        if (newStream)
        {
            StopReceiver(state);
            state.ReceiverAudio = listener is { } wearer
                ? _audio.PlayGlobal(broadcast.Song, wearer, state.MusicParams)?.Entity
                : _audio.PlayPvs(broadcast.Song, receiver.Owner, state.MusicParams)?.Entity;
            state.SourceAudio = broadcast.Audio;
            state.Listener = listener;
        }
        else if (state.AudioStart == audio.AudioStart && state.PauseTime == audio.PauseTime)
        {
            return;
        }

        // Use the source stream as the clock, including after tuning in, seeking, or resuming.
        var position = (float) ((audio.PauseTime ?? _timing.CurTime) - audio.AudioStart).TotalSeconds;
        if (audio.State == AudioState.Playing)
            _audio.SetState(state.ReceiverAudio, AudioState.Playing);
        _audio.SetPlaybackPosition(state.ReceiverAudio, Math.Max(0f, position));
        if (audio.State == AudioState.Paused)
            _audio.SetState(state.ReceiverAudio, AudioState.Paused);

        state.AudioStart = audio.AudioStart;
        state.PauseTime = audio.PauseTime;
    }

    private bool CanListen(EntityUid receiver, string channel, out EntityUid? listener)
    {
        listener = null;
        if (!TryComp<RadioSpeakerComponent>(receiver, out var speaker) || !speaker.Enabled)
            return false;

        if (TryComp<PipBoyRadioWearerComponent>(receiver, out var pipBoy))
        {
            listener = pipBoy.WearerUid;
            return HasComp<ActorComponent>(listener);
        }

        return TryComp<ActiveRadioComponent>(receiver, out var radio) && radio.Channels.Contains(channel);
    }

    private bool TryGetSourceAudio(MusicBroadcast broadcast, [NotNullWhen(true)] out AudioComponent? audio)
    {
        return TryComp(broadcast.Audio, out audio) && !TerminatingOrDeleted(broadcast.Audio) && audio.State != AudioState.Stopped;
    }

    private void StopReceiver(StudioRadioReceiverComponent receiver)
    {
        receiver.ReceiverAudio = _audio.Stop(receiver.ReceiverAudio);
        receiver.SourceAudio = null;
        receiver.Listener = null;
    }

    private sealed record MusicBroadcast(
        EntityUid Source,
        EntityUid Transmitter,
        StudioRadioFrequency Tuning,
        EntityUid Audio,
        SoundSpecifier Song);
}

public readonly record struct StudioRadioFrequency(ProtoId<RadioChannelPrototype> Channel, int Frequency);

public sealed class StudioMusicEndedEvent : EntityEventArgs;
