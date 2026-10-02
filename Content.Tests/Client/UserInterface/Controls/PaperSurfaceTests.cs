using System;
using Content.Client._Forge.Paper;
using Content.Shared._Forge.Paper;
using NUnit.Framework;

namespace Content.Tests.Client.UserInterface.Controls;

[TestFixture]
public sealed class PaperSurfaceTests
{
    [Test]
    public void RecycledMaterialDoesNotRequireWearOrChangeTheSheetOutline()
    {
        var plain = new PaperSurfaceAppearance { Seed = 17, Wear = 0, Fibers = 0, Impurities = 0 };
        var rough = plain.Clone(); rough.Fibers = 1; rough.Impurities = 1;
        var a = PaperSurfaceGenerator.Generate(plain, new(), 180, 160, 0);
        var b = PaperSurfaceGenerator.Generate(rough, new(), 180, 160, 0);
        using var ab = a.Background; using var ao = a.Overlay;
        using var bb = b.Background; using var bo = b.Overlay;
        var differences = 0;
        for (var y = 0; y < ab.Height; y++)
        for (var x = 0; x < ab.Width; x++)
        {
            Assert.That(ab[x, y], Is.EqualTo(ab[0, 0]));
            Assert.That(bb[x, y].A, Is.EqualTo(ab[x, y].A));
            Assert.That(bo[x, y].A, Is.Zero);
            if (ab[x, y] != bb[x, y]) differences++;
        }
        Assert.That(differences, Is.GreaterThan(100));
    }

    [Test]
    public void ReopeningTheSameSheetReproducesEveryPixel()
    {
        var appearance = new PaperSurfaceAppearance { Seed = 91723 };
        var a = PaperSurfaceGenerator.Generate(appearance, new(), 620, 662, 0);
        var b = PaperSurfaceGenerator.Generate(appearance.Clone(), new(), 620, 662, 0);
        using var ab = a.Background; using var ao = a.Overlay;
        using var bb = b.Background; using var bo = b.Overlay;
        for (var y = 0; y < ab.Height; y++)
        for (var x = 0; x < ab.Width; x++)
        {
            Assert.That(bb[x, y], Is.EqualTo(ab[x, y]));
            Assert.That(bo[x, y], Is.EqualTo(ao[x, y]));
        }
    }

    [Test]
    public void DifferentCopiesHaveDifferentSurfaces()
    {
        var a = PaperSurfaceGenerator.Generate(new() { Seed = 127 }, new(), 620, 662, 0);
        var b = PaperSurfaceGenerator.Generate(new() { Seed = 812 }, new(), 620, 662, 0);
        using var ab = a.Background; using var ao = a.Overlay;
        using var bb = b.Background; using var bo = b.Overlay;
        var differences = 0;
        for (var y = 0; y < ab.Height; y++)
        for (var x = 0; x < ab.Width; x++) if (ab[x, y] != bb[x, y]) differences++;
        Assert.That(differences, Is.GreaterThan(ab.Width * ab.Height / 2));
    }

    [Test]
    public void ReverseSideMirrorsTheSamePhysicalCreases()
    {
        var appearance = new PaperSurfaceAppearance { Seed = 91723 };
        var front = PaperSurfaceGenerator.Generate(appearance, new(), 620, 662, 0);
        var back = PaperSurfaceGenerator.Generate(appearance, new(), 620, 662, 1);
        using var fb = front.Background; using var fo = front.Overlay;
        using var bb = back.Background; using var bo = back.Overlay;
        for (var y = 0; y < fo.Height; y++)
        for (var x = 0; x < fo.Width; x++)
        {
            var a = fo[x, y]; var b = bo[bo.Width - x - 1, y];
            Assert.That(Math.Abs(a.A - b.A), Is.LessThanOrEqualTo(1));
            if (a.A > 1 && b.A > 1) Assert.That(b.R, Is.EqualTo(a.R));
        }
    }

    [TestCase(0f)]
    [TestCase(0.65f)]
    [TestCase(1f)]
    public void WearNeverObscuresPrintedContents(float wear)
    {
        var layers = PaperSurfaceGenerator.Generate(new() { Seed = 128, Wear = wear }, new(), 980, 935, 0);
        using var background = layers.Background; using var overlay = layers.Overlay;
        Assert.That(Math.Max(background.Width, background.Height), Is.LessThanOrEqualTo(512));
        var maxAlpha = 0;
        for (var y = 0; y < overlay.Height; y++)
        for (var x = 0; x < overlay.Width; x++) maxAlpha = Math.Max(maxAlpha, overlay[x, y].A);
        Assert.That(maxAlpha, Is.LessThanOrEqualTo(26));
        if (wear == 0) Assert.That(maxAlpha, Is.Zero);
    }
    [TestCase(1)]
    [TestCase(812)]
    [TestCase(91723)]
    public void TornOutlineMatchesBothFacesAndDoesNotReachContent(int seed)
    {
        var appearance = new PaperSurfaceAppearance { Seed = seed, Wear = 1 };
        var front = PaperSurfaceGenerator.Generate(appearance, new(), 180, 160, 0);
        var back = PaperSurfaceGenerator.Generate(appearance, new(), 180, 160, 1);
        using var fb = front.Background; using var fo = front.Overlay;
        using var bb = back.Background; using var bo = back.Overlay;
        var damaged = 0;
        for (var y = 0; y < fb.Height; y++)
        for (var x = 0; x < fb.Width; x++)
        {
            var alpha = fb[x, y].A;
            Assert.That(Math.Abs(alpha - bb[bb.Width - x - 1, y].A), Is.LessThanOrEqualTo(1));
            if (alpha < 255) damaged++;
            if (x > 8 && y > 8 && x < fb.Width - 8 && y < fb.Height - 8)
                Assert.That(alpha, Is.EqualTo(255));
        }
        Assert.That(damaged, Is.GreaterThan(0));
    }

}
