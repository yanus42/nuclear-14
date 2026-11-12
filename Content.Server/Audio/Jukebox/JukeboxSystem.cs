using Content.Server._Forge.Radio; // Forge-Change
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Shared.Audio.Jukebox;
using Content.Shared.Popups; // Forge-Change
using Content.Shared.Power;
using Robust.Server.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using JukeboxComponent = Content.Shared.Audio.Jukebox.JukeboxComponent;

namespace Content.Server.Audio.Jukebox;


public sealed partial class JukeboxSystem : SharedJukeboxSystem // Forge-Change
{
    [Dependency] private readonly IPrototypeManager _protoManager = default!;
    [Dependency] private readonly AppearanceSystem _appearanceSystem = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!; // Forge-Change
    [Dependency] private readonly StudioRadioSystem _studioRadio = default!; // Forge-Change

    public override void Initialize()
    {
        base.Initialize();
        // Forge-Change-Start
        UpdatesBefore.Add(typeof(StudioRadioSystem));
        SubscribeLocalEvent<JukeboxComponent, JukeboxEnqueueMessage>(OnEnqueue);
        SubscribeLocalEvent<JukeboxComponent, JukeboxEditQueueMessage>(OnEditQueue);
        SubscribeLocalEvent<JukeboxComponent, JukeboxNextMessage>(OnNext);
        SubscribeLocalEvent<JukeboxComponent, JukeboxSetVolumeMessage>(OnJukeboxSetVolume);
        // Forge-Change-End
        SubscribeLocalEvent<JukeboxComponent, JukeboxSelectedMessage>(OnJukeboxSelected);
        SubscribeLocalEvent<JukeboxComponent, JukeboxPlayingMessage>(OnJukeboxPlay);
        SubscribeLocalEvent<JukeboxComponent, JukeboxPauseMessage>(OnJukeboxPause);
        SubscribeLocalEvent<JukeboxComponent, JukeboxStopMessage>(OnJukeboxStop);
        SubscribeLocalEvent<JukeboxComponent, JukeboxBroadcastMessage>(OnJukeboxBroadcast); // Forge-Change
        SubscribeLocalEvent<JukeboxComponent, StudioMusicEndedEvent>(OnBroadcastEnded); // Forge-Change
        SubscribeLocalEvent<JukeboxComponent, JukeboxSetTimeMessage>(OnJukeboxSetTime);
        SubscribeLocalEvent<JukeboxComponent, ComponentInit>(OnComponentInit);
        SubscribeLocalEvent<JukeboxComponent, ComponentShutdown>(OnComponentShutdown);

        SubscribeLocalEvent<JukeboxComponent, PowerChangedEvent>(OnPowerChanged);
    }

    private void OnComponentInit(EntityUid uid, JukeboxComponent component, ComponentInit args)
    {
        component.VolumePercent = Math.Min(component.VolumePercent, JukeboxComponent.MaxVolumePercent); // Forge-Change
        if (HasComp<ApcPowerReceiverComponent>(uid))
        {
            TryUpdateVisualState(uid, component);
        }
    }

    // Forge-Change-Start
    private void OnJukeboxPlay(EntityUid uid, JukeboxComponent component, ref JukeboxPlayingMessage args)
    {
        Play(uid, component);
    }

    private void Play(EntityUid uid, JukeboxComponent component)
    {
        if (!this.IsPowered(uid, EntityManager))
            return;

        if (Exists(component.AudioStream))
        {
            Audio.SetState(component.AudioStream, AudioState.Playing);
        }
        else
        {
            if (component.Queue.Count > 0)
                PlayNext((uid, component));
            else if (component.SelectedSongId is { } song)
                StartTrack((uid, component), song);
        }
    }

    // Forge-Change-End

    private void OnJukeboxPause(Entity<JukeboxComponent> ent, ref JukeboxPauseMessage args)
    {
        Audio.SetState(ent.Comp.AudioStream, AudioState.Paused);
    }

    private void OnJukeboxSetTime(EntityUid uid, JukeboxComponent component, JukeboxSetTimeMessage args)
    {
        if (float.IsFinite(args.SongTime) && TryComp(args.Actor, out ActorComponent? actorComp)) // Forge-Change
        {
            var offset = actorComp.PlayerSession.Channel.Ping * 1.5f / 1000f;
            Audio.SetPlaybackPosition(component.AudioStream, args.SongTime + offset);
        }
    }

    private void OnPowerChanged(Entity<JukeboxComponent> entity, ref PowerChangedEvent args)
    {
        TryUpdateVisualState(entity);

        if (!this.IsPowered(entity.Owner, EntityManager))
        {
            entity.Comp.PendingVolumePercent = null; // Forge-Change
            Stop(entity);
        }
    }

    private void OnJukeboxStop(Entity<JukeboxComponent> entity, ref JukeboxStopMessage args)
    {
        Stop(entity);
    }

    private void Stop(Entity<JukeboxComponent> entity)
    {
        // Forge-Change-Start
        if (entity.Comp.Broadcasting)
            _studioRadio.StopMusic(entity);
        // Forge-Change-End

        entity.Comp.AudioStream = Audio.Stop(entity.Comp.AudioStream); // Forge-Change
        Dirty(entity);
    }

    // Forge-Change-Start
    private void OnJukeboxBroadcast(Entity<JukeboxComponent> entity, ref JukeboxBroadcastMessage args)
    {
        if (entity.Comp.Broadcasting)
        {
            _studioRadio.StopMusic(entity);
            return;
        }

        if (!this.IsPowered(entity.Owner, EntityManager))
            return;

        if (!_studioRadio.TryGetTransmitter(entity, out var transmitter, out var tuning))
        {
            _popup.PopupEntity(Loc.GetString("jukebox-broadcast-no-transmitter"), entity, args.Actor);
            return;
        }

        if (entity.Comp.SelectedSongId == null && entity.Comp.Queue.Count == 0)
        {
            _popup.PopupEntity(Loc.GetString("jukebox-broadcast-select-song"), entity, args.Actor);
            return;
        }

        if (_studioRadio.IsOccupied(tuning))
        {
            _popup.PopupEntity(Loc.GetString("jukebox-broadcast-frequency-busy"), entity, args.Actor);
            return;
        }

        Play(entity, entity.Comp);
        if (entity.Comp.AudioStream is not { } audio)
            return;

        if (!_protoManager.TryIndex(entity.Comp.SelectedSongId, out var song))
            return;

        entity.Comp.Broadcasting = true;
        Dirty(entity);
        _studioRadio.StartMusic(entity, transmitter, tuning, audio, song.PositionalPath);
    }

    private void OnBroadcastEnded(EntityUid uid, JukeboxComponent jukebox, StudioMusicEndedEvent args)
    {
        jukebox.Broadcasting = false;
        Dirty(uid, jukebox);
    }
    // Forge-Change-End

    private void OnJukeboxSelected(EntityUid uid, JukeboxComponent component, JukeboxSelectedMessage args)
    {
        if (!TryComp<AudioComponent>(component.AudioStream, out var audio) || audio.State == AudioState.Stopped) // Forge-Change
        {
            // Forge-Change-Start
            if (component.Broadcasting)
                _studioRadio.StopMusic(uid);
            // Forge-Change-End

            component.SelectedSongId = args.SongId;
            DirectSetVisualState(uid, JukeboxVisualState.Select);
            component.Selecting = true;
            component.AudioStream = Audio.Stop(component.AudioStream);
        }

        Dirty(uid, component);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<JukeboxComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            UpdatePlayback(uid, comp); // Forge-Change
            if (comp.Selecting)
            {
                comp.SelectAccumulator += frameTime;
                if (comp.SelectAccumulator >= 0.5f)
                {
                    comp.SelectAccumulator = 0f;
                    comp.Selecting = false;

                    TryUpdateVisualState(uid, comp);
                }
            }
        }
    }

    private void OnComponentShutdown(EntityUid uid, JukeboxComponent component, ComponentShutdown args)
    {
        // Forge-Change-Start
        if (component.Broadcasting)
            _studioRadio.StopMusic(uid);
        // Forge-Change-End

        component.AudioStream = Audio.Stop(component.AudioStream);
    }

    private void DirectSetVisualState(EntityUid uid, JukeboxVisualState state)
    {
        _appearanceSystem.SetData(uid, JukeboxVisuals.VisualState, state);
    }

    private void TryUpdateVisualState(EntityUid uid, JukeboxComponent? jukeboxComponent = null)
    {
        if (!Resolve(uid, ref jukeboxComponent))
            return;

        var finalState = JukeboxVisualState.On;

        if (!this.IsPowered(uid, EntityManager))
        {
            finalState = JukeboxVisualState.Off;
        }

        _appearanceSystem.SetData(uid, JukeboxVisuals.VisualState, finalState);
    }
}
