using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DeckMark.Core.Mermaid;
using DeckMark.Core.Model;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;
using Thm = DocumentFormat.OpenXml.Drawing;
using PSlide = DocumentFormat.OpenXml.Presentation.Slide;
using ApplicationNonVisualDrawingProperties = DocumentFormat.OpenXml.Presentation.ApplicationNonVisualDrawingProperties;

namespace DeckMark.Core.Converter;

/// <summary>
/// Converts a <see cref="DeckDocument"/> to a PowerPoint (.pptx) file.
/// </summary>
public sealed class PptxConverter
{
    private readonly IMermaidRenderer _mermaid;

    // Slide size in EMUs: 16:9 = 9144000 x 5143500
    private const long SlideWidth = 9144000L;
    private const long SlideHeight = 5143500L;

    // Vertical gap between stacked blocks (EMUs, ~3 mm)
    private const long BlockGap = 114300L;

    // Approximate line height for body text at 18pt (EMUs)
    private const long LineHeight = 342900L;

    public PptxConverter(IMermaidRenderer mermaidRenderer)
    {
        _mermaid = mermaidRenderer;
    }

    public void Convert(DeckDocument doc, string outputPath)
    {
        ConvertAsync(doc, outputPath).GetAwaiter().GetResult();
    }

    public async Task ConvertAsync(DeckDocument doc, string outputPath, CancellationToken cancellationToken = default)
    {
        using var prs = PresentationDocument.Create(outputPath, PresentationDocumentType.Presentation);

        // Core properties
        prs.PackageProperties.Title = doc.Header.Title;
        prs.PackageProperties.Creator = doc.Header.Author;
        prs.PackageProperties.Description = doc.Header.Subtitle;

        var presPart = prs.AddPresentationPart();
        // Assign an empty Presentation now so relationship IDs can be resolved below
        presPart.Presentation = new P.Presentation();
        presPart.Presentation.AddNamespaceDeclaration("a", "http://schemas.openxmlformats.org/drawingml/2006/main");
        presPart.Presentation.AddNamespaceDeclaration("r", "http://schemas.openxmlformats.org/officeDocument/2006/relationships");
        presPart.Presentation.AddNamespaceDeclaration("p", "http://schemas.openxmlformats.org/presentationml/2006/main");

        // Slide layout/master setup
        var slideMasterPart = presPart.AddNewPart<SlideMasterPart>();
        var slideLayoutPart = slideMasterPart.AddNewPart<SlideLayoutPart>();
        InitSlideMaster(slideMasterPart, slideLayoutPart);

        // Build slides (split any single-column overflow before rendering)
        uint slideId = 256;
        var slideIdList = new P.SlideIdList();
        var slides = SplitOverflowSlides(doc.Slides);
        string? currentTransition = null;
        foreach (var slide in slides)
        {
            if (!string.IsNullOrWhiteSpace(slide.Transition))
                currentTransition = slide.Transition;

            var slidePart = presPart.AddNewPart<SlidePart>();
            slidePart.Slide = await BuildSlideAsync(slide, currentTransition, slidePart, cancellationToken);
            slidePart.AddPart(slideLayoutPart);

            if (slide.Notes.Count > 0)
                AddNotes(slidePart, slide.Notes);

            slideIdList.Append(new P.SlideId
            {
                Id = slideId++,
                RelationshipId = presPart.GetIdOfPart(slidePart),
            });
        }

        // Assemble Presentation children in schema-required order:
        // SlideMasterIdList → SlideIdList → SlideSize → NotesSize
        var masterIdList = new P.SlideMasterIdList();
        masterIdList.Append(new P.SlideMasterId
        {
            Id = 2147483648U,
            RelationshipId = presPart.GetIdOfPart(slideMasterPart),
        });

        presPart.Presentation.Append(masterIdList);
        presPart.Presentation.Append(slideIdList);
        presPart.Presentation.Append(new P.SlideSize { Cx = (Int32Value)(int)SlideWidth, Cy = (Int32Value)(int)SlideHeight });
        presPart.Presentation.Append(new P.NotesSize { Cx = 6858000L, Cy = 9144000L });

        presPart.Presentation.Save();
    }

    // ── Slide master / layout (minimal, allows valid PPTX) ──────────────────

    private static void InitSlideMaster(SlideMasterPart masterPart, SlideLayoutPart layoutPart)
    {
        var master = new P.SlideMaster();
        master.AddNamespaceDeclaration("a", "http://schemas.openxmlformats.org/drawingml/2006/main");
        master.AddNamespaceDeclaration("r", "http://schemas.openxmlformats.org/officeDocument/2006/relationships");
        master.AddNamespaceDeclaration("p", "http://schemas.openxmlformats.org/presentationml/2006/main");

        var masterCsd = new P.CommonSlideData();
        var masterTree = new P.ShapeTree();
        masterTree.Append(MakeGroupNvProps(1U, string.Empty));
        masterTree.Append(new P.GroupShapeProperties(new A.TransformGroup()));
        masterCsd.Append(masterTree);
        master.Append(masterCsd);

        var colorMap = new P.ColorMap
        {
            Background1 = A.ColorSchemeIndexValues.Light1,
            Text1 = A.ColorSchemeIndexValues.Dark1,
            Background2 = A.ColorSchemeIndexValues.Light2,
            Text2 = A.ColorSchemeIndexValues.Dark2,
            Accent1 = A.ColorSchemeIndexValues.Accent1,
            Accent2 = A.ColorSchemeIndexValues.Accent2,
            Accent3 = A.ColorSchemeIndexValues.Accent3,
            Accent4 = A.ColorSchemeIndexValues.Accent4,
            Accent5 = A.ColorSchemeIndexValues.Accent5,
            Accent6 = A.ColorSchemeIndexValues.Accent6,
            Hyperlink = A.ColorSchemeIndexValues.Hyperlink,
            FollowedHyperlink = A.ColorSchemeIndexValues.FollowedHyperlink,
        };
        master.Append(colorMap);

        // Schema order for SlideMaster children: cSld → clrMap → sldLayoutIdLst → txStyles
        var layoutIdList = new P.SlideLayoutIdList();
        layoutIdList.Append(new P.SlideLayoutId
        {
            Id = 2147483649U,
            RelationshipId = masterPart.GetIdOfPart(layoutPart),
        });
        master.Append(layoutIdList);

        // TextStyles must come after sldLayoutIdLst
        master.Append(new P.TextStyles(
            new P.TitleStyle(),
            new P.BodyStyle(),
            new P.OtherStyle()));

        masterPart.SlideMaster = master;

        // Theme is required on the slide master
        var themePart = masterPart.AddNewPart<ThemePart>();
        themePart.Theme = BuildMinimalTheme();

        // Slide layout
        var layout = new P.SlideLayout();
        layout.AddNamespaceDeclaration("a", "http://schemas.openxmlformats.org/drawingml/2006/main");
        layout.AddNamespaceDeclaration("r", "http://schemas.openxmlformats.org/officeDocument/2006/relationships");
        layout.AddNamespaceDeclaration("p", "http://schemas.openxmlformats.org/presentationml/2006/main");

        var layoutCsd = new P.CommonSlideData();
        var layoutTree = new P.ShapeTree();
        layoutTree.Append(MakeGroupNvProps(1U, string.Empty));
        layoutTree.Append(new P.GroupShapeProperties(new A.TransformGroup()));
        layoutCsd.Append(layoutTree);
        layout.Append(layoutCsd);
        layout.Append(new P.ColorMapOverride(new A.MasterColorMapping()));
        layoutPart.SlideLayout = layout;

        layoutPart.AddPart(masterPart);
    }

    private static A.Theme BuildMinimalTheme()
    {
        var theme = new A.Theme { Name = "DeckMark" };
        theme.AddNamespaceDeclaration("a", "http://schemas.openxmlformats.org/drawingml/2006/main");

        var themeElements = new A.ThemeElements();

        var colorScheme = new A.ColorScheme { Name = "DeckMark" };
        colorScheme.Append(new A.Dark1Color(new A.SystemColor { LastColor = "000000", Val = A.SystemColorValues.WindowText }));
        colorScheme.Append(new A.Light1Color(new A.SystemColor { LastColor = "FFFFFF", Val = A.SystemColorValues.Window }));
        colorScheme.Append(new A.Dark2Color(new A.RgbColorModelHex { Val = "1F3864" }));
        colorScheme.Append(new A.Light2Color(new A.RgbColorModelHex { Val = "E9EEF4" }));
        colorScheme.Append(new A.Accent1Color(new A.RgbColorModelHex { Val = "4472C4" }));
        colorScheme.Append(new A.Accent2Color(new A.RgbColorModelHex { Val = "ED7D31" }));
        colorScheme.Append(new A.Accent3Color(new A.RgbColorModelHex { Val = "A9D18E" }));
        colorScheme.Append(new A.Accent4Color(new A.RgbColorModelHex { Val = "FFC000" }));
        colorScheme.Append(new A.Accent5Color(new A.RgbColorModelHex { Val = "5B9BD5" }));
        colorScheme.Append(new A.Accent6Color(new A.RgbColorModelHex { Val = "70AD47" }));
        colorScheme.Append(new A.Hyperlink(new A.RgbColorModelHex { Val = "0563C1" }));
        colorScheme.Append(new A.FollowedHyperlinkColor(new A.RgbColorModelHex { Val = "954F72" }));
        themeElements.Append(colorScheme);

        var fontScheme = new A.FontScheme { Name = "DeckMark" };
        var majorFont = new A.MajorFont();
        majorFont.Append(new A.LatinFont { Typeface = "Calibri Light" });
        majorFont.Append(new A.EastAsianFont { Typeface = "" });
        majorFont.Append(new A.ComplexScriptFont { Typeface = "" });
        var minorFont = new A.MinorFont();
        minorFont.Append(new A.LatinFont { Typeface = "Calibri" });
        minorFont.Append(new A.EastAsianFont { Typeface = "" });
        minorFont.Append(new A.ComplexScriptFont { Typeface = "" });
        fontScheme.Append(majorFont);
        fontScheme.Append(minorFont);
        themeElements.Append(fontScheme);

        var fmtScheme = new A.FormatScheme { Name = "DeckMark" };
        var fillStyleList = new A.FillStyleList();
        fillStyleList.Append(new A.NoFill());
        fillStyleList.Append(new A.SolidFill(new A.SchemeColor { Val = A.SchemeColorValues.PhColor }));
        fillStyleList.Append(new A.SolidFill(new A.SchemeColor { Val = A.SchemeColorValues.PhColor }));
        fmtScheme.Append(fillStyleList);
        var lnStyleList = new A.LineStyleList();
        var ln = new A.Outline { Width = 6350 };
        ln.Append(new A.SolidFill(new A.SchemeColor { Val = A.SchemeColorValues.PhColor }));
        lnStyleList.Append(ln);
        lnStyleList.Append((A.Outline)ln.CloneNode(true));
        lnStyleList.Append((A.Outline)ln.CloneNode(true));
        fmtScheme.Append(lnStyleList);
        var effectStyleList = new A.EffectStyleList();
        effectStyleList.Append(new A.EffectStyle(new A.EffectList()));
        effectStyleList.Append(new A.EffectStyle(new A.EffectList()));
        effectStyleList.Append(new A.EffectStyle(new A.EffectList()));
        fmtScheme.Append(effectStyleList);
        var bgFillStyleList = new A.BackgroundFillStyleList();
        bgFillStyleList.Append(new A.NoFill());
        bgFillStyleList.Append(new A.SolidFill(new A.SchemeColor { Val = A.SchemeColorValues.PhColor }));
        bgFillStyleList.Append(new A.SolidFill(new A.SchemeColor { Val = A.SchemeColorValues.PhColor }));
        fmtScheme.Append(bgFillStyleList);
        themeElements.Append(fmtScheme);

        theme.Append(themeElements);
        theme.Append(new A.ObjectDefaults());
        theme.Append(new A.ExtraColorSchemeList());
        return theme;
    }

    // ── Overflow splitter ───────────────────────────────────────────────────────

    private static List<Model.Slide> SplitOverflowSlides(IReadOnlyList<Model.Slide> slides)
    {
        var result = new List<Model.Slide>();
        foreach (var slide in slides)
        {
            // Two-column slides use scale-to-fit; only split single-column content slides
            if (slide.Layout == "two-column")
            {
                result.Add(slide);
                continue;
            }

            long bodyWidth = SlideWidth - 914400L;
            long bodyTop = 1143000L;
            long bodyBottom = SlideHeight - 457200L;
            long available = bodyBottom - bodyTop;

            // Expand any bullet list block whose lines overflow into multiple blocks
            var expanded = ExpandBulletBlocks(slide.Body, bodyWidth, available);

            // Pack blocks into pages, starting a new continuation slide when full
            var current = new List<ContentBlock>();
            long usedHeight = 0;
            bool isFirst = true;

            foreach (var block in expanded)
            {
                long h = EstimateBlockHeight(block, bodyWidth);
                long needed = usedHeight == 0 ? h : usedHeight + BlockGap + h;
                if (!isFirst && needed > available && current.Count > 0)
                {
                    result.Add(MakeContinuationSlide(slide, current, isFirst));
                    current = [];
                    usedHeight = 0;
                    isFirst = false;
                }
                current.Add(block);
                usedHeight = usedHeight == 0 ? h : usedHeight + BlockGap + h;
                isFirst = false;
            }

            if (current.Count > 0 || result.Count == 0 || result[^1] != slide)
                result.Add(MakeContinuationSlide(slide, current, result.Count == 0 || !result.Any(s => s.Title == slide.Title)));
        }
        return result;
    }

    /// <summary>Splits bullet/numbered list blocks whose total height exceeds the available height into smaller chunks.</summary>
    private static List<ContentBlock> ExpandBulletBlocks(
        IReadOnlyList<ContentBlock> blocks, long bodyWidth, long available)
    {
        var result = new List<ContentBlock>();
        foreach (var block in blocks)
        {
            if (block.Kind is not (BlockKind.BulletList or BlockKind.NumberedList))
            {
                result.Add(block);
                continue;
            }
            var lines = block.RawContent.Split('\n')
                .Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
            // How many lines fit in one block before it overflows?
            int maxLines = Math.Max(1, (int)(available / LineHeight));
            for (int i = 0; i < lines.Count; i += maxLines)
            {
                var chunk = lines.Skip(i).Take(maxLines).ToList();
                result.Add(new ContentBlock
                {
                    Kind = block.Kind,
                    RawContent = string.Join("\n", chunk),
                });
            }
        }
        return result;
    }

    private static Model.Slide MakeContinuationSlide(
        Model.Slide source, List<ContentBlock> body, bool keepOriginalId)
    {
        return new Model.Slide
        {
            Title = source.Title,
            Id = keepOriginalId ? source.Id : null,
            Layout = source.Layout,
            Background = source.Background,
            Transition = source.Transition,
            Build = source.Build,
            Footer = source.Footer,
            Body = body,
            Notes = keepOriginalId ? source.Notes : [],
        };
    }

    // ── Slide builder ────────────────────────────────────────────────────────

    private async Task<PSlide> BuildSlideAsync(
        Model.Slide slide,
        string? effectiveTransition,
        SlidePart slidePart,
        CancellationToken ct)
    {
        var shapeTree = new P.ShapeTree();
        shapeTree.Append(MakeGroupNvProps(1U, string.Empty));
        shapeTree.Append(new P.GroupShapeProperties(new A.TransformGroup()));

        uint shapeId = 2;

        // Title shape
        shapeTree.Append(MakeTitleShape(slide.Title, shapeId++));

        long bodyTop = 1143000L;
        long bodyLeft = 457200L;
        long bodyWidth = SlideWidth - 914400L;
        long bodyBottom = SlideHeight - 457200L;

        if (slide.Layout == "two-column")
        {
            var halfWidth = bodyWidth / 2 - 91440L;
            long availableHeight = bodyBottom - bodyTop;
            foreach (var block in slide.Body)
            {
                if (block.Kind == BlockKind.Columns)
                {
                    long rightLeft = bodyLeft + halfWidth + 182880L;
                    await PlaceColumnBlocksAsync(block.Left,  bodyLeft,  bodyTop, halfWidth, availableHeight, shapeTree, slidePart, shapeId, id => shapeId = id, ct);
                    await PlaceColumnBlocksAsync(block.Right, rightLeft, bodyTop, halfWidth, availableHeight, shapeTree, slidePart, shapeId, id => shapeId = id, ct);
                    continue;
                }
                long bh = EstimateBlockHeight(block, bodyWidth);
                var shape = await BlockToShapeAsync(block, shapeId++, bodyLeft, bodyTop, bodyWidth, bh, slidePart, ct);
                if (shape is not null) shapeTree.Append(shape);
                bodyTop += bh + BlockGap;
            }
        }
        else
        {
            long currentY = bodyTop;
            foreach (var block in slide.Body)
            {
                long h = EstimateBlockHeight(block, bodyWidth);
                // Clamp so a block never exceeds the remaining space
                h = Math.Min(h, bodyBottom - currentY);
                if (h <= 0) break;
                var shape = await BlockToShapeAsync(block, shapeId++, bodyLeft, currentY, bodyWidth, h, slidePart, ct);
                if (shape is not null) shapeTree.Append(shape);
                currentY += h + BlockGap;
            }
        }

        var csd = new P.CommonSlideData();
        csd.Append(shapeTree);

        var pSlide = new PSlide();
        pSlide.AddNamespaceDeclaration("a", "http://schemas.openxmlformats.org/drawingml/2006/main");
        pSlide.AddNamespaceDeclaration("r", "http://schemas.openxmlformats.org/officeDocument/2006/relationships");
        pSlide.AddNamespaceDeclaration("p", "http://schemas.openxmlformats.org/presentationml/2006/main");
        pSlide.Append(csd);
        if (CreateTransition(effectiveTransition) is P.Transition transition)
            pSlide.Append(transition);
        pSlide.Append(new P.ColorMapOverride(new A.MasterColorMapping()));
        return pSlide;
    }

    private static P.Transition? CreateTransition(string? transition)
    {
        return transition?.Trim().ToLowerInvariant() switch
        {
            "blinds" => new P.Transition(new P.BlindsTransition()),
            "checker" => new P.Transition(new P.CheckerTransition()),
            "circle" => new P.Transition(new P.CircleTransition()),
            "comb" => new P.Transition(new P.CombTransition()),
            "cover" => new P.Transition(new P.CoverTransition()),
            "cut" => new P.Transition(new P.CutTransition()),
            "diamond" => new P.Transition(new P.DiamondTransition()),
            "dissolve" => new P.Transition(new P.DissolveTransition()),
            "fade" => new P.Transition(new P.FadeTransition()),
            "newsflash" => new P.Transition(new P.NewsflashTransition()),
            "plus" => new P.Transition(new P.PlusTransition()),
            "pull" => new P.Transition(new P.PullTransition()),
            "push" => new P.Transition(new P.PushTransition()),
            "random" => new P.Transition(new P.RandomTransition()),
            "randombar" => new P.Transition(new P.RandomBarTransition()),
            "split" => new P.Transition(new P.SplitTransition()),
            "strips" => new P.Transition(new P.StripsTransition()),
            "wedge" => new P.Transition(new P.WedgeTransition()),
            "wheel" => new P.Transition(new P.WheelTransition()),
            "wipe" => new P.Transition(new P.WipeTransition()),
            "zoom" => new P.Transition(new P.ZoomTransition()),
            _ => null,
        };
    }

    // ── Column layout helper (scale-to-fit) ──────────────────────────────────

    private async Task PlaceColumnBlocksAsync(
        IReadOnlyList<ContentBlock> blocks,
        long x, long startY, long cx, long availableHeight,
        P.ShapeTree shapeTree,
        SlidePart slidePart,
        uint shapeIdStart,
        Action<uint> updateShapeId,
        CancellationToken ct)
    {
        if (blocks.Count == 0) return;

        // Estimate heights using the actual column width for accurate wrap counting
        var heights = blocks.Select(b => EstimateBlockHeight(b, cx)).ToList();
        long totalGaps = BlockGap * (blocks.Count - 1);
        long totalContent = heights.Sum() + totalGaps;

        // Scale proportionally to fill the available height (shrink or grow)
        double scale = (double)availableHeight / totalContent;

        long y = startY;
        uint id = shapeIdStart;
        for (int i = 0; i < blocks.Count; i++)
        {
            long h = Math.Max(114300L, (long)(heights[i] * scale));
            var s = await BlockToShapeAsync(blocks[i], id++, x, y, cx, h, slidePart, ct);
            if (s is not null) shapeTree.Append(s);
            y += h + (long)(BlockGap * scale);
        }
        updateShapeId(id);
    }

    // ── Block height estimation ──────────────────────────────────────────────

    // Approximate width of one character at 18pt Calibri in EMU (~9pt wide)
    private const long CharWidthEmu = 102600L; // 9pt * 11378 EMU/pt ≈ 102600

    private static long EstimateBlockHeight(ContentBlock block, long availableWidth)
    {
        return block.Kind switch
        {
            BlockKind.MermaidBlock => 1600000L,
            BlockKind.Image        => 1200000L,
            BlockKind.CodeBlock    => LineHeight * (CountWrappedLines(block.RawContent, availableWidth, isMono: true) + 2),
            BlockKind.Callout      => LineHeight * (CountWrappedLines(block.RawContent, availableWidth) + 3),
            BlockKind.Heading      => LineHeight + 114300L,
            _                      => LineHeight * Math.Max(1, CountWrappedLines(block.RawContent, availableWidth)),
        };
    }

    /// <summary>Counts the total rendered lines after soft-wrapping each source line to <paramref name="availableWidth"/>.</summary>
    private static int CountWrappedLines(string text, long availableWidth, bool isMono = false)
    {
        if (string.IsNullOrWhiteSpace(text)) return 1;
        // Mono text uses a wider per-character estimate (~10pt)
        long charWidth = isMono ? 114300L : CharWidthEmu;
        long charsPerLine = Math.Max(10, availableWidth / charWidth);
        int total = 0;
        foreach (var line in text.Split('\n').Where(l => !string.IsNullOrWhiteSpace(l)))
        {
            var stripped = line.TrimStart('-', '*', '+', ' ').Trim();
            int len = Math.Max(1, stripped.Length);
            total += (int)Math.Ceiling((double)len / charsPerLine);
        }
        return Math.Max(1, total);
    }

    // ── Content block → shape ────────────────────────────────────────────────

    private async Task<OpenXmlElement?> BlockToShapeAsync(
        ContentBlock block,
        uint id,
        long x, long y, long cx, long cy,
        SlidePart slidePart,
        CancellationToken ct)
    {
        return block.Kind switch
        {
            BlockKind.MermaidBlock => await MakeMermaidShapeAsync(block, id, x, y, cx, cy, slidePart, ct),
            BlockKind.Image => MakeImagePlaceholderShape(block, id, x, y, cx, cy),
            BlockKind.CodeBlock => MakeCodeShape(block, id, x, y, cx, cy),
            BlockKind.Callout => MakeCalloutShape(block, id, x, y, cx, cy),
            BlockKind.Columns => null,
            _ => MakeTextShape(block, id, x, y, cx, cy),
        };
    }

    // ── Title shape ───────────────────────────────────────────────────────────

    private static P.Shape MakeTitleShape(string title, uint id)
    {
        var nvProps = new P.NonVisualShapeProperties(
            new P.NonVisualDrawingProperties { Id = id, Name = "Title" },
            new P.NonVisualShapeDrawingProperties(new A.ShapeLocks { NoGrouping = true }),
            new ApplicationNonVisualDrawingProperties(new P.PlaceholderShape { Type = P.PlaceholderValues.Title }));

        var spPr = new P.ShapeProperties();
        spPr.Append(new A.Transform2D(
            new A.Offset { X = 457200L, Y = 274638L },
            new A.Extents { Cx = 8229600L, Cy = 1143000L }));

        // Scale font down so the title fits on one line: available width ~648pt, Calibri Light ~0.55 width ratio
        // fontSize (hundredths of pt) = 64800 / (len * 0.55); clamped to [1800, 3600]
        int titleFontSize = title.Length > 0
            ? Math.Clamp((int)(64800 / (title.Length * 0.55)), 1800, 3600)
            : 3600;
        var rpr = new A.RunProperties { Language = "en-US", FontSize = titleFontSize };
        var run = new A.Run();
        run.Append(rpr);
        run.Append(new A.Text(title));

        var para = new A.Paragraph();
        para.Append(run);

        var txBody = new P.TextBody();
        txBody.Append(new A.BodyProperties());
        txBody.Append(new A.ListStyle());
        txBody.Append(para);

        var shape = new P.Shape();
        shape.Append(nvProps);
        shape.Append(spPr);
        shape.Append(txBody);
        return shape;
    }

    // ── Text / bullet / heading shape ─────────────────────────────────────────

    private static P.Shape MakeTextShape(ContentBlock block, uint id, long x, long y, long cx, long cy)
    {
        var nvProps = new P.NonVisualShapeProperties(
            new P.NonVisualDrawingProperties { Id = id, Name = "Content" },
            new P.NonVisualShapeDrawingProperties(),
            new ApplicationNonVisualDrawingProperties());

        var spPr = new P.ShapeProperties();
        spPr.Append(new A.Transform2D(
            new A.Offset { X = x, Y = y },
            new A.Extents { Cx = cx, Cy = cy }));

        var bodyPr = new A.BodyProperties { Wrap = A.TextWrappingValues.Square };
        bodyPr.Append(new A.NormalAutoFit());

        var txBody = new P.TextBody();
        txBody.Append(bodyPr);
        txBody.Append(new A.ListStyle());

        if (block.Kind == BlockKind.Heading)
        {
            var headingText = block.RawContent.TrimStart('#', ' ');
            var level = block.RawContent.TakeWhile(c => c == '#').Count();
            var fontSize = level switch { 1 => 2800, 2 => 2400, _ => 2000 };
            var rpr = new A.RunProperties { Language = "en-US", FontSize = fontSize, Bold = true };
            var run = new A.Run();
            run.Append(rpr);
            run.Append(new A.Text(headingText));
            var para = new A.Paragraph();
            para.Append(run);
            txBody.Append(para);
        }
        else
        {
            foreach (var line in block.RawContent.Split('\n').Where(l => !string.IsNullOrWhiteSpace(l)))
            {
                var isBullet = line.TrimStart().StartsWith('-') || line.TrimStart().StartsWith('*') || line.TrimStart().StartsWith('+');
                var text = isBullet ? line.TrimStart('-', '*', '+', ' ').Trim() : line.Trim();

                var pPr = isBullet ? new A.ParagraphProperties { Indent = -342900, LeftMargin = 342900 } : null;
                var rpr = new A.RunProperties { Language = "en-US" };
                var run = new A.Run();
                run.Append(rpr);
                run.Append(new A.Text(isBullet ? "• " + text : text));
                var para = new A.Paragraph();
                if (pPr is not null) para.Append(pPr);
                para.Append(run);
                txBody.Append(para);
            }
        }

        if (!txBody.Elements<A.Paragraph>().Any())
            txBody.Append(new A.Paragraph());

        var shape = new P.Shape();
        shape.Append(nvProps);
        shape.Append(spPr);
        shape.Append(txBody);
        return shape;
    }

    // ── Code block shape ─────────────────────────────────────────────────────

    private static P.Shape MakeCodeShape(ContentBlock block, uint id, long x, long y, long cx, long cy)
    {
        var nvProps = new P.NonVisualShapeProperties(
            new P.NonVisualDrawingProperties { Id = id, Name = "CodeBlock" },
            new P.NonVisualShapeDrawingProperties(),
            new ApplicationNonVisualDrawingProperties());

        var spPr = new P.ShapeProperties();
        spPr.Append(new A.Transform2D(
            new A.Offset { X = x, Y = y },
            new A.Extents { Cx = cx, Cy = cy }));
        // prstGeom must precede fill in a:spPr schema order
        spPr.Append(new A.PresetGeometry { Preset = A.ShapeTypeValues.Rectangle });
        // Light gray background
        spPr.Append(new A.SolidFill(new A.RgbColorModelHex { Val = "F2F2F2" }));

        var bodyPr = new A.BodyProperties
        {
            Wrap = A.TextWrappingValues.Square,
            Anchor = A.TextAnchoringTypeValues.Top,
            LeftInset = 182880,   // ~16pt padding
            RightInset = 182880,
            TopInset = 91440,     // ~8pt padding
            BottomInset = 91440,
        };
        bodyPr.Append(new A.NoAutoFit());

        // SpacingPoints Val is in hundredths of a point: 1000 = 10pt for 14pt font (forces tight packing).
        var codeListStyle = new A.ListStyle();
        var defPPr = new A.DefaultParagraphProperties();
        defPPr.Append(new A.LineSpacing(new A.SpacingPoints { Val = 1000 }));
        defPPr.Append(new A.SpaceBefore(new A.SpacingPoints { Val = 0 }));
        defPPr.Append(new A.SpaceAfter(new A.SpacingPoints { Val = 0 }));
        codeListStyle.Append(defPPr);

        var txBody = new P.TextBody();
        txBody.Append(bodyPr);
        txBody.Append(codeListStyle);

        // All lines go into a single paragraph separated by <a:br/> to avoid
        // inter-paragraph spacing that PowerPoint applies between <a:p> elements.
        var codePara = new A.Paragraph();
        var codeParaPr = new A.ParagraphProperties();
        codeParaPr.Append(new A.LineSpacing(new A.SpacingPoints { Val = 1000 }));
        codeParaPr.Append(new A.SpaceBefore(new A.SpacingPoints { Val = 0 }));
        codeParaPr.Append(new A.SpaceAfter(new A.SpacingPoints { Val = 0 }));
        codePara.Append(codeParaPr);

        var lines = block.RawContent.Split('\n').Where(l => !string.IsNullOrEmpty(l)).ToList();
        for (int i = 0; i < lines.Count; i++)
        {
            var latin = new A.LatinFont { Typeface = "Consolas" };
            var rpr = new A.RunProperties { Language = "en-US", FontSize = 1400, Dirty = false };
            rpr.Append(latin);
            var run = new A.Run();
            run.Append(rpr);
            run.Append(new A.Text(lines[i]));
            codePara.Append(run);

            if (i < lines.Count - 1)
            {
                var brRpr = new A.RunProperties { Language = "en-US", FontSize = 1400, Dirty = false };
                brRpr.Append((A.LatinFont)latin.CloneNode(true));
                codePara.Append(new A.Break { RunProperties = brRpr });
            }
        }

        if (!codePara.Elements<A.Run>().Any())
            codePara.Append(new A.Run(new A.RunProperties { Language = "en-US", FontSize = 1400 }, new A.Text()));

        txBody.Append(codePara);

        var shape = new P.Shape();
        shape.Append(nvProps);
        shape.Append(spPr);
        shape.Append(txBody);
        return shape;
    }

    // ── Callout shape ────────────────────────────────────────────────────────

    private static P.Shape MakeCalloutShape(ContentBlock block, uint id, long x, long y, long cx, long cy)
    {
        var nvProps = new P.NonVisualShapeProperties(
            new P.NonVisualDrawingProperties { Id = id, Name = "Callout" },
            new P.NonVisualShapeDrawingProperties(),
            new ApplicationNonVisualDrawingProperties());

        var spPr = new P.ShapeProperties();
        spPr.Append(new A.Transform2D(
            new A.Offset { X = x, Y = y },
            new A.Extents { Cx = cx, Cy = cy }));

        var txBody = new P.TextBody();
        txBody.Append(new A.BodyProperties { Wrap = A.TextWrappingValues.Square });
        txBody.Append(new A.ListStyle());

        var headerText = $"[{block.CalloutType?.ToUpperInvariant() ?? "INFO"}] {block.CalloutTitle}";
        var hRpr = new A.RunProperties { Language = "en-US", Bold = true };
        var hRun = new A.Run();
        hRun.Append(hRpr);
        hRun.Append(new A.Text(headerText));
        var hPara = new A.Paragraph();
        hPara.Append(hRun);
        txBody.Append(hPara);

        foreach (var line in block.RawContent.Split('\n').Where(l => !string.IsNullOrWhiteSpace(l)))
        {
            var rpr = new A.RunProperties { Language = "en-US" };
            var run = new A.Run();
            run.Append(rpr);
            run.Append(new A.Text(line.Trim()));
            var para = new A.Paragraph();
            para.Append(run);
            txBody.Append(para);
        }

        var shape = new P.Shape();
        shape.Append(nvProps);
        shape.Append(spPr);
        shape.Append(txBody);
        return shape;
    }

    // ── Mermaid: render or placeholder ───────────────────────────────────────

    private async Task<OpenXmlElement?> MakeMermaidShapeAsync(
        ContentBlock block,
        uint id,
        long x, long y, long cx, long cy,
        SlidePart slidePart,
        CancellationToken ct)
    {
        var pngBytes = await _mermaid.RenderAsync(block.RawContent, ct).ConfigureAwait(false);

        if (pngBytes is not null)
        {
            // Compute fillRect offsets so the image is letterboxed/pillarboxed
            // within the allocated box without distorting its aspect ratio.
            var (imgW, imgH) = ReadPngDimensions(pngBytes);
            int fillL = 0, fillT = 0, fillR = 0, fillB = 0;
            if (imgW > 0 && imgH > 0)
            {
                double imgAspect = (double)imgW / imgH;
                double boxAspect = (double)cx / cy;
                if (imgAspect > boxAspect)
                {
                    // Image is wider than box → letterbox (black bars top/bottom)
                    double rendered = cx / imgAspect;   // rendered height in EMU
                    double pad = (cy - rendered) / 2.0 / cy;
                    fillT = (int)Math.Round(pad * 100000);
                    fillB = fillT;
                }
                else
                {
                    // Image is taller than box → pillarbox (bars left/right)
                    double rendered = cy * imgAspect;   // rendered width in EMU
                    double pad = (cx - rendered) / 2.0 / cx;
                    fillL = (int)Math.Round(pad * 100000);
                    fillR = fillL;
                }
            }
            return MakePicture(pngBytes, id, x, y, cx, cy, slidePart, "mermaid", fillL, fillT, fillR, fillB);
        }

        // Placeholder text shape
        var nvProps = new P.NonVisualShapeProperties(
            new P.NonVisualDrawingProperties { Id = id, Name = "Mermaid" },
            new P.NonVisualShapeDrawingProperties(),
            new ApplicationNonVisualDrawingProperties());

        var spPr = new P.ShapeProperties();
        spPr.Append(new A.Transform2D(
            new A.Offset { X = x, Y = y },
            new A.Extents { Cx = cx, Cy = cy }));

        var txBody = new P.TextBody();
        txBody.Append(new A.BodyProperties { Wrap = A.TextWrappingValues.Square });
        txBody.Append(new A.ListStyle());

        foreach (var line in $"[mermaid]\n{block.RawContent}".Split('\n'))
        {
            var latin = new A.LatinFont { Typeface = "Courier New" };
            var rpr = new A.RunProperties { Language = "en-US" };
            rpr.Append(latin);
            var run = new A.Run();
            run.Append(rpr);
            run.Append(new A.Text(line));
            var para = new A.Paragraph();
            para.Append(run);
            txBody.Append(para);
        }

        var shape = new P.Shape();
        shape.Append(nvProps);
        shape.Append(spPr);
        shape.Append(txBody);
        return shape;
    }

    // ── Image placeholder ─────────────────────────────────────────────────────

    private static P.Shape MakeImagePlaceholderShape(ContentBlock block, uint id, long x, long y, long cx, long cy)
    {
        var nvProps = new P.NonVisualShapeProperties(
            new P.NonVisualDrawingProperties { Id = id, Name = "Image" },
            new P.NonVisualShapeDrawingProperties(),
            new ApplicationNonVisualDrawingProperties());

        var spPr = new P.ShapeProperties();
        spPr.Append(new A.Transform2D(
            new A.Offset { X = x, Y = y },
            new A.Extents { Cx = cx, Cy = cy }));

        var rpr = new A.RunProperties { Language = "en-US" };
        var run = new A.Run();
        run.Append(rpr);
        run.Append(new A.Text($"[Image: {block.AltText}]\n{block.ImageUrl}"));
        var para = new A.Paragraph();
        para.Append(run);

        var txBody = new P.TextBody();
        txBody.Append(new A.BodyProperties());
        txBody.Append(new A.ListStyle());
        txBody.Append(para);

        var shape = new P.Shape();
        shape.Append(nvProps);
        shape.Append(spPr);
        shape.Append(txBody);
        return shape;
    }

    // ── PNG dimension reader ────────────────────────────────────────────────

    private static (int width, int height) ReadPngDimensions(byte[] png)
    {
        // PNG: 8-byte signature, then IHDR chunk: 4-byte length, 4-byte type, then width (4) and height (4) big-endian
        if (png.Length < 24) return (0, 0);
        int w = (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19];
        int h = (png[20] << 24) | (png[21] << 16) | (png[22] << 8) | png[23];
        return (w, h);
    }

    // ── Embed PNG as picture ──────────────────────────────────────────────────

    private static P.Picture MakePicture(
        byte[] pngBytes,
        uint id,
        long x, long y, long cx, long cy,
        SlidePart slidePart,
        string name,
        int fillLeft = 0, int fillTop = 0, int fillRight = 0, int fillBottom = 0)
    {
        var imagePart = slidePart.AddImagePart(ImagePartType.Png);
        using var ms = new MemoryStream(pngBytes);
        imagePart.FeedData(ms);
        var rId = slidePart.GetIdOfPart(imagePart);

        var nvPicPr = new P.NonVisualPictureProperties(
            new P.NonVisualDrawingProperties { Id = id, Name = name },
            new P.NonVisualPictureDrawingProperties(new A.PictureLocks { NoChangeAspect = true }),
            new ApplicationNonVisualDrawingProperties());

        var blipFill = new P.BlipFill();
        var blip = new A.Blip { Embed = rId };
        var fillRect = new A.FillRectangle
        {
            Left   = fillLeft,
            Top    = fillTop,
            Right  = fillRight,
            Bottom = fillBottom,
        };
        var stretch = new A.Stretch(fillRect);
        blipFill.Append(blip);
        blipFill.Append(stretch);

        var spPr = new P.ShapeProperties();
        spPr.Append(new A.Transform2D(
            new A.Offset { X = x, Y = y },
            new A.Extents { Cx = cx, Cy = cy }));
        spPr.Append(new A.PresetGeometry { Preset = A.ShapeTypeValues.Rectangle });

        var pic = new P.Picture();
        pic.Append(nvPicPr);
        pic.Append(blipFill);
        pic.Append(spPr);
        return pic;
    }

    // ── Speaker notes ────────────────────────────────────────────────────────

    private static void AddNotes(SlidePart slidePart, IReadOnlyList<ContentBlock> notes)
    {
        var notesPart = slidePart.AddNewPart<NotesSlidePart>();

        var nvProps = new P.NonVisualShapeProperties(
            new P.NonVisualDrawingProperties { Id = 2U, Name = "Notes" },
            new P.NonVisualShapeDrawingProperties(),
            new ApplicationNonVisualDrawingProperties(
                new P.PlaceholderShape { Type = P.PlaceholderValues.Body }));

        var txBody = new P.TextBody();
        txBody.Append(new A.BodyProperties());
        txBody.Append(new A.ListStyle());

        foreach (var line in notes.SelectMany(n => n.RawContent.Split('\n')).Where(l => !string.IsNullOrWhiteSpace(l)))
        {
            var rpr = new A.RunProperties { Language = "en-US" };
            var run = new A.Run();
            run.Append(rpr);
            run.Append(new A.Text(line.Trim()));
            var para = new A.Paragraph();
            para.Append(run);
            txBody.Append(para);
        }

        var notesShape = new P.Shape();
        notesShape.Append(nvProps);
        notesShape.Append(new P.ShapeProperties());
        notesShape.Append(txBody);

        var tree = new P.ShapeTree();
        tree.Append(MakeGroupNvProps(1U, string.Empty));
        tree.Append(new P.GroupShapeProperties(new A.TransformGroup()));
        tree.Append(notesShape);

        var csd = new P.CommonSlideData();
        csd.Append(tree);

        var notesSlide = new P.NotesSlide();
        notesSlide.AddNamespaceDeclaration("a", "http://schemas.openxmlformats.org/drawingml/2006/main");
        notesSlide.AddNamespaceDeclaration("r", "http://schemas.openxmlformats.org/officeDocument/2006/relationships");
        notesSlide.AddNamespaceDeclaration("p", "http://schemas.openxmlformats.org/presentationml/2006/main");
        notesSlide.Append(csd);
        notesSlide.Append(new P.ColorMapOverride(new A.MasterColorMapping()));

        notesPart.NotesSlide = notesSlide;
    }

    private static P.NonVisualGroupShapeProperties MakeGroupNvProps(uint id, string name) =>
        new(
            new P.NonVisualDrawingProperties { Id = id, Name = name },
            new P.NonVisualGroupShapeDrawingProperties(),
            new ApplicationNonVisualDrawingProperties());
}
