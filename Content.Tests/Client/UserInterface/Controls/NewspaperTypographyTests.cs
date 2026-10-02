using System;
using System.Linq;
using System.IO;
using System.Numerics;
using System.Text;
using Content.Client._Forge.Newspapers;
using NUnit.Framework;
using Content.Shared._Forge.Newspapers;
using Robust.Client.Graphics;
using Robust.Shared.Maths;

namespace Content.Tests.Client.UserInterface.Controls;

[TestFixture]
public sealed class NewspaperTypographyTests
{
    [TestCase(0.5f)]
    [TestCase(0.75f)]
    [TestCase(1f)]
    public void ThinRulesRemainVisibleAtSmallUiScales(float scale)
    {
        var horizontal = NewspaperPage.RuleRectangle(new NewspaperBlock { X = 24, Y = 42, Width = 572, Height = 1 }, scale);
        var vertical = NewspaperPage.RuleRectangle(new NewspaperBlock { X = 24, Y = 42, Width = 1, Height = 300 }, scale);
        Assert.That(horizontal.Height, Is.GreaterThanOrEqualTo(1));
        Assert.That(vertical.Width, Is.GreaterThanOrEqualTo(1));
    }

    [Test]
    public void LongTextReportsOverflowWithoutShrinkingOrLosingCharacters()
    {
        var text = new string('Ы', 350);
        var fitted = NewspaperTypography.FitBox(text, size => new TestFont(size), 576, 130, 16);
        Assert.That(fitted.Fits, Is.False);
        Assert.That(fitted.Size, Is.EqualTo(16));
        var lines = fitted.Text.Split('\n');
        Assert.That(string.Concat(lines), Is.EqualTo(text));
        Assert.That(lines.All(line => line.Length * fitted.Size <= 572), Is.True);
    }

    [Test]
    public void GlyphWiderThanTheBlockIsOverflow()
    {
        var fit = NewspaperTypography.FitBox("W", size => new TestFont(size), 4, 50, 16);
        Assert.That(fit.Fits, Is.False);
        Assert.That(fit.Text, Is.EqualTo("W"));
    }

    [Test]
    public void SmallMetadataBlocksDoNotSilentlyShrink()
    {
        var fit = NewspaperTypography.FitBox("Edition 1", size => new TestFont(size), 200, 10, 12);
        Assert.That(fit.Fits, Is.False);
        Assert.That(fit.Size, Is.EqualTo(12));
        Assert.That(fit.Text, Is.EqualTo("Edition 1"));
    }

    [Test]
    public void ParagraphSpacingCountsTowardsHeight()
    {
        const string text = "First\n\nSecond\n\nThird";
        var fitted = NewspaperTypography.FitBox(text, size => new TestFont(size), 200, 80, 20);
        Assert.That(fitted.Fits, Is.False);
        Assert.That(fitted.Size, Is.EqualTo(20));
        Assert.That(fitted.Text, Is.EqualTo(text));
    }

    [Test]
    public void ImpossibleContentIsReportedWithoutTruncatingIt()
    {
        var text = new string('\n', 100);
        var fitted = NewspaperTypography.FitBox(text, size => new TestFont(size), 576, 20, 16);
        Assert.That(fitted.Fits, Is.False);
        Assert.That(fitted.Text, Is.EqualTo(text));
    }

    [Test]
    public void InlineStylesPreserveUnicodeAndNestedEmphasis()
    {
        var characters = NewspaperTypography.ParseStyles("[b]Ж[b]и[/b]р[/b][i]К[/i]😀");
        Assert.That(string.Concat(characters.Select(c => c.Rune.ToString())), Is.EqualTo("ЖирК😀"));
        Assert.That(characters.Take(3).All(c => c.Bold && !c.Italic), Is.True);
        Assert.That(characters[3].Italic && !characters[3].Bold, Is.True);
        Assert.That(characters[4].Bold || characters[4].Italic, Is.False);
        var unsupported = "[command=help]text[/command]";
        Assert.That(string.Concat(NewspaperTypography.ParseStyles(unsupported).Select(c => c.Rune.ToString())), Is.EqualTo(unsupported));
    }

    [Test]
    public void StyledWrappingMeasuresEmphasizedGlyphs()
    {
        var result = NewspaperTypography.LayoutStyled("aa [b]bb[/b]", (bold, _) => new TestFont(bold ? 20 : 10), 65, 100);
        Assert.That(result.Fits, Is.True);
        Assert.That(result.Lines, Has.Count.EqualTo(2));
        Assert.That(result.Lines[1].Width, Is.EqualTo(40));
        var overflow = NewspaperTypography.LayoutStyled("[b]bb[/b]", (bold, _) => new TestFont(bold ? 20 : 10), 65, 15);
        Assert.That(overflow.Fits, Is.False);
    }

    [Test]
    public void FormattingSelectionCanBeToggledWithoutChangingSurroundingText()
    {
        var styled = NewspaperTypography.ToggleStyle("a слово z", 2, 7, "b");
        Assert.That(styled.Text, Is.EqualTo("a [b]слово[/b] z"));
        var plain = NewspaperTypography.ToggleStyle(styled.Text, styled.Start, styled.End, "b");
        Assert.That(plain.Text, Is.EqualTo("a слово z"));
        Assert.That(plain.Start, Is.EqualTo(2));
        Assert.That(plain.End, Is.EqualTo(7));
    }

    [Test]
    public void AllFontStylesHaveBundledResources()
    {
        var root = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (root != null && !Directory.Exists(Path.Combine(root.FullName, "Resources", "Fonts"))) root = root.Parent;
        Assert.That(root, Is.Not.Null);
        foreach (var kind in Enum.GetValues<NewspaperFont>())
        foreach (var bold in new[] { false, true })
        foreach (var italic in new[] { false, true })
        {
            var path = NewspaperTextBlock.FontPath(kind, bold, italic);
            Assert.That(File.Exists(Path.Combine(root!.FullName, "Resources", path.TrimStart('/'))), Is.True, path);
        }
    }

    [Test]
    public void LongArticleMeasurementRemainsLinear()
    {
        var font = new TestFont(2);
        var text = new string('W', 4000);
        NewspaperTypography.LayoutStyled(text, (_, _) => font, 1200, 1200);
        Assert.That(font.MetricsRequests, Is.LessThan(text.Length * 6));
    }

    [Test]
    public void PhotographShaderParses()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Resources", "Textures", "Shaders", "newspaper_photo.swsl")))
            directory = directory.Parent;
        var code = File.ReadAllText(Path.Combine(directory!.FullName, "Resources", "Textures", "Shaders", "newspaper_photo.swsl"));
        var parser = typeof(Texture).Assembly.GetType("Robust.Client.Graphics.ShaderParser")!;
        var parse = parser.GetMethod("Parse", new[] { typeof(string), typeof(Robust.Shared.ContentPack.IResourceManager) })!;
        Assert.DoesNotThrow(() => parse.Invoke(null, new object?[] { code, null }));
    }

    private sealed class TestFont(int size) : Font
    {
        public int MetricsRequests;
        public override int GetAscent(float scale) => size;
        public override int GetHeight(float scale) => size;
        public override int GetDescent(float scale) => 0;
        public override int GetLineHeight(float scale) => size + 2;
        public override CharMetrics? GetCharMetrics(Rune rune, float scale, bool fallback = true)
        { MetricsRequests++; return new CharMetrics(0, size, size, size, size); }
        public override float DrawChar(DrawingHandleBase handle, Rune rune, Vector2 baseline, float scale,
            Color color, bool fallback = true) => throw new NotSupportedException();
    }
}
