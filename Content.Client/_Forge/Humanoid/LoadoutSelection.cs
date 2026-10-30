namespace Content.Client._Forge.Humanoid;

public static class LoadoutSelection
{
    // Editing an existing choice does not purchase it a second time.
    public static bool Resolve(bool wasSelected, bool requested, int cost, float remaining) =>
        requested && (wasSelected || remaining >= cost);
}
