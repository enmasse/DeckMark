namespace DeckMark.Core.Mermaid;

/// <summary>
/// Renders Mermaid diagrams by calling the mermaid.ink public API.
/// Requires internet access. No Node.js installation needed.
/// </summary>
public sealed class MermaidInkRenderer : IMermaidRenderer
{
    private static readonly HttpClient Http = new();
    private const string BaseUrl = "https://mermaid.ink/img/";

    public async Task<byte[]?> RenderAsync(string mermaidSource, CancellationToken cancellationToken = default)
    {
        try
        {
            var encoded = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(mermaidSource));
            var url = BaseUrl + encoded;
            var response = await Http.GetAsync(url, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }
}
