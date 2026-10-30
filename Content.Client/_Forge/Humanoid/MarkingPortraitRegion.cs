using Content.Shared.Humanoid.Markings;

using Robust.Shared.Utility;

namespace Content.Client._Forge.Humanoid;

// Alpha analysis runs offline in Tools/_Forge/AppearancePortraits. No pixel reads in sandbox.
public static partial class MarkingPortraitRegion
{
    private static readonly Dictionary<string, UIBox2> Cache = new();

    public static UIBox2 Get(MarkingPrototype prototype)
    {
        if (Cache.TryGetValue(prototype.ID, out var cached)) return cached;
        var left = float.PositiveInfinity;
        var top = float.PositiveInfinity;
        var right = float.NegativeInfinity;
        var bottom = float.NegativeInfinity;
        foreach (var sprite in prototype.Sprites)
        {
            if (sprite is not SpriteSpecifier.Rsi rsi) continue;
            var path = rsi.RsiPath.ToString().Replace('\\', '/').TrimStart('/');
            if (path.StartsWith("Textures/", StringComparison.Ordinal)) path = path[9..];
            if (!Bounds.TryGetValue(path + "/" + rsi.RsiState, out var bounds)) continue;
            left = MathF.Min(left, bounds.Left);
            top = MathF.Min(top, bounds.Top);
            right = MathF.Max(right, bounds.Right);
            bottom = MathF.Max(bottom, bounds.Bottom);
        }
        var result = new UIBox2(0, 0, 32, 32);
        if (float.IsFinite(left))
        {
            var cx = (left + right) / 2;
            var cy = (top + bottom) / 2;
            var side = MathF.Max(12, MathF.Max(right - left, bottom - top) + 6);
            result = new UIBox2(cx - side / 2, cy - side / 2, cx + side / 2, cy + side / 2);
        }
        Cache[prototype.ID] = result;
        return result;
    }
}
