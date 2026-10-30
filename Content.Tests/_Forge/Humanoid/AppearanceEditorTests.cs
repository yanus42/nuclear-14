#nullable enable
using System.Collections.Generic;
using System;
using System.Linq;
using System.Runtime.CompilerServices;
using Content.Client._Forge.Humanoid;
using Content.Shared.Clothing.Loadouts.Systems;
using Content.Shared.Humanoid.Markings;
using NUnit.Framework;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;

namespace Content.Tests._Forge.Humanoid;

[TestFixture, NonParallelizable]
public sealed class AppearanceEditorTests
{
    [Test]
    public void FavoritesMergeIndependentEditorsAndReadLegacyWhitespace()
    {
        string? file = " hair-a\r\n\nhair-a\n";
        var first = new AppearanceFavorites(() => file, value => file = value);
        var second = new AppearanceFavorites(() => file, value => file = value);
        first.Refresh();
        second.Refresh();
        Assert.That(first.Contains("hair-a"), Is.True);
        first.Toggle("hair-b");
        second.Toggle("beard-a");
        first.Refresh();
        Assert.That(first.Contains("beard-a"), Is.True);
        Assert.That(first.Contains("hair-b"), Is.True);
        second.Toggle("hair-b");
        first.Refresh();
        Assert.That(first.Contains("hair-b"), Is.False);
        Assert.That(first.Contains("hair-a"), Is.True);
        Assert.That(file, Is.EqualTo("beard-a\nhair-a"));
    }

    [Test]
    public void FailedFavoriteWriteDoesNotCommitInMemory()
    {
        var favorites = new AppearanceFavorites(() => "existing", _ => throw new InvalidOperationException());
        favorites.Refresh();
        Assert.Throws<InvalidOperationException>(() => favorites.Toggle("new"));
        Assert.That(favorites.Contains("existing"), Is.True);
        Assert.That(favorites.Contains("new"), Is.False);
    }

    [Test]
    public void EqualClothingValuesDoNotRequireRebuildingDolls()
    {
        var original = new LoadoutPreference("coat", "name", "desc", "#123456", true) { Selected = true };
        var copy = LoadoutPreferenceDraft.Copy(original);
        Assert.That(AppearanceClothingComparison.SameLoadouts(new[] { original }, new[] { copy }), Is.True);
        copy.CustomColorTint = "#654321";
        Assert.That(AppearanceClothingComparison.SameLoadouts(new[] { original }, new[] { copy }), Is.False);
        Assert.That(AppearanceClothingComparison.SameLoadouts(new[] { original, original, copy }, new[] { original, copy, copy }), Is.False);
        Assert.That(AppearanceClothingComparison.SameLoadouts(new[] { original, copy }, new[] { copy, original }), Is.True);
    }

    [Test]
    public void PreviewQueueCoalescesAndSharesBudgetAcrossOneThousandCards()
    {
        var queues = Enumerable.Range(0, 1000).Select(_ => new AppearancePreviewQueue()).ToArray();
        var applied = 0;
        foreach (var queue in queues) queue.Enqueue(TimeSpan.Zero, () => applied++);
        foreach (var queue in queues) queue.Update(100, TimeSpan.FromSeconds(.05), true);
        Assert.That(applied, Is.Zero);
        foreach (var queue in queues) queue.Update(101, TimeSpan.FromSeconds(1), true);
        Assert.That(applied, Is.EqualTo(2));
        foreach (var queue in queues) queue.Update(102, TimeSpan.FromSeconds(1), false);
        Assert.That(applied, Is.EqualTo(2));
        foreach (var queue in queues) queue.Update(103, TimeSpan.FromSeconds(1), true);
        Assert.That(applied, Is.EqualTo(4));
        var latest = new AppearancePreviewQueue();
        var value = 0;
        for (var i = 1; i <= 100; i++) { var captured = i; latest.Enqueue(TimeSpan.Zero, () => value = captured); }
        Assert.That(latest.Update(104, TimeSpan.FromSeconds(1), true), Is.True);
        Assert.That(value, Is.EqualTo(100));
        Assert.That(latest.Update(105, TimeSpan.FromSeconds(1), true), Is.False);
    }

    [Test]
    public void PreviewAlternativeRespectsOccupiedSlotAndPreservesOtherCategories()
    {
        var saved = new MarkingSet();
        saved.Points[MarkingCategories.Chest] = new MarkingPoints { Points = 1 };
        saved.AddBack(MarkingCategories.Chest, new Marking("scar", new List<Color> { Color.Red }));
        saved.AddBack(MarkingCategories.Chest, new Marking("discarded", new List<Color> { Color.Green }));
        saved.AddBack(MarkingCategories.RightArm, new Marking("bracelet", new List<Color> { Color.Blue }));
        Assert.That(saved.Markings[MarkingCategories.Chest].Count, Is.EqualTo(1));
        var preview = MarkingPreviewComposition.Compose(saved, MarkingCategories.Chest, new Marking("tattoo", new List<Color> { Color.Green }));
        Assert.That(preview.Select(m => m.MarkingId), Is.EquivalentTo(new[] { "bracelet", "tattoo" }));
        var loaded = new MarkingSet();
        loaded.Points[MarkingCategories.Chest] = new MarkingPoints { Points = 1 };
        loaded.AddBack(MarkingCategories.Chest, preview.Single(m => m.MarkingId == "tattoo"));
        Assert.That(loaded.Markings[MarkingCategories.Chest].Single().MarkingId, Is.EqualTo("tattoo"));
        preview.Single(m => m.MarkingId == "bracelet").SetColor(0, Color.Green);
        Assert.That(saved.Markings[MarkingCategories.RightArm][0].MarkingColors[0], Is.EqualTo(Color.Blue));
        Assert.That(saved.Points[MarkingCategories.Chest].Points, Is.Zero);
    }

    [Test]
    public void DraftColorDoesNotOverrideLockedColorsOrMutateExistingLayers()
    {
        var saved = new Marking("layers", new List<Color> { Color.Red, Color.Blue });
        Assert.That(MarkingDraftColor.Apply(saved, Color.Green, false, false).MarkingColors, Is.All.EqualTo(Color.Green));
        Assert.That(saved.MarkingColors, Is.EqualTo(new[] { Color.Red, Color.Blue }));
        Assert.That(MarkingDraftColor.Apply(saved, Color.Green, true, false).MarkingColors, Is.EqualTo(saved.MarkingColors));
        Assert.That(MarkingDraftColor.Apply(saved, Color.Green, false, true).MarkingColors, Is.EqualTo(saved.MarkingColors));
        Assert.That(MarkingDraftColor.Apply(saved, null, false, false).MarkingColors, Is.EqualTo(saved.MarkingColors));
    }

    [TestCase(true, true, 5, 0, true)]
    [TestCase(true, true, 5, -1, true)]
    [TestCase(false, true, 5, 0, false)]
    [TestCase(false, true, 5, 5, true)]
    [TestCase(true, false, 5, 0, false)]
    [TestCase(false, true, 0, 0, true)]
    public void CustomizingExistingLoadoutDoesNotPurchaseItAgain(bool previous, bool requested, int cost, int remaining, bool expected) =>
        Assert.That(LoadoutSelection.Resolve(previous, requested, cost, remaining), Is.EqualTo(expected));

    [Test]
    public void LoadoutDraftRetainsSelectionAndDoesNotModifySavedName()
    {
        var saved = new LoadoutPreference("coat", "old", "desc", "#123456", true) { Selected = true };
        var draft = LoadoutPreferenceDraft.Copy(saved);
        draft.CustomName = "new";
        Assert.That(saved.CustomName, Is.EqualTo("old"));
        Assert.That(draft.Selected, Is.True);
        Assert.That(draft.CustomDescription, Is.EqualTo(saved.CustomDescription));
        Assert.That(draft.CustomColorTint, Is.EqualTo(saved.CustomColorTint));
        Assert.That(draft.CustomHeirloom, Is.True);
    }

    [TestCase(false, false, false)]
    [TestCase(false, true, false)]
    [TestCase(true, false, false)]
    [TestCase(true, true, true)]
    public void MasterClothingSwitchAlsoControlsArmor(bool clothes, bool inventory, bool expected) =>
        Assert.That(AppearancePreviewClothing.IncludeLoadouts(clothes, inventory), Is.EqualTo(expected));

    [Test]
    public void EnginePressedAssignmentDoesNotInvokeUserToggleHandlers()
    {
        // Bypass IoC construction only for this narrow contract. Base DrawModeChanged is empty.
        var button = (ProbeButton) RuntimeHelpers.GetUninitializedObject(typeof(ProbeButton));
        var events = 0;
        button.OnToggled += _ => events++;
        button.Pressed = true;
        Assert.That(button.Pressed, Is.True);
        button.Pressed = false;
        Assert.That(button.Pressed, Is.False);
        Assert.That(events, Is.Zero, "Programmatic synchronization must explicitly reload the preview.");
    }

    [TestCase(1, false, false, MarkingTileAction.Add)]
    [TestCase(-1, false, false, MarkingTileAction.Add)]
    [TestCase(0, true, false, MarkingTileAction.Remove)]
    [TestCase(1, true, false, MarkingTileAction.Remove)]
    [TestCase(0, false, false, MarkingTileAction.Unavailable)]
    [TestCase(0, false, true, MarkingTileAction.Add)]
    public void TileClickTogglesSelectionWithoutReplacingAnotherMarking(int remaining, bool selected, bool forced, MarkingTileAction expected) =>
        Assert.That(MarkingTileSelection.Resolve(remaining, selected, forced), Is.EqualTo(expected));

    [Test]
    public void LazyPortraitsCreateOnlyVisibleDollsWithinSharedBudget()
    {
        var created = 0;
        var released = 0;
        var dolls = Enumerable.Range(0, 1000).Select(_ => new AppearancePreviewDoll<int>(_ => released++)).ToArray();
        var queues = dolls.Select(_ => new AppearancePreviewQueue()).ToArray();
        for (var i = 0; i < dolls.Length; i++)
        {
            var doll = dolls[i];
            doll.Configure(() => ++created, _ => { });
            queues[i].Enqueue(TimeSpan.Zero, doll.Apply, true);
        }
        Assert.That(created, Is.Zero, "Character/catalogue initialization must not spawn any preview dolls.");
        for (uint frame = 500; frame < 505; frame++)
            for (var i = 0; i < dolls.Length; i++) queues[i].Update(frame, TimeSpan.FromSeconds(1), i < 10);
        Assert.That(created, Is.EqualTo(10), "Only the ten visible cards are instantiated.");
        Assert.That(dolls.Skip(10).All(d => d.Entity == null), Is.True);
        foreach (var doll in dolls) doll.Release();
        Assert.That(released, Is.EqualTo(10));
        foreach (var doll in dolls) doll.Clear();
        Assert.That(released, Is.EqualTo(10), "Closing/rebuilding must not release entities twice.");
    }

    [Test]
    public void LazyPortraitUsesNewestCharacterAndReusesExistingEntity()
    {
        var updated = 0;
        var deleted = 0;
        var doll = new AppearancePreviewDoll<int>(_ => deleted++);
        doll.Configure(() => 1, _ => updated = 1);
        doll.Configure(() => 2, _ => updated = 2);
        doll.Apply();
        Assert.That(doll.Entity, Is.EqualTo(2), "A hidden portrait must not instantiate the previous character.");
        Assert.That(updated, Is.Zero, "Factory already loads its profile; do not apply it twice on creation.");
        doll.Apply();
        Assert.That(updated, Is.EqualTo(2));
        doll.Release();
        doll.Configure(() => 3, _ => { });
        doll.Apply();
        Assert.That(doll.Entity, Is.EqualTo(3));
        doll.Clear();
        Assert.That(deleted, Is.EqualTo(2));
        doll.Apply();
        Assert.That(doll.Entity, Is.Null, "Disposed portraits retain no factory to recreate a stale entity.");
    }
    [Test]
    public void RemovingThenAddingTileKeepsCategoryLimitAndOtherSelectedMarkings()
    {
        var set = new MarkingSet();
        set.Points[MarkingCategories.RightArm] = new MarkingPoints { Points = 2 };
        set.AddBack(MarkingCategories.RightArm, new Marking("bracelet", new List<Color> { Color.Red }));
        set.AddBack(MarkingCategories.RightArm, new Marking("tattoo", new List<Color> { Color.Blue }));
        Assert.That(MarkingTileSelection.Resolve(set.PointsLeft(MarkingCategories.RightArm), false, false), Is.EqualTo(MarkingTileAction.Unavailable));
        Assert.That(MarkingTileSelection.Resolve(0, true, false), Is.EqualTo(MarkingTileAction.Remove));
        set.Remove(MarkingCategories.RightArm, "bracelet");
        Assert.That(set.PointsLeft(MarkingCategories.RightArm), Is.EqualTo(1));
        Assert.That(MarkingTileSelection.Resolve(1, false, false), Is.EqualTo(MarkingTileAction.Add));
        set.AddBack(MarkingCategories.RightArm, new Marking("new", new List<Color> { Color.Green }));
        Assert.That(set.PointsLeft(MarkingCategories.RightArm), Is.Zero);
        Assert.That(set.Markings[MarkingCategories.RightArm].Select(m => m.MarkingId), Is.EqualTo(new[] { "tattoo", "new" }));
        Assert.That(set.Markings[MarkingCategories.RightArm][0].MarkingColors[0], Is.EqualTo(Color.Blue));
    }
    private sealed class ProbeButton : BaseButton { }
}
