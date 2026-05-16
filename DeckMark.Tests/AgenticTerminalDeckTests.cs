using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using DocumentFormat.OpenXml;
using DeckMark.Core.Converter;
using DeckMark.Core.Mermaid;
using DeckMark.Core.Parser;

namespace DeckMark.Tests;

public class AgenticTerminalDeckTests : IDisposable
{
    private static readonly string DeckPath =
        Path.Combine(AppContext.BaseDirectory, "TestData", "AgenticTerminal-presentation.deck.md");

    private readonly List<string> _tempFiles = [];

    private string ConvertDeckToFile()
    {
        var source = File.ReadAllText(DeckPath);
        var doc = DeckMarkParser.Parse(source);
        var path = Path.GetTempFileName() + ".pptx";
        _tempFiles.Add(path);
        new PptxConverter(new MermaidPlaceholderRenderer()).Convert(doc, path);
        return path;
    }

    public void Dispose()
    {
        foreach (var f in _tempFiles)
            if (File.Exists(f)) File.Delete(f);
    }

    [Fact]
    public void Parse_AgenticTerminalDeck_ExtractsDeckTitle()
    {
        var source = File.ReadAllText(DeckPath);
        var doc = DeckMarkParser.Parse(source);
        Assert.Equal("AgenticTerminal", doc.Header.Title);
    }

    [Fact]
    public void Parse_AgenticTerminalDeck_ExtractsAuthor()
    {
        var source = File.ReadAllText(DeckPath);
        var doc = DeckMarkParser.Parse(source);
        Assert.Equal("Mats Alritzson", doc.Header.Author);
    }

    [Fact]
    public void Parse_AgenticTerminalDeck_ProducesExpectedSlideCount()
    {
        var source = File.ReadAllText(DeckPath);
        var doc = DeckMarkParser.Parse(source);
        // The deck has 13 slides (separated by ---)
        Assert.Equal(13, doc.Slides.Count);
    }

    [Fact]
    public void Parse_AgenticTerminalDeck_FirstSlideTitleIsAgenticTerminal()
    {
        var source = File.ReadAllText(DeckPath);
        var doc = DeckMarkParser.Parse(source);
        Assert.Equal("AgenticTerminal", doc.Slides[0].Title);
    }

    [Fact]
    public void Parse_AgenticTerminalDeck_FirstSlideHasFadeTransition()
    {
        var source = File.ReadAllText(DeckPath);
        var doc = DeckMarkParser.Parse(source);
        Assert.Equal("fade", doc.Slides[0].Transition);
    }

    [Fact]
    public void Parse_AgenticTerminalDeck_SecondSlideHasNoExplicitTransition()
    {
        var source = File.ReadAllText(DeckPath);
        var doc = DeckMarkParser.Parse(source);
        Assert.Null(doc.Slides[1].Transition);
    }

    [Fact]
    public void Parse_AgenticTerminalDeck_NotesBlockIsNotInSlideBody()
    {
        var source = File.ReadAllText(DeckPath);
        var doc = DeckMarkParser.Parse(source);
        // The first slide has a :::notes block; its text should not appear in the body blocks
        var firstSlide = doc.Slides[0];
        var bodyText = string.Join(" ", firstSlide.Body.Select(b => b.ToString()));
        Assert.DoesNotContain("Start with the problem statement", bodyText);
    }

    [Fact]
    public void Convert_AgenticTerminalDeck_ProducesValidPptx()
    {
        var path = ConvertDeckToFile();
        Assert.True(File.Exists(path));
        Assert.True(new FileInfo(path).Length > 0);
        using var prs = PresentationDocument.Open(path, false);
        Assert.NotNull(prs.PresentationPart);
    }

    [Fact]
    public void Convert_AgenticTerminalDeck_SlideCountAtLeastSourceCount()
    {
        var source = File.ReadAllText(DeckPath);
        var doc = DeckMarkParser.Parse(source);
        var path = ConvertDeckToFile();

        using var prs = PresentationDocument.Open(path, false);
        var slideCount = prs.PresentationPart!.Presentation.SlideIdList!.Count();
        // Overflow splitting may produce more output slides than source slides
        Assert.True(slideCount >= doc.Slides.Count,
            $"Expected at least {doc.Slides.Count} slides but got {slideCount}");
    }

    [Fact]
    public void Convert_AgenticTerminalDeck_TitleAppearsInCoreProperties()
    {
        var path = ConvertDeckToFile();
        using var prs = PresentationDocument.Open(path, false);
        Assert.Equal("AgenticTerminal", prs.PackageProperties.Title);
    }

    [Fact]
    public void Convert_AgenticTerminalDeck_AuthorAppearsInCoreProperties()
    {
        var path = ConvertDeckToFile();
        using var prs = PresentationDocument.Open(path, false);
        Assert.Equal("Mats Alritzson", prs.PackageProperties.Creator);
    }

    [Fact]
    public void Convert_AgenticTerminalDeck_FirstSlideTitleAppearsInSlideXml()
    {
        var path = ConvertDeckToFile();
        using var prs = PresentationDocument.Open(path, false);
        var firstSlideXml = prs.PresentationPart!.SlideParts.First().Slide.OuterXml;
        Assert.Contains("AgenticTerminal", firstSlideXml);
    }

    [Fact]
    public void Convert_AgenticTerminalDeck_SecondSlideInheritsFadeTransition()
    {
        var path = ConvertDeckToFile();
        using var prs = PresentationDocument.Open(path, false);
        var slides = prs.PresentationPart!.SlideParts.ToList();
        Assert.Contains("<p:fade", slides[1].Slide.OuterXml, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Convert_AgenticTerminalDeck_MermaidDiagramsProducePlaceholders()
    {
        var path = ConvertDeckToFile();
        using var prs = PresentationDocument.Open(path, false);
        // At least one slide should contain a mermaid placeholder
        var anyMermaid = prs.PresentationPart!.SlideParts
            .Any(sp => sp.Slide.OuterXml.Contains("mermaid", StringComparison.OrdinalIgnoreCase));
        Assert.True(anyMermaid);
    }

    [Fact]
    public void Convert_AgenticTerminalDeck_SpeakerNotesAppearsOnFirstSlide()
    {
        var path = ConvertDeckToFile();
        using var prs = PresentationDocument.Open(path, false);
        var firstSlidePart = prs.PresentationPart!.SlideParts.First();
        Assert.NotNull(firstSlidePart.NotesSlidePart);
        var notesXml = firstSlidePart.NotesSlidePart!.NotesSlide.OuterXml;
        Assert.Contains("Start with the problem statement", notesXml);
    }

    [Fact]
    public void Convert_AgenticTerminalDeck_PassesOpenXmlValidation()
    {
        var path = ConvertDeckToFile();
        using var prs = PresentationDocument.Open(path, false);
        var validator = new OpenXmlValidator(FileFormatVersions.Microsoft365);
        var errors = validator.Validate(prs).ToList();
        // Report all errors in the failure message for easy diagnosis
        Assert.True(errors.Count == 0,
            $"{errors.Count} validation error(s):\n" +
            string.Join("\n", errors.Select(e => $"  [{e.ErrorType}] {e.Description} @ {e.Path?.XPath}")));
    }
}
