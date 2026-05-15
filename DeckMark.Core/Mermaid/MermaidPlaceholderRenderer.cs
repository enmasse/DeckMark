namespace DeckMark.Core.Mermaid;

/// <summary>
/// Falls back gracefully: returns null so the converter inserts a styled text placeholder.
/// </summary>
public sealed class MermaidPlaceholderRenderer : IMermaidRenderer
{
    public Task<byte[]?> RenderAsync(string mermaidSource, CancellationToken cancellationToken = default)
        => Task.FromResult<byte[]?>(null);
}
