using Content.Shared.Clothing.Loadouts.Systems;

namespace Content.Client._Forge.Humanoid;

public static class AppearanceClothingComparison
{
    public static bool SameLoadouts(IEnumerable<LoadoutPreference> previous, IEnumerable<LoadoutPreference> current)
    {
        var counts = new Dictionary<(string, string?, string?, string?, bool?, bool), int>();
        foreach (var item in previous)
        {
            var key = Key(item);
            counts.TryGetValue(key, out var count);
            counts[key] = count + 1;
        }
        foreach (var item in current)
        {
            var key = Key(item);
            if (!counts.TryGetValue(key, out var count)) return false;
            if (count == 1) counts.Remove(key);
            else counts[key] = count - 1;
        }
        return counts.Count == 0;
    }
    private static (string, string?, string?, string?, bool?, bool) Key(LoadoutPreference item) =>
        (item.LoadoutName, item.CustomName, item.CustomDescription, item.CustomColorTint, item.CustomHeirloom, item.Selected);
}
