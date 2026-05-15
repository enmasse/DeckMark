namespace DeckMark.Core.Mermaid;

/// <summary>
/// Renders a Mermaid diagram source to a PNG or SVG byte array,
/// or returns null when the renderer wants the caller to fall back to a placeholder.
/// </summary>
public interface IMermaidRenderer
{
    /// <summary>
    /// Renders the given Mermaid source.
    /// </summary>
    /// <param name="mermaidSource">The Mermaid diagram source text.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>PNG bytes, or null if rendering is not available.</returns>
    Task<byte[]?> RenderAsync(string mermaidSource, CancellationToken cancellationToken = default);
}
