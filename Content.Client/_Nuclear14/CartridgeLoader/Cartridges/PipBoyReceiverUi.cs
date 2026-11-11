/// Forge-Change-Start
using System.Linq;
using Content.Client.UserInterface.Fragments;
using Content.Shared._Nuclear14.CartridgeLoader.Cartridges;
using Content.Shared.CartridgeLoader;
using Content.Shared.Radio;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.IoC;
using Robust.Shared.Prototypes;

namespace Content.Client._Nuclear14.CartridgeLoader.Cartridges;

public sealed partial class PipBoyReceiverUi : UIFragment
{
    private readonly List<(ProtoId<RadioChannelPrototype> Channel, Button Button)> _buttons = new();
    private List<ProtoId<RadioChannelPrototype>> _channels = new();
    private BoxContainer _root = default!;
    private BoxContainer _stations = default!;
    private BoundUserInterface _userInterface = default!;

    public override Control GetUIFragmentRoot() => _root;

    public override void Setup(BoundUserInterface userInterface, EntityUid? fragmentOwner)
    {
        _userInterface = userInterface;
        if (_root is { Disposed: false })
            return;

        _buttons.Clear();
        _channels.Clear();
        _root = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            SeparationOverride = 4,
        };
        _root.AddChild(new Label { Text = Loc.GetString("pipboy-receiver-title") });
        _stations = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            SeparationOverride = 4,
        };
        _root.AddChild(_stations);
    }

    public override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is not PipBoyReceiverUiState receiver)
            return;

        if (!_channels.SequenceEqual(receiver.Channels))
            RebuildStations(receiver.Channels);

        foreach (var (id, button) in _buttons)
            button.Pressed = receiver.Enabled && id.Id == receiver.SelectedChannel;
    }

    private void RebuildStations(List<ProtoId<RadioChannelPrototype>> channels)
    {
        _stations.DisposeAllChildren();
        _buttons.Clear();
        _channels = new List<ProtoId<RadioChannelPrototype>>(channels);
        var prototypes = IoCManager.Resolve<IPrototypeManager>();
        foreach (var channelId in channels)
        {
            if (!prototypes.TryIndex(channelId, out var channel))
                continue;

            var button = new Button
            {
                Text = Loc.GetString("pipboy-receiver-station", ("name", channel.LocalizedName), ("frequency", channel.Frequency)),
                ToggleMode = true,
                HorizontalExpand = true,
            };
            button.OnPressed += _ => Send(new PipBoyReceiverUiMessageEvent(channel: channelId));
            _buttons.Add((channelId, button));
            _stations.AddChild(button);
        }
    }

    private void Send(PipBoyReceiverUiMessageEvent message)
    {
        _userInterface.SendMessage(new CartridgeUiMessage(message));
    }
}
/// Forge-Change-End
