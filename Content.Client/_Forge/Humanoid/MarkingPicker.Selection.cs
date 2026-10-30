using Content.Client._Forge.Humanoid;
using Content.Shared.Humanoid.Markings;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.Humanoid;

public sealed partial class MarkingPicker
{
    private readonly Label _forgeSelectionHint = new() { HorizontalExpand = true };

    private void ApplyForgeTile(MarkingPrototype prototype)
    {
        CancelForgeHover();
        _forgeSelectionHint.Text = "";
        var selected = _currentMarkings.TryGetMarking(prototype.MarkingCategory, prototype.ID, out _);
        var action = MarkingTileSelection.Resolve(_currentMarkings.PointsLeft(prototype.MarkingCategory), selected, Forced);
        if (action == MarkingTileAction.Remove)
        {
            RestoreForgeUsedSelection(prototype.ID);
            MarkingRemove();
        }
        else if (action == MarkingTileAction.Add) MarkingAdd();
        else _forgeSelectionHint.Text = Loc.GetString("forge-marking-toggle-limit");
        FilterForgeTiles();
    }

    private void ClearForgeColorSelection()
    {
        if (_forgeProfile == null) return;
        _selectedMarking = null;
        _forgeSelectionHint.Text = "";
        SyncForgeDraftColor();
    }
}
