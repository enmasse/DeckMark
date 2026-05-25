using DeckMark.Core.Mermaid;
using DeckMark.Core.Model;
using DeckMark.Viewer;
using SkiaSharp;

namespace DeckMark.Tests;

public sealed class ViewerRegressionTests
{
    [Fact]
    public void GetMermaidLayouts_AndRenderedPixels_StayWithinComputedBounds()
    {
        const string source = "flowchart LR\nA-->B";
        using var diagramBitmap = new SKBitmap(400, 200, true);
        using (var canvas = new SKCanvas(diagramBitmap))
        {
            canvas.Clear(new SKColor(255, 0, 255));
        }

        using var diagramImage = SKImage.FromBitmap(diagramBitmap);
        using var encodedDiagram = diagramImage.Encode(SKEncodedImageFormat.Png, 100);
        var renderer = new SlideRenderer(new Dictionary<string, MermaidRenderAsset?>
        {
            [source] = new MermaidRenderAsset(MermaidRenderFormat.Png, encodedDiagram.ToArray(), new MermaidRenderSize(400f, 200f)),
        });

        var slide = new Slide
        {
            Title = "Mermaid",
            Body =
            [
                new ContentBlock
                {
                    Kind = BlockKind.MermaidBlock,
                    Language = "mermaid",
                    RawContent = source,
                },
            ],
        };

        var header = new DeckHeader();
        var layout = Assert.Single(renderer.GetMermaidLayouts(slide, header, 0, 1));

        using var surface = SKSurface.Create(new SKImageInfo((int)SlideRenderer.SlideWidth, (int)SlideRenderer.SlideHeight));
        Assert.NotNull(surface);
        renderer.Draw(surface.Canvas, slide, header, 0, 1);

        using var snapshot = surface.Snapshot();
        using var rendered = SKBitmap.FromImage(snapshot);
        var magentaBounds = FindColorBounds(rendered, static color => color.Red > 220 && color.Green < 32 && color.Blue > 220);

        Assert.True(magentaBounds.Width > 0);
        Assert.True(magentaBounds.Height > 0);
        Assert.InRange(magentaBounds.Left, layout.Bounds.Left - 2f, layout.Bounds.Left + 2f);
        Assert.InRange(magentaBounds.Top, layout.Bounds.Top - 2f, layout.Bounds.Top + 2f);
        Assert.InRange(magentaBounds.Right, layout.Bounds.Right - 2f, layout.Bounds.Right + 2f);
        Assert.InRange(magentaBounds.Bottom, layout.Bounds.Bottom - 2f, layout.Bounds.Bottom + 2f);
        Assert.InRange(layout.Bounds.Width / layout.Bounds.Height, 1.98f, 2.02f);
    }

    [Fact]
    public void MermaidFocusStateFactory_KeepsFocusedDiagramVisibleAfterAnimationCompletes()
    {
        var layouts = new[]
        {
            new SlideRenderer.MermaidOverlayLayout("diagram", new SKRect(10f, 20f, 110f, 70f)),
        };
        var state = new ViewerState { SlideCount = 1 };

        Assert.True(state.AdvanceMermaidFocus(layouts.Length));

        var focus = MermaidFocusStateFactory.Create(
            slideIndex: 0,
            currentSlideIndex: 0,
            layouts,
            state,
            DateTimeOffset.UtcNow + TimeSpan.FromSeconds(1));

        Assert.NotNull(focus);
        Assert.Null(focus.From);
        Assert.NotNull(focus.To);
        Assert.Equal("diagram", focus.To!.Source);
        Assert.Equal(layouts[0].Bounds, focus.To.Bounds);
        Assert.Equal(1f, focus.To.Progress);
        Assert.Equal(1f, focus.Progress);
    }

    [Fact]
    public void GetExecutableCodeLayouts_CollectsRunnableCodeBlocks()
    {
        var renderer = new SlideRenderer();
        var slide = new Slide
        {
            Title = "Code",
            Body =
            [
                new ContentBlock
                {
                    Kind = BlockKind.CodeBlock,
                    Language = "csharp",
                    IsExecutable = true,
                    RawContent = "return 42;",
                },
            ],
        };

        var layout = Assert.Single(renderer.GetExecutableCodeLayouts(slide, new DeckHeader(), 0, 1));
        Assert.Equal(0, layout.Index);
        Assert.True(layout.Bounds.Width > 0f);
        Assert.True(layout.Bounds.Height > 0f);
    }

    [Fact]
    public void GetExecutableCodeOverlay_ReturnsPanelAndRunButton()
    {
        var renderer = new SlideRenderer();
        var slide = new Slide
        {
            Title = "Code",
            Body =
            [
                new ContentBlock
                {
                    Kind = BlockKind.CodeBlock,
                    Language = "csharp",
                    IsExecutable = true,
                    RawContent = "return 42;",
                },
            ],
        };

        var layouts = renderer.GetExecutableCodeLayouts(slide, new DeckHeader(), 0, 1);
        var overlay = renderer.GetExecutableCodeOverlay(layouts, focusedIndex: 0, codeOutput: "42");

        Assert.NotNull(overlay);
        Assert.True(overlay.Value.PanelBounds.Width > 0f);
        Assert.True(overlay.Value.RunButtonBounds.Width > 0f);
        Assert.True(overlay.Value.PanelBounds.Contains(overlay.Value.RunButtonBounds.Left, overlay.Value.RunButtonBounds.Top));
    }

    private static SKRect FindColorBounds(SKBitmap bitmap, Func<SKColor, bool> predicate)
    {
        int minX = bitmap.Width;
        int minY = bitmap.Height;
        int maxX = -1;
        int maxY = -1;

        var pixels = bitmap.Pixels;
        for (int y = 0; y < bitmap.Height; y++)
        {
            int rowStart = y * bitmap.Width;
            for (int x = 0; x < bitmap.Width; x++)
            {
                if (!predicate(pixels[rowStart + x]))
                    continue;

                if (x < minX) minX = x;
                if (y < minY) minY = y;
                if (x > maxX) maxX = x;
                if (y > maxY) maxY = y;
            }
        }

        return maxX < minX || maxY < minY
            ? SKRect.Empty
            : new SKRect(minX, minY, maxX + 1, maxY + 1);
    }
}
