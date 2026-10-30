using Content.Client._Forge.Humanoid;
using Robust.Client.Utility;
using Content.Client.Humanoid;
using Content.Shared.Humanoid;
using Content.Shared.Preferences;

namespace Content.Client.Lobby.UI;

public sealed partial class HumanoidProfileEditor
{

    private bool _forgeClosing;

    private bool ForgePreviewJobClothes => ShowClothes.Pressed;
    private bool ForgePreviewLoadouts => AppearancePreviewClothing.IncludeLoadouts(ShowClothes.Pressed, ShowLoadouts.Pressed);

    private void ConfigureForgeMarkingTiles()
    {
        Markings.OnForgeTryMarking += TryForgeMarking;
        Markings.OnForgeRotate += right =>
        {
            _previewRotation = right ? _previewRotation.TurnCcw() : _previewRotation.TurnCw();
            SetPreviewRotation(_previewRotation);
        };
        Markings.OnForgeClothes += SetForgeClothingVisibility;
    }
    private void SetForgeClothingVisibility(bool show)
    {
        // Pressed is a silent setter in Robust: it does not raise OnToggled.
        ShowClothes.Pressed = show;
        ReloadPreview();
    }

    private void UpdateForgeMarkingTiles()
    {
        if (Profile == null) return;
        Markings.SetForgePreviewContext(Profile,
            p => _controller.LoadProfileEntity(p, ForgePreviewJobClothes, ForgePreviewLoadouts),
            SpriteView.OverrideDirection ?? Direction.South, ForgePreviewJobClothes, ForgePreviewLoadouts);
    }
    private void TryForgeMarking(HumanoidCharacterProfile? preview)
    {
        if (_forgeClosing || Profile == null || !_entManager.EntityExists(PreviewDummy) ||
            !_entManager.TryGetComponent<HumanoidAppearanceComponent>(PreviewDummy, out var humanoid)) return;
        var hidden = humanoid.HiddenLayers;
        var system = _entManager.System<HumanoidAppearanceSystem>();
        system.LoadProfile(PreviewDummy, preview ?? Profile, humanoid);
        system.SetLayersVisibility(PreviewDummy, hidden, false, humanoid: humanoid);
    }
}
