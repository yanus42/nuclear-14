using Robust.Client.Graphics;
using Content.Client.Stylesheets;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._Forge.Humanoid;

// Reuse the active stylesheet, including its hover/pressed colors and theme changes.
public sealed class AppearanceButton : Button
{
    protected override void StylePropertiesChanged()
    {
        base.StylePropertiesChanged();
        // NewVegas already supplies a rounded box. Replace only the classic texture shape;
        // its stylesheet continues supplying the state-dependent modulation/colors.
        StyleBoxOverride = TryGetStyleProperty<StyleBox>(StylePropertyStyleBox, out var box) && box is StyleBoxTexture
            ? new AppearanceRoundedBox() : null;
    }

    public AppearanceButton()
    {
        MinHeight = 30;

    }
}
