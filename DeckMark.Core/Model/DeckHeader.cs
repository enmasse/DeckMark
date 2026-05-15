namespace DeckMark.Core.Model;

public sealed class DeckHeader
{
    public string Title { get; init; } = string.Empty;
    public string Subtitle { get; init; } = string.Empty;
    public string Author { get; init; } = string.Empty;
    public string Event { get; init; } = string.Empty;
    public string Theme { get; init; } = string.Empty;
    public string Aspect { get; init; } = "16:9";
    public string Footer { get; init; } = string.Empty;
    public string Language { get; init; } = "en-US";
    public string Company { get; init; } = string.Empty;
}
