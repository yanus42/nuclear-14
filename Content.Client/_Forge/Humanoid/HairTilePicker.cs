using Robust.Shared.ContentPack;
using System.Linq;
using System.Numerics;
using Content.Client.Humanoid;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Preferences;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client._Forge.Humanoid;

// Hair-only adaptation of the tile interaction in Goob PRs #7455 and #7463.
// Uses Forge's existing appearance prototypes and profile loading, without Goob UI styling.
public sealed class HairTilePicker : BoxContainer
{
    [Dependency] private readonly MarkingManager _markings = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly IEntityManager _entities = default!;
    [Dependency] private readonly IResourceCache _resources = default!;
    public MarkingCategories Category { get; set; }
    public Action<(int slot, string id)>? OnMarkingSelect;
    public Action<(int slot, Marking marking)>? OnColorChanged;
    public Action<int>? OnSlotRemove;

    public Action<string?>? OnTryStyle;
    private readonly TileFlowContainer _flow = new() { HorizontalExpand = true };
    private readonly LineEdit _search = new() { HorizontalExpand = true };
    private readonly Button _favoritesOnly = new AppearanceButton() { ToggleMode = true };
    private readonly ColorSelectorSliders _color = new() { HorizontalExpand = true };
    private readonly List<(string Id, string Name, BoxContainer Card, Button Select, Button Star, HairPortraitView View)> _cards = new();
    private readonly AppearanceFavorites _favorites;
    private bool _hoverActive;
    private HumanoidCharacterProfile? _profile;
    private bool _settingColor;
    private static readonly ResPath FavoritesPath = new("/forge-hair-favorites.txt");
    private string DefaultId => Category == MarkingCategories.Hair ? HairStyles.DefaultHairStyle : HairStyles.DefaultFacialHairStyle;
    private string SelectedId => _profile == null ? DefaultId : Category == MarkingCategories.Hair ? _profile.Appearance.HairStyleId : _profile.Appearance.FacialHairStyleId;

    public HairTilePicker()
    {
        IoCManager.InjectDependencies(this);
        Orientation = LayoutOrientation.Vertical;
        HorizontalExpand = true;
        _favorites = new AppearanceFavorites(
            () => _resources.UserData.TryReadAllText(FavoritesPath, out var saved) ? saved : null,
            data => { using var writer = _resources.UserData.OpenWriteText(FavoritesPath); writer.Write(data); });
        _favorites.Refresh();
        _search.PlaceHolder = Loc.GetString("forge-hair-search");
        _favoritesOnly.Text = Loc.GetString("forge-hair-favorites-filter");
        AddChild(_color);
        AddChild(_search);
        var filters = new BoxContainer();
        var all = new AppearanceButton { Text = Loc.GetString("forge-hair-all") };
        all.OnPressed += _ => { _favoritesOnly.Pressed = false; Filter(); };
        filters.AddChild(all);
        filters.AddChild(_favoritesOnly);
        AddChild(filters);
        var scroll = new ScrollContainer { SetHeight = 360, HorizontalExpand = true, HScrollEnabled = false };
        scroll.AddChild(_flow);
        AddChild(new AppearanceGalleryPanel(scroll));
        _search.OnTextChanged += _ => Filter();
        _favoritesOnly.OnToggled += _ => Filter();
        _color.OnColorChanged += color =>
        {
            if (_settingColor) return;
            OnColorChanged?.Invoke((0, new Marking(SelectedId, new List<Color> { color })));
        };
    }


    public void SetProfile(HumanoidCharacterProfile profile)
    {
        TabContainer.SetTabTitle(this, Loc.GetString(Category == MarkingCategories.Hair ? "forge-hair-tab" : "forge-beard-tab"));
        var rebuild = _profile == null || _profile.Species != profile.Species || _profile.Sex != profile.Sex;
        var appearanceChanged = _profile == null || !_profile.Appearance.Equals(profile.Appearance);
        _profile = profile;
        _settingColor = true;
        var color = Category == MarkingCategories.Hair ? profile.Appearance.HairColor : profile.Appearance.FacialHairColor;
        if (_color.Color != color) _color.Color = color;
        _settingColor = false;
        if (rebuild)
        {
            ClearCards();
            var allowed = _markings.MarkingsByCategoryAndSpecies(Category, profile.Species);
            // TabContainer owns visibility; showing both children overlays their contents.
            AddCard(DefaultId, Loc.GetString(Category == MarkingCategories.Hair ? "forge-hair-none" : "forge-beard-none"));
            foreach (var (id, proto) in allowed.OrderBy(p => Loc.GetString($"marking-{p.Key}")))
            {
                if (id != DefaultId && _markings.CanBeApplied(profile.Species, profile.Sex, proto, _prototypes))
                    AddCard(id, Loc.GetString($"marking-{id}"));
            }
            Filter();
        }
        foreach (var card in _cards)
        {
            card.Select.Pressed = card.Id == SelectedId;

            if (rebuild || appearanceChanged)
                card.View.QueueDoll(() => CreateDoll(profile, card.Id),
                    entity => _entities.System<HumanoidAppearanceSystem>().LoadProfile(entity, WithStyle(profile, card.Id)), rebuild);
        }
    }

    private HumanoidCharacterProfile WithStyle(HumanoidCharacterProfile profile, string id) =>
        profile.WithHeight(1f).WithWidth(1f).WithCharacterAppearance(Category == MarkingCategories.Hair
            ? profile.Appearance.WithHairStyleName(id)
            : profile.Appearance.WithFacialHairStyleName(id));

    private EntityUid CreateDoll(HumanoidCharacterProfile profile, string id)
    {
        var doll = _entities.SpawnEntity(_prototypes.Index<SpeciesPrototype>(profile.Species).DollPrototype, MapCoordinates.Nullspace);
        _entities.System<HumanoidAppearanceSystem>().LoadProfile(doll, WithStyle(profile, id));
        return doll;
    }

    private void AddCard(string id, string name)
    {
        if (_profile == null) return;
        var card = new AppearanceTile(name, true);
        var select = card.Select;
        var view = card.View;
        var star = card.Star;
        star.Text = _favorites.Contains(id) ? "★" : "☆";

        _flow.AddChild(card);
        _cards.Add((id, name, card, select, star, view));
        select.OnPressed += _ =>
        {
            CancelStylePreview();
            if (id == DefaultId) OnSlotRemove?.Invoke(0);
            else OnMarkingSelect?.Invoke((0, id));
        };
        select.OnMouseEntered += _ => { _hoverActive = true; OnTryStyle?.Invoke(id); };
        select.OnMouseExited += _ => CancelStylePreview();
        star.OnPressed += _ =>
        {
            _favorites.Toggle(id);
            Filter();
        };
    }

    private void Filter()
    {
        CancelStylePreview();
        foreach (var card in _cards)
        {
            card.Star.Text = _favorites.Contains(card.Id) ? "★" : "☆";
            card.Card.Visible = (!_favoritesOnly.Pressed || _favorites.Contains(card.Id)) &&
                (card.Name.Contains(_search.Text, StringComparison.CurrentCultureIgnoreCase) || card.Id.Contains(_search.Text, StringComparison.OrdinalIgnoreCase));
        }
    }

    private void CancelStylePreview()
    {
        if (!_hoverActive) return;
        _hoverActive = false;
        OnTryStyle?.Invoke(null);
    }
    private void ClearCards()
    {
        CancelStylePreview();
        _cards.Clear();
        _flow.DisposeAllChildren();
    }

    protected override void VisibilityChanged(bool newVisible)
    {
        base.VisibilityChanged(newVisible);
        if (!newVisible) CancelStylePreview();
        else if (_favorites != null && _profile != null)
        {
            _favorites.Refresh();
            Filter();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) ClearCards();
        base.Dispose(disposing);
    }
}
