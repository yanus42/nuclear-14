namespace Content.Client._Forge.Humanoid;

public enum MarkingTileAction { Add, Remove, Unavailable }

public static class MarkingTileSelection
{
    public static MarkingTileAction Resolve(int remaining, bool selected, bool forced)
    {
        if (selected) return MarkingTileAction.Remove;
        return forced || remaining != 0 ? MarkingTileAction.Add : MarkingTileAction.Unavailable;
    }
}
