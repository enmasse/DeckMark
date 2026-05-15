namespace DeckMark.Core.Model;

public sealed class Slide
{
    public string Title { get; init; } = string.Empty;
    public string? Id { get; init; }
    public string Layout { get; init; } = "content";
    public string? Background { get; init; }
    public string? Transition { get; init; }
    public string? Build { get; init; }
    public string? Footer { get; init; }
    public IReadOnlyList<ContentBlock> Body { get; init; } = [];
    public IReadOnlyList<ContentBlock> Notes { get; init; } = [];
}
