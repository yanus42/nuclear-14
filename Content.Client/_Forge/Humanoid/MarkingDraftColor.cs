using Robust.Shared.Maths;
using Content.Shared.Humanoid.Markings;

namespace Content.Client._Forge.Humanoid;

public static class MarkingDraftColor
{
    public static Marking Apply(Marking original, Color? color, bool forcedColoring, bool matchSkin)
    {
        if (color == null || forcedColoring || matchSkin) return original;
        var draft = new Marking(original);
        for (var i = 0; i < draft.MarkingColors.Count; i++) draft.SetColor(i, color.Value);
        return draft;
    }
}
