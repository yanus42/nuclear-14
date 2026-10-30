using System.Linq;
using Content.Shared.Humanoid.Markings;

namespace Content.Client.Humanoid;

public sealed partial class MarkingPicker
{
    private IEnumerable<Marking> ForgeUsedMarkings() => _forgeProfile == null
        ? _currentMarkings.GetReverseEnumerator(_selectedMarkingCategory)
        : _markingCategories.Where(c => !_ignoreCategories.Contains(c))
            .SelectMany(c => _currentMarkings.GetReverseEnumerator(c));

    private void RestoreForgeUsedSelection(string? id)
    {
        if (_forgeProfile == null || id == null) return;
        for (var i = 0; i < CMarkingsUsed.Count; i++)
            if (((MarkingPrototype) CMarkingsUsed[i].Metadata!).ID == id)
            {
                CMarkingsUsed[i].Selected = true;
                SelectForgeUsedIndex(i);
                return;
            }
    }

    private void ShiftForgeUsed(int direction)
    {
        if (_selectedMarking?.Metadata is not MarkingPrototype selected) return;
        var category = selected.MarkingCategory;
        var count = _currentMarkings.Markings[category].Count;
        var index = count - 1 - _currentMarkings.FindIndexOf(category, selected.ID);
        if (index + direction < 0 || index + direction >= count) return;
        if (direction < 0) _currentMarkings.ShiftRankDownFromEnd(category, index);
        else _currentMarkings.ShiftRankUpFromEnd(category, index);
        PopulateUsed();
        RestoreForgeUsedSelection(selected.ID);
        OnMarkingRankChange?.Invoke(_currentMarkings);
    }
}
