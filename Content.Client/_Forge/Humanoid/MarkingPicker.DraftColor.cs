using System.Linq;
using Content.Client._Forge.Humanoid;
using Content.Shared.Humanoid.Markings;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.Humanoid;

public sealed partial class MarkingPicker
{
    private readonly ColorSelectorSliders _forgeDraftSelector = new() { HorizontalExpand = true };
    private readonly Label _forgeColorLabel = new() { HorizontalExpand = true };
    private readonly OptionButton _forgeColorLayers = new();
    private readonly Button _forgeAutomaticColor = new AppearanceButton();
    private Color? _forgeDraftColor;
    private bool _forgeSettingDraft;
    private string? _forgeColorTargetId;
    private int _forgeColorLayer;

    private (MarkingPrototype Prototype, Marking Marking, int Index)? ForgeColorTarget()
    {
        if (_selectedMarking?.Metadata is not MarkingPrototype prototype) return null;
        var index = _currentMarkings.FindIndexOf(prototype.MarkingCategory, prototype.ID);
        return index < 0 ? null : (prototype, _currentMarkings.Markings[prototype.MarkingCategory][index], index);
    }

    private bool ForgeColorEditable(MarkingPrototype prototype) => !prototype.ForcedColoring &&
        !_markingManager.MustMatchSkin(_currentSpecies, prototype.BodyPart, out _, _prototypeManager);

    private void InitializeForgeDraftColor(BoxContainer column)
    {
        var row = new BoxContainer { SeparationOverride = 8 };
        row.AddChild(_forgeColorLabel);
        _forgeAutomaticColor.Text = Loc.GetString("forge-marking-auto-color");
        _forgeAutomaticColor.OnPressed += _ => ApplyAutomaticForgeColor();
        row.AddChild(_forgeAutomaticColor);
        column.AddChild(row);
        column.AddChild(_forgeColorLayers);
        column.AddChild(_forgeDraftSelector);
        _forgeColorLayers.OnItemSelected += args =>
        {
            _forgeColorLayer = args.Id;
            _forgeColorLayers.SelectId(args.Id);
            SyncForgeDraftColor();
            if (ForgeColorTarget() is { } target) _forgeDraftColor = target.Marking.MarkingColors[_forgeColorLayer];
            QueueForgeDraftPreviews();
        };
        _forgeDraftSelector.OnColorChanged += color =>
        {
            if (_forgeSettingDraft) return;
            if (ForgeColorTarget() is { } target)
            {
                if (!ForgeColorEditable(target.Prototype)) return;
                _currentMarkingColors.Clear();
                _currentMarkingColors.AddRange(target.Marking.MarkingColors);
                _currentMarkingColors[_forgeColorLayer] = color;
                _forgeDraftColor = color;
                ColorChanged(_forgeColorLayer);
            }
            else _forgeDraftColor = color;
            QueueForgeDraftPreviews();
        };
    }

    private void ActivateForgeUsedColor(MarkingPrototype prototype)
    {
        CMarkingColors.Visible = false;
        if (_forgeColorTargetId != prototype.ID) _forgeColorLayer = 0;
        if (ForgeColorTarget() is { } target && target.Marking.MarkingColors.Count > 0)
            _forgeDraftColor = target.Marking.MarkingColors[Math.Clamp(_forgeColorLayer, 0, target.Marking.MarkingColors.Count - 1)];
        if (_selectedMarkingCategory != prototype.MarkingCategory)
        {
            _selectedMarkingCategory = prototype.MarkingCategory;
            Populate(CMarkingSearch.Text);
        }
        SyncForgeDraftColor();
        QueueForgeDraftPreviews();
    }

    public void ResetForgeDraftColor()
    {
        _forgeDraftColor = null;
        _forgeColorTargetId = null;
        _forgeColorLayer = 0;
        SyncForgeDraftColor();
    }

    private void ApplyAutomaticForgeColor()
    {
        _forgeDraftColor = null;
        if (ForgeColorTarget() is { } target && ForgeColorEditable(target.Prototype))
        {
            var defaults = CreateForgeMarking(target.Prototype);
            var updated = new Marking(target.Marking);
            for (var i = 0; i < updated.MarkingColors.Count && i < defaults.MarkingColors.Count; i++)
                updated.SetColor(i, defaults.MarkingColors[i]);
            _currentMarkings.Replace(target.Prototype.MarkingCategory, target.Index, updated);
            OnMarkingColorChange?.Invoke(_currentMarkings);
        }
        SyncForgeDraftColor();
        QueueForgeDraftPreviews();
    }

    private void SyncForgeDraftColor()
    {
        var target = ForgeColorTarget();
        var editable = target == null || (target.Value.Marking.MarkingColors.Count > 0 && ForgeColorEditable(target.Value.Prototype));
        _forgeColorLabel.Text = target is { } active
            ? Loc.GetString(editable ? "forge-marking-active-color" : "forge-marking-locked-color", ("name", GetMarkingName(active.Prototype)))
            : Loc.GetString("forge-marking-color");
        var count = target?.Marking.MarkingColors.Count ?? 0;
        var id = target?.Prototype.ID;
        if (_forgeColorTargetId != id)
        {
            _forgeColorTargetId = id;
            _forgeColorLayer = 0;
            _forgeColorLayers.Clear();
            if (target is { } selected)
            {
                var names = GetMarkingStateNames(selected.Prototype);
                for (var i = 0; i < count; i++) _forgeColorLayers.AddItem(i < names.Count ? names[i] : Loc.GetString("forge-marking-layer", ("number", i + 1)), i);
            }
        }
        _forgeColorLayers.Visible = editable && count > 1;
        _forgeColorLayer = count == 0 ? 0 : Math.Clamp(_forgeColorLayer, 0, count - 1);
        _forgeAutomaticColor.Disabled = !editable;
        _forgeDraftSelector.Visible = editable;
        _forgeSettingDraft = true;
        var color = target is { } value && count > 0 ? value.Marking.MarkingColors[_forgeColorLayer] : _forgeDraftColor ?? CurrentSkinColor;
        if (_forgeDraftSelector.Color != color) _forgeDraftSelector.Color = color;
        _forgeSettingDraft = false;
    }

    private void QueueForgeDraftPreviews()
    {
        if (_forgeProfile == null) return;
        foreach (var (id, card) in _forgeCards)
            if (_markingManager.Markings.TryGetValue(id, out var proto))
                QueueForgeTile(proto, card);
    }
}
