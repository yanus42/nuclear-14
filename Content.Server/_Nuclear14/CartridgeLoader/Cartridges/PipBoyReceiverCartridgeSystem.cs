/// Forge-Change-Start
using Content.Server.CartridgeLoader;
using Content.Server.Radio.Components;
using Content.Server.Radio.EntitySystems;
using Content.Shared._Nuclear14.CartridgeLoader.Cartridges;
using Content.Shared.CartridgeLoader;

namespace Content.Server._Nuclear14.CartridgeLoader.Cartridges;

public sealed class PipBoyReceiverCartridgeSystem : EntitySystem
{
    [Dependency] private readonly CartridgeLoaderSystem _loader = default!;
    [Dependency] private readonly RadioDeviceSystem _radio = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PipBoyReceiverCartridgeComponent, CartridgeUiReadyEvent>(OnUiReady);
        SubscribeLocalEvent<PipBoyReceiverCartridgeComponent, CartridgeMessageEvent>(OnUiMessage);
        SubscribeLocalEvent<PipBoyReceiverCartridgeComponent, CartridgeRemovedEvent>(OnRemoved);
    }

    private void OnUiReady(EntityUid uid, PipBoyReceiverCartridgeComponent component, CartridgeUiReadyEvent args)
    {
        UpdateUi(args.Loader, component);
    }

    private void OnUiMessage(EntityUid uid, PipBoyReceiverCartridgeComponent component, CartridgeMessageEvent args)
    {
        if (args is not PipBoyReceiverUiMessageEvent message || !component.Channels.Contains(message.Channel))
            return;

        var receiver = GetEntity(args.LoaderUid);
        if (!TryComp<RadioSpeakerComponent>(receiver, out var speaker) ||
            !TryComp<RadioMicrophoneComponent>(receiver, out var microphone) ||
            !TryComp<PipBoyRadioWearerComponent>(receiver, out var wearer) ||
            (wearer.WearerUid is { } owner && owner != args.Actor))
            return;

        var enabled = !speaker.Enabled || microphone.BroadcastChannel != message.Channel.Id;
        if (enabled)
            _radio.SetReceiverChannel(receiver, message.Channel);

        _radio.SetSpeakerEnabled(receiver, args.Actor, enabled, quiet: true, component: speaker);

        UpdateUi(receiver, component);
    }

    private void OnRemoved(Entity<PipBoyReceiverCartridgeComponent> cartridge, ref CartridgeRemovedEvent args)
    {
        if (TryComp<RadioSpeakerComponent>(args.Loader, out var speaker))
            _radio.SetSpeakerEnabled(args.Loader, null, false, quiet: true, component: speaker);
    }

    private void UpdateUi(EntityUid receiver, PipBoyReceiverCartridgeComponent component)
    {
        if (!TryComp<RadioSpeakerComponent>(receiver, out var speaker) ||
            !TryComp<RadioMicrophoneComponent>(receiver, out var microphone))
            return;

        _loader.UpdateCartridgeUiState(receiver, new PipBoyReceiverUiState(component.Channels, microphone.BroadcastChannel, speaker.Enabled));
    }
}
/// Forge-Change-End
