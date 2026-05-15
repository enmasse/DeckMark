using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using DeckMark.Core.Converter;
using DeckMark.Core.Mermaid;
using DeckMark.Core.Model;
using DeckMark.Core.Parser;

namespace DeckMark.Tests;

public class ConverterTests : IDisposable
{
    private readonly List<string> _tempFiles = [];

    private string ConvertToFile(string source, IMermaidRenderer? mermaid = null)
    {
        var doc = DeckMarkParser.Parse(source);
        var path = Path.GetTempFileName() + ".pptx";
        _tempFiles.Add(path);
        var converter = new PptxConverter(mermaid ?? new MermaidPlaceholderRenderer());
        converter.Convert(doc, path);
        return path;
    }

    public void Dispose()
    {
        foreach (var f in _tempFiles)
            if (File.Exists(f)) File.Delete(f);
    }

    // ── Output file ───────────────────────────────────────────────────────────

    [Fact]
    public void Convert_ProducesValidPptxFile()
    {
        var path = ConvertToFile("""
            :::deck
            title: Test
            :::

            ---
            # Hello
            """);

        Assert.True(File.Exists(path));
        Assert.True(new FileInfo(path).Length > 0);
        // Should open without exception
        using var prs = PresentationDocument.Open(path, false);
        Assert.NotNull(prs.PresentationPart);
    }

    // ── Slide count ───────────────────────────────────────────────────────────

    [Fact]
    public void Convert_SlideCount_MatchesSourceSlides()
    {
        var path = ConvertToFile("""
            :::deck
            title: Test
            :::

            ---
            # One

            ---
            # Two

            ---
            # Three
            """);

        using var prs = PresentationDocument.Open(path, false);
        var slideIdList = prs.PresentationPart!.Presentation.SlideIdList;
        Assert.Equal(3, slideIdList!.Count());
    }

    // ── Slide titles ──────────────────────────────────────────────────────────

    [Fact]
    public void Convert_SlideTitles_AppearInSlideXml()
    {
        var path = ConvertToFile("""
            :::deck
            title: Test
            :::

            ---
            # Architecture Overview
            """);

        using var prs = PresentationDocument.Open(path, false);
        var slidePart = prs.PresentationPart!.SlideParts.First();
        var xml = slidePart.Slide.OuterXml;
        Assert.Contains("Architecture Overview", xml);
    }

    // ── Presentation metadata ─────────────────────────────────────────────────

    [Fact]
    public void Convert_DeckTitle_AppearsInCoreProperties()
    {
        var path = ConvertToFile("""
            :::deck
            title: My Deck
            author: Mats
            :::

            ---
            # Slide
            """);

        using var prs = PresentationDocument.Open(path, false);
        var props = prs.PackageProperties;
        Assert.Equal("My Deck", props.Title);
        Assert.Equal("Mats", props.Creator);
    }

    // ── Speaker notes ─────────────────────────────────────────────────────────

    [Fact]
    public void Convert_SpeakerNotes_AppearInNotesSlidePart()
    {
        var path = ConvertToFile("""
            :::deck
            title: Test
            :::

            ---
            # Slide

            :::notes
            Remember to breathe.
            :::
            """);

        using var prs = PresentationDocument.Open(path, false);
        var slidePart = prs.PresentationPart!.SlideParts.First();
        Assert.NotNull(slidePart.NotesSlidePart);
        var notesXml = slidePart.NotesSlidePart!.NotesSlide.OuterXml;
        Assert.Contains("Remember to breathe.", notesXml);
    }

    // ── Code blocks ───────────────────────────────────────────────────────────

    [Fact]
    public void Convert_CodeBlock_ContentAppearsInSlide()
    {
        var path = ConvertToFile("""
            :::deck
            title: Test
            :::

            ---
            # Code

            ```csharp
            var x = 42;
            ```
            """);

        using var prs = PresentationDocument.Open(path, false);
        var xml = prs.PresentationPart!.SlideParts.First().Slide.OuterXml;
        Assert.Contains("var x = 42;", xml);
    }

    // ── Mermaid placeholder ───────────────────────────────────────────────────

    [Fact]
    public void Convert_MermaidBlock_PlaceholderTextAppearsInSlide()
    {
        var path = ConvertToFile("""
            :::deck
            title: Test
            :::

            ---
            # Diagram

            ```mermaid
            flowchart LR
                A --> B
            ```
            """, new MermaidPlaceholderRenderer());

        using var prs = PresentationDocument.Open(path, false);
        var xml = prs.PresentationPart!.SlideParts.First().Slide.OuterXml;
        // Placeholder renderer should include mermaid source or [Mermaid] marker
        Assert.Contains("mermaid", xml, StringComparison.OrdinalIgnoreCase);
    }

    // ── Callout ───────────────────────────────────────────────────────────────

    [Fact]
    public void Convert_Callout_ContentAppearsInSlide()
    {
        var path = ConvertToFile("""
            :::deck
            title: Test
            :::

            ---
            # Info

            :::callout type=info title="Key point"
            This is important.
            :::
            """);

        using var prs = PresentationDocument.Open(path, false);
        var xml = prs.PresentationPart!.SlideParts.First().Slide.OuterXml;
        Assert.Contains("This is important.", xml);
    }
}
