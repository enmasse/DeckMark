namespace DeckMark.Core.Model;

public enum BlockKind
{
    Paragraph,
    BulletList,
    NumberedList,
    Heading,
    CodeBlock,
    MermaidBlock,
    Image,
    Table,
    BlockQuote,
    Columns,
    Notes,
    Callout,
}

public sealed class ContentBlock
{
    public BlockKind Kind { get; init; }

    /// <summary>Raw text or Markdown source for this block.</summary>
    public string RawContent { get; init; } = string.Empty;

    /// <summary>Language tag for code/mermaid blocks.</summary>
    public string? Language { get; init; }

    /// <summary>Callout type (info, success, warning, danger).</summary>
    public string? CalloutType { get; init; }

    /// <summary>Callout title.</summary>
    public string? CalloutTitle { get; init; }

    /// <summary>Left column blocks (for Columns kind).</summary>
    public IReadOnlyList<ContentBlock> Left { get; init; } = [];

    /// <summary>Center column blocks (for Columns kind with three columns).</summary>
    public IReadOnlyList<ContentBlock> Center { get; init; } = [];

    /// <summary>Right column blocks (for Columns kind).</summary>
    public IReadOnlyList<ContentBlock> Right { get; init; } = [];

    /// <summary>Image alt text.</summary>
    public string? AltText { get; init; }

    /// <summary>Image URL or path.</summary>
    public string? ImageUrl { get; init; }

    /// <summary>Optional width attribute (e.g. "80%").</summary>
    public string? ImageWidth { get; init; }

    /// <summary>Optional height attribute.</summary>
    public string? ImageHeight { get; init; }

    /// <summary>Optional align attribute.</summary>
    public string? ImageAlign { get; init; }
}
