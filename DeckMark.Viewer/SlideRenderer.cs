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

    // Theme colours
    private static readonly SKColor Background  = new(0x1E, 0x1E, 0x2E);
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
    private readonly IReadOnlyDictionary<string, SKImage?> _diagrams;

    public SlideRenderer(IReadOnlyDictionary<string, SKImage?>? diagrams = null)
    {
        _diagrams = diagrams ?? new Dictionary<string, SKImage?>();
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
        canvas.Clear(Background);

        bool isTitle = slide.Layout is "title" or "section";

        if (isTitle)
            DrawTitleLayout(canvas, slide, header);
        else if (slide.Layout == "two-column")
            DrawTwoColumnLayout(canvas, slide);
        else
            DrawContentLayout(canvas, slide);

        DrawFooter(canvas, slide, header, slideIndex, slideCount);
    }

    // ── Layouts ──────────────────────────────────────────────────────────────

    private void DrawTitleLayout(SKCanvas canvas, Slide slide, DeckHeader header)
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
            bodyY = DrawBlock(canvas, block, 80f, bodyY, SlideWidth - 160f);
    }

    private void DrawContentLayout(SKCanvas canvas, Slide slide)
    {
        float y = 60f;
        DrawText(canvas, slide.Title, 80f, y, _bold, 48f, Accent, SKTextAlign.Left);
        DrawHRule(canvas, 80f, y + 58f, SlideWidth - 160f);
        y += 90f;

        foreach (var block in slide.Body)
            y = DrawBlock(canvas, block, 80f, y, SlideWidth - 160f);
    }

    private void DrawTwoColumnLayout(SKCanvas canvas, Slide slide)
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
                    leftY = DrawBlock(canvas, b, leftX, leftY, colWidth);
                foreach (var b in block.Right)
                    rightY = DrawBlock(canvas, b, rightX, rightY, colWidth);
            }
            else
            {
                y = DrawBlock(canvas, block, leftX, y, SlideWidth - 160f);
            }
        }
    }

    // ── Block dispatcher ─────────────────────────────────────────────────────

    private float DrawBlock(SKCanvas canvas, ContentBlock block, float x, float y, float width)
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
            BlockKind.MermaidBlock => DrawMermaidPlaceholder(canvas, block.RawContent, x, y, width),
            _                      => y + 16f,
        };
    }

    // ── Block renderers ──────────────────────────────────────────────────────

    private float DrawHeading(SKCanvas canvas, ContentBlock block, float x, float y, float width)
    {
        float size = block.RawContent.StartsWith("## ") ? 28f : 22f;
        string text = block.RawContent.TrimStart('#').Trim();
        DrawText(canvas, text, x, y, _bold, size, Accent, SKTextAlign.Left);
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

    private float DrawMermaidPlaceholder(SKCanvas canvas, string source, float x, float y, float width)
    {
        if (_diagrams.TryGetValue(source, out var img) && img is not null)
        {
            float footerTop = SlideHeight - 38f;
            float maxH  = footerTop - y - 16f;
            float scale = Math.Min(width / img.Width, maxH / img.Height);
            float dw    = img.Width  * scale;
            float dh    = img.Height * scale;
            float dx    = x + (width - dw) / 2f;

            using var paint = new SKPaint { IsAntialias = true };
            canvas.DrawImage(
                img,
                new SKRect(dx, y, dx + dw, y + dh),
                new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear),
                paint);
            return y + dh + 16f;
        }

        float boxH = 80f;
        using var bgPaint = new SKPaint { Color = Surface, IsAntialias = true };
        canvas.DrawRoundRect(new SKRoundRect(new SKRect(x, y, x + width, y + boxH), 6f), bgPaint);
        DrawText(canvas, "[ diagram ]", x + width / 2f, y + boxH / 2f + 8f,
                 _regular, 18f, TextMuted, SKTextAlign.Center);
        return y + boxH + 16f;
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
}
