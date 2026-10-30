using Content.Shared.Clothing.Loadouts.Systems;

namespace Content.Client._Forge.Humanoid;

public static class LoadoutPreferenceDraft
{
    public static LoadoutPreference Copy(LoadoutPreference value) =>
        new(value.LoadoutName, value.CustomName, value.CustomDescription, value.CustomColorTint, value.CustomHeirloom)
        { Selected = value.Selected };
}
