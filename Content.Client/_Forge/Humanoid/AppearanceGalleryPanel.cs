using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._Forge.Humanoid;

// A transparent inset inherits the editor's background in both interface themes.
public sealed class AppearanceGalleryPanel : BoxContainer
{
    public AppearanceGalleryPanel(Control content)
    {
        HorizontalExpand = true;
        content.Margin = new Thickness(8);
        AddChild(content);
    }
}
