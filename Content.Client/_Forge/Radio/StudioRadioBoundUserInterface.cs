using Content.Shared._Forge.Radio;
using Content.Shared.Radio;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.IoC;
using Robust.Shared.Prototypes;

namespace Content.Client._Forge.Radio;

public sealed class StudioRadioBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private static readonly Color SelectedStationColor = Color.FromHex("#47734C");
    private readonly Dictionary<ProtoId<RadioChannelPrototype>, Button> _stations = new();
    private Label? _warning;

    protected override void Open()
    {
        base.Open();
        var window = this.CreateWindow<DefaultWindow>();
        window.Title = Loc.GetString("studio-radio-tune");
        var stations = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 4 };
        window.Contents.AddChild(stations);
        _stations.Clear();
        var prototypes = IoCManager.Resolve<IPrototypeManager>();
        foreach (var id in StudioRadioChannels.Public)
        {
            if (!prototypes.TryIndex(id, out var channel))
                continue;

            var button = new Button
            {
                Text = Loc.GetString("studio-radio-station", ("name", channel.LocalizedName), ("frequency", channel.Frequency)),
                HorizontalExpand = true,
            };
            button.OnPressed += _ => SendMessage(new StudioRadioSelectChannelMessage(id));
            _stations.Add(id, button);
            stations.AddChild(button);
        }

        _warning = new Label { Text = Loc.GetString("studio-radio-retune-warning"), Visible = false };
        stations.AddChild(_warning);
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is not StudioRadioUiState radio)
            return;

        foreach (var (id, button) in _stations)
        {
            var selected = id.Id == radio.Channel;
            button.Disabled = selected;
            button.ModulateSelfOverride = selected ? SelectedStationColor : null;
        }

        if (_warning != null)
            _warning.Visible = radio.Transmitter;
    }
}
