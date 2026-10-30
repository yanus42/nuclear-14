using Content.Shared.Humanoid.Markings;

namespace Content.Client.Humanoid;

public sealed partial class MarkingPicker
{
    private Marking CreateForgeMarking(MarkingPrototype marking)
    {
        var markingObject = marking.AsMarking();

        // We need add hair markings in cloned set manually because _currentMarkings doesn't have it
        var markingSet = new MarkingSet(_currentMarkings);
        if (HairMarking != null)
        {
            markingSet.AddBack(MarkingCategories.Hair, HairMarking);
        }
        if (FacialHairMarking != null)
        {
            markingSet.AddBack(MarkingCategories.FacialHair, FacialHairMarking);
        }

        if (!_markingManager.MustMatchSkin(_currentSpecies, marking.BodyPart, out var _, _prototypeManager))
        {
            // Do default coloring
            var colors = MarkingColoring.GetMarkingLayerColors(
                marking,
                CurrentSkinColor,
                CurrentEyeColor,
                markingSet
            );
            for (var i = 0; i < colors.Count; i++)
            {
                markingObject.SetColor(i, colors[i]);
            }
        }
        else
        {
            // Color everything in skin color
            for (var i = 0; i < marking.Sprites.Count; i++)
            {
                markingObject.SetColor(i, CurrentSkinColor);
            }
        }

        markingObject.Forced = Forced;

        return _forgeProfile == null ? markingObject : Content.Client._Forge.Humanoid.MarkingDraftColor.Apply(
            markingObject, _forgeDraftColor, marking.ForcedColoring,
            _markingManager.MustMatchSkin(_currentSpecies, marking.BodyPart, out _, _prototypeManager));
    }
}
