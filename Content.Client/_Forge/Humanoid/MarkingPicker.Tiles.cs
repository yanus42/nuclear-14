using System.Linq;
using Content.Client._Forge.Humanoid;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Preferences;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.ContentPack;
using Robust.Shared.Utility;

namespace Content.Client.Humanoid;

public sealed partial class MarkingPicker
{
    private BoxContainer _forgeColumn = default!;
    private readonly TileFlowContainer _forgeFlow = new() { HorizontalExpand = true };
    private readonly Dictionary<string, AppearanceTile> _forgeCards = new();
    private AppearanceFavorites _forgeFavorites = default!;
    private readonly Button _forgeOnlyFavorites = new AppearanceButton() { ToggleMode = true };
    private readonly Button _forgeClothes = new AppearanceButton { ToggleMode = true };
    private HumanoidCharacterProfile? _forgeProfile;
    private Func<HumanoidCharacterProfile, EntityUid>? _forgeFactory;
    private IEntityManager _forgeEntities = default!;
    private IResourceCache _forgeResources = default!;
    private MarkingCategories? _forgeBuiltCategory;
    private string? _forgeBuiltSpecies;
    private bool _forgeClothed;
    private bool _forgeHover;
    private bool _forgeLoadouts;
    private bool _forgeSettingControls;
    private Direction _forgeDirection = Direction.South;
    private static readonly ResPath ForgeFavoritesPath = new("/forge-marking-favorites.txt");
    public Action<HumanoidCharacterProfile?>? OnForgeTryMarking;
    public Action<bool>? OnForgeClothes;
    public Action<bool>? OnForgeRotate;


    private void InitializeForgeTiles()
    {
        _forgeEntities = IoCManager.Resolve<IEntityManager>();
        _forgeResources = IoCManager.Resolve<IResourceCache>();
        _forgeFavorites = new AppearanceFavorites(
            () => _forgeResources.UserData.TryReadAllText(ForgeFavoritesPath, out var saved) ? saved : null,
            data => { using var writer = _forgeResources.UserData.OpenWriteText(ForgeFavoritesPath); writer.Write(data); });
        _forgeFavorites.Refresh();
        var column = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true };
        var controls = new BoxContainer { SeparationOverride = 8, Margin = new Thickness(0, 6, 0, 6) };
        var left = new AppearanceButton { Text = Loc.GetString("forge-marking-turn-left"), ToolTip = Loc.GetString("forge-marking-turn-left") };
        var right = new AppearanceButton { Text = Loc.GetString("forge-marking-turn-right"), ToolTip = Loc.GetString("forge-marking-turn-right") };
        left.OnPressed += _ => OnForgeRotate?.Invoke(false);
        right.OnPressed += _ => OnForgeRotate?.Invoke(true);
        _forgeClothes.Text = Loc.GetString("forge-marking-clothes-button");
        _forgeClothes.ToolTip = Loc.GetString("forge-marking-clothes");
        _forgeClothes.OnToggled += args => { if (!_forgeSettingControls) OnForgeClothes?.Invoke(args.Pressed); };
        controls.AddChild(left);
        controls.AddChild(right);
        controls.AddChild(_forgeClothes);
        column.AddChild(controls);
        InitializeForgeDraftColor(column);
        var filters = new BoxContainer { SeparationOverride = 6, Margin = new Thickness(0, 0, 0, 6) };
        var all = new AppearanceButton { Text = Loc.GetString("forge-hair-all") };
        all.OnPressed += _ => { _forgeOnlyFavorites.Pressed = false; FilterForgeTiles(); };
        _forgeOnlyFavorites.Text = Loc.GetString("forge-hair-favorites-filter");
        _forgeOnlyFavorites.OnToggled += _ => FilterForgeTiles();
        filters.AddChild(all);
        filters.AddChild(_forgeOnlyFavorites);
        column.AddChild(filters);
        column.AddChild(_forgeSelectionHint);
        var scroll = new ScrollContainer { SetHeight = 400, HorizontalExpand = true, HScrollEnabled = false };
        scroll.AddChild(_forgeFlow);
        column.AddChild(new AppearanceGalleryPanel(scroll));
        _forgeColumn = column;
        column.Visible = false; // Other users of MarkingPicker retain their existing list.
        CMarkingsUnused.Parent!.AddChild(column);
        column.SetPositionInParent(CMarkingsUnused.GetPositionInParent());
    }

    public void SetForgePreviewContext(HumanoidCharacterProfile profile,
        Func<HumanoidCharacterProfile, EntityUid> factory, Direction direction, bool clothes, bool loadouts)
    {
        if (_forgeProfile != null && _forgeProfile.Species != profile.Species) ResetForgeDraftColor();
        else SyncForgeDraftColor();
        CMarkingCatalogTitle.Text = Loc.GetString("forge-marking-catalog");
        CMarkingsUnused.Visible = false;
        CMarkingAdd.Visible = false;
        CMarkingColors.Visible = false;
        _forgeColumn.Visible = true;
        CMarkingsUsed.MinSize = new System.Numerics.Vector2(240, 250);
        CMarkingsUsed.Parent!.HorizontalExpand = false;
        CMarkingsUsed.Parent.SetWidth = 280;
        CMarkingPoints.Margin = new Thickness(12, 0, 12, 0);
        var changed = _forgeProfile == null || !_forgeProfile.Appearance.Equals(profile.Appearance) ||
            _forgeProfile.Species != profile.Species || _forgeProfile.Sex != profile.Sex;
        var clothingChanged = _forgeProfile != null && (clothes || loadouts) &&
            (_forgeProfile.Species != profile.Species || _forgeProfile.Sex != profile.Sex ||
             _forgeProfile.Clothing != profile.Clothing || _forgeProfile.Backpack != profile.Backpack ||
             _forgeProfile.JobPriorities.Count != profile.JobPriorities.Count ||
             _forgeProfile.JobPriorities.Any(p => !profile.JobPriorities.TryGetValue(p.Key, out var priority) || priority != p.Value) ||
             !AppearanceClothingComparison.SameLoadouts(_forgeProfile.LoadoutPreferences, profile.LoadoutPreferences));
        if (_forgeClothed != clothes || _forgeLoadouts != loadouts || clothingChanged) ClearForgeTiles();
        _forgeLoadouts = loadouts;
        _forgeClothed = clothes;
        _forgeSettingControls = true;
        _forgeClothes.Pressed = clothes;
        _forgeSettingControls = false;
        var firstContext = _forgeProfile == null;
        _forgeProfile = profile;
        if (firstContext) { Populate(CMarkingSearch.Text); PopulateUsed(); }
        _forgeFactory = factory;
        SetForgePreviewDirection(direction);
        RefreshForgeTiles();
        if (changed)
            foreach (var (id, card) in _forgeCards)
                if (_markingManager.Markings.TryGetValue(id, out var proto))
                    QueueForgeTile(proto, card);
    }

    private void QueueForgeTile(MarkingPrototype prototype, AppearanceTile tile, bool immediate = false)
    {
        tile.View.QueueDoll(
            () => _forgeFactory!(ForgeCandidateProfile(prototype).WithHeight(1f).WithWidth(1f)),
            entity => ApplyForgeCardProfile(entity, ForgeCandidateProfile(prototype).WithHeight(1f).WithWidth(1f)), immediate);
    }

    private void ApplyForgeCardProfile(EntityUid doll, HumanoidCharacterProfile profile)
    {
        if (!_forgeEntities.TryGetComponent<HumanoidAppearanceComponent>(doll, out var humanoid)) return;
        var hidden = humanoid.HiddenLayers;
        var system = _forgeEntities.System<HumanoidAppearanceSystem>();
        system.LoadProfile(doll, profile, humanoid);
        system.SetLayersVisibility(doll, hidden, false, humanoid: humanoid);
    }

    public void SetForgePreviewDirection(Direction direction)
    {
        _forgeDirection = direction;
        foreach (var card in _forgeCards.Values) card.View.OverrideDirection = direction;
    }

    private HumanoidCharacterProfile ForgeCandidateProfile(MarkingPrototype prototype)
    {
        var markings = MarkingPreviewComposition.Compose(
            _currentMarkings, prototype.MarkingCategory, CreateForgeMarking(prototype));
        return _forgeProfile!
            .WithCharacterAppearance(_forgeProfile.Appearance.WithMarkings(markings));
    }

    private void RefreshForgeTiles()
    {
        if (_forgeProfile == null || _forgeFactory == null || _forgeProfile.Species != _currentSpecies) return;
        if (_forgeBuiltCategory != _selectedMarkingCategory || _forgeBuiltSpecies != _currentSpecies)
        {
            ClearForgeTiles();
            _forgeBuiltCategory = _selectedMarkingCategory;
            _forgeBuiltSpecies = _currentSpecies;
        }
        var head = _selectedMarkingCategory is MarkingCategories.Head or MarkingCategories.HeadTop or
            MarkingCategories.HeadSide or MarkingCategories.Face or MarkingCategories.Snout;
        foreach (var item in CMarkingsUnused)
        {
            var proto = (MarkingPrototype) item.Metadata!;
            if (_forgeCards.ContainsKey(proto.ID)) continue;
            var tile = new AppearanceTile(GetMarkingName(proto), head);
            ((HairPortraitView) tile.View).Region = MarkingPortraitRegion.Get(proto);
            QueueForgeTile(proto, tile, true);
            tile.View.OverrideDirection = _forgeDirection;
            tile.Star.Text = _forgeFavorites.Contains(proto.ID) ? "★" : "☆";
            _forgeCards.Add(proto.ID, tile);
            _forgeFlow.AddChild(tile);
            tile.Select.OnPressed += _ =>
            {
                // Toggle selection through the same slot validation and coloring as the editor.
                _selectedUnusedMarking = CMarkingsUnused.FirstOrDefault(i => ((MarkingPrototype) i.Metadata!).ID == proto.ID);
                ApplyForgeTile(proto);
            };
            tile.Select.OnMouseEntered += _ => { _forgeHover = true; OnForgeTryMarking?.Invoke(ForgeCandidateProfile(proto)); };
            tile.Select.OnMouseExited += _ => CancelForgeHover();
            tile.Star.OnPressed += _ =>
            {
                _forgeFavorites.Toggle(proto.ID);
                FilterForgeTiles();
            };
        }
        FilterForgeTiles();
    }

    private void FilterForgeTiles()
    {
        CancelForgeHover();
        var available = CMarkingsUnused.Select(i => ((MarkingPrototype) i.Metadata!).ID).ToHashSet();
        foreach (var (id, card) in _forgeCards)
        {
            card.Star.Text = _forgeFavorites.Contains(id) ? "★" : "☆";
            card.Visible = available.Contains(id) && (!_forgeOnlyFavorites.Pressed || _forgeFavorites.Contains(id));
            card.Select.Pressed = _currentMarkings.TryGetMarking(_selectedMarkingCategory, id, out _);
        }
    }

    private void CancelForgeHover()
    {
        if (!_forgeHover) return;
        _forgeHover = false;
        OnForgeTryMarking?.Invoke(null);
    }

    private void ClearForgeTiles()
    {
        CancelForgeHover();
        _forgeCards.Clear();
        _forgeFlow.DisposeAllChildren();
    }

    protected override void VisibilityChanged(bool newVisible)
    {
        base.VisibilityChanged(newVisible);
        if (!newVisible) CancelForgeHover();
        else if (_forgeFavorites != null && _forgeProfile != null)
        {
            _forgeFavorites.Refresh();
            FilterForgeTiles();
        }

    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) ClearForgeTiles();
        base.Dispose(disposing);
    }
}
