using System.Linq;
using Content.Server.Power.EntitySystems;
using Content.Server.Radio.Components;
using Content.Server.Radio.EntitySystems;
using Content.Shared._Forge.Radio;
using Content.Shared.Access.Systems;
using Content.Shared.Popups;
using Content.Shared.Radio;
using Content.Shared.Verbs;
using Robust.Server.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Server._Forge.Radio;

public sealed class StudioRadioTuningSystem : EntitySystem
{
    [Dependency] private readonly AccessReaderSystem _access = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly RadioDeviceSystem _radio = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly StudioRadioSystem _studio = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<StudioRadioTuningComponent, GetVerbsEvent<AlternativeVerb>>(OnGetVerbs);
        SubscribeLocalEvent<StudioRadioTuningComponent, BoundUIOpenedEvent>(OnOpened);
        SubscribeLocalEvent<StudioRadioTuningComponent, StudioRadioSelectChannelMessage>(OnSelect);
    }

    private void OnGetVerbs(EntityUid uid, StudioRadioTuningComponent component, GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || !CanTune(uid, component, args.User))
            return;

        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("studio-radio-tune"),
            Act = () => _ui.TryOpenUi(uid, StudioRadioUiKey.Key, args.User),
        });
    }

    private void OnOpened(EntityUid uid, StudioRadioTuningComponent component, BoundUIOpenedEvent args)
    {
        if (Equals(args.UiKey, StudioRadioUiKey.Key))
            UpdateUi(uid);
    }

    private void OnSelect(EntityUid uid, StudioRadioTuningComponent component, StudioRadioSelectChannelMessage args)
    {
        if (!CanTune(uid, component, args.Actor) ||
            !StudioRadioChannels.Public.Contains(args.Channel) ||
            !_prototypes.TryIndex(args.Channel, out RadioChannelPrototype? channel) ||
            !TryComp<RadioMicrophoneComponent>(uid, out var microphone))
        {
            return;
        }

        if (microphone.BroadcastChannel != channel.ID || microphone.Frequency != channel.Frequency)
        {
            if (_studio.StopTransmitterMusic(uid))
                _popup.PopupEntity(Loc.GetString("studio-radio-retuned"), uid, args.Actor);

            _radio.SetStationChannel(uid, channel);
        }

        UpdateUi(uid);
    }

    private bool CanTune(EntityUid uid, StudioRadioTuningComponent component, EntityUid actor)
    {
        return component.CanTune &&
               TryComp<RadioMicrophoneComponent>(uid, out var microphone) &&
               (!microphone.PowerRequired || this.IsPowered(uid, EntityManager)) &&
               _access.IsAllowed(actor, uid);
    }

    private void UpdateUi(EntityUid uid)
    {
        if (TryComp<RadioMicrophoneComponent>(uid, out var microphone))
        {
            _ui.SetUiState(uid, StudioRadioUiKey.Key,
                new StudioRadioUiState(microphone.BroadcastChannel, !HasComp<RadioSpeakerComponent>(uid)));
        }
    }
}
