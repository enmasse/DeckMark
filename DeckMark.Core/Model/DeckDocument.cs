namespace DeckMark.Core.Model;

public sealed class DeckDocument
{
    public DeckHeader Header { get; init; } = new();
    public IReadOnlyList<Slide> Slides { get; init; } = [];
}
