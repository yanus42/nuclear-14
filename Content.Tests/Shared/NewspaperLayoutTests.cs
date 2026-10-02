using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Content.Shared._Forge.Newspapers;
using NUnit.Framework;
using Robust.Shared.IoC;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager;

namespace Content.Tests.Shared;

[TestFixture]
public sealed class NewspaperLayoutTests : ContentUnitTest
{
    private IPrototypeManager _prototypes = default!;
    private string _root = default!;

    [OneTimeSetUp]
    public void Setup()
    {
        var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Resources", "Prototypes", "_Nuclear14", "newspaper_templates.yml")))
            dir = dir.Parent;
        _root = dir?.FullName ?? throw new DirectoryNotFoundException("Repository resources not found");
        IoCManager.Resolve<ISerializationManager>().Initialize();
        _prototypes = IoCManager.Resolve<IPrototypeManager>();
        _prototypes.Initialize();
        _prototypes.LoadString(File.ReadAllText(Path.Combine(_root, "Resources", "Prototypes", "_Nuclear14", "newspaper_templates.yml")));
        _prototypes.ResolveResults();
    }

    [Test]
    public void StarterLayoutsLoadAndStayInsideTheirPages()
    {
        var starters = _prototypes.EnumeratePrototypes<NewspaperTemplatePrototype>().ToArray();
        Assert.That(starters, Has.Length.EqualTo(5));
        Assert.That(starters.Count(p => p.Default), Is.EqualTo(1));
        foreach (var starter in starters)
            Assert.That(NewspaperLayout.IsValid(starter.Layout), Is.True, starter.ID);
        Assert.That(starters.Count(p => p.Layout.Blocks.Any(b => b.Kind == NewspaperBlockKind.Photo)), Is.EqualTo(4));
    }

    [TestCase("ru-RU")]
    [TestCase("en-US")]
    public void StarterTextKeysExist(string language)
    {
        var text = File.ReadAllText(Path.Combine(_root, "Resources", "Locale", language, "_Nuclear14", "newspaper.ftl"));
        var keys = Regex.Matches(text, @"(?m)^([a-z0-9-]+) =").Select(m => m.Groups[1].Value).ToArray();
        Assert.That(keys.Distinct().Count(), Is.EqualTo(keys.Length), "Duplicate translation keys");
        foreach (var font in Enum.GetValues<NewspaperFont>())
            Assert.That(keys, Does.Contain($"newspaper-font-{font.ToString().ToLowerInvariant()}"));
        foreach (var starter in _prototypes.EnumeratePrototypes<NewspaperTemplatePrototype>())
        {
            Assert.That(keys, Does.Contain(starter.Name));
            foreach (var block in starter.Layout.Blocks)
            {
                if (block.TextKey != null) Assert.That(keys, Does.Contain(block.TextKey));
                if (block.DemoKey != null) Assert.That(keys, Does.Contain(block.DemoKey));
            }
        }
    }

    [Test]
    public void PublishedSnapshotDoesNotFollowDraftEdits()
    {
        var original = _prototypes.EnumeratePrototypes<NewspaperTemplatePrototype>().First(p => p.Layout.Pages == 2).Layout.Clone();
        original.PhotoId = 42;
        original.PublicationId = 3;
        var published = original.Clone();
        var previousX = published.Blocks[0].X;
        original.Blocks[0].Text = "Changed";
        original.Blocks[0].X = 0;
        original.Blocks.RemoveAt(1);
        original.PhotoId = 43;
        Assert.That(published.Blocks[0].Text, Is.Not.EqualTo("Changed"));
        Assert.That(published.Blocks[0].X, Is.EqualTo(previousX));
        Assert.That(published.Blocks.Count, Is.EqualTo(original.Blocks.Count + 1));
        Assert.That(published.PhotoId, Is.EqualTo(42));
        Assert.That(published.PublicationId, Is.EqualTo(3));
    }

    [Test]
    public void PublicationNumbersStayIndependentWhenNamesChange()
    {
        var editions = new[]
        {
            new NewspaperEdition { PublicationId = 1, Name = "First", Number = 4 },
            new NewspaperEdition { PublicationId = 2, Name = "Second", Number = 7 },
        };
        Assert.That(NewspaperLayout.NextEditionNumber(editions, 1), Is.EqualTo(5));
        Assert.That(NewspaperLayout.NextEditionNumber(editions, 2), Is.EqualTo(8));
        Assert.That(NewspaperLayout.NextEditionNumber(editions, 3), Is.EqualTo(1));
        editions[0].Name = "Renamed";
        Assert.That(NewspaperLayout.NextEditionNumber(editions, 1), Is.EqualTo(5));
    }

    [Test]
    public void PhotoCaptionFollowsResizingAndPublishedPhotoStyleStaysIndependent()
    {
        var draft = _prototypes.Index<NewspaperTemplatePrototype>("N14NewspaperArticle").Layout.Clone();
        var published = draft.Clone();
        var photo = draft.Blocks.First(b => b.Kind == NewspaperBlockKind.Photo);
        var caption = draft.Blocks.First(b => b.CaptionFor == photo.Id);
        var originalCaptionY = caption.Y;
        photo.Width = photo.Height = 200;
        photo.Grayscale = true;
        NewspaperLayout.ResolvePhotoCaptions(draft);
        Assert.That(caption.Y, Is.EqualTo(photo.Y + photo.Height + 6));
        Assert.That(caption.Width, Is.EqualTo(photo.Width));
        Assert.That(NewspaperLayout.IsValid(draft), Is.True);
        Assert.That(published.Blocks.First(b => b.CaptionFor == photo.Id).Y, Is.EqualTo(originalCaptionY));
        Assert.That(published.Blocks.First(b => b.Kind == NewspaperBlockKind.Photo).Grayscale, Is.False);
        caption.CaptionFor = "missing-photo";
        Assert.That(NewspaperLayout.IsValid(draft), Is.False);
    }

    [Test]
    public void InvalidGeometryAndExcessiveContentAreRejected()
    {
        var draft = new NewspaperEdition();
        var block = new NewspaperBlock { X = int.MaxValue };
        draft.Blocks.Add(block);
        Assert.That(NewspaperLayout.IsValid(draft), Is.False);
        block.X = 0; block.Page = 2;
        Assert.That(NewspaperLayout.IsValid(draft), Is.False);
        block.Page = 0; block.Kind = (NewspaperBlockKind)255;
        Assert.That(NewspaperLayout.IsValid(draft), Is.False);
        block.Kind = NewspaperBlockKind.Text; block.Text = new string('x', NewspaperLayout.MaxTextLength + 1);
        Assert.That(NewspaperLayout.IsValid(draft), Is.False);
    }
}
