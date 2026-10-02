using System;
using System.Reflection;
using Content.Server._Forge.Newspapers;
using Content.Shared._Forge.Newspapers;
using NUnit.Framework;

namespace Content.Tests.Shared;

[TestFixture]
public sealed class NewspaperPhotoBufferTests
{
    [Test]
    public void ImportsAreBoundedByCountPayloadAndTotalMemory()
    {
        Assert.That(NewspaperPhotoBuffer.CanImport(11, NewspaperPhotoBuffer.MaxBytes - 100, 100), Is.True);
        Assert.That(NewspaperPhotoBuffer.CanImport(12, 0, 100), Is.False);
        Assert.That(NewspaperPhotoBuffer.CanImport(0, NewspaperPhotoBuffer.MaxBytes - 99, 100), Is.False);
        Assert.That(NewspaperPhotoBuffer.CanImport(0, long.MaxValue, 100), Is.False);
        Assert.That(NewspaperPhotoBuffer.CanImport(0, 0, 4 * 1024 * 1024 + 1), Is.False);
        Assert.That(NewspaperPhotoBuffer.CanImport(0, 0, 0), Is.False);
    }

    [Test]
    public void PruningKeepsBufferAndPublishedImagesAndRemovesOnlyUnusedImages()
    {
        var desk = new NewspaperDeskComponent();
        for (var id = 1; id <= 5; id++) desk.Photos[id] = new byte[] { (byte)id };
        desk.BufferedPhotos.Add(1);
        desk.Editions.Add(new NewspaperEdition { PhotoId = 2 });
        desk.Draft.PhotoId = 3;
        desk.PublicationDrafts[1] = new NewspaperEdition { PhotoId = 4 };
        var prune = typeof(NewspaperSystem).GetMethod("PrunePhotos", BindingFlags.NonPublic | BindingFlags.Static)!;
        prune.Invoke(null, new object[] { desk });
        Assert.That(desk.Photos.Keys, Is.EquivalentTo(new[] { 1, 2, 3, 4 }));
        desk.BufferedPhotos.Clear();
        prune.Invoke(null, new object[] { desk });
        Assert.That(desk.Photos.Keys, Is.EquivalentTo(new[] { 2, 3, 4 }));
    }
}
