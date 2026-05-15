using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DeckMark.Core.Mermaid;
using DeckMark.Core.Model;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;
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
        presPart.Presentation = BuildPresentation();

        // Slide layout/master setup
        var slideMasterPart = presPart.AddNewPart<SlideMasterPart>();
        var slideLayoutPart = slideMasterPart.AddNewPart<SlideLayoutPart>();

        InitSlideMaster(slideMasterPart, slideLayoutPart);

        uint slideId = 256;
        var slideIdList = new P.SlideIdList();

        foreach (var slide in doc.Slides)
        {
            var slidePart = presPart.AddNewPart<SlidePart>();
            var slideXml = await BuildSlideAsync(slide, slidePart, cancellationToken);
            slidePart.Slide = slideXml;
            slidePart.AddPart(slideLayoutPart);

            if (slide.Notes.Count > 0)
                AddNotes(slidePart, slide.Notes);

            var rId = presPart.GetIdOfPart(slidePart);
            slideIdList.Append(new P.SlideId { Id = slideId++, RelationshipId = rId });
        }

        presPart.Presentation.SlideIdList = slideIdList;
        presPart.Presentation.Save();
    }

    // ── Presentation skeleton ────────────────────────────────────────────────

    private static P.Presentation BuildPresentation()
    {
        var pres = new P.Presentation();
        pres.AddNamespaceDeclaration("a", "http://schemas.openxmlformats.org/drawingml/2006/main");
        pres.AddNamespaceDeclaration("r", "http://schemas.openxmlformats.org/officeDocument/2006/relationships");
        pres.AddNamespaceDeclaration("p", "http://schemas.openxmlformats.org/presentationml/2006/main");
        pres.Append(new P.SlideSize { Cx = (Int32Value)(int)SlideWidth, Cy = (Int32Value)(int)SlideHeight });
        pres.Append(new P.NotesSize { Cx = 6858000L, Cy = 9144000L });
        return pres;
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

        var layoutIdList = new P.SlideLayoutIdList();
        layoutIdList.Append(new P.SlideLayoutId
        {
            Id = 2049U,
            RelationshipId = masterPart.GetIdOfPart(layoutPart),
        });
        master.Append(layoutIdList);

        masterPart.SlideMaster = master;

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

    // ── Slide builder ────────────────────────────────────────────────────────

    private async Task<PSlide> BuildSlideAsync(
        Model.Slide slide,
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
        long bodyHeight = SlideHeight - 1371600L;

        if (slide.Layout == "two-column")
        {
            var halfWidth = bodyWidth / 2 - 91440L;
            foreach (var block in slide.Body)
            {
                if (block.Kind == BlockKind.Columns)
                {
                    foreach (var child in block.Left)
                    {
                        var s = await BlockToShapeAsync(child, shapeId++, bodyLeft, bodyTop, halfWidth, bodyHeight, slidePart, ct);
                        if (s is not null) shapeTree.Append(s);
                    }
                    long rightLeft = bodyLeft + halfWidth + 182880L;
                    foreach (var child in block.Right)
                    {
                        var s = await BlockToShapeAsync(child, shapeId++, rightLeft, bodyTop, halfWidth, bodyHeight, slidePart, ct);
                        if (s is not null) shapeTree.Append(s);
                    }
                    continue;
                }
                var shape = await BlockToShapeAsync(block, shapeId++, bodyLeft, bodyTop, bodyWidth, bodyHeight, slidePart, ct);
                if (shape is not null) shapeTree.Append(shape);
            }
        }
        else
        {
            foreach (var block in slide.Body)
            {
                var shape = await BlockToShapeAsync(block, shapeId++, bodyLeft, bodyTop, bodyWidth, bodyHeight, slidePart, ct);
                if (shape is not null) shapeTree.Append(shape);
            }
        }

        var csd = new P.CommonSlideData();
        csd.Append(shapeTree);

        var pSlide = new PSlide();
        pSlide.AddNamespaceDeclaration("a", "http://schemas.openxmlformats.org/drawingml/2006/main");
        pSlide.AddNamespaceDeclaration("r", "http://schemas.openxmlformats.org/officeDocument/2006/relationships");
        pSlide.AddNamespaceDeclaration("p", "http://schemas.openxmlformats.org/presentationml/2006/main");
        pSlide.Append(csd);
        pSlide.Append(new P.ColorMapOverride(new A.MasterColorMapping()));
        return pSlide;
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

        var rpr = new A.RunProperties { Language = "en-US", FontSize = 3600 };
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

        var txBody = new P.TextBody();
        txBody.Append(new A.BodyProperties { Wrap = A.TextWrappingValues.Square });
        txBody.Append(new A.ListStyle());

        foreach (var line in block.RawContent.Split('\n').Where(l => !string.IsNullOrWhiteSpace(l)))
        {
            var rpr = new A.RunProperties { Language = "en-US" };
            var run = new A.Run();
            run.Append(rpr);
            run.Append(new A.Text(line.TrimStart('-', '*', '+', ' ').Trim()));
            var para = new A.Paragraph();
            para.Append(run);
            txBody.Append(para);
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
        spPr.Append(new A.PresetGeometry { Preset = A.ShapeTypeValues.Rectangle });

        var txBody = new P.TextBody();
        txBody.Append(new A.BodyProperties { Wrap = A.TextWrappingValues.Square });
        txBody.Append(new A.ListStyle());

        foreach (var line in block.RawContent.Split('\n'))
        {
            var latin = new A.LatinFont { Typeface = "Courier New" };
            var rpr = new A.RunProperties { Language = "en-US", Dirty = false };
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
            return MakePicture(pngBytes, id, x, y, cx, cy, slidePart, "mermaid");

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

    // ── Embed PNG as picture ──────────────────────────────────────────────────

    private static P.Picture MakePicture(
        byte[] pngBytes,
        uint id,
        long x, long y, long cx, long cy,
        SlidePart slidePart,
        string name)
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
        var stretch = new A.Stretch(new A.FillRectangle());
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
