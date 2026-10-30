namespace Content.Client._Forge.Humanoid;

// Clothes is the master preview switch, including armor selected in the loadout.
// Keep the inventory switch intact so its setting is restored when clothing is shown.
public static class AppearancePreviewClothing
{
    public static bool IncludeLoadouts(bool showClothes, bool showInventory) => showClothes && showInventory;
}
