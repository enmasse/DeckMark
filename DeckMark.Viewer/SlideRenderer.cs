using DeckMark.Core.Mermaid;
using DeckMark.Core.Model;
using SkiaSharp;

namespace DeckMark.Viewer;

/// <summary>
/// Renders a single <see cref="Slide"/> onto a Skia canvas at the logical slide size.
/// The caller is responsible for applying zoom/pan transforms before invoking Draw.
/// </summary>
internal sealed class SlideRenderer
{
    // Logical slide dimensions (16:9)
    public const float SlideWidth = 1280f;
    public const float SlideHeight = 720f;
    private const float InlineMermaidGap = 16f;
    private const float InlineMermaidMinScale = 0.20f;
    private const byte SlideBackgroundAlpha = 0x30;

    // Theme colours
    private static readonly SKColor Background  = new(0x1E, 0x1E, 0x2E, SlideBackgroundAlpha);
    private static readonly SKColor Surface      = new(0x31, 0x32, 0x44);
    private static readonly SKColor Accent       = new(0x89, 0xB4, 0xFA);
    private static readonly SKColor TextPrimary  = new(0xCD, 0xD6, 0xF4);
    private static readonly SKColor TextMuted    = new(0x6C, 0x70, 0x86);
    private static readonly SKColor CodeBg       = new(0x18, 0x18, 0x26);
    private static readonly SKColor CodeFg       = new(0xA6, 0xE3, 0xA1);
    private static readonly SKColor CalloutBg    = new(0x1E, 0x3A, 0x5F);
    private static readonly SKColor CalloutBorder= new(0x89, 0xB4, 0xFA);
    private static readonly SKColor QuoteFg      = new(0xF9, 0xE2, 0xAF);

    private readonly SKTypeface _regular;
    private readonly SKTypeface _bold;
    private readonly SKTypeface _mono;
    private readonly IReadOnlyDictionary<string, MermaidRenderAsset?> _diagrams;
    private List<SKRect>? _occupiedBodyRects;
    private List<LayoutDebugRect>? _layoutDebugRects;
    private int _currentSlideIndex;

    public SlideRenderer(IReadOnlyDictionary<string, MermaidRenderAsset?>? diagrams = null)
    {
        _diagrams = diagrams ?? Array.Empty<KeyValuePair<string, MermaidRenderAsset?>>().ToDictionary(static pair => pair.Key, static pair => pair.Value);
        _regular = SKTypeface.FromFamilyName("Segoe UI",    SKFontStyle.Normal) ??
                   SKTypeface.FromFamilyName("DejaVu Sans", SKFontStyle.Normal) ??
                   SKTypeface.Default;
        _bold    = SKTypeface.FromFamilyName("Segoe UI",    SKFontStyle.Bold) ??
                   SKTypeface.FromFamilyName("DejaVu Sans", SKFontStyle.Bold) ??
                   SKTypeface.Default;
        _mono    = SKTypeface.FromFamilyName("Cascadia Code",  SKFontStyle.Normal) ??
                   SKTypeface.FromFamilyName("Consolas",        SKFontStyle.Normal) ??
                   SKTypeface.FromFamilyName("DejaVu Sans Mono",SKFontStyle.Normal) ??
                   SKTypeface.Default;
    }

    public void Draw(SKCanvas canvas, Slide slide, DeckHeader header, int slideIndex, int slideCount)
    {
        Draw(canvas, slide, header, slideIndex, slideCount, includeMermaid: true, showLayoutDebugOverlay: false);
    }

    public void Draw(SKCanvas canvas, Slide slide, DeckHeader header, int slideIndex, int slideCount, bool includeMermaid)
    {
        Draw(canvas, slide, header, slideIndex, slideCount, includeMermaid, collector: null, mermaidFocus: null, showLayoutDebugOverlay: false);
    }

    public void Draw(SKCanvas canvas, Slide slide, DeckHeader header, int slideIndex, int slideCount, bool includeMermaid, bool showLayoutDebugOverlay)
    {
        Draw(canvas, slide, header, slideIndex, slideCount, includeMermaid, collector: null, mermaidFocus: null, showLayoutDebugOverlay: showLayoutDebugOverlay);
    }

    public void Draw(SKCanvas canvas, Slide slide, DeckHeader header, int slideIndex, int slideCount, MermaidFocusRenderState mermaidFocus)
    {
        Draw(canvas, slide, header, slideIndex, slideCount, includeMermaid: true, collector: null, mermaidFocus: mermaidFocus, showLayoutDebugOverlay: false);
    }

    public void Draw(SKCanvas canvas, Slide slide, DeckHeader header, int slideIndex, int slideCount, MermaidFocusRenderState mermaidFocus, bool showLayoutDebugOverlay)
    {
        Draw(canvas, slide, header, slideIndex, slideCount, includeMermaid: true, collector: null, mermaidFocus: mermaidFocus, showLayoutDebugOverlay: showLayoutDebugOverlay);
    }

    private void Draw(SKCanvas canvas, Slide slide, DeckHeader header, int slideIndex, int slideCount, bool includeMermaid, List<MermaidOverlayLayout>? collector, MermaidFocusRenderState? mermaidFocus, bool showLayoutDebugOverlay)
    {
        canvas.Clear(Background);

        var previousOccupiedBodyRects = _occupiedBodyRects;
        var previousLayoutDebugRects = _layoutDebugRects;
        int previousSlideIndex = _currentSlideIndex;
        _currentSlideIndex = slideIndex;
        _occupiedBodyRects = collector is null ? [] : null;
        _layoutDebugRects = showLayoutDebugOverlay && collector is null ? [] : null;

        bool isTitle = slide.Layout is "title" or "section";

        if (isTitle)
            DrawTitleLayout(canvas, slide, header, includeMermaid, collector, mermaidFocus);
        else if (slide.Layout == "two-column")
            DrawTwoColumnLayout(canvas, slide, includeMermaid, collector, mermaidFocus);
        else
            DrawContentLayout(canvas, slide, includeMermaid, collector, mermaidFocus);

        if (includeMermaid && collector is null)
            DrawFocusedMermaidOverlay(canvas, mermaidFocus);

        if (_layoutDebugRects is not null)
            DrawLayoutDebugOverlay(canvas);

        DrawFooter(canvas, slide, header, slideIndex, slideCount);
        _occupiedBodyRects = previousOccupiedBodyRects;
        _layoutDebugRects = previousLayoutDebugRects;
        _currentSlideIndex = previousSlideIndex;
    }

    public IReadOnlyList<MermaidOverlayLayout> GetMermaidLayouts(Slide slide, DeckHeader header, int slideIndex, int slideCount)
    {
        using var surface = SKSurface.Create(new SKImageInfo(1, 1));
        if (surface is null)
            return [];

        var collector = new List<MermaidOverlayLayout>();
        Draw(surface.Canvas, slide, header, slideIndex, slideCount, false, collector, null, showLayoutDebugOverlay: false);
        return collector;
    }

    // ── Layouts ──────────────────────────────────────────────────────────────

    private void DrawTitleLayout(SKCanvas canvas, Slide slide, DeckHeader header, bool includeMermaid, List<MermaidOverlayLayout>? collector = null, MermaidFocusRenderState? mermaidFocus = null)
    {
        bool hasBody = slide.Body.Count > 0;

        float cy = hasBody ? SlideHeight * 0.14f : SlideHeight * 0.42f;

        DrawText(canvas, slide.Title, SlideWidth / 2f, cy, _bold, 64f, Accent,
            SKTextAlign.Center);

        string sub = string.IsNullOrEmpty(slide.Title) ? header.Subtitle : string.Empty;
        if (!string.IsNullOrEmpty(sub))
            DrawText(canvas, sub, SlideWidth / 2f, cy + 80f, _regular, 32f, TextPrimary,
                SKTextAlign.Center);

        float bodyY = cy + 90f;
        foreach (var block in slide.Body)
            bodyY = DrawBlock(canvas, block, 80f, bodyY, SlideWidth - 160f, includeMermaid, collector, mermaidFocus);
    }

    private void DrawContentLayout(SKCanvas canvas, Slide slide, bool includeMermaid, List<MermaidOverlayLayout>? collector = null, MermaidFocusRenderState? mermaidFocus = null)
    {
        float y = 60f;
        DrawText(canvas, slide.Title, 80f, y, _bold, 48f, Accent, SKTextAlign.Left);
        DrawHRule(canvas, 80f, y + 58f, SlideWidth - 160f);
        y += 90f;

        foreach (var block in slide.Body)
            y = DrawBlock(canvas, block, 80f, y, SlideWidth - 160f, includeMermaid, collector, mermaidFocus);
    }

    private void DrawTwoColumnLayout(SKCanvas canvas, Slide slide, bool includeMermaid, List<MermaidOverlayLayout>? collector = null, MermaidFocusRenderState? mermaidFocus = null)
    {
        float y = 60f;
        DrawText(canvas, slide.Title, 80f, y, _bold, 48f, Accent, SKTextAlign.Left);
        DrawHRule(canvas, 80f, y + 58f, SlideWidth - 160f);
        y += 90f;

        float colWidth = (SlideWidth - 200f) / 2f;
        float leftX  = 80f;
        float rightX = 80f + colWidth + 40f;

        foreach (var block in slide.Body)
        {
            if (block.Kind == BlockKind.Columns)
            {
                float leftY  = y;
                float rightY = y;
                foreach (var b in block.Left)
                    leftY = DrawBlock(canvas, b, leftX, leftY, colWidth, includeMermaid, collector, mermaidFocus);
                foreach (var b in block.Right)
                    rightY = DrawBlock(canvas, b, rightX, rightY, colWidth, includeMermaid, collector, mermaidFocus);
            }
            else
            {
                y = DrawBlock(canvas, block, leftX, y, SlideWidth - 160f, includeMermaid, collector, mermaidFocus);
            }
        }
    }

    // ── Block dispatcher ─────────────────────────────────────────────────────

    private float DrawBlock(SKCanvas canvas, ContentBlock block, float x, float y, float width, bool includeMermaid, List<MermaidOverlayLayout>? collector, MermaidFocusRenderState? mermaidFocus)
    {
        return block.Kind switch
        {
            BlockKind.Heading      => DrawHeading(canvas, block, x, y, width),
            BlockKind.BulletList   => DrawList(canvas, block, x, y, width, bullet: true),
            BlockKind.NumberedList => DrawList(canvas, block, x, y, width, bullet: false),
            BlockKind.CodeBlock    => DrawCode(canvas, block, x, y, width),
            BlockKind.BlockQuote   => DrawBlockQuote(canvas, block, x, y, width),
            BlockKind.Callout      => DrawCallout(canvas, block, x, y, width),
            BlockKind.Paragraph    => DrawParagraph(canvas, block.RawContent, x, y, width),
            BlockKind.MermaidBlock => DrawMermaidPlaceholder(canvas, block.RawContent, x, y, width, includeMermaid, collector, mermaidFocus),
            _                      => y + 16f,
        };
    }

    // ── Block renderers ──────────────────────────────────────────────────────

    private float DrawHeading(SKCanvas canvas, ContentBlock block, float x, float y, float width)
    {
        float size = block.RawContent.StartsWith("## ") ? 28f : 22f;
        string text = block.RawContent.TrimStart('#').Trim();
        DrawText(canvas, text, x, y, _bold, size, Accent, SKTextAlign.Left);
        RecordOccupiedRect(x, y, MeasureTextWidth(text, _bold, size), size * 1.35f);
        return y + size + 12f;
    }

    private float DrawList(SKCanvas canvas, ContentBlock block, float x, float y, float width, bool bullet)
    {
        var lines = block.RawContent.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        int counter = 1;
        foreach (var raw in lines)
        {
            string stripped = StripLeadingListMarker(raw);
            string prefix   = bullet ? "•  " : $"{counter++}.  ";
            float  wrapped  = DrawWrapped(canvas, prefix + StripInlineMarkdown(stripped),
                                          x, y, width, _regular, 24f, TextPrimary);
            y = wrapped + 8f;
        }
        return y + 4f;
    }

    private float DrawCode(SKCanvas canvas, ContentBlock block, float x, float y, float width)
    {
        var lines  = block.RawContent.Split('\n');
        float lineH = 22f;
        float pad   = 16f;
        float boxH  = lines.Length * lineH + pad * 2f;

        using var bgPaint = new SKPaint { Color = CodeBg, IsAntialias = true };
        canvas.DrawRoundRect(new SKRoundRect(new SKRect(x, y, x + width, y + boxH), 6f), bgPaint);
        RecordOccupiedRect(new SKRect(x, y, x + width, y + boxH));

        float ty = y + pad + lineH - 4f;
        foreach (var line in lines)
        {
            DrawText(canvas, line, x + pad, ty, _mono, 18f, CodeFg, SKTextAlign.Left);
            ty += lineH;
        }
        return y + boxH + 16f;
    }

    private float DrawBlockQuote(SKCanvas canvas, ContentBlock block, float x, float y, float width)
    {
        using var barPaint = new SKPaint { Color = Accent, IsAntialias = true };
        canvas.DrawRect(new SKRect(x, y, x + 4f, y + 36f), barPaint);
        RecordOccupiedRect(new SKRect(x, y, x + 4f, y + 36f));
        DrawWrapped(canvas, StripInlineMarkdown(block.RawContent.TrimStart('>', ' ')),
                    x + 14f, y, width - 14f, _regular, 22f, QuoteFg);
        return y + 44f;
    }

    private float DrawCallout(SKCanvas canvas, ContentBlock block, float x, float y, float width)
    {
        float pad  = 14f;
        float innerW = width - pad * 2f;

        // Estimate height
        float textH = EstimateWrappedHeight(block.RawContent, innerW, 21f) + 12f;
        float titleH = string.IsNullOrEmpty(block.CalloutTitle) ? 0f : 30f;
        float boxH   = titleH + textH + pad * 2f;

        using var bgPaint     = new SKPaint { Color = CalloutBg,     IsAntialias = true };
        using var borderPaint = new SKPaint { Color = CalloutBorder, IsAntialias = true,
                                              IsStroke = true, StrokeWidth = 2f };
        var rect = new SKRect(x, y, x + width, y + boxH);
        canvas.DrawRoundRect(new SKRoundRect(rect, 6f), bgPaint);
        canvas.DrawRoundRect(new SKRoundRect(rect, 6f), borderPaint);
        RecordOccupiedRect(rect);

        float ty = y + pad;
        if (!string.IsNullOrEmpty(block.CalloutTitle))
        {
            DrawText(canvas, block.CalloutTitle, x + pad, ty, _bold, 20f, Accent, SKTextAlign.Left);
            ty += 30f;
        }
        DrawWrapped(canvas, StripInlineMarkdown(block.RawContent), x + pad, ty, innerW, _regular, 21f, TextPrimary);
        return y + boxH + 16f;
    }

    private float DrawParagraph(SKCanvas canvas, string text, float x, float y, float width)
    {
        float bottom = DrawWrapped(canvas, StripInlineMarkdown(text), x, y, width, _regular, 24f, TextPrimary);
        return bottom + 12f;
    }

    private float DrawMermaidPlaceholder(SKCanvas canvas, string source, float x, float y, float width, bool includeMermaid, List<MermaidOverlayLayout>? collector, MermaidFocusRenderState? mermaidFocus)
    {
        if (_diagrams.TryGetValue(source, out var asset) && asset is { Format: MermaidRenderFormat.Png })
        {
            using var img = SKImage.FromEncodedData(asset.Content);
            if (img is null)
                return y + 16f;

            var sourceRect = GetVisibleImageBounds(img);

            float footerTop = SlideHeight - 38f;
            var availableRect = GetBestInlineMermaidAvailableRect(x, y, width, footerTop, sourceRect.Width, sourceRect.Height);
            if (availableRect.Width <= 0f || availableRect.Height <= 0f)
                return y + 16f;

            float scale = Math.Min(1f, Math.Min(availableRect.Width / sourceRect.Width, availableRect.Height / sourceRect.Height));
            if (!float.IsFinite(scale) || scale < InlineMermaidMinScale)
                return y + 16f;

            float dw    = sourceRect.Width * scale;
            float dh    = sourceRect.Height * scale;
            float dx    = availableRect.Left + (availableRect.Width - dw) / 2f;
            float dy    = availableRect.Top + (availableRect.Height - dh) / 2f;
            var targetRect = new SKRect(dx, dy, dx + dw, dy + dh);
            RecordLayoutDebugRect(targetRect, SKColors.DeepSkyBlue, "mermaid");
            ValidateInlineMermaidPlacement(source, targetRect, mermaidFocus);

            collector?.Add(new MermaidOverlayLayout(source, targetRect));

            if (!includeMermaid)
                return dy + dh + InlineMermaidGap;

            if (IsFocusedMermaidSource(source, mermaidFocus))
                return dy + dh + InlineMermaidGap;

            using var paint = new SKPaint { IsAntialias = true };
            canvas.DrawImage(img, sourceRect, targetRect, paint);
            return dy + dh + InlineMermaidGap;
        }

        float boxH = 80f;
        using var bgPaint = new SKPaint { Color = Surface, IsAntialias = true };
        canvas.DrawRoundRect(new SKRoundRect(new SKRect(x, y, x + width, y + boxH), 6f), bgPaint);
        DrawText(canvas, "[ diagram ]", x + width / 2f, y + boxH / 2f + 8f,
                 _regular, 18f, TextMuted, SKTextAlign.Center);
        return y + boxH + 16f;
    }

    private void DrawMermaidFallback(SKCanvas canvas, SKRect rect)
    {
        using var bgPaint = new SKPaint { Color = Surface, IsAntialias = true };
        canvas.DrawRoundRect(new SKRoundRect(rect, 6f), bgPaint);
        DrawText(canvas, "[ diagram ]", rect.MidX, rect.MidY + 8f, _regular, 18f, TextMuted, SKTextAlign.Center);
    }

    private static bool IsFocusedMermaidSource(string source, MermaidFocusRenderState? focus)
    {
        if (focus is null)
            return false;

        return string.Equals(focus.From?.Source, source, StringComparison.Ordinal) ||
               string.Equals(focus.To?.Source, source, StringComparison.Ordinal);
    }

    private void DrawFocusedMermaidOverlay(SKCanvas canvas, MermaidFocusRenderState? focus)
    {
        if (focus is null)
            return;

        DrawFocusedMermaidFrame(canvas, focus.From);
        DrawFocusedMermaidFrame(canvas, focus.To);
    }

    private void DrawFocusedMermaidFrame(SKCanvas canvas, MermaidFocusFrame? frame)
    {
        if (frame is null)
            return;

        if (!_diagrams.TryGetValue(frame.Source, out var asset) || asset is not { Format: MermaidRenderFormat.Png })
            return;

        using var img = SKImage.FromEncodedData(asset.Content);
        if (img is null)
            return;

        var sourceRect = GetVisibleImageBounds(img);

        var targetRect = LerpRect(frame.Bounds, GetFocusedMermaidRect(sourceRect.Width, sourceRect.Height), EaseOutCubic(frame.Progress));
        if (targetRect.Width <= 0f || targetRect.Height <= 0f)
            return;

        using var imagePaint = new SKPaint { IsAntialias = true, Color = SKColors.White };
        canvas.DrawImage(img, sourceRect, targetRect, imagePaint);
    }

    private static SKRect GetVisibleImageBounds(SKImage image)
    {
        using var bitmap = SKBitmap.FromImage(image);
        if (!bitmap.ReadyToDraw)
            return new SKRect(0f, 0f, image.Width, image.Height);

        const byte alphaThreshold = 48;
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
                if (pixels[rowStart + x].Alpha < alphaThreshold)
                    continue;

                if (x < minX) minX = x;
                if (y < minY) minY = y;
                if (x > maxX) maxX = x;
                if (y > maxY) maxY = y;
            }
        }

        if (maxX < minX || maxY < minY)
            return new SKRect(0f, 0f, image.Width, image.Height);

        return new SKRect(minX, minY, maxX + 1, maxY + 1);
    }

    private static SKRect GetInlineMermaidAvailableRect(float x, float y, float width, float footerTop)
    {
        float left = x;
        float top = y;
        float right = x + width;
        float bottom = footerTop - InlineMermaidGap;

        return right <= left || bottom <= top
            ? SKRect.Empty
            : new SKRect(left, top, right, bottom);
    }

    private SKRect GetBestInlineMermaidAvailableRect(float x, float y, float width, float footerTop, float imageWidth, float imageHeight)
    {
        var bestRect = GetInlineMermaidAvailableRect(x, y, width, footerTop);
        float bestScale = GetInlineMermaidFitScale(bestRect, imageWidth, imageHeight);
        RecordLayoutDebugRect(bestRect, SKColors.LimeGreen, "full");

        if (_occupiedBodyRects is null || _occupiedBodyRects.Count == 0)
            return bestRect;

        var occupiedBounds = GetOccupiedBounds(x, x + width, footerTop);
        if (occupiedBounds is null)
            return bestRect;

        var belowRect = new SKRect(
            x,
            Math.Max(y, occupiedBounds.Value.Bottom + InlineMermaidGap),
            x + width,
            footerTop - InlineMermaidGap);
        float belowScale = GetInlineMermaidFitScale(belowRect, imageWidth, imageHeight);
        RecordLayoutDebugRect(belowRect, SKColors.Goldenrod, "below");
        if (belowScale > bestScale)
        {
            bestRect = belowRect;
            bestScale = belowScale;
        }

        var rightRect = new SKRect(occupiedBounds.Value.Right + InlineMermaidGap, occupiedBounds.Value.Top, x + width, footerTop - InlineMermaidGap);
        float rightScale = GetInlineMermaidFitScale(rightRect, imageWidth, imageHeight);
        RecordLayoutDebugRect(rightRect, SKColors.OrangeRed, "right");
        if (rightScale > bestScale)
        {
            bestRect = rightRect;
            bestScale = rightScale;
        }

        var leftRect = new SKRect(x, occupiedBounds.Value.Top, occupiedBounds.Value.Left - InlineMermaidGap, footerTop - InlineMermaidGap);
        float leftScale = GetInlineMermaidFitScale(leftRect, imageWidth, imageHeight);
        RecordLayoutDebugRect(leftRect, SKColors.MediumPurple, "left");
        if (leftScale > bestScale)
            bestRect = leftRect;

        RecordLayoutDebugRect(bestRect, SKColors.Cyan, "best");

        return bestRect;
    }

    private SKRect? GetOccupiedBounds(float minX, float maxX, float maxBottom)
    {
        if (_occupiedBodyRects is null || _occupiedBodyRects.Count == 0)
            return null;

        SKRect? bounds = null;
        foreach (var rect in _occupiedBodyRects)
        {
            if (rect.Right <= minX || rect.Left >= maxX || rect.Top >= maxBottom)
                continue;

            bounds = bounds is null
                ? rect
                : SKRect.Union(bounds.Value, rect);
        }

        return bounds;
    }

    private static float GetInlineMermaidFitScale(SKRect availableRect, float imageWidth, float imageHeight)
    {
        if (availableRect.Width <= 0f || availableRect.Height <= 0f)
            return 0f;

        float scale = Math.Min(1f, Math.Min(availableRect.Width / imageWidth, availableRect.Height / imageHeight));
        return float.IsFinite(scale) ? scale : 0f;
    }

    private static SKRect GetFocusedMermaidRect(float imageWidth, float imageHeight)
    {
        const float maxWidth = SlideWidth * 0.88f;
        const float maxHeight = SlideHeight * 0.72f;
        float scale = Math.Min(maxWidth / imageWidth, maxHeight / imageHeight);
        float width = imageWidth * scale;
        float height = imageHeight * scale;
        float left = (SlideWidth - width) / 2f;
        float top = (SlideHeight - height) / 2f;
        return new SKRect(left, top, left + width, top + height);
    }

    private static SKRect LerpRect(SKRect from, SKRect to, float t)
    {
        return new SKRect(
            Lerp(from.Left, to.Left, t),
            Lerp(from.Top, to.Top, t),
            Lerp(from.Right, to.Right, t),
            Lerp(from.Bottom, to.Bottom, t));
    }

    private static float Lerp(float from, float to, float t) => from + ((to - from) * t);

    private void RecordOccupiedRect(float x, float y, float width, float height)
    {
        RecordOccupiedRect(new SKRect(x, y, x + width, y + height));
    }

    private void RecordOccupiedRect(SKRect rect)
    {
        if (_occupiedBodyRects is null || rect.Width <= 0f || rect.Height <= 0f)
            return;

        _occupiedBodyRects.Add(rect);
        RecordLayoutDebugRect(rect, SKColors.Red, "text");
    }

    private void RecordLayoutDebugRect(SKRect rect, SKColor color, string label)
    {
        if (_layoutDebugRects is null || rect.Width <= 0f || rect.Height <= 0f)
            return;

        _layoutDebugRects.Add(new LayoutDebugRect(rect, color, label));
    }

    private void DrawLayoutDebugOverlay(SKCanvas canvas)
    {
        if (_layoutDebugRects is null || _layoutDebugRects.Count == 0)
            return;

        foreach (var debugRect in _layoutDebugRects)
        {
            using var fillPaint = new SKPaint
            {
                Color = debugRect.Color.WithAlpha(36),
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
            };
            using var strokePaint = new SKPaint
            {
                Color = debugRect.Color.WithAlpha(220),
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 2f,
            };

            canvas.DrawRect(debugRect.Bounds, fillPaint);
            canvas.DrawRect(debugRect.Bounds, strokePaint);
            DrawText(canvas, debugRect.Label, debugRect.Bounds.Left + 4f, debugRect.Bounds.Top + 14f, _mono, 12f, debugRect.Color, SKTextAlign.Left);
        }
    }

    private void ValidateInlineMermaidPlacement(string source, SKRect targetRect, MermaidFocusRenderState? mermaidFocus)
    {
        if (_layoutDebugRects is null || _occupiedBodyRects is null || IsFocusedMermaidSource(source, mermaidFocus))
            return;

        foreach (var occupiedRect in _occupiedBodyRects)
        {
            if (!targetRect.IntersectsWith(occupiedRect))
                continue;

            throw new InvalidOperationException(
                $"Inline Mermaid overlaps occupied content on slide {_currentSlideIndex + 1}. Mermaid='{GetMermaidDebugLabel(source)}', target={FormatRect(targetRect)}, occupied={FormatRect(occupiedRect)}");
        }
    }

    private static string GetMermaidDebugLabel(string source)
    {
        string singleLine = source.ReplaceLineEndings(" ").Trim();
        return singleLine.Length <= 80 ? singleLine : singleLine[..80] + "...";
    }

    private static string FormatRect(SKRect rect)
    {
        return $"[{rect.Left:0.#},{rect.Top:0.#},{rect.Right:0.#},{rect.Bottom:0.#}]";
    }

    private float MeasureTextWidth(string text, SKTypeface face, float size)
    {
        using var font = new SKFont(face, size);
        return font.MeasureText(text);
    }

    private static float EaseOutCubic(float t)
    {
        float clamped = Math.Clamp(t, 0f, 1f);
        float inverse = 1f - clamped;
        return 1f - (inverse * inverse * inverse);
    }

    // ── Footer ───────────────────────────────────────────────────────────────

    private void DrawFooter(SKCanvas canvas, Slide slide, DeckHeader header, int index, int count)
    {
        string footer = string.IsNullOrEmpty(slide.Footer) ? header.Footer : slide.Footer;
        float  fy     = SlideHeight - 24f;

        DrawHRule(canvas, 60f, SlideHeight - 38f, SlideWidth - 120f);

        if (!string.IsNullOrEmpty(footer))
            DrawText(canvas, footer, 80f, fy, _regular, 16f, TextMuted, SKTextAlign.Left);

        DrawText(canvas, $"{index + 1} / {count}", SlideWidth - 80f, fy,
                 _regular, 16f, TextMuted, SKTextAlign.Right);
    }

    // ── Drawing helpers ──────────────────────────────────────────────────────

    private void DrawText(SKCanvas canvas, string text, float x, float y,
                          SKTypeface face, float size, SKColor color, SKTextAlign align)
    {
        using var font  = new SKFont(face, size);
        using var paint = new SKPaint { Color = color, IsAntialias = true };
        canvas.DrawText(text, x, y, align, font, paint);
    }

    private float DrawWrapped(SKCanvas canvas, string text, float x, float y,
                               float maxWidth, SKTypeface face, float size, SKColor color)
    {
        using var font  = new SKFont(face, size);
        using var paint = new SKPaint { Color = color, IsAntialias = true };

        float lineH = size * 1.35f;
        foreach (var line in WrapText(text, font, maxWidth))
        {
            canvas.DrawText(line, x, y + size, SKTextAlign.Left, font, paint);
            RecordOccupiedRect(x, y, font.MeasureText(line), lineH);
            y += lineH;
        }
        return y;
    }

    private float EstimateWrappedHeight(string text, float maxWidth, float size)
    {
        using var font = new SKFont(_regular, size);
        return WrapText(text, font, maxWidth).Count * size * 1.35f;
    }

    private void DrawHRule(SKCanvas canvas, float x, float y, float width)
    {
        using var paint = new SKPaint { Color = Surface, IsAntialias = false, StrokeWidth = 1.5f, IsStroke = true };
        canvas.DrawLine(x, y, x + width, y, paint);
    }

    private static List<string> WrapText(string text, SKFont font, float maxWidth)
    {
        var result = new List<string>();
        foreach (var paragraph in text.Split('\n'))
        {
            var words  = paragraph.Split(' ');
            var sb     = new System.Text.StringBuilder();
            foreach (var word in words)
            {
                string candidate = sb.Length == 0 ? word : sb + " " + word;
                if (font.MeasureText(candidate) > maxWidth && sb.Length > 0)
                {
                    result.Add(sb.ToString());
                    sb.Clear();
                    sb.Append(word);
                }
                else
                {
                    if (sb.Length > 0) sb.Append(' ');
                    sb.Append(word);
                }
            }
            if (sb.Length > 0) result.Add(sb.ToString());
        }
        return result;
    }

    // ── Markdown stripping ───────────────────────────────────────────────────

    private static string StripLeadingListMarker(string line)
    {
        line = line.TrimStart();
        if (line.StartsWith("- ") || line.StartsWith("* ") || line.StartsWith("+ "))
            return line[2..];
        // numbered: "1. "
        int dot = line.IndexOf(". ");
        if (dot > 0 && line[..dot].All(char.IsDigit))
            return line[(dot + 2)..];
        return line;
    }

    private static string StripInlineMarkdown(string text)
    {
        // Remove bold/italic markers and backtick code spans
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\*\*(.+?)\*\*", "$1");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\*(.+?)\*",     "$1");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"`(.+?)`",       "$1");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"__(.+?)__",     "$1");
        return text;
    }

    public readonly record struct MermaidOverlayLayout(string Source, SKRect Bounds);

    private readonly record struct LayoutDebugRect(SKRect Bounds, SKColor Color, string Label);

    public sealed record MermaidFocusRenderState(MermaidFocusFrame? From, MermaidFocusFrame? To, float Progress);

    public sealed record MermaidFocusFrame(string Source, SKRect Bounds, float Progress);
}
