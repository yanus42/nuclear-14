using Content.Shared._Forge.Radio;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.IoC;
using Robust.Shared.Prototypes;

namespace Content.Client._Forge.Radio;

public sealed class StudioRadioBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private static readonly Color SelectedStationColor = Color.FromHex("#47734C");
    private BoxContainer? _stations;

    protected override void Open()
    {
        base.Open();
        var window = this.CreateWindow<DefaultWindow>();
        window.Title = Loc.GetString("studio-radio-tune");
        _stations = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 4 };
        window.Contents.AddChild(_stations);
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        if (_stations == null || state is not StudioRadioUiState radio)
            return;

        _stations.RemoveAllChildren();
        var prototypes = IoCManager.Resolve<IPrototypeManager>();
        foreach (var id in StudioRadioChannels.Public)
        {
            if (!prototypes.TryIndex(id, out var channel))
                continue;

            var selected = channel.ID == radio.Channel;
            var button = new Button
            {
                Text = Loc.GetString("studio-radio-station", ("name", channel.LocalizedName), ("frequency", channel.Frequency)),
                Disabled = selected,
                ModulateSelfOverride = selected ? SelectedStationColor : null,
                HorizontalExpand = true,
            };
            button.OnPressed += _ => SendMessage(new StudioRadioSelectChannelMessage(id));
            _stations.AddChild(button);
        }

        if (radio.Transmitter)
            _stations.AddChild(new Label { Text = Loc.GetString("studio-radio-retune-warning") });
    }
}
