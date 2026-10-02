using System;
using Content.Client._Forge.Newspapers;
using NUnit.Framework;

namespace Content.Tests.Client.UserInterface.Controls;

[TestFixture]
public sealed class NewspaperPhotoRequestTests
{
    [Test]
    public void LostRequestIsRetriedUntilImageArrives()
    {
        var request = new NewspaperPhotoRequest();
        request.SetTarget(7, 0);
        Assert.That(request.TryRequest(TimeSpan.Zero, false, out var edition), Is.True);
        Assert.That(edition, Is.Zero);
        Assert.That(request.TryRequest(TimeSpan.FromSeconds(1), false, out _), Is.False);
        // Applying the same cached state must not postpone the next attempt.
        request.SetTarget(7, 0);
        Assert.That(request.TryRequest(TimeSpan.FromSeconds(2), false, out _), Is.True);
        Assert.That(request.TryRequest(TimeSpan.FromSeconds(4), true, out _), Is.False);
    }

    [Test]
    public void ReopenedWindowRequestsSavedDraftPhotoAgain()
    {
        var closedWindow = new NewspaperPhotoRequest();
        closedWindow.SetTarget(7, 0);
        closedWindow.TryRequest(TimeSpan.Zero, false, out _);
        var reopenedWindow = new NewspaperPhotoRequest();
        reopenedWindow.SetTarget(7, 0);
        Assert.That(reopenedWindow.TryRequest(TimeSpan.FromSeconds(0.1), false, out _), Is.True);
    }

    [Test]
    public void SwitchingToArchiveRequestsItsPhotoImmediately()
    {
        var request = new NewspaperPhotoRequest();
        request.SetTarget(7, 0);
        request.TryRequest(TimeSpan.Zero, false, out _);
        request.SetTarget(8, 3);
        Assert.That(request.TryRequest(TimeSpan.FromSeconds(0.1), false, out var edition), Is.True);
        Assert.That(edition, Is.EqualTo(3));
        request.SetTarget(-1, 0);
        Assert.That(request.TryRequest(TimeSpan.FromSeconds(5), false, out _), Is.False);
    }
    [Test]
    public void MissingImageCannotGenerateUnboundedNetworkRequests()
    {
        var request = new NewspaperPhotoRequest();
        request.SetTarget(7, 0);
        var sent = 0;
        for (var seconds = 0; seconds < 120; seconds++)
            if (request.TryRequest(TimeSpan.FromSeconds(seconds), false, out _)) sent++;
        Assert.That(sent, Is.EqualTo(5));
        request.SetTarget(8, 0);
        Assert.That(request.TryRequest(TimeSpan.FromSeconds(120), false, out _), Is.True);
    }

}
