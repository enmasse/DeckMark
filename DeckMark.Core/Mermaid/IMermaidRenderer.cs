namespace DeckMark.Core.Mermaid;

/// <summary>
/// Renders a Mermaid diagram source to an encoded Mermaid asset,
/// or returns null when the renderer wants the caller to fall back to a placeholder.
/// </summary>
public interface IMermaidRenderer
{
    /// <summary>
    /// Renders the given Mermaid source.
    /// </summary>
    /// <param name="mermaidSource">The Mermaid diagram source text.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The rendered Mermaid asset, or null if rendering is not available.</returns>
    Task<MermaidRenderAsset?> RenderAsync(string mermaidSource, CancellationToken cancellationToken = default);
}
