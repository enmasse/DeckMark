using DeckMark.Core.Model;
using DeckMark.Core.Parser;

namespace DeckMark.Tests;

public class ParserTests
{
    private static DeckDocument Parse(string source) =>
        DeckMarkParser.Parse(source);

    // ── Deck header ──────────────────────────────────────────────────────────

    [Fact]
    public void Parse_DeckHeader_ExtractsTitle()
    {
        var doc = Parse("""
            :::deck
            title: My Presentation
            :::
            """);

        Assert.Equal("My Presentation", doc.Header.Title);
    }

    [Fact]
    public void Parse_DeckHeader_ExtractsAllSupportedKeys()
    {
        var doc = Parse("""
            :::deck
            title: T
            subtitle: S
            author: A
            event: E
            theme: dark
            aspect: 16:9
            footer: F
            language: sv-SE
            company: C
            :::
            """);

        Assert.Equal("T", doc.Header.Title);
        Assert.Equal("S", doc.Header.Subtitle);
        Assert.Equal("A", doc.Header.Author);
        Assert.Equal("E", doc.Header.Event);
        Assert.Equal("dark", doc.Header.Theme);
        Assert.Equal("16:9", doc.Header.Aspect);
        Assert.Equal("F", doc.Header.Footer);
        Assert.Equal("sv-SE", doc.Header.Language);
        Assert.Equal("C", doc.Header.Company);
    }

    // ── Slide boundaries ─────────────────────────────────────────────────────

    [Fact]
    public void Parse_TwoSlides_ProducesTwoSlides()
    {
        var doc = Parse("""
            :::deck
            title: X
            :::

            ---
            # First

            ---
            # Second
            """);

        Assert.Equal(2, doc.Slides.Count);
    }

    [Fact]
    public void Parse_SlideTitle_IsFirstH1()
    {
        var doc = Parse("""
            :::deck
            title: X
            :::

            ---
            # Hello World
            """);

        Assert.Equal("Hello World", doc.Slides[0].Title);
    }

    // ── Slide directives ─────────────────────────────────────────────────────

    [Fact]
    public void Parse_SlideDirectives_ExtractsIdAndLayout()
    {
        var doc = Parse("""
            :::deck
            title: X
            :::

            ---
            # Slide
            @id: arch
            @layout: two-column
            """);

        Assert.Equal("arch", doc.Slides[0].Id);
        Assert.Equal("two-column", doc.Slides[0].Layout);
    }

    [Fact]
    public void Parse_SlideDirectives_ExtractsAllKeys()
    {
        var doc = Parse("""
            :::deck
            title: X
            :::

            ---
            # Slide
            @id: s1
            @layout: content
            @background: dark
            @transition: fade
            @build: by-bullet
            @footer: My Footer
            """);

        Assert.Equal("s1", doc.Slides[0].Id);
        Assert.Equal("content", doc.Slides[0].Layout);
        Assert.Equal("dark", doc.Slides[0].Background);
        Assert.Equal("fade", doc.Slides[0].Transition);
        Assert.Equal("by-bullet", doc.Slides[0].Build);
        Assert.Equal("My Footer", doc.Slides[0].Footer);
    }

    [Fact]
    public void Parse_SlideDirectives_UnknownTransitionIsPreserved()
    {
        var doc = Parse("""
            :::deck
            title: X
            :::

            ---
            # Slide
            @transition: dissolve
            """);

        Assert.Equal("dissolve", doc.Slides[0].Transition);
    }

    // ── Body content ─────────────────────────────────────────────────────────

    [Fact]
    public void Parse_Paragraph_ProducesParagraphBlock()
    {
        var doc = Parse("""
            :::deck
            title: X
            :::

            ---
            # Slide
            Hello paragraph.
            """);

        var body = doc.Slides[0].Body;
        Assert.Contains(body, b => b.Kind == BlockKind.Paragraph && b.RawContent.Contains("Hello paragraph."));
    }

    [Fact]
    public void Parse_CodeBlock_ProducesCodeBlock()
    {
        var doc = Parse("""
            :::deck
            title: X
            :::

            ---
            # Slide

            ```csharp
            var x = 1;
            ```
            """);

        var code = doc.Slides[0].Body.FirstOrDefault(b => b.Kind == BlockKind.CodeBlock);
        Assert.NotNull(code);
        Assert.Equal("csharp", code.Language);
        Assert.Contains("var x = 1;", code.RawContent);
    }

    [Fact]
    public void Parse_ExecutableCodeBlock_SetsExecutableFlag()
    {
        var doc = Parse("""
            :::deck
            title: X
            :::

            ---
            # Slide

            ```csharp exec
            var x = 1;
            ```
            """);

        var code = doc.Slides[0].Body.FirstOrDefault(b => b.Kind == BlockKind.CodeBlock);
        Assert.NotNull(code);
        Assert.Equal("csharp", code.Language);
        Assert.True(code.IsExecutable);
    }

    [Fact]
    public void Parse_MermaidBlock_ProducesMermaidBlock()
    {
        var doc = Parse("""
            :::deck
            title: X
            :::

            ---
            # Slide

            ```mermaid
            flowchart LR
                A --> B
            ```
            """);

        var mermaid = doc.Slides[0].Body.FirstOrDefault(b => b.Kind == BlockKind.MermaidBlock);
        Assert.NotNull(mermaid);
        Assert.Contains("flowchart LR", mermaid.RawContent);
    }

    // ── Speaker notes ─────────────────────────────────────────────────────────

    [Fact]
    public void Parse_NotesBlock_ProducesNotesNotInBody()
    {
        var doc = Parse("""
            :::deck
            title: X
            :::

            ---
            # Slide
            Some content.

            :::notes
            Presenter reminder.
            :::
            """);

        var slide = doc.Slides[0];
        Assert.DoesNotContain(slide.Body, b => b.Kind == BlockKind.Notes);
        Assert.NotEmpty(slide.Notes);
        Assert.Contains(slide.Notes, n => n.RawContent.Contains("Presenter reminder."));
    }

    // ── Columns ───────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_ColumnsBlock_ProducesColumnsBlockWithChildren()
    {
        var doc = Parse("""
            :::deck
            title: X
            :::

            ---
            # Slide

            :::columns
            :::left
            Left text.
            :::
            :::right
            Right text.
            :::
            :::
            """);

        var cols = doc.Slides[0].Body.FirstOrDefault(b => b.Kind == BlockKind.Columns);
        Assert.NotNull(cols);
        Assert.Contains(cols.Left, b => b.RawContent.Contains("Left text."));
        Assert.Contains(cols.Right, b => b.RawContent.Contains("Right text."));
    }

    // ── Images ────────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_ImageWithSizeHint_ExtractsAttributes()
    {
        var doc = Parse("""
            :::deck
            title: X
            :::

            ---
            # Slide
            ![Terminal UI](images/shot.png){width=80%}
            """);

        var img = doc.Slides[0].Body.FirstOrDefault(b => b.Kind == BlockKind.Image);
        Assert.NotNull(img);
        Assert.Equal("Terminal UI", img.AltText);
        Assert.Equal("images/shot.png", img.ImageUrl);
        Assert.Equal("80%", img.ImageWidth);
    }

    // ── Callout ───────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_CalloutBlock_ExtractsTypeAndTitle()
    {
        var doc = Parse("""
            :::deck
            title: X
            :::

            ---
            # Slide

            :::callout type=info title="Key point"
            The agent stays in the same terminal workflow.
            :::
            """);

        var callout = doc.Slides[0].Body.FirstOrDefault(b => b.Kind == BlockKind.Callout);
        Assert.NotNull(callout);
        Assert.Equal("info", callout.CalloutType);
        Assert.Equal("Key point", callout.CalloutTitle);
        Assert.Contains("The agent stays", callout.RawContent);
    }

    // ── Empty / edge cases ────────────────────────────────────────────────────

    [Fact]
    public void Parse_EmptyBody_ProducesZeroSlides()
    {
        var doc = Parse("""
            :::deck
            title: X
            :::
            """);

        Assert.Empty(doc.Slides);
    }

    [Fact]
    public void Parse_DefaultAspect_Is16x9()
    {
        var doc = Parse("""
            :::deck
            title: X
            :::
            """);

        Assert.Equal("16:9", doc.Header.Aspect);
    }
}
