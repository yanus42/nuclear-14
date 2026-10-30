using Content.Client.Humanoid;
using Content.Shared.Humanoid;

namespace Content.Client.Lobby.UI;

public sealed partial class HumanoidProfileEditor
{
    private void UpdateForgeHairTiles()
    {
        if (Profile == null) return;
        HairStylePicker.SetProfile(Profile);
        FacialHairPicker.SetProfile(Profile);
        UpdateForgeMarkingTiles();
    }

    private void TryHairStyle(string? id, bool facial)
    {
        if (_forgeClosing || Profile == null || !_entManager.EntityExists(PreviewDummy)) return;
        var preview = id == null ? Profile : Profile.WithCharacterAppearance(facial
            ? Profile.Appearance.WithFacialHairStyleName(id)
            : Profile.Appearance.WithHairStyleName(id));
        if (!_entManager.TryGetComponent<HumanoidAppearanceComponent>(PreviewDummy, out var humanoid)) return;
        var hidden = humanoid.HiddenLayers;
        var system = _entManager.System<HumanoidAppearanceSystem>();
        system.LoadProfile(PreviewDummy, preview, humanoid);
        system.SetLayersVisibility(PreviewDummy, hidden, false, humanoid: humanoid);
    }
}
