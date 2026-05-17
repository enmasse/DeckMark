using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using DocumentFormat.OpenXml;
using DeckMark.Core.Converter;
using DeckMark.Core.Mermaid;
using DeckMark.Core.Parser;

namespace DeckMark.Tests;

public class AgenticTerminalDeckTests : IDisposable
{
    private const string DeckSource = """
        :::deck
        title: AgenticTerminal
        author: Mats Alritzson
        footer: AgenticTerminal · .NET 10 · Hex1b · Avalonia · GitHub Copilot SDK
        :::

        ---
        # AgenticTerminal
        @transition: fade

        AgenticTerminal helps drive an interactive terminal presentation.

        :::notes
        Start with the problem statement.
        :::

        ---
        # What the app does

        - Starts an interactive shell session
        - Connects to GitHub Copilot through GitHub.Copilot.SDK
        - Sends prompts with terminal context

        ---
        # Startup flow

        1. It resolves whether it should run as the full terminal experience.
        2. It loads configuration and startup options.

        ```mermaid
        sequenceDiagram
            participant User
            participant Program
            User->>Program: launch app
            Program-->>User: ready
        ```

        ---
        # Presenter mode

        Toggle presentation mode independently from the audience screen.

        ---
        # Viewer controls

        - Space advances slides
        - Backspace goes back
        - D toggles debug overlay

        ---
        # Mermaid focus

        Focused diagrams zoom into a highlighted view.

        ---
        # Rendering

        Slides are rendered with SkiaSharp.

        ---
        # Input handling

        Mouse drag pans the content while scroll zooms.

        ---
        # Transitions

        Slide transitions are resolved from the current or previous slide.

        ---
        # Notes support

        Speaker notes stay out of the visible slide body.

        ---
        # Layout debugging

        Debug overlays visualize occupied regions and Mermaid bounds.

        ---
        # Regression coverage

        Tests should rely on self-contained input.

        ---
        # Summary

        AgenticTerminal combines parsing, rendering, and presentation features.
        """;

    private readonly List<string> _tempFiles = [];

    private string ConvertDeckToFile()
    {
        var doc = DeckMarkParser.Parse(DeckSource);
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
        var doc = DeckMarkParser.Parse(DeckSource);
        Assert.Equal("AgenticTerminal", doc.Header.Title);
    }

    [Fact]
    public void Parse_AgenticTerminalDeck_ExtractsAuthor()
    {
        var doc = DeckMarkParser.Parse(DeckSource);
        Assert.Equal("Mats Alritzson", doc.Header.Author);
    }

    [Fact]
    public void Parse_AgenticTerminalDeck_ProducesExpectedSlideCount()
    {
        var doc = DeckMarkParser.Parse(DeckSource);
        Assert.Equal(13, doc.Slides.Count);
    }

    [Fact]
    public void Parse_AgenticTerminalDeck_FirstSlideTitleIsAgenticTerminal()
    {
        var doc = DeckMarkParser.Parse(DeckSource);
        Assert.Equal("AgenticTerminal", doc.Slides[0].Title);
    }

    [Fact]
    public void Parse_AgenticTerminalDeck_FirstSlideHasFadeTransition()
    {
        var doc = DeckMarkParser.Parse(DeckSource);
        Assert.Equal("fade", doc.Slides[0].Transition);
    }

    [Fact]
    public void Parse_AgenticTerminalDeck_SecondSlideHasNoExplicitTransition()
    {
        var doc = DeckMarkParser.Parse(DeckSource);
        Assert.Null(doc.Slides[1].Transition);
    }

    [Fact]
    public void Parse_AgenticTerminalDeck_NotesBlockIsNotInSlideBody()
    {
        var doc = DeckMarkParser.Parse(DeckSource);
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
        var doc = DeckMarkParser.Parse(DeckSource);
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
