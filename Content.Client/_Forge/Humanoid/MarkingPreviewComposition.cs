using System.Linq;
using Content.Shared.Humanoid.Markings;

namespace Content.Client._Forge.Humanoid;

public static class MarkingPreviewComposition
{
    // Like hair tiles: preview an alternative in its slot, rather than appending
    // it behind a saved marking that already consumes the category's point limit.
    public static List<Marking> Compose(MarkingSet selected, MarkingCategories category, Marking candidate)
    {
        var result = selected.Markings.Where(group => group.Key != category)
            .SelectMany(group => group.Value).Select(marking => new Marking(marking)).ToList();
        result.Add(new Marking(candidate));
        return result;
    }
}
