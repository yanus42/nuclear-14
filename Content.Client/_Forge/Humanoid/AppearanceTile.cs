using System.Numerics;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Utility;

namespace Content.Client._Forge.Humanoid;

public sealed class AppearanceTile : BoxContainer
{
    public Button Select { get; }
    public Button Star { get; }
    public HairPortraitView View { get; }

    public AppearanceTile(string name, bool head)
    {
        Orientation = LayoutOrientation.Vertical;
        SetWidth = 152;
        SeparationOverride = 3;
        Select = new AppearanceButton { ToggleMode = true, ToolTip = name };
        View = new HairPortraitView { Region = head ? new UIBox2(8, 0, 24, 16) : new UIBox2(0, 0, 32, 32) };
        View.SetSize = new Vector2(96, 96);
        View.HorizontalAlignment = HAlignment.Center;
        View.OverrideDirection = Direction.South;
        View.MouseFilter = MouseFilterMode.Ignore;
        var column = new BoxContainer { Orientation = LayoutOrientation.Vertical, MouseFilter = MouseFilterMode.Ignore };
        column.AddChild(View);
        var label = new RichTextLabel { HorizontalExpand = true, MinHeight = 40, MouseFilter = MouseFilterMode.Ignore };
        label.SetMessage(FormattedMessage.FromUnformatted(name));
        column.AddChild(label);
        Select.AddChild(column);
        AddChild(Select);
        Star = new AppearanceButton { Text = "☆", ToolTip = Loc.GetString("forge-hair-favorite"), SetHeight = 28, MinHeight = 28, SetWidth = 28, HorizontalAlignment = HAlignment.Right };
        AddChild(Star);
    }
}
